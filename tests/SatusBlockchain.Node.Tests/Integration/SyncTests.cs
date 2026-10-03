using System.Net;
using System.Text;
using System.Text.Json;
using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests.Integration;

/// <summary>
/// Testes de integração da SINCRONIZAÇÃO (etapa 8): nós reais em processos isolados,
/// com a mesma topologia do <c>docker-compose</c> (PEERS apontando para a porta de outro nó).
///
/// Aqui o que importa é o comportamento distribuído: recuperar o nó que ficou atrás,
/// manter a cadeia local quando o peer não traz novidade, e o reorg de um fork com as
/// transações órfãs voltando para a mempool.
/// </summary>
public class SyncTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Sync_AdotaACadeiaMaisLongaDoPeer()
    {
        // nodeB "caiu" e perdeu 2 blocos: localmente ele tem só o genesis.
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2,
            peers: [nodeA.BaseAddress]);

        await PostTransactionAsync(nodeA, new Transaction("alice", "bob", 10m));
        await MineBlockAsync(nodeA);
        await PostTransactionAsync(nodeA, new Transaction("bob", "carol", 5m));
        await MineBlockAsync(nodeA);

        Assert.Equal(1, await LengthAsync(nodeB));

        var response = await nodeB.Client.PostAsync("/sync", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var relatorio = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var raiz = relatorio.RootElement;
        Assert.Equal(1, raiz.GetProperty("previousLength").GetInt32());
        Assert.Equal(3, raiz.GetProperty("length").GetInt32());
        Assert.Equal(nodeA.BaseAddress, raiz.GetProperty("adopted").GetProperty("from").GetString());

        // A cadeia do nodeB passou a ser a do nodeA, bloco a bloco, e continua válida.
        Assert.Equal(await HashesAsync(nodeA), await HashesAsync(nodeB));

        using var validacao = await GetJsonAsync(nodeB, "/chain/validate");
        Assert.True(validacao.RootElement.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task Sync_ComPeerQueEstaAtras_NaoTrocaACadeiaLocal()
    {
        // Regra "first seen" aplicada ao sync: peer menor não faz este nó voltar atrás.
        using var atrasado = await NodeServer.StartAsync(nodeId: "node-atrasado", difficulty: 2);
        using var local = await NodeServer.StartAsync(nodeId: "node-local", difficulty: 2,
            peers: [atrasado.BaseAddress]);

        await PostTransactionAsync(local, new Transaction("a", "b", 1m));
        await MineBlockAsync(local);
        var meuHash = await UltimoHashAsync(local);

        var response = await local.Client.PostAsync("/sync", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var relatorio = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, relatorio.RootElement.GetProperty("adopted").ValueKind);
        Assert.Equal(2, relatorio.RootElement.GetProperty("length").GetInt32());
        Assert.Equal(meuHash, await UltimoHashAsync(local));
    }

    [Fact]
    public async Task Sync_Fork_AdotaACadeiaVencedora_E_OrfasVoltamParaAMempool()
    {
        // As duas pontas mineram sobre o MESMO genesis e divergem — um fork.
        //
        // A ORDEM importa, e é a lição: com push (etapa 6) e gossip (etapa 7), um nó
        // "em dia" não diverge de ninguém. O fork nasce quando um nó está À FRENTE e o
        // outro mina depois: o bloco que sobe é recusado com 409 (índice fora de sequência),
        // e cada ponta segue com a sua versão. Aqui o nodeA não tem peer configurado, então
        // os blocos dele ficam locais — é o "nó isolado" sem precisar derrubar processo.
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2,
            peers: [nodeA.BaseAddress]);

        // 1) nodeA avança sozinho: dois blocos que ninguém mais tem.
        await PostTransactionAsync(nodeA, new Transaction("alice", "bob", 10m));
        await MineBlockAsync(nodeA);
        await PostTransactionAsync(nodeA, new Transaction("bob", "dave", 3m));
        await MineBlockAsync(nodeA);

        // 2) nodeB mineria por conta própria. O bloco dele é empurrado ao nodeA e recusado
        //    com 409 (o nodeA já está no índice 2) — nasce o fork.
        var orfa = new Transaction("b", "carol", 7m);
        await PostTransactionAsync(nodeB, orfa);
        await MineBlockAsync(nodeB);
        Assert.Equal(2, await LengthAsync(nodeB));

        // 3) nodeB pergunta: a cadeia do nodeA é mais longa (3 contra 2) e vence.
        var response = await nodeB.Client.PostAsync("/sync", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var relatorio = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, relatorio.RootElement.GetProperty("previousLength").GetInt32());
        Assert.Equal(3, relatorio.RootElement.GetProperty("length").GetInt32());
        Assert.Equal(1, relatorio.RootElement.GetProperty("orphansBackToMempool").GetInt32());

        // O bloco do nodeB perdeu a disputa: sua transação volta para a mempool (Bitcoin).
        using var pending = await GetJsonAsync(nodeB, "/transactions/pending");
        var voltou = Assert.Single(Elements(pending.RootElement));
        Assert.Equal("carol", voltou.GetProperty("to").GetString());
        Assert.Equal(7m, voltou.GetProperty("amount").GetDecimal());

        // E o nodeB terminou exatamente com a cadeia do nodeA.
        Assert.Equal(await HashesAsync(nodeA), await HashesAsync(nodeB));
    }

    [Fact]
    public async Task Sync_PeerInacessivel_Retorna200_ComRelatorioDeFalha()
    {
        // Peer fora do ar é situação normal numa rede parcial: 200 + relatório, e a
        // cadeia local fica intacta (não pode ser erro para quem pediu o sync).
        using var node = await NodeServer.StartAsync(nodeId: "node-sozinho", difficulty: 2,
            peers: ["http://127.0.0.1:1"]);

        await PostTransactionAsync(node, new Transaction("a", "b", 1m));
        await MineBlockAsync(node);
        var meuHash = await UltimoHashAsync(node);

        var response = await node.Client.PostAsync("/sync", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var relatorio = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var raiz = relatorio.RootElement;
        Assert.Equal(JsonValueKind.Null, raiz.GetProperty("adopted").ValueKind);
        Assert.Equal(2, raiz.GetProperty("length").GetInt32());

        var peer = Assert.Single(Elements(raiz.GetProperty("peers")));
        Assert.Equal("Unreachable", peer.GetProperty("outcome").GetString());
        Assert.Equal("inacessível", peer.GetProperty("detail").GetString());

        Assert.Equal(meuHash, await UltimoHashAsync(node));
    }

    [Fact]
    public async Task Sync_SemPeers_RetornaRelatorioVazio()
    {
        // Nó solitário: nada a consultar, e ainda assim 200 com relatório coerente.
        using var node = await NodeServer.StartAsync(nodeId: "node-sozinho", difficulty: 2);

        var response = await node.Client.PostAsync("/sync", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var relatorio = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var raiz = relatorio.RootElement;
        Assert.Empty(Elements(raiz.GetProperty("peers")));
        Assert.Equal(JsonValueKind.Null, raiz.GetProperty("adopted").ValueKind);
        Assert.Equal(0, raiz.GetProperty("orphansBackToMempool").GetInt32());
    }

    private static async Task MineBlockAsync(NodeServer node)
    {
        var response = await node.Client.PostAsync("/blocks/mine", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonDocument> GetJsonAsync(NodeServer node, string path)
    {
        var response = await node.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<int> LengthAsync(NodeServer node)
    {
        using var chain = await GetJsonAsync(node, "/chain");
        return chain.RootElement.GetArrayLength();
    }

    private static async Task<string> UltimoHashAsync(NodeServer node)
    {
        using var chain = await GetJsonAsync(node, "/chain");
        return chain.RootElement.EnumerateArray().Last().GetProperty("hash").GetString()!;
    }

    private static async Task<string[]> HashesAsync(NodeServer node)
    {
        using var chain = await GetJsonAsync(node, "/chain");
        return [.. chain.RootElement.EnumerateArray().Select(bloco => bloco.GetProperty("hash").GetString()!)];
    }

    private static Task<HttpResponseMessage> PostTransactionAsync(NodeServer node, Transaction transaction) =>
        node.Client.PostAsync("/transactions", new StringContent(
            JsonSerializer.Serialize(transaction, WebJson), Encoding.UTF8, "application/json"));

    private static List<JsonElement> Elements(JsonElement array) => [.. array.EnumerateArray()];
}

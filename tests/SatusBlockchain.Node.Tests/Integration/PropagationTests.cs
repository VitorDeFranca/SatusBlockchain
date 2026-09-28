using System.Net;
using System.Text;
using System.Text.Json;
using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests.Integration;

/// <summary>
/// Testes de integração da PROPAGAÇÃO (etapa 6): nós reais em processos isolados,
/// ligados por PEERS e conversando por HTTP — a mesma topologia do docker-compose.
///
/// Cobrem o push no mine, o recebimento (aceitar x recusar), o bloco duplicado, o
/// bloco adulterado, o bloco órfão e a tolerância a um peer offline.
/// </summary>
public class PropagationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task PostMine_PropagaOBlocoParaTodosOsPeers()
    {
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2);
        using var nodeC = await NodeServer.StartAsync(nodeId: "node-c", difficulty: 2);
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2,
            peers: [nodeB.BaseAddress, nodeC.BaseAddress]);

        await PostTransactionAsync(nodeA, new Transaction("alice", "bob", 10m));

        var response = await nodeA.Client.PostAsync("/blocks/mine", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var mined = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var minedHash = mined.RootElement.GetProperty("hash").GetString();

        // O push é aguardado antes de o mine responder: quando ele responde, os peers
        // JÁ têm o bloco. Nenhuma sincronização (etapa 7) foi feita.
        foreach (var peer in new[] { nodeB, nodeC })
        {
            using var chain = await GetJsonAsync(peer, "/chain");
            Assert.Equal(2, chain.RootElement.GetArrayLength());
            Assert.Equal(minedHash, chain.RootElement[1].GetProperty("hash").GetString());

            using var validation = await GetJsonAsync(peer, "/chain/validate");
            Assert.True(validation.RootElement.GetProperty("valid").GetBoolean());
            Assert.Equal(2, validation.RootElement.GetProperty("length").GetInt32());
        }
    }

    [Fact]
    public async Task GetPeers_ListaOsPeersConfigurados()
    {
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2);
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2,
            peers: [nodeB.BaseAddress]);

        using var json = await GetJsonAsync(nodeA, "/peers");

        Assert.Equal("node-a", json.RootElement.GetProperty("node").GetString());
        Assert.Equal(new[] { nodeB.BaseAddress }, Strings(json.RootElement.GetProperty("peers")));
    }

    [Fact]
    public async Task GetPeers_SemPeersConfigurados_DevolveListaVazia_E_MineContinuaFuncionando()
    {
        // É o caso do `dotnet run` sem PEERS: nó isolado, push para ninguém.
        using var node = await NodeServer.StartAsync(difficulty: 2);

        using var json = await GetJsonAsync(node, "/peers");
        Assert.Empty(Strings(json.RootElement.GetProperty("peers")));

        var response = await node.Client.PostAsync("/blocks/mine", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostReceive_BlocoValido_Aceita_E_Duplicado_Retorna409()
    {
        using var nodeD = await NodeServer.StartAsync(nodeId: "node-d", difficulty: 2);
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2); // sem peers

        var block = await MineBlockJsonAsync(nodeA);

        var first = await PostBlockAsync(nodeD, block);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync()))
        {
            Assert.True(body.RootElement.GetProperty("accepted").GetBoolean());
            Assert.Equal(2, body.RootElement.GetProperty("length").GetInt32());
        }

        // Reenvio do MESMO bloco (o peer já o tem): recusa sem alterar a cadeia.
        var second = await PostBlockAsync(nodeD, block);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using (var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync()))
            Assert.False(body.RootElement.GetProperty("accepted").GetBoolean());

        using var validation = await GetJsonAsync(nodeD, "/chain/validate");
        Assert.Equal(2, validation.RootElement.GetProperty("length").GetInt32());
        Assert.True(validation.RootElement.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task PostReceive_BlocoAdulterado_Retorna409_E_NaoAlteraACadeia()
    {
        using var nodeD = await NodeServer.StartAsync(nodeId: "node-d", difficulty: 2);
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2); // sem peers

        await PostTransactionAsync(nodeA, new Transaction("alice", "bob", 10m));
        var block = await MineBlockJsonAsync(nodeA);

        // Mesmo bloco (índice e elo corretos), mas com o valor da transação trocado: o
        // hash recalculado pelo receptor não bate com o Hash declarado → recusa. É a
        // defesa contra um nó adulterador se disfarçando de peer.
        var tampered = block.Replace("\"amount\":10", "\"amount\":999");
        Assert.Contains("\"amount\":999", tampered);

        var response = await PostBlockAsync(nodeD, tampered);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var chain = await GetJsonAsync(nodeD, "/chain");
        Assert.Equal(1, chain.RootElement.GetArrayLength()); // continua apenas com o genesis
    }

    [Fact]
    public async Task PostReceive_BlocoOrfao_Retorna409()
    {
        // Cenário do nó que ficou para trás: está no genesis e recebe o bloco 2 de um
        // peer (o bloco 1 nunca chegou). O bloco é válido, mas não encaixa AQUI — é
        // essa a lacuna que o POST /sync da etapa 7 fecha.
        using var nodeD = await NodeServer.StartAsync(nodeId: "node-d", difficulty: 2);
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2); // sem peers

        await MineBlockJsonAsync(nodeA);                          // bloco 1 (não vai a ninguém)
        var block2 = await MineBlockJsonAsync(nodeA);

        var response = await PostBlockAsync(nodeD, block2);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var chain = await GetJsonAsync(nodeD, "/chain");
        Assert.Equal(1, chain.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task PostMine_ComPeerInacessivel_MinaNormalmente_E_PropagaAosDemais()
    {
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2);
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2,
            peers: ["http://127.0.0.1:1", nodeB.BaseAddress]); // 127.0.0.1:1 = porta morta

        var response = await nodeA.Client.PostAsync("/blocks/mine", content: null);

        // Peer offline não derruba a mineração local: 200 na resposta, cadeia local intacta.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var localChain = await GetJsonAsync(nodeA, "/chain");
        Assert.Equal(2, localChain.RootElement.GetArrayLength());

        using var peerChain = await GetJsonAsync(nodeB, "/chain");
        Assert.Equal(2, peerChain.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task PostReceive_RemoveDaMempoolAsTransacoesJaConfirmadas()
    {
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2);
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2,
            peers: [nodeB.BaseAddress]);

        // A MESMA transação está pendente nos dois nós (os dois receberam o POST).
        var transacao = new Transaction("alice", "bob", 10m);
        await PostTransactionAsync(nodeA, transacao);
        await PostTransactionAsync(nodeB, transacao);

        var response = await nodeA.Client.PostAsync("/blocks/mine", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // nodeB aceitou o bloco do nodeA e tirou a transação da fila: sem isso ele
        // re-mineraria a mesma transação e criaria um fork desnecessário.
        using var pending = await GetJsonAsync(nodeB, "/transactions/pending");
        Assert.Empty(pending.RootElement.EnumerateArray());
    }

    private static async Task<string> MineBlockJsonAsync(NodeServer node)
    {
        var response = await node.Client.PostAsync("/blocks/mine", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var block = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return block.RootElement.GetRawText();
    }

    private static Task<HttpResponseMessage> PostBlockAsync(NodeServer node, string blockJson) =>
        node.Client.PostAsync("/blocks/receive",
            new StringContent(blockJson, Encoding.UTF8, "application/json"));

    private static Task<HttpResponseMessage> PostTransactionAsync(NodeServer node, Transaction transaction) =>
        node.Client.PostAsync("/transactions", new StringContent(
            JsonSerializer.Serialize(transaction, WebJson), Encoding.UTF8, "application/json"));

    private static async Task<JsonDocument> GetJsonAsync(NodeServer node, string path)
    {
        var response = await node.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static List<string> Strings(JsonElement array) =>
        [.. array.EnumerateArray().Select(element => element.GetString()!)];
}

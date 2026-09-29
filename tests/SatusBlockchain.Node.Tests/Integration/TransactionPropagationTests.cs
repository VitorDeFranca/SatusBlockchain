using System.Net;
using System.Text;
using System.Text.Json;
using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests.Integration;

/// <summary>
/// Testes de integração do GOSSIP de transações (etapa 7): a transação postada em um nó
/// chega aos peers e pode ser minerada em QUALQUER um deles — a mempool continua sendo
/// local a cada nó, e o que existe entre eles é a propagação.
/// </summary>
public class TransactionPropagationTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task PostTransaction_PropagaParaTodosOsPeers()
    {
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2);
        using var nodeC = await NodeServer.StartAsync(nodeId: "node-c", difficulty: 2);
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2,
            peers: [nodeB.BaseAddress, nodeC.BaseAddress]);

        var response = await PostTransactionAsync(nodeA, new Transaction("alice", "bob", 10m));

        // O gossip é aguardado antes de responder: quando o 201 chega, os peers já têm
        // a transação na mempool deles.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        foreach (var peer in new[] { nodeB, nodeC })
        {
            using var pending = await GetJsonAsync(peer, "/transactions/pending");
            var transacao = Assert.Single(Elements(pending.RootElement));
            Assert.Equal("alice", transacao.GetProperty("from").GetString());
            Assert.Equal(10m, transacao.GetProperty("amount").GetDecimal());
        }
    }

    [Fact]
    public async Task PostTransaction_EmUmNo_PodeSerMineradaEmOutro()
    {
        // A pergunta que originou esta etapa: se eu postar a transação no node1, ela pode
        // ser minerada no node2/node3? Com o gossip, sim — é o fluxo de uma carteira real,
        // que fala com UM nó e conta com a propagação para o resto da rede.
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2);
        using var nodeC = await NodeServer.StartAsync(nodeId: "node-c", difficulty: 2);
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2,
            peers: [nodeB.BaseAddress, nodeC.BaseAddress]);

        await PostTransactionAsync(nodeA, new Transaction("alice", "bob", 10m));

        // Mineração em OUTRO nó (o que nunca viu o POST do cliente).
        var response = await nodeC.Client.PostAsync("/blocks/mine", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var block = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var minerada = Assert.Single(Elements(block.RootElement.GetProperty("transactions")));
        Assert.Equal("alice", minerada.GetProperty("from").GetString());
        Assert.Equal(10m, minerada.GetProperty("amount").GetDecimal());

        // E saiu da fila local do nodeC, porque agora está dentro do bloco.
        using var pending = await GetJsonAsync(nodeC, "/transactions/pending");
        Assert.Empty(pending.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task PostReceiveTransacao_Aceita_E_Duplicada_Retorna409()
    {
        using var node = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        var json = JsonSerializer.Serialize(new Transaction("alice", "bob", 10m), WebJson);

        var first = await PostTransactionReceiveAsync(node, json);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync()))
        {
            Assert.True(body.RootElement.GetProperty("accepted").GetBoolean());
            Assert.Equal(1, body.RootElement.GetProperty("pending").GetInt32());
        }

        // O mesmo peer (ou outro caminho) anuncia de novo: recusa sem duplicar a fila.
        var second = await PostTransactionReceiveAsync(node, json);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var pending = await GetJsonAsync(node, "/transactions/pending");
        Assert.Single(Elements(pending.RootElement));
    }

    [Fact]
    public async Task PostReceiveTransacao_JaConfirmadaEmBloco_Retorna409_E_NaoVoltaParaAMempool()
    {
        // Cenário do "bloco chegou antes da transação": depois de minerar, a transação
        // está confirmada e fora da fila — se ela reaparecer por gossip atrasado, não
        // pode voltar a ficar pendente (e acabar entrando em um segundo bloco).
        using var node = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        var transacao = new Transaction("alice", "bob", 10m);

        await PostTransactionAsync(node, transacao);
        var mined = await node.Client.PostAsync("/blocks/mine", content: null);
        Assert.Equal(HttpStatusCode.OK, mined.StatusCode);

        var response = await PostTransactionReceiveAsync(node, JsonSerializer.Serialize(transacao, WebJson));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("accepted").GetBoolean());

        using var pending = await GetJsonAsync(node, "/transactions/pending");
        Assert.Empty(pending.RootElement.EnumerateArray());
    }

    [Theory]
    [InlineData("", "bob", 10)]      // sem origem
    [InlineData("alice", "", 10)]    // sem destino
    [InlineData("alice", "bob", 0)]  // valor zerado
    [InlineData("alice", "bob", -5)] // valor negativo
    public async Task PostReceiveTransacaoInvalida_Retorna400_E_NaoEntraNaMempool(
        string from, string to, decimal amount)
    {
        using var node = await NodeServer.StartAsync(difficulty: 2);

        var response = await PostTransactionReceiveAsync(node,
            JsonSerializer.Serialize(new Transaction(from, to, amount), WebJson));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var pending = await GetJsonAsync(node, "/transactions/pending");
        Assert.Empty(pending.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task PostTransaction_ComPeerInacessivel_AindaRetorna201_E_PropagaAosDemais()
    {
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2);
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2,
            peers: ["http://127.0.0.1:1", nodeB.BaseAddress]); // 127.0.0.1:1 = porta morta

        var response = await PostTransactionAsync(nodeA, new Transaction("alice", "bob", 10m));

        // A transação do cliente é aceita localmente mesmo com um peer fora do ar.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var localPending = await GetJsonAsync(nodeA, "/transactions/pending");
        Assert.Single(Elements(localPending.RootElement));

        using var peerPending = await GetJsonAsync(nodeB, "/transactions/pending");
        Assert.Single(Elements(peerPending.RootElement));
    }

    private static Task<HttpResponseMessage> PostTransactionAsync(NodeServer node, Transaction transaction) =>
        node.Client.PostAsync("/transactions", new StringContent(
            JsonSerializer.Serialize(transaction, WebJson), Encoding.UTF8, "application/json"));

    private static Task<HttpResponseMessage> PostTransactionReceiveAsync(NodeServer node, string transactionJson) =>
        node.Client.PostAsync("/transactions/receive",
            new StringContent(transactionJson, Encoding.UTF8, "application/json"));

    private static async Task<JsonDocument> GetJsonAsync(NodeServer node, string path)
    {
        var response = await node.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static List<JsonElement> Elements(JsonElement array) => [.. array.EnumerateArray()];
}

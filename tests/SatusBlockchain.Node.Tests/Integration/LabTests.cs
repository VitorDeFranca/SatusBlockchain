using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests.Integration;

/// <summary>
/// Laboratório de falhas e fork (etapa 12) com nós reais: a partição simulada provoca um fork
/// de verdade, e curar + sincronizar faz a rede convergir pela longest chain rule, com a
/// transação do lado perdedor voltando para a mempool.
/// </summary>
public class LabTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Particao_GeraFork_E_CurarMaisSync_Converge()
    {
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2, peers: [nodeA.BaseAddress]);
        await SendJsonAsync(nodeA, HttpMethod.Post, "/peers", new { url = nodeB.BaseAddress }); // A e B se listam

        await SendJsonAsync(nodeB, HttpMethod.Put, "/lab/partition", new { group = "B" });

        // Lado principal: transação + dois blocos. O gossip e o push para o B são recusados (503).
        await PostTransactionAsync(nodeA, new Transaction("alice", "bob", 10m));
        await MineAsync(nodeA);
        await MineAsync(nodeA);

        // Lado isolado: o console continua operando o B (sem cabeçalho de peer, nada é filtrado).
        await PostTransactionAsync(nodeB, new Transaction("carol", "dave", 7m));
        await MineAsync(nodeB);

        var hashesA = await HashesAsync(nodeA);
        var hashesB = await HashesAsync(nodeB);
        Assert.Equal(3, hashesA.Length);
        Assert.Equal(2, hashesB.Length);
        Assert.Equal(hashesA[0], hashesB[0]);     // mesmo genesis...
        Assert.NotEqual(hashesA[1], hashesB[1]);  // ...e daí em diante, um fork

        // Enquanto a partição dura, nem o pull atravessa: o sync do B não adota nada.
        using (var blocked = await SyncAsync(nodeB))
            Assert.Equal(JsonValueKind.Null, blocked.RootElement.GetProperty("adopted").ValueKind);

        // Curar a partição e sincronizar: a cadeia mais longa (A) vence.
        Assert.Equal(HttpStatusCode.OK, (await nodeB.Client.DeleteAsync("/lab")).StatusCode);
        using (var healed = await SyncAsync(nodeB))
        {
            Assert.Equal(3, healed.RootElement.GetProperty("length").GetInt32());
            Assert.Equal(1, healed.RootElement.GetProperty("orphansBackToMempool").GetInt32());
        }

        Assert.Equal(hashesA, await HashesAsync(nodeB));

        // O bloco do B virou órfão; a transação dele voltou para a mempool.
        using var pending = await GetJsonAsync(nodeB, "/transactions/pending");
        var orphan = Assert.Single(pending.RootElement.EnumerateArray());
        Assert.Equal("carol", orphan.GetProperty("from").GetString());
    }

    [Fact]
    public async Task NoParticionado_RecusaSoPeersDeOutroGrupo()
    {
        using var node = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2);
        await SendJsonAsync(node, HttpMethod.Put, "/lab/partition", new { group = "B" });

        // Quem não é peer (console, curl) continua sendo atendido.
        Assert.Equal(HttpStatusCode.OK, (await node.Client.GetAsync("/chain")).StatusCode);

        // Peer sem grupo: outro lado da partição.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await GetAsPeerAsync(node, "/chain", group: null)).StatusCode);

        // Peer do mesmo grupo: passa.
        Assert.Equal(HttpStatusCode.OK, (await GetAsPeerAsync(node, "/chain", group: "B")).StatusCode);
    }

    [Fact]
    public async Task Lentidao_AtrasaSoOsPeers()
    {
        using var node = await NodeServer.StartAsync(nodeId: "node-lento", difficulty: 2);
        await SendJsonAsync(node, HttpMethod.Put, "/lab/delay", new { ms = 700 });
        await node.Client.GetAsync("/chain/validate"); // aquecimento

        var peer = Stopwatch.StartNew();
        await GetAsPeerAsync(node, "/chain/validate", group: null);
        peer.Stop();

        var console = Stopwatch.StartNew();
        await node.Client.GetAsync("/chain/validate");
        console.Stop();

        Assert.True(peer.ElapsedMilliseconds >= 700, $"peer levou {peer.ElapsedMilliseconds} ms");
        Assert.True(console.ElapsedMilliseconds < 700, $"console levou {console.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Inspect_MostraPorQueOBlocoAdulteradoSeriaRecusado()
    {
        using var node = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        await PostTransactionAsync(node, new Transaction("alice", "bob", 10m));
        await MineAsync(node);

        using var chain = await GetJsonAsync(node, "/chain");
        var block = JsonNode.Parse(chain.RootElement[1].GetRawText())!;

        using (var original = await SendJsonAsync(node, HttpMethod.Post, "/blocks/inspect", block))
        {
            Assert.True(original.RootElement.GetProperty("hashMatches").GetBoolean());
            Assert.True(original.RootElement.GetProperty("inChain").GetBoolean());
        }

        block["transactions"]![0]!["amount"] = 999;
        using var tampered = await SendJsonAsync(node, HttpMethod.Post, "/blocks/inspect", block);
        var root = tampered.RootElement;
        Assert.False(root.GetProperty("hashMatches").GetBoolean());
        Assert.False(root.GetProperty("wouldAccept").GetBoolean());
        Assert.NotEqual(block["hash"]!.GetValue<string>(), root.GetProperty("recalculatedHash").GetString());
    }

    [Fact]
    public async Task Lab_ValidaEntrada_E_ResetVoltaAoNormal()
    {
        using var node = await NodeServer.StartAsync(difficulty: 2);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(node, HttpMethod.Put, "/lab/partition", new { group = "a b" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(node, HttpMethod.Put, "/lab/delay", new { ms = -1 })).StatusCode);

        await SendJsonAsync(node, HttpMethod.Put, "/lab/partition", new { group = "B" });
        await SendJsonAsync(node, HttpMethod.Put, "/lab/delay", new { ms = 3000 });
        using (var state = await GetJsonAsync(node, "/lab"))
        {
            Assert.Equal("B", state.RootElement.GetProperty("partition").GetString());
            Assert.Equal(3000, state.RootElement.GetProperty("delayMs").GetInt32());
        }

        await node.Client.DeleteAsync("/lab");
        using var reset = await GetJsonAsync(node, "/lab");
        Assert.Equal(JsonValueKind.Null, reset.RootElement.GetProperty("partition").ValueKind);
        Assert.Equal(0, reset.RootElement.GetProperty("delayMs").GetInt32());
    }

    /// <summary>Uma requisição "como um peer faria" — com os cabeçalhos do PeerHeadersHandler.</summary>
    private static Task<HttpResponseMessage> GetAsPeerAsync(NodeServer node, string path, string? group)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Satus-Peer", "1");
        if (group is not null)
            request.Headers.Add("X-Satus-Partition", group);
        return node.Client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendAsync(NodeServer node, HttpMethod method, string path, object body) =>
        node.Client.SendAsync(new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, WebJson), Encoding.UTF8, "application/json")
        });

    private static async Task<JsonDocument> SendJsonAsync(NodeServer node, HttpMethod method, string path, object body)
    {
        var response = await SendAsync(node, method, path, body);
        Assert.True(response.IsSuccessStatusCode, $"{method} {path}: {(int)response.StatusCode}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonDocument> SyncAsync(NodeServer node)
    {
        var response = await node.Client.PostAsync("/sync", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task MineAsync(NodeServer node)
    {
        var response = await node.Client.PostAsync("/blocks/mine", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task PostTransactionAsync(NodeServer node, Transaction transaction)
    {
        var response = await node.Client.PostAsync("/transactions", new StringContent(
            JsonSerializer.Serialize(transaction, WebJson), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<JsonDocument> GetJsonAsync(NodeServer node, string path)
    {
        var response = await node.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string[]> HashesAsync(NodeServer node)
    {
        using var chain = await GetJsonAsync(node, "/chain");
        return [.. chain.RootElement.EnumerateArray().Select(block => block.GetProperty("hash").GetString()!)];
    }
}

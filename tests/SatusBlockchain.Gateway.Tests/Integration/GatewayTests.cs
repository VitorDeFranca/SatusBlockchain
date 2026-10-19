using System.Net;
using System.Text;
using System.Text.Json;
using SatusBlockchain.Node.Tests.Integration;

namespace SatusBlockchain.Gateway.Tests.Integration;

/// <summary>
/// O gateway de ponta a ponta contra NÓS REAIS (processos do nó, como nos testes do nó):
/// o snapshot reflete a rede de verdade, as operações chegam ao nó e voltam como ele
/// respondeu, e um nó que morre aparece como inacessível.
/// </summary>
public class GatewayTests
{
    [Fact]
    public async Task Snapshot_DescobreAPartirDeUmNo_E_MinerarPeloGatewayPropaga()
    {
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2, peers: [nodeA.BaseAddress]);

        // O gateway só conhece o node-b: o node-a vem da lista de peers dele.
        using var gateway = await GatewayServer.StartAsync(nodeB.BaseAddress);

        using (var before = await NetworkAsync(gateway))
        {
            Assert.Equal(2, before.RootElement.GetProperty("nodes").GetArrayLength());
            var b = Node(before, "node-b");
            Assert.Equal("node-a", b.GetProperty("peers")[0].GetProperty("nodeId").GetString());
        }

        // Minerar no node-b pelo gateway: o push do nó leva o bloco ao node-a.
        var mined = await gateway.Client.PostAsync("/api/nodes/node-b/mine", content: null);
        Assert.Equal(HttpStatusCode.OK, mined.StatusCode);
        using (var block = JsonDocument.Parse(await mined.Content.ReadAsStringAsync()))
            Assert.Equal(1, block.RootElement.GetProperty("index").GetInt32()); // o corpo é o bloco, cru

        using var after = await NetworkAsync(gateway);
        var summary = after.RootElement.GetProperty("summary");
        Assert.True(summary.GetProperty("converged").GetBoolean());
        Assert.Equal(2, summary.GetProperty("maxLength").GetInt32());
        Assert.Equal("ok", Node(after, "node-a").GetProperty("status").GetString());
    }

    [Fact]
    public async Task NoAtrasado_SincronizadoPeloGateway_VoltaParaACadeiaLider()
    {
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        await nodeA.Client.PostAsync("/blocks/mine", content: null);
        await nodeA.Client.PostAsync("/blocks/mine", content: null);
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2, peers: [nodeA.BaseAddress]);
        using var gateway = await GatewayServer.StartAsync(nodeA.BaseAddress, nodeB.BaseAddress);

        using (var before = await NetworkAsync(gateway))
            Assert.Equal("behind", Node(before, "node-b").GetProperty("status").GetString());

        var sync = await gateway.Client.PostAsync("/api/nodes/node-b/sync", content: null);
        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
        using (var report = JsonDocument.Parse(await sync.Content.ReadAsStringAsync()))
            Assert.Equal(3, report.RootElement.GetProperty("length").GetInt32()); // o relatório do nó, intacto

        using var after = await NetworkAsync(gateway);
        Assert.Equal("ok", Node(after, "node-b").GetProperty("status").GetString());
        Assert.True(after.RootElement.GetProperty("summary").GetProperty("converged").GetBoolean());
    }

    [Fact]
    public async Task NoQueMorre_ApareceInacessivel_SemDerrubarOSnapshot()
    {
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2);
        using var gateway = await GatewayServer.StartAsync(nodeA.BaseAddress, nodeB.BaseAddress);

        nodeB.Dispose(); // queda

        using var snapshot = await NetworkAsync(gateway);
        var dead = snapshot.RootElement.GetProperty("nodes").EnumerateArray()
            .Single(node => node.GetProperty("url").GetString() == nodeB.BaseAddress);
        Assert.Equal("unreachable", dead.GetProperty("state").GetString());
        Assert.Equal(1, snapshot.RootElement.GetProperty("summary").GetProperty("reachable").GetInt32());
    }

    [Fact]
    public async Task Operacoes_PreservamOStatusDoNo_E_NoDesconhecidoE404()
    {
        using var node = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        using var gateway = await GatewayServer.StartAsync(node.BaseAddress);
        (await NetworkAsync(gateway)).Dispose();

        var valid = await PostJsonAsync(gateway, "/api/nodes/node-a/transactions", new { from = "alice", to = "bob", amount = 10 });
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);

        // A validação é a do nó, e a mensagem dele chega à tela como está.
        var invalid = await PostJsonAsync(gateway, "/api/nodes/node-a/transactions", new { from = "", to = "bob", amount = 10 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("From", await invalid.Content.ReadAsStringAsync());

        using (var pending = JsonDocument.Parse(await gateway.Client.GetStringAsync("/api/nodes/node-a/pending")))
            Assert.Equal(1, pending.RootElement.GetArrayLength());

        await gateway.Client.PostAsync("/api/nodes/node-a/mine", content: null);
        using (var blocks = JsonDocument.Parse(await gateway.Client.GetStringAsync("/api/nodes/node-a/chain?last=1")))
            Assert.Equal(1, Assert.Single(blocks.RootElement.EnumerateArray()).GetProperty("index").GetInt32());

        var unknown = await gateway.Client.PostAsync("/api/nodes/nao-existe/mine", content: null);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task SemDocker_CapacidadesDizemQueNaoGerencia_E_CriarE501()
    {
        using var node = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        using var gateway = await GatewayServer.StartAsync(node.BaseAddress);

        using (var capabilities = JsonDocument.Parse(await gateway.Client.GetStringAsync("/api/capabilities")))
        {
            Assert.Equal("none", capabilities.RootElement.GetProperty("runtime").GetString());
            Assert.False(capabilities.RootElement.GetProperty("canManageNodes").GetBoolean());
            Assert.StartsWith("http", capabilities.RootElement.GetProperty("links").GetProperty("grafana").GetString());
        }

        var create = await PostJsonAsync(gateway, "/api/nodes", new { });
        Assert.Equal(HttpStatusCode.NotImplemented, create.StatusCode);
    }

    [Fact]
    public async Task Laboratorio_ParticaoGeraFork_ResetMaisSyncAllConverge()
    {
        // Etapa 12, de ponta a ponta: o roteiro de fork do console, com nós reais.
        using var nodeA = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        using var nodeB = await NodeServer.StartAsync(nodeId: "node-b", difficulty: 2, peers: [nodeA.BaseAddress]);
        await nodeA.Client.PostAsync("/peers", Json(new { url = nodeB.BaseAddress })); // A e B se listam
        using var gateway = await GatewayServer.StartAsync(nodeA.BaseAddress);
        (await NetworkAsync(gateway)).Dispose();

        // 1. Isolar o node-b.
        var partition = await gateway.Client.PutAsync("/api/lab/partition",
            Json(new { groups = new Dictionary<string, string> { ["node-b"] = "B" } }));
        Assert.Equal(HttpStatusCode.OK, partition.StatusCode);

        // 2. Minerar dos dois lados (o console opera os dois: ele não é peer).
        Assert.Equal(HttpStatusCode.Created,
            (await PostJsonAsync(gateway, "/api/nodes/node-b/transactions", new { from = "carol", to = "dave", amount = 7 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gateway.Client.PostAsync("/api/nodes/node-b/mine", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gateway.Client.PostAsync("/api/nodes/node-a/mine", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gateway.Client.PostAsync("/api/nodes/node-a/mine", null)).StatusCode);

        // 3. O snapshot mostra o fork (cadeia mais curta com o topo fora da líder) e a partição.
        using (var forked = await NetworkAsync(gateway))
        {
            var b = Node(forked, "node-b");
            Assert.Equal("diverged", b.GetProperty("status").GetString());
            Assert.Equal("B", b.GetProperty("lab").GetProperty("partition").GetString());
            Assert.False(forked.RootElement.GetProperty("summary").GetProperty("converged").GetBoolean());
        }

        // O explorador lê as duas cadeias inteiras: mesmo genesis, blocos 1 diferentes.
        using (var chainA = JsonDocument.Parse(await gateway.Client.GetStringAsync("/api/nodes/node-a/chain")))
        using (var chainB = JsonDocument.Parse(await gateway.Client.GetStringAsync("/api/nodes/node-b/chain")))
        {
            Assert.Equal(3, chainA.RootElement.GetArrayLength());
            Assert.Equal(2, chainB.RootElement.GetArrayLength());
            Assert.NotEqual(chainA.RootElement[1].GetProperty("hash").GetString(), chainB.RootElement[1].GetProperty("hash").GetString());
        }

        // 4. Curar e sincronizar todos: a cadeia mais longa vence, o bloco do B vira órfão.
        Assert.Equal(HttpStatusCode.OK, (await gateway.Client.DeleteAsync("/api/lab")).StatusCode);
        var syncAll = await gateway.Client.PostAsync("/api/lab/sync-all", null);
        using (var reports = JsonDocument.Parse(await syncAll.Content.ReadAsStringAsync()))
        {
            var b = reports.RootElement.EnumerateArray().Single(r => r.GetProperty("node").GetString() == "node-b");
            Assert.Equal(1, b.GetProperty("body").GetProperty("orphansBackToMempool").GetInt32());
        }

        using var converged = await NetworkAsync(gateway);
        Assert.True(converged.RootElement.GetProperty("summary").GetProperty("converged").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Node(converged, "node-b").GetProperty("lab").GetProperty("partition").ValueKind);
    }

    [Fact]
    public async Task Inspect_PeloGateway_MostraOBlocoAdulterado()
    {
        using var node = await NodeServer.StartAsync(nodeId: "node-a", difficulty: 2);
        using var gateway = await GatewayServer.StartAsync(node.BaseAddress);
        (await NetworkAsync(gateway)).Dispose();
        await PostJsonAsync(gateway, "/api/nodes/node-a/transactions", new { from = "alice", to = "bob", amount = 10 });
        await gateway.Client.PostAsync("/api/nodes/node-a/mine", null);

        using var chain = JsonDocument.Parse(await gateway.Client.GetStringAsync("/api/nodes/node-a/chain"));
        var tampered = chain.RootElement[1].GetRawText().Replace("\"amount\":10", "\"amount\":999");

        var inspect = await gateway.Client.PostAsync("/api/nodes/node-a/blocks/inspect",
            new StringContent(tampered, Encoding.UTF8, "application/json"));
        using var verdict = JsonDocument.Parse(await inspect.Content.ReadAsStringAsync());
        Assert.False(verdict.RootElement.GetProperty("hashMatches").GetBoolean());

        // Entregue como um peer faria, o bloco adulterado é recusado.
        var receive = await gateway.Client.PostAsync("/api/nodes/node-a/blocks/receive",
            new StringContent(tampered, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Conflict, receive.StatusCode);
    }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> NetworkAsync(GatewayServer gateway)
    {
        var response = await gateway.Client.GetAsync("/api/network");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static JsonElement Node(JsonDocument snapshot, string id) =>
        snapshot.RootElement.GetProperty("nodes").EnumerateArray().Single(node => node.GetProperty("id").GetString() == id);

    private static Task<HttpResponseMessage> PostJsonAsync(GatewayServer gateway, string path, object body) =>
        gateway.Client.PostAsync(path, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
}

using SatusBlockchain.Gateway.Network;
using SatusBlockchain.Gateway.Runtime;

namespace SatusBlockchain.Gateway.Tests.Unit;

/// <summary>
/// O snapshot da rede: descoberta (plataforma + lista + peers), probe em paralelo e status —
/// sobre nós simulados, sem rede e sem processo.
/// </summary>
public class NetworkServiceTests
{
    [Fact]
    public async Task RedeEmDia_TodosOk_EConvergida_EmOrdemNatural()
    {
        var nodes = new FakeNetwork(
            new FakeNode("http://n10:8080", "node10", Length: 3, LatestHash: "x"),
            new FakeNode("http://n2:8080", "node2", Length: 3, LatestHash: "x", Pending: 2),
            new FakeNode("http://n1:8080", "node1", Length: 3, LatestHash: "x"));
        var network = TestGateway.Network(nodes, staticNodes: ["http://n10:8080", "http://n2:8080", "http://n1:8080"]);

        var snapshot = await network.GetSnapshotAsync();

        Assert.Equal(new[] { "node1", "node2", "node10" }, snapshot.Nodes.Select(n => n.Id)); // node2 antes de node10
        Assert.All(snapshot.Nodes, node => Assert.Equal(NodeStatus.Ok, node.Status));
        Assert.Equal(new NetworkSummary(Total: 3, Reachable: 3, MaxLength: 3, Converged: true), snapshot.Summary);
        Assert.Equal("static", snapshot.Mode);
        Assert.Equal(2, snapshot.Nodes.Single(n => n.Id == "node2").Mempool);
    }

    [Fact]
    public async Task NoAtrasado_EFork_AparecemNoStatus()
    {
        var nodes = new FakeNetwork(
            new FakeNode("http://a:1", "a", Length: 3, LatestHash: "x"),
            new FakeNode("http://b:1", "b", Length: 3, LatestHash: "x"),
            new FakeNode("http://c:1", "c", Length: 3, LatestHash: "y"),
            new FakeNode("http://d:1", "d", Length: 1, LatestHash: "g"));
        var network = TestGateway.Network(nodes, staticNodes: ["http://a:1", "http://b:1", "http://c:1", "http://d:1"]);

        var snapshot = await network.GetSnapshotAsync();

        Assert.Equal(NodeStatus.Diverged, Status(snapshot, "c"));
        Assert.Equal(NodeStatus.Behind, Status(snapshot, "d"));
        Assert.False(snapshot.Summary.Converged);
    }

    [Fact]
    public async Task NoForaDoAr_NaoDerrubaOSnapshot()
    {
        var nodes = new FakeNetwork(
            new FakeNode("http://a:1", "a", Length: 2, LatestHash: "x"),
            new FakeNode("http://b:1", "b", Down: true));
        var network = TestGateway.Network(nodes, staticNodes: ["http://a:1", "http://b:1"]);

        var snapshot = await network.GetSnapshotAsync();

        var down = snapshot.Nodes.Single(n => n.Url == "http://b:1");
        Assert.Equal(NodeState.Unreachable, down.State);
        Assert.Equal(NodeStatus.Unknown, down.Status);
        Assert.Null(down.Chain);

        // "Convergida" fala de quem respondeu; quem está fora aparece à parte (Reachable < Total).
        Assert.Equal(new NetworkSummary(Total: 2, Reachable: 1, MaxLength: 2, Converged: true), snapshot.Summary);
    }

    [Fact]
    public async Task CadeiaMaisCurta_ForkOuAtraso_PelaCadeiaDaLider()
    {
        // Etapa 12: "c" minerou num ramo próprio (topo y1, fora da líder); "d" só ficou para trás.
        string[] leader = ["g", "x1", "x2"];
        var nodes = new FakeNetwork(
            new FakeNode("http://a:1", "a", Length: 3, LatestHash: "x2", Chain: leader),
            new FakeNode("http://b:1", "b", Length: 3, LatestHash: "x2", Chain: leader),
            new FakeNode("http://c:1", "c", Length: 2, LatestHash: "y1"),
            new FakeNode("http://d:1", "d", Length: 2, LatestHash: "x1"));
        var network = TestGateway.Network(nodes, staticNodes: ["http://a:1", "http://b:1", "http://c:1", "http://d:1"]);

        var snapshot = await network.GetSnapshotAsync();

        Assert.Equal(NodeStatus.Diverged, snapshot.Nodes.Single(n => n.Id == "c").Status);
        Assert.Equal(NodeStatus.Behind, snapshot.Nodes.Single(n => n.Id == "d").Status);
        Assert.Equal(1, nodes.Handler.Requests.Count(r => r.Url.EndsWith("/chain"))); // só a líder
    }

    [Fact]
    public async Task RedeEmDia_NaoBuscaCadeiaNenhuma()
    {
        var nodes = new FakeNetwork(
            new FakeNode("http://a:1", "a", Length: 2, LatestHash: "x"),
            new FakeNode("http://b:1", "b", Length: 2, LatestHash: "x"));
        var network = TestGateway.Network(nodes, staticNodes: ["http://a:1", "http://b:1"]);

        await network.GetSnapshotAsync();

        Assert.DoesNotContain(nodes.Handler.Requests, r => r.Url.EndsWith("/chain"));
    }

    [Fact]
    public async Task EstadoDoLaboratorio_VemNoSnapshot()
    {
        var nodes = new FakeNetwork(new FakeNode("http://a:1", "a", Partition: "B", DelayMs: 3000));
        var network = TestGateway.Network(nodes, staticNodes: ["http://a:1"]);

        var snapshot = await network.GetSnapshotAsync();

        Assert.Equal(new LabView("B", 3000), Assert.Single(snapshot.Nodes).Lab);
    }

    [Fact]
    public async Task NoQueCai_MantemNomeEPapelDoUltimoSnapshot()
    {
        // Caído, o nó não diz mais quem é nem se se anuncia — mas continua sendo o mesmo nó.
        var dynamic = new FakeNode("http://b:1", "node-b", Advertise: "http://b:1");
        var nodes = new FakeNetwork(new FakeNode("http://a:1", "node-a", Peers: ["http://b:1"]), dynamic);
        var network = TestGateway.Network(nodes, staticNodes: ["http://a:1"]);
        await network.GetSnapshotAsync();

        nodes.Set(dynamic with { Down = true });
        var snapshot = await network.GetSnapshotAsync();

        var down = snapshot.Nodes.Single(n => n.Url == "http://b:1");
        Assert.Equal(NodeState.Unreachable, down.State);
        Assert.Equal("node-b", down.Id);          // identidade em cache
        Assert.Equal(NodeRole.Dynamic, down.Role); // papel do snapshot anterior
    }

    [Fact]
    public async Task DescobreARedePelasListasDePeers_ApartirDeUmEndereco()
    {
        var nodes = new FakeNetwork(
            new FakeNode("http://a:1", "a", Peers: ["http://b:1"]),
            new FakeNode("http://b:1", "b", Peers: ["http://a:1", "http://c:1"]),
            new FakeNode("http://c:1", "c"));
        var network = TestGateway.Network(nodes, staticNodes: ["http://a:1"]);

        var snapshot = await network.GetSnapshotAsync();

        Assert.Equal(new[] { "a", "b", "c" }, snapshot.Nodes.Select(n => n.Id));

        // A lista de peers vem ligada aos ids conhecidos (a UI desenha as arestas por eles).
        var peersOfB = snapshot.Nodes.Single(n => n.Id == "b").Peers;
        Assert.Equal(new[] { "a", "c" }, peersOfB.Select(p => p.NodeId));
    }

    [Fact]
    public async Task MesmoNoPorDoisEnderecos_ContaUmaVez()
    {
        var node = new FakeNode("http://127.0.0.1:5001", "node-a", Advertise: "http://127.0.0.1:5001");
        var nodes = new FakeNetwork(node);
        nodes.Alias("http://localhost:5001", node);
        var network = TestGateway.Network(nodes, staticNodes: ["http://localhost:5001", "http://127.0.0.1:5001"]);

        var snapshot = await network.GetSnapshotAsync();

        Assert.Single(snapshot.Nodes);
    }

    [Fact]
    public async Task Plataforma_NoParadoNaoEConsultado_EPortaDoHostVemDela()
    {
        var nodes = new FakeNetwork(new FakeNode("http://node1:8080", "node1", Length: 2, LatestHash: "x"));
        var runtime = new FakeRuntime();
        runtime.Nodes.Add(new RuntimeNode("node1", "http://node1:8080", RuntimeNodeState.Running, NodeRole.Seed, 8080));
        runtime.Nodes.Add(new RuntimeNode("node4", "http://node4:8080", RuntimeNodeState.Stopped, NodeRole.Dynamic, 8083));
        var network = TestGateway.Network(nodes, runtime);

        var snapshot = await network.GetSnapshotAsync();

        var stopped = snapshot.Nodes.Single(n => n.Id == "node4");
        Assert.Equal(NodeState.Stopped, stopped.State);
        Assert.Equal(8083, stopped.HostPort);
        Assert.DoesNotContain(nodes.Handler.Requests, request => request.Url.Contains("node4"));

        Assert.Equal(8080, snapshot.Nodes.Single(n => n.Id == "node1").HostPort);
        Assert.Equal("docker", snapshot.Mode);
    }

    [Fact]
    public async Task PlataformaFora_SegueComAListaEstatica()
    {
        // Docker indisponível não pode deixar a tela em branco.
        var nodes = new FakeNetwork(new FakeNode("http://a:1", "a"));
        var runtime = new FakeRuntime { ListFailure = new HttpRequestException("socket do Docker") };
        var network = TestGateway.Network(nodes, runtime, staticNodes: ["http://a:1"]);

        var snapshot = await network.GetSnapshotAsync();

        Assert.Equal("a", Assert.Single(snapshot.Nodes).Id);
    }

    [Fact]
    public async Task Identidade_FicaEmCache_AteVencer()
    {
        // GET / revalida a cadeia inteira no nó: não precisa ser chamado a cada poll.
        var nodes = new FakeNetwork(new FakeNode("http://a:1", "a"));
        var clock = new ManualClock();
        var network = TestGateway.Network(nodes, staticNodes: ["http://a:1"], clock: clock);

        await network.GetSnapshotAsync();
        await network.GetSnapshotAsync();
        Assert.Equal(1, nodes.CountRequests("http://a:1", "/"));

        clock.Now += NodeProbe.IdentityTtl + TimeSpan.FromSeconds(1);
        await network.GetSnapshotAsync();
        Assert.Equal(2, nodes.CountRequests("http://a:1", "/"));
    }

    [Fact]
    public async Task OutroGenesis_Incompativel()
    {
        var nodes = new FakeNetwork(
            new FakeNode("http://a:1", "a", Length: 2, LatestHash: "x"),
            new FakeNode("http://b:1", "b", Length: 2, LatestHash: "x"),
            new FakeNode("http://c:1", "c", Genesis: "outro-universo", Difficulty: 4));
        var network = TestGateway.Network(nodes, staticNodes: ["http://a:1", "http://b:1", "http://c:1"]);

        var snapshot = await network.GetSnapshotAsync();

        var incompatible = snapshot.Nodes.Single(n => n.Id == "c");
        Assert.Equal(NodeStatus.Incompatible, incompatible.Status);
        Assert.Equal(4, incompatible.Difficulty);
    }

    [Fact]
    public async Task FindAsync_UsaOUltimoSnapshot()
    {
        var nodes = new FakeNetwork(new FakeNode("http://a:1", "node-a"));
        var network = TestGateway.Network(nodes, staticNodes: ["http://a:1"]);
        await network.GetSnapshotAsync();
        var before = nodes.Handler.Requests.Count;

        var node = await network.FindAsync("NODE-A");

        Assert.Equal("http://a:1", node!.Url);
        Assert.Equal(before, nodes.Handler.Requests.Count); // nenhum probe novo
        Assert.Null(await network.FindAsync("nao-existe"));
    }

    private static NodeStatus Status(NetworkSnapshot snapshot, string id) =>
        snapshot.Nodes.Single(n => n.Id == id).Status;
}

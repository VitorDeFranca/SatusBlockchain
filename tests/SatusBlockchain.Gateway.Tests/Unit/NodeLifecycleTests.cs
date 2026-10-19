using SatusBlockchain.Gateway.Network;
using SatusBlockchain.Gateway.Nodes;
using SatusBlockchain.Gateway.Runtime;

namespace SatusBlockchain.Gateway.Tests.Unit;

/// <summary>
/// As regras de entrar e sair pelo console (nome, semente, dificuldade, o que pode ser
/// removido) — com plataforma e nós de mentira.
/// </summary>
public class NodeLifecycleTests
{
    [Fact]
    public async Task Create_ProximoNome_SementeNaCadeiaLider_DificuldadeCopiada()
    {
        var (runtime, network) = ThreeSeeds(
            new FakeNode("http://node1:8080", "node1", Length: 3, LatestHash: "x", Difficulty: 3),
            new FakeNode("http://node2:8080", "node2", Length: 3, LatestHash: "x", Difficulty: 3),
            new FakeNode("http://node3:8080", "node3", Length: 3, LatestHash: "x", Difficulty: 3));

        var created = await TestGateway.Lifecycle(runtime, network).CreateAsync(new CreateNodeRequest(null, null));

        Assert.Equal("node4", created.Id);
        Assert.Equal(new NodeSpec("node4", "http://node1:8080", 3, null), runtime.LastSpec);
        Assert.Equal("node1", created.Seed);
    }

    [Fact]
    public async Task Create_NaoUsaComoSementeNoAtrasadoOuForaDoAr()
    {
        var (runtime, network) = ThreeSeeds(
            new FakeNode("http://node1:8080", "node1", Down: true),
            new FakeNode("http://node2:8080", "node2", Length: 1, LatestHash: "g"),
            new FakeNode("http://node3:8080", "node3", Length: 3, LatestHash: "x"));

        var created = await TestGateway.Lifecycle(runtime, network).CreateAsync(new CreateNodeRequest(null, 8090));

        Assert.Equal("node3", created.Seed);
        Assert.Equal(8090, runtime.LastSpec!.HostPort);
    }

    [Fact]
    public async Task Create_SemNenhumNoSaudavel_409()
    {
        var (runtime, network) = ThreeSeeds(
            new FakeNode("http://node1:8080", "node1", Down: true),
            new FakeNode("http://node2:8080", "node2", Down: true),
            new FakeNode("http://node3:8080", "node3", Down: true));

        var error = await Assert.ThrowsAsync<NodeOperationException>(() =>
            TestGateway.Lifecycle(runtime, network).CreateAsync(new CreateNodeRequest(null, null)));

        Assert.Equal(409, error.StatusCode);
        Assert.Empty(runtime.Calls);
    }

    [Theory]
    [InlineData("node2", null, 409)]          // já existe
    [InlineData("meu-no", null, 400)]         // fora do padrão nodeN
    [InlineData(null, 80, 400)]               // porta privilegiada
    public async Task Create_EntradaInvalida(string? name, int? port, int status)
    {
        var (runtime, network) = ThreeSeeds();

        var error = await Assert.ThrowsAsync<NodeOperationException>(() =>
            TestGateway.Lifecycle(runtime, network).CreateAsync(new CreateNodeRequest(name, port)));

        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task Remove_Semente_409()
    {
        var (runtime, network) = ThreeSeeds();

        var error = await Assert.ThrowsAsync<NodeOperationException>(() =>
            TestGateway.Lifecycle(runtime, network).RemoveAsync("node1"));

        Assert.Equal(409, error.StatusCode);
        Assert.Empty(runtime.Calls);
    }

    [Fact]
    public async Task Remove_NoExtra_DespedeSeAntesDeSumir()
    {
        var (runtime, network) = ThreeSeeds();
        runtime.Nodes.Add(new RuntimeNode("node4", "http://node4:8080", RuntimeNodeState.Running, NodeRole.Dynamic, 8083));

        await TestGateway.Lifecycle(runtime, network).RemoveAsync("node4");

        Assert.Equal(new[] { "stop node4", "remove node4" }, runtime.Calls);
    }

    [Theory]
    [InlineData(null, "stop node2")]
    [InlineData("graceful", "stop node2")]
    [InlineData("crash", "kill node2")]
    public async Task Stop_ModoDespedidaOuQueda(string? mode, string expected)
    {
        var (runtime, network) = ThreeSeeds();

        await TestGateway.Lifecycle(runtime, network).StopAsync("node2", mode);

        Assert.Equal(expected, Assert.Single(runtime.Calls));
    }

    [Fact]
    public async Task Stop_ModoInvalido_400()
    {
        var (runtime, network) = ThreeSeeds();

        var error = await Assert.ThrowsAsync<NodeOperationException>(() =>
            TestGateway.Lifecycle(runtime, network).StopAsync("node2", "explodir"));

        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task SemDocker_501()
    {
        var network = TestGateway.Network(new FakeNetwork());

        var error = await Assert.ThrowsAsync<NodeOperationException>(() =>
            TestGateway.Lifecycle(new NullNodeRuntime(), network).CreateAsync(new CreateNodeRequest(null, null)));

        Assert.Equal(501, error.StatusCode);
    }

    [Fact]
    public void NextName_UmAMaisQueOMaiorEmUso() =>
        Assert.Equal("node11", NodeLifecycle.NextName(["node1", "node2", "node10", "console", "jaeger"]));

    /// <summary>A topologia do compose: node1..node3, sementes, de pé e (por padrão) em dia.</summary>
    private static (FakeRuntime Runtime, NetworkService Network) ThreeSeeds(params FakeNode[] nodes)
    {
        if (nodes.Length == 0)
        {
            nodes =
            [
                new FakeNode("http://node1:8080", "node1"),
                new FakeNode("http://node2:8080", "node2"),
                new FakeNode("http://node3:8080", "node3")
            ];
        }

        var runtime = new FakeRuntime();
        foreach (var node in nodes)
        {
            runtime.Nodes.Add(new RuntimeNode(node.Id, node.Url, RuntimeNodeState.Running, NodeRole.Seed,
                8079 + int.Parse(node.Id[4..])));
        }

        return (runtime, TestGateway.Network(new FakeNetwork(nodes), runtime));
    }
}

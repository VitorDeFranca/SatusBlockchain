using System.Text.Json;

namespace SatusBlockchain.Gateway.Tests.Unit;

/// <summary>Ações do laboratório sobre a rede inteira (etapa 12), contra nós simulados.</summary>
public class LabServiceTests
{
    private static FakeNetwork Three() => new(
        new FakeNode("http://node1:8080", "node1"),
        new FakeNode("http://node2:8080", "node2"),
        new FakeNode("http://node3:8080", "node3"));

    private static readonly string[] Urls = ["http://node1:8080", "http://node2:8080", "http://node3:8080"];

    [Fact]
    public async Task Particao_CadaNoRecebeOGrupoDoMapa_EOsDemaisSaem()
    {
        var nodes = Three();
        var network = TestGateway.Network(nodes, staticNodes: Urls);

        var results = await TestGateway.Lab(nodes, network).PartitionAsync(new Dictionary<string, string?> { ["NODE3"] = "B" });

        Assert.All(results, result => Assert.Equal(200, result.Status));
        Assert.Equal("B", GroupSentTo(nodes, "node3"));      // id sem diferenciar maiúsculas
        Assert.Null(GroupSentTo(nodes, "node1"));            // fora do mapa = sem partição
        Assert.Null(GroupSentTo(nodes, "node2"));
    }

    [Fact]
    public async Task Particao_NoQueNaoEstaRodando_VemComo404()
    {
        var nodes = Three();
        var network = TestGateway.Network(nodes, staticNodes: Urls);

        var results = await TestGateway.Lab(nodes, network).PartitionAsync(new Dictionary<string, string?> { ["node9"] = "B" });

        Assert.Equal(404, results.Single(r => r.Node == "node9").Status);
    }

    [Fact]
    public async Task Reset_TiraAsFalhasDeTodos()
    {
        var nodes = Three();
        var network = TestGateway.Network(nodes, staticNodes: Urls);

        var results = await TestGateway.Lab(nodes, network).ResetAsync();

        Assert.Equal(3, results.Count);
        Assert.Equal(3, nodes.Handler.Requests.Count(r => r.Method == "DELETE" && r.Url.EndsWith("/lab")));
    }

    [Fact]
    public async Task SyncAll_UmDeCadaVez_EmOrdem_ComORelatorioDeCadaNo()
    {
        var nodes = Three();
        var network = TestGateway.Network(nodes, staticNodes: Urls);

        var results = await TestGateway.Lab(nodes, network).SyncAllAsync();

        Assert.Equal(new[] { "node1", "node2", "node3" }, results.Select(r => r.Node));
        Assert.Equal(new[] { "http://node1:8080/sync", "http://node2:8080/sync", "http://node3:8080/sync" },
            nodes.Handler.Requests.Where(r => r.Method == "POST").Select(r => r.Url));
        Assert.Equal("node2", results[1].Body!.Value.GetProperty("node").GetString());
    }

    [Fact]
    public async Task NoForaDoAr_NaoImpedeOsOutros()
    {
        var nodes = Three();
        var network = TestGateway.Network(nodes, staticNodes: Urls);
        await network.GetSnapshotAsync();
        nodes.Set(new FakeNode("http://node2:8080", "node2", Down: true));

        var results = await TestGateway.Lab(nodes, network).SyncAllAsync();

        // node2 caiu: nem entra na lista (só os que estão rodando são sincronizados).
        Assert.Equal(new[] { "node1", "node3" }, results.Select(r => r.Node));
    }

    /// <summary>O grupo que o gateway mandou no PUT /lab/partition do nó.</summary>
    private static string? GroupSentTo(FakeNetwork nodes, string id)
    {
        var body = nodes.Handler.Requests.Single(r => r.Method == "PUT" && r.Url == $"http://{id}:8080/lab/partition").Body;
        using var json = JsonDocument.Parse(body);
        var group = json.RootElement.GetProperty("group");
        return group.ValueKind == JsonValueKind.Null ? null : group.GetString();
    }
}

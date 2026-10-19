using System.Net;
using System.Text.Json;
using SatusBlockchain.Gateway.Nodes;
using SatusBlockchain.Gateway.Runtime;

namespace SatusBlockchain.Gateway.Tests.Unit;

/// <summary>
/// O lado Docker do console, contra uma Engine API de mentira: o que é listado como nó, o que
/// vai no corpo do <c>containers/create</c> e quais chamadas cada ação faz. É a cobertura
/// possível sem Docker na máquina de desenvolvimento — o teste com o Docker real é manual.
/// </summary>
public class DockerNodeRuntimeTests
{
    private const string Network = "satusblockchain_satus-net";

    [Fact]
    public async Task List_SoNosDoProjeto_ComPapelEstadoEPorta()
    {
        var docker = Docker(Containers(
            Container("aaa", "node1", "running", "node1", hostPort: 8080),
            Container("bbb", "node4", "exited", "node-extra"),
            Container("ccc", "jaeger", "running", "jaeger")));

        var nodes = await new DockerNodeRuntime(Client(docker)).ListAsync();

        Assert.Equal(2, nodes.Count); // o Jaeger tem a label do projeto, mas não é nó
        Assert.Equal(new RuntimeNode("node1", "http://node1:8080", RuntimeNodeState.Running, NodeRole.Seed, 8080), nodes[0]);
        Assert.Equal(new RuntimeNode("node4", "http://node4:8080", RuntimeNodeState.Stopped, NodeRole.Dynamic, null), nodes[1]);

        var list = Uri.UnescapeDataString(docker.Requests[0].Url);
        Assert.Contains("all=true", list);
        Assert.Contains("com.docker.compose.project=satusblockchain", list);
    }

    [Fact]
    public async Task Create_CopiaImagemERedeDeUmNo_ELabelsDoMoldeNodeExtra()
    {
        var docker = Docker(
            Containers(
                Container("aaa", "node1", "running", "node1", hostPort: 8080),
                Container("ddd", "node4", "running", "node-extra", hostPort: 8083),
                Container("ccc", "jaeger", "running", "jaeger")),
            create: _ => StubHandler.Raw("""{"Id":"new-id"}""", HttpStatusCode.Created));

        var created = await new DockerNodeRuntime(Client(docker))
            .CreateAsync(new NodeSpec("node5", "http://node1:8080", 3, HostPort: null));

        Assert.Equal(new RuntimeNode("node5", "http://node5:8080", RuntimeNodeState.Running, NodeRole.Dynamic, 8084), created);

        var create = docker.Requests.Single(r => r.Url.Contains("containers/create"));
        Assert.EndsWith("containers/create?name=node5", create.Url);

        using var body = JsonDocument.Parse(create.Body);
        var root = body.RootElement;
        Assert.Equal("satus-node:local", root.GetProperty("Image").GetString());

        var env = root.GetProperty("Env").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("NODE_ID=node5", env);
        Assert.Contains("ADVERTISE_URL=http://node5:8080", env);
        Assert.Contains("PEERS=http://node1:8080", env);
        Assert.Contains("DIFFICULTY=3", env);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT=http://jaeger:4317", env); // Jaeger no ar

        // Mesmas labels do `docker compose run` do add-node: Prometheus e Alloy o enxergam.
        var labels = root.GetProperty("Labels");
        Assert.Equal("satusblockchain", labels.GetProperty("com.docker.compose.project").GetString());
        Assert.Equal("node-extra", labels.GetProperty("com.docker.compose.service").GetString());

        Assert.Equal(Network, root.GetProperty("HostConfig").GetProperty("NetworkMode").GetString());
        Assert.Equal("8084", root.GetProperty("HostConfig").GetProperty("PortBindings")
            .GetProperty("8080/tcp")[0].GetProperty("HostPort").GetString()); // 8083 já era do node4

        Assert.Contains(docker.Requests, r => r.Method == "POST" && r.Url.EndsWith("containers/new-id/start"));
    }

    [Fact]
    public async Task Create_SeNaoLiga_ApagaOContainer_EExplica()
    {
        var docker = Docker(
            Containers(Container("aaa", "node1", "running", "node1", hostPort: 8080)),
            create: _ => StubHandler.Raw("""{"Id":"new-id"}""", HttpStatusCode.Created),
            start: _ => StubHandler.Raw("""{"message":"port is already allocated"}""", HttpStatusCode.InternalServerError));

        var error = await Assert.ThrowsAsync<NodeOperationException>(() => new DockerNodeRuntime(Client(docker))
            .CreateAsync(new NodeSpec("node5", "http://node1:8080", 2, HostPort: 9000)));

        Assert.Contains("port is already allocated", error.Message);
        Assert.Contains(docker.Requests, r => r.Method == "DELETE" && r.Url.Contains("containers/new-id"));
    }

    [Fact]
    public async Task Create_NomeJaUsado_409()
    {
        var docker = Docker(Containers(Container("aaa", "node1", "running", "node1")));

        var error = await Assert.ThrowsAsync<NodeOperationException>(() => new DockerNodeRuntime(Client(docker))
            .CreateAsync(new NodeSpec("node1", "http://node1:8080", 2, null)));

        Assert.Equal(409, error.StatusCode);
    }

    [Theory]
    [InlineData(true, "containers/aaa/stop?t=10")]   // SIGTERM: o nó se despede
    [InlineData(false, "containers/aaa/kill")]       // SIGKILL: queda
    public async Task Stop_DespedidaOuQueda(bool graceful, string expected)
    {
        var docker = Docker(Containers(Container("aaa", "node1", "running", "node1")));

        await new DockerNodeRuntime(Client(docker)).StopAsync("node1", graceful);

        Assert.Contains(docker.Requests, r => r.Method == "POST" && r.Url.EndsWith(expected));
    }

    [Fact]
    public async Task Start_ERemove_ChamamAEngineApi()
    {
        var docker = Docker(Containers(Container("bbb", "node4", "exited", "node-extra")));
        var runtime = new DockerNodeRuntime(Client(docker));

        await runtime.StartAsync("node4");
        await runtime.RemoveAsync("node4");

        Assert.Contains(docker.Requests, r => r.Method == "POST" && r.Url.EndsWith("containers/bbb/start"));
        Assert.Contains(docker.Requests, r => r.Method == "DELETE" && r.Url.EndsWith("containers/bbb?force=true"));
    }

    [Fact]
    public async Task ContainerQueNaoENo_NaoEhTocado()
    {
        // Fronteira de segurança: o console só age sobre nodeN do projeto.
        var docker = Docker(Containers(Container("ccc", "jaeger", "running", "jaeger")));

        var error = await Assert.ThrowsAsync<NodeOperationException>(() =>
            new DockerNodeRuntime(Client(docker)).StopAsync("jaeger", graceful: false));

        Assert.Equal(404, error.StatusCode);
        Assert.Single(docker.Requests); // só a listagem
    }

    private static HttpClient Client(StubHandler handler) => new(handler) { BaseAddress = new Uri("http://docker/") };

    /// <summary>Engine API de mentira: listagem fixa; create/start configuráveis; o resto responde 204.</summary>
    private static StubHandler Docker(string containers,
        Func<string, HttpResponseMessage>? create = null, Func<string, HttpResponseMessage>? start = null) =>
        new((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/containers/json")
                return StubHandler.Raw(containers);
            if (path == "/containers/create")
                return create?.Invoke(body) ?? StubHandler.Raw("""{"Id":"new-id"}""", HttpStatusCode.Created);
            if (path.EndsWith("/start") && start is not null)
                return start(body);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

    private static string Containers(params object[] containers) => JsonSerializer.Serialize(containers);

    private static object Container(string id, string name, string state, string service, int? hostPort = null) => new
    {
        Id = id,
        Names = new[] { $"/{name}" },
        Image = "satus-node:local",
        State = state,
        Labels = new Dictionary<string, string>
        {
            ["com.docker.compose.project"] = "satusblockchain",
            ["com.docker.compose.service"] = service
        },
        Ports = hostPort is null
            ? Array.Empty<object>()
            : [
                new { IP = "0.0.0.0", PrivatePort = 8080, PublicPort = hostPort.Value, Type = "tcp" },
                new { IP = "::", PrivatePort = 8080, PublicPort = hostPort.Value, Type = "tcp" }
            ],
        NetworkSettings = new { Networks = new Dictionary<string, object> { [Network] = new { } } }
    };
}

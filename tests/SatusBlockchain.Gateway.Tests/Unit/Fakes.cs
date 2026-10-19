using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SatusBlockchain.Gateway;
using SatusBlockchain.Gateway.Network;
using SatusBlockchain.Gateway.Nodes;
using SatusBlockchain.Gateway.Runtime;

namespace SatusBlockchain.Gateway.Tests.Unit;

/// <summary>
/// <c>HttpMessageHandler</c> de mentira, escrito à mão como no projeto de testes do nó (sem
/// pacote de mock): registra cada requisição e devolve o que o teste mandar.
/// </summary>
internal sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(string Method, string Url, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

        lock (Requests)
            Requests.Add((request.Method.Method, request.RequestUri!.ToString(), body));

        return respond(request, body);
    }

    public static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Raw(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>
/// Um nó simulado: o que ele responderia em <c>/</c>, <c>/peers</c>, <c>/chain/validate</c>, na
/// mempool, em <c>/chain</c> (<paramref name="Chain"/>: os hashes, em ordem) e em <c>/lab</c>.
/// </summary>
internal sealed record FakeNode(
    string Url,
    string Id,
    int Length = 1,
    string LatestHash = "h1",
    bool Valid = true,
    string Genesis = "genesis",
    int Difficulty = 2,
    string[]? Peers = null,
    string? Advertise = null,
    int Pending = 0,
    bool Down = false,
    string[]? Chain = null,
    string? Partition = null,
    int DelayMs = 0);

/// <summary>
/// Uma rede de nós simulados atrás de um <see cref="StubHandler"/>: cada requisição é
/// atendida pelo nó dono do endereço (esquema + host + porta); nó <c>Down</c> recusa conexão.
/// </summary>
internal sealed class FakeNetwork
{
    private readonly Dictionary<string, FakeNode> _byAuthority = new(StringComparer.OrdinalIgnoreCase);

    public FakeNetwork(params FakeNode[] nodes)
    {
        Handler = new StubHandler((request, _) => Respond(request));
        foreach (var node in nodes)
            Set(node);
    }

    public StubHandler Handler { get; }

    /// <summary>Acrescenta ou troca um nó (ex.: ele avançou a cadeia ou caiu).</summary>
    public void Set(FakeNode node) => _byAuthority[new Uri(node.Url).GetLeftPart(UriPartial.Authority)] = node;

    /// <summary>Faz um segundo endereço responder pelo mesmo nó (localhost x 127.0.0.1).</summary>
    public void Alias(string url, FakeNode node) => _byAuthority[new Uri(url).GetLeftPart(UriPartial.Authority)] = node;

    public int CountRequests(string url, string path) =>
        Handler.Requests.Count(request => request.Url == $"{url}{path}");

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        if (!_byAuthority.TryGetValue(uri.GetLeftPart(UriPartial.Authority), out var node) || node.Down)
            throw new HttpRequestException("conexão recusada");

        var lab = new { node = node.Id, partition = node.Partition, delayMs = node.DelayMs };

        return (request.Method.Method, uri.AbsolutePath) switch
        {
            ("GET", "/") => StubHandler.Json(new { node = node.Id, difficulty = node.Difficulty, genesis = node.Genesis, blocks = node.Length, valid = node.Valid }),
            ("GET", "/peers") => StubHandler.Json(new { node = node.Id, url = node.Advertise, peers = node.Peers ?? [] }),
            ("GET", "/chain/validate") => StubHandler.Json(new { valid = node.Valid, length = node.Length, latestHash = node.LatestHash }),
            ("GET", "/transactions/pending") => StubHandler.Json(Enumerable.Range(0, node.Pending)
                .Select(i => new { from = "alice", to = "bob", amount = i + 1 })),
            ("GET", "/chain") => StubHandler.Json((node.Chain ?? DefaultChain(node))
                .Select((hash, index) => new { index, hash })),
            ("GET", "/lab") or ("PUT", "/lab/partition") or ("DELETE", "/lab") => StubHandler.Json(lab),
            ("POST", "/sync") => StubHandler.Json(new
            {
                node = node.Id, previousLength = node.Length, length = node.Length,
                adopted = (object?)null, orphansBackToMempool = 0, peers = Array.Empty<object>()
            }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    /// <summary>
    /// Cadeia padrão com o tamanho do nó: genesis "g", blocos intermediários "b1", "b2"... e o
    /// topo <c>LatestHash</c>. Nós sem <c>Chain</c> explícita compartilham o prefixo — como na
    /// rede real, em que todos partem do mesmo genesis.
    /// </summary>
    private static string[] DefaultChain(FakeNode node)
    {
        if (node.Length <= 1)
            return [node.LatestHash];

        return ["g", .. Enumerable.Range(1, node.Length - 2).Select(i => $"b{i}"), node.LatestHash];
    }
}

/// <summary>Plataforma de mentira: lista o que o teste mandar e registra o que pediram a ela.</summary>
internal sealed class FakeRuntime(string name = "docker", bool canManage = true) : INodeRuntime
{
    public List<RuntimeNode> Nodes { get; } = [];

    public List<string> Calls { get; } = [];

    public Exception? ListFailure { get; set; }

    public NodeSpec? LastSpec { get; private set; }

    public string Name => name;

    public bool CanManage => canManage;

    public Task<IReadOnlyList<RuntimeNode>> ListAsync(CancellationToken cancellationToken = default) =>
        ListFailure is not null
            ? Task.FromException<IReadOnlyList<RuntimeNode>>(ListFailure)
            : Task.FromResult<IReadOnlyList<RuntimeNode>>([.. Nodes]);

    public Task<RuntimeNode> CreateAsync(NodeSpec spec, CancellationToken cancellationToken = default)
    {
        LastSpec = spec;
        Calls.Add($"create {spec.Name}");
        var node = new RuntimeNode(spec.Name, $"http://{spec.Name}:8080", RuntimeNodeState.Running, NodeRole.Dynamic, spec.HostPort);
        Nodes.Add(node);
        return Task.FromResult(node);
    }

    public Task StopAsync(string id, bool graceful, CancellationToken cancellationToken = default)
    {
        Calls.Add($"{(graceful ? "stop" : "kill")} {id}");
        return Task.CompletedTask;
    }

    public Task StartAsync(string id, CancellationToken cancellationToken = default)
    {
        Calls.Add($"start {id}");
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        Calls.Add($"remove {id}");
        return Task.CompletedTask;
    }
}

/// <summary>Relógio controlado pelo teste (para o cache da identidade dos nós).</summary>
internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Monta o <see cref="NetworkService"/> sobre uma rede e uma plataforma de mentira.</summary>
internal static class TestGateway
{
    public static NetworkService Network(FakeNetwork nodes, INodeRuntime? runtime = null,
        IReadOnlyList<string>? staticNodes = null, ManualClock? clock = null)
    {
        clock ??= new ManualClock();
        var options = new GatewayOptions(staticNodes ?? [], RuntimeMode.Auto, GatewayOptions.DefaultDockerSocket,
            new GatewayLinks("g", "j", "p"));

        return new NetworkService(new NodeProbe(new HttpClient(nodes.Handler), clock),
            runtime ?? new NullNodeRuntime(), options, clock, NullLogger<NetworkService>.Instance);
    }

    public static NodeLifecycle Lifecycle(INodeRuntime runtime, NetworkService network) =>
        new(runtime, network, NullLogger<NodeLifecycle>.Instance);

    public static LabService Lab(FakeNetwork nodes, NetworkService network) =>
        new(network, new NodeProxy(new HttpClient(nodes.Handler)));
}

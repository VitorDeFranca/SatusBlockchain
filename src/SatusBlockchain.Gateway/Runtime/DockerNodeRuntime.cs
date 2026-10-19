using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SatusBlockchain.Gateway.Nodes;

namespace SatusBlockchain.Gateway.Runtime;

/// <summary>
/// Nós como containers, pelo Docker Engine API — HTTP pelo socket Unix
/// (<c>/var/run/docker.sock</c>), sem pacote extra: o <c>SocketsHttpHandler</c> só precisa de um
/// <c>ConnectCallback</c> que abra o socket no lugar de uma conexão TCP.
///
/// Fronteira de segurança: o gateway só ENXERGA containers com a label do projeto, só AGE
/// sobre os que se chamam <c>nodeN</c>, e só CRIA a partir de um nó que já existe (mesma
/// imagem, mesma rede) — nenhuma imagem, comando ou volume vem da requisição.
/// </summary>
public sealed partial class DockerNodeRuntime(HttpClient http) : INodeRuntime
{
    /// <summary>Label que o Compose (e o <c>docker compose run</c> do add-node) põe nos containers.</summary>
    public const string ProjectLabel = "com.docker.compose.project=satusblockchain";

    /// <summary>Porta interna dos nós (<c>ASPNETCORE_HTTP_PORTS</c> do Dockerfile do nó).</summary>
    public const int NodePort = 8080;

    /// <summary>Primeira porta do host oferecida a nós novos (8080–8082 são as sementes).</summary>
    public const int FirstExtraHostPort = 8083;

    private static readonly JsonSerializerOptions DockerJson = new()
    {
        // A Engine API usa PascalCase ("Image", "HostConfig") — o padrão do serializador.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Name => "docker";

    public bool CanManage => true;

    /// <summary>
    /// <see cref="HttpClient"/> ligado ao socket do Docker. O host da URL (<c>docker</c>) é só
    /// um nome: quem decide o destino é o <c>ConnectCallback</c>. O timeout cobre o
    /// <c>docker stop</c>, que espera até 10 s o nó se despedir.
    /// </summary>
    public static HttpClient CreateClient(string socketPath) => new(new SocketsHttpHandler
    {
        ConnectCallback = async (_, cancellationToken) =>
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    })
    {
        BaseAddress = new Uri("http://docker/"),
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<IReadOnlyList<RuntimeNode>> ListAsync(CancellationToken cancellationToken = default) =>
        [.. (await ListContainersAsync(cancellationToken)).Where(c => c.IsNode).Select(c => c.ToNode())];

    public async Task<RuntimeNode> CreateAsync(NodeSpec spec, CancellationToken cancellationToken = default)
    {
        if (!NodeName().IsMatch(spec.Name))
            throw new NodeOperationException(StatusCodes.Status400BadRequest, $"Nome inválido: '{spec.Name}' (use nodeN).");

        var containers = await ListContainersAsync(cancellationToken);

        if (containers.Any(c => c.Name == spec.Name))
            throw new NodeOperationException(StatusCodes.Status409Conflict, $"Já existe um container '{spec.Name}'.");

        // Molde: um nó do projeto já em execução. Imagem e rede vêm dele — as mesmas do
        // compose —, então o nó novo enxerga os outros pelo nome, como o add-node.ps1 faz.
        var template = containers
            .Where(c => c.IsNode && c.IsRunning && c.Networks.Count > 0)
            .OrderBy(c => c.Role == NodeRole.Seed ? 0 : 1)
            .FirstOrDefault()
            ?? throw new NodeOperationException(StatusCodes.Status409Conflict,
                "Nenhum nó do projeto em execução para servir de molde (suba os nós com docker compose up).");

        var network = template.Networks[0];
        var hostPort = spec.HostPort ?? NextHostPort(containers);

        var env = new List<string>
        {
            $"NODE_ID={spec.Name}",
            $"ADVERTISE_URL=http://{spec.Name}:{NodePort}",
            $"PEERS={spec.Seed}",
            $"DIFFICULTY={spec.Difficulty}"
        };

        // Com a stack de observabilidade no ar, o nó novo também exporta traces (como o add-node).
        if (containers.Any(c => c.Name == "jaeger" && c.IsRunning))
            env.Add("OTEL_EXPORTER_OTLP_ENDPOINT=http://jaeger:4317");

        var body = new
        {
            Image = template.Image,
            Env = env,
            // As mesmas labels do `docker compose run` do add-node: o Alloy (logs) e o Prometheus
            // (métricas, relabel "node.*") pegam o nó novo sem configuração nenhuma.
            Labels = new Dictionary<string, string>
            {
                ["com.docker.compose.project"] = "satusblockchain",
                ["com.docker.compose.service"] = "node-extra",
                ["com.docker.compose.oneoff"] = "True"
            },
            ExposedPorts = new Dictionary<string, object> { [$"{NodePort}/tcp"] = new { } },
            HostConfig = new
            {
                NetworkMode = network,
                PortBindings = new Dictionary<string, object[]>
                {
                    [$"{NodePort}/tcp"] = [new { HostPort = hostPort.ToString() }]
                }
            },
            NetworkingConfig = new
            {
                EndpointsConfig = new Dictionary<string, object> { [network] = new { Aliases = new[] { spec.Name } } }
            }
        };

        using var created = await http.PostAsJsonAsync(
            $"containers/create?name={Uri.EscapeDataString(spec.Name)}", body, DockerJson, cancellationToken);
        await EnsureAsync(created, "criar o container", cancellationToken);

        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("Id").GetString()!;

        using var started = await http.PostAsync($"containers/{id}/start", content: null, cancellationToken);
        if (!started.IsSuccessStatusCode && started.StatusCode != HttpStatusCode.NotModified)
        {
            // Não deixa um container criado e parado para trás (ex.: porta do host ocupada).
            var reason = await DockerMessageAsync(started, cancellationToken);
            using var _ = await http.DeleteAsync($"containers/{id}?force=true", cancellationToken);
            throw new NodeOperationException(StatusCodes.Status502BadGateway, $"O Docker não ligou o nó: {reason}");
        }

        return new RuntimeNode(spec.Name, $"http://{spec.Name}:{NodePort}", RuntimeNodeState.Running,
            NodeRole.Dynamic, hostPort);
    }

    public async Task StopAsync(string id, bool graceful, CancellationToken cancellationToken = default)
    {
        var container = await FindNodeAsync(id, cancellationToken);

        // stop = SIGTERM e até 10 s de espera: o nó roda o desligamento gracioso e se despede.
        // kill = SIGKILL: o processo some sem despedida — uma queda.
        var path = graceful ? $"containers/{container.Id}/stop?t=10" : $"containers/{container.Id}/kill";

        using var response = await http.PostAsync(path, content: null, cancellationToken);

        // 304 = já estava parado; 409 no kill = não estava rodando. Nos dois casos o pedido
        // ("que ele esteja parado") já está atendido.
        if (response.StatusCode is HttpStatusCode.NotModified or HttpStatusCode.Conflict)
            return;

        await EnsureAsync(response, graceful ? "parar o nó" : "derrubar o nó", cancellationToken);
    }

    public async Task StartAsync(string id, CancellationToken cancellationToken = default)
    {
        var container = await FindNodeAsync(id, cancellationToken);

        using var response = await http.PostAsync($"containers/{container.Id}/start", content: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified)
            return;

        await EnsureAsync(response, "religar o nó", cancellationToken);
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        var container = await FindNodeAsync(id, cancellationToken);

        using var response = await http.DeleteAsync($"containers/{container.Id}?force=true", cancellationToken);
        await EnsureAsync(response, "remover o container", cancellationToken);
    }

    /// <summary>Só age sobre nós do projeto: qualquer outro nome é "não encontrado".</summary>
    private async Task<Container> FindNodeAsync(string id, CancellationToken cancellationToken) =>
        (await ListContainersAsync(cancellationToken)).FirstOrDefault(c => c.IsNode && c.Name == id)
        ?? throw new NodeOperationException(StatusCodes.Status404NotFound, $"Nó '{id}' não encontrado no Docker.");

    private async Task<List<Container>> ListContainersAsync(CancellationToken cancellationToken)
    {
        var filters = Uri.EscapeDataString($$"""{"label":["{{ProjectLabel}}"]}""");

        using var response = await http.GetAsync($"containers/json?all=true&filters={filters}", cancellationToken);
        await EnsureAsync(response, "listar os containers", cancellationToken);

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        return [.. document.RootElement.EnumerateArray().Select(Container.From).OfType<Container>()];
    }

    /// <summary>Próxima porta do host livre entre os containers do projeto (a partir de 8083).</summary>
    private static int NextHostPort(IEnumerable<Container> containers)
    {
        var used = containers.SelectMany(c => c.HostPorts).ToHashSet();
        var port = FirstExtraHostPort;
        while (used.Contains(port))
            port++;
        return port;
    }

    private static async Task EnsureAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var reason = await DockerMessageAsync(response, cancellationToken);
        var status = response.StatusCode switch
        {
            HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
            HttpStatusCode.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status502BadGateway
        };

        throw new NodeOperationException(status, $"O Docker recusou {action}: {reason}");
    }

    /// <summary>Erros da Engine API vêm como <c>{"message": "..."}</c>.</summary>
    private static async Task<string> DockerMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.TryGetProperty("message", out var message))
                return message.GetString() ?? text;
        }
        catch (JsonException)
        {
        }

        return string.IsNullOrWhiteSpace(text) ? $"HTTP {(int)response.StatusCode}" : text;
    }

    /// <summary>O pedaço de <c>GET /containers/json</c> que interessa ao gateway.</summary>
    private sealed record Container(string Id, string Name, string State, string? Service, string Image,
        IReadOnlyList<string> Networks, IReadOnlyList<int> HostPorts)
    {
        /// <summary>Só containers <c>nodeN</c> são nós — Jaeger, Grafana etc. também têm a label do projeto.</summary>
        public bool IsNode => NodeName().IsMatch(Name);

        // "paused" está vivo, mas congelado: conta como de pé — quem descobre que ele não
        // responde é o probe. exited, created e dead são nó parado.
        public bool IsRunning => State is "running" or "restarting" or "paused";

        // Sementes são os serviços node1..nodeN do compose; os criados em execução vêm do
        // molde "node-extra" (add-node.ps1 e o próprio console).
        public NodeRole Role => Service is not null && NodeName().IsMatch(Service) ? NodeRole.Seed : NodeRole.Dynamic;

        public RuntimeNode ToNode() => new(Name, $"http://{Name}:{NodePort}",
            IsRunning ? RuntimeNodeState.Running : RuntimeNodeState.Stopped, Role,
            HostPorts.Count > 0 ? HostPorts[0] : null);

        public static Container? From(JsonElement element)
        {
            if (!element.TryGetProperty("Names", out var names) || names.GetArrayLength() == 0)
                return null;

            var name = names[0].GetString()?.TrimStart('/');
            if (string.IsNullOrEmpty(name))
                return null;

            var service = element.TryGetProperty("Labels", out var labels)
                && labels.ValueKind == JsonValueKind.Object
                && labels.TryGetProperty("com.docker.compose.service", out var serviceLabel)
                    ? serviceLabel.GetString()
                    : null;

            var networks = element.TryGetProperty("NetworkSettings", out var settings)
                && settings.TryGetProperty("Networks", out var networkMap)
                && networkMap.ValueKind == JsonValueKind.Object
                    ? networkMap.EnumerateObject().Select(network => network.Name).ToList()
                    : [];

            // Porta do host ligada à porta interna do nó (IPv4 e IPv6 repetem a mesma).
            var hostPorts = element.TryGetProperty("Ports", out var ports) && ports.ValueKind == JsonValueKind.Array
                ? ports.EnumerateArray()
                    .Where(port => port.TryGetProperty("PublicPort", out _)
                        && port.TryGetProperty("PrivatePort", out var privatePort)
                        && privatePort.GetInt32() == NodePort)
                    .Select(port => port.GetProperty("PublicPort").GetInt32())
                    .Distinct()
                    .ToList()
                : [];

            return new Container(
                element.TryGetProperty("Id", out var id) ? id.GetString() ?? name : name,
                name,
                element.TryGetProperty("State", out var state) ? state.GetString() ?? "" : "",
                service,
                element.TryGetProperty("Image", out var image) ? image.GetString() ?? "" : "",
                networks,
                hostPorts);
        }
    }

    [GeneratedRegex(@"^node\d+$")]
    private static partial Regex NodeName();
}

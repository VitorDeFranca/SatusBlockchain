namespace SatusBlockchain.Gateway;

/// <summary>Como o gateway descobre (e gerencia) os nós.</summary>
public enum RuntimeMode
{
    /// <summary>Docker se o socket existir; senão, só a lista estática (<c>GATEWAY_NODES</c>).</summary>
    Auto,

    /// <summary>Exige o Docker: sem o socket, o gateway falha na subida.</summary>
    Docker,

    /// <summary>Não usa Docker: só <c>GATEWAY_NODES</c> + a descoberta pelas listas de peers.</summary>
    None
}

/// <summary>Endereços das ferramentas de observabilidade, abertos pelos links do console.</summary>
public sealed record GatewayLinks(string Grafana, string Jaeger, string Prometheus);

/// <summary>
/// Configuração do gateway, lida do ambiente. Mesma filosofia do <c>NodeOptions</c> do nó:
/// valor inválido derruba o processo na subida, com mensagem clara.
/// </summary>
/// <param name="StaticNodes">URLs de nós conhecidas de antemão (<c>GATEWAY_NODES</c>).</param>
/// <param name="Runtime">De onde vem a lista de nós (<c>RUNTIME</c>).</param>
/// <param name="DockerSocket">Caminho do socket do Docker (<c>DOCKER_SOCKET</c>).</param>
public record GatewayOptions(IReadOnlyList<string> StaticNodes, RuntimeMode Runtime, string DockerSocket,
    GatewayLinks Links)
{
    public const string DefaultDockerSocket = "/var/run/docker.sock";

    public static GatewayOptions FromConfiguration(IConfiguration configuration) => new(
        ParseNodes(configuration["GATEWAY_NODES"]),
        ParseRuntime(configuration["RUNTIME"]),
        Or(configuration["DOCKER_SOCKET"], DefaultDockerSocket),
        new GatewayLinks(
            Or(configuration["GRAFANA_URL"], "http://localhost:3000"),
            Or(configuration["JAEGER_URL"], "http://localhost:16686"),
            Or(configuration["PROMETHEUS_URL"], "http://localhost:9090")));

    /// <summary>
    /// <c>GATEWAY_NODES</c>: URLs separadas por vírgula, ex.: <c>http://localhost:5165,http://localhost:5166</c>.
    /// Basta um nó — os outros são descobertos pelas listas de peers dele.
    /// </summary>
    public static IReadOnlyList<string> ParseNodes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var nodes = new List<string>();

        foreach (var entry in value.Split(','))
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            if (!TryNormalizeUrl(entry, out var url))
            {
                throw new InvalidOperationException(
                    $"GATEWAY_NODES inválida: '{entry}'. Informe URLs absolutas separadas por vírgula " +
                    "(ex.: http://localhost:5165,http://localhost:5166).");
            }

            if (!nodes.Contains(url, StringComparer.OrdinalIgnoreCase))
                nodes.Add(url);
        }

        return nodes;
    }

    public static RuntimeMode ParseRuntime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return RuntimeMode.Auto;

        return value.Trim().ToLowerInvariant() switch
        {
            "auto" => RuntimeMode.Auto,
            "docker" => RuntimeMode.Docker,
            "none" => RuntimeMode.None,
            _ => throw new InvalidOperationException($"RUNTIME inválido: '{value}'. Use auto, docker ou none.")
        };
    }

    /// <summary>
    /// Mesma regra de endereço de nó do <c>NodeOptions.TryNormalizeUrl</c> (URL http(s)
    /// absoluta, sem espaços nem barra final): é por este texto que o gateway reconhece que
    /// duas URLs são o mesmo nó.
    /// </summary>
    public static bool TryNormalizeUrl(string? value, out string url)
    {
        url = value?.Trim().TrimEnd('/') ?? string.Empty;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node;

/// <summary>
/// Configuração do nó, lida do ambiente (variáveis de ambiente / .env / Docker).
/// A validação acontece na inicialização: um valor inválido derruba o nó na hora,
/// com mensagem clara, em vez de deixá-lo minerando para sempre.
/// </summary>
/// <param name="AdvertiseUrl">
/// Endereço pelo qual os OUTROS nós chamam este (etapa 10) — ex.: <c>http://node4:8080</c>.
/// Opcional: sem ele o nó não se anuncia nem se despede (fica como nas etapas 6–9), mas
/// continua aceitando o registro de nós que se anunciam a ele.
/// </param>
public record NodeOptions(string NodeId, byte Difficulty, IReadOnlyList<string> Peers,
    string? AdvertiseUrl = null)
{
    public const string DefaultNodeId = "node-local";

    public static NodeOptions FromConfiguration(IConfiguration configuration) => new(
        configuration["NODE_ID"] ?? DefaultNodeId,
        ParseDifficulty(configuration["DIFFICULTY"]),
        ParsePeers(configuration["PEERS"]),
        ParseAdvertiseUrl(configuration["ADVERTISE_URL"]));

    public static byte ParseDifficulty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ProofOfWork.DefaultDifficulty;

        if (!byte.TryParse(value, out var difficulty)
            || difficulty > ProofOfWork.MaxDifficulty)
        {
            throw new InvalidOperationException(
                $"DIFFICULTY inválida: '{value}'. Informe um número inteiro entre 0 e {ProofOfWork.MaxDifficulty}.");
        }

        return difficulty;
    }

    /// <summary>
    /// Peers iniciais (PEERS), separados por vírgula — ex.:
    /// <c>http://node2:8080,http://node3:8080</c>. Desde a etapa 10 são as <b>sementes</b>
    /// do <see cref="Networking.PeerRegistry"/>: a lista com que o nó nasce, que cresce e
    /// encolhe durante a execução conforme nós entram e saem.
    ///
    /// Normaliza o que o operador escreveu (espaços, barra final, repetições) e falha
    /// na inicialização se alguma entrada não for URL http(s) absoluta: mesma filosofia
    /// do <see cref="ParseDifficulty"/> — erro de configuração aparece na subida, e não
    /// como um push que "silenciosamente" nunca sai.
    /// </summary>
    public static IReadOnlyList<string> ParsePeers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var peers = new List<string>();

        foreach (var entry in value.Split(','))
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            if (!TryNormalizeUrl(entry, out var peer))
            {
                throw new InvalidOperationException(
                    $"PEERS inválida: '{entry}'. Informe URLs absolutas separadas por vírgula " +
                    "(ex.: http://node2:8080,http://node3:8080).");
            }

            // Um peer repetido geraria dois POSTs para o mesmo nó.
            if (!peers.Contains(peer, StringComparer.OrdinalIgnoreCase))
                peers.Add(peer);
        }

        return peers;
    }

    /// <summary>
    /// ADVERTISE_URL (etapa 10): o endereço que este nó anuncia aos outros. O nó não tem
    /// como descobri-lo sozinho — dentro do Docker ele é <c>http://node4:8080</c>, mas o
    /// processo só enxerga a própria porta interna. Vazio = nó sem anúncio.
    /// </summary>
    public static string? ParseAdvertiseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!TryNormalizeUrl(value, out var url))
        {
            throw new InvalidOperationException(
                $"ADVERTISE_URL inválida: '{value}'. Informe a URL absoluta pela qual os outros " +
                "nós chamam este (ex.: http://node4:8080).");
        }

        return url;
    }

    /// <summary>
    /// Regra única de "endereço de nó": URL http(s) absoluta, sem espaços nem barra final.
    /// Vale para PEERS, ADVERTISE_URL e para os endereços que chegam pela rede em
    /// <c>POST /peers</c> — o mesmo nó nunca aparece duas vezes por diferença de escrita.
    /// </summary>
    public static bool TryNormalizeUrl(string? value, out string url)
    {
        // TrimEnd('/') evita "http://node2:8080//blocks/receive" na hora do push.
        url = value?.Trim().TrimEnd('/') ?? string.Empty;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
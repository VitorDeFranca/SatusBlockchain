using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node;

/// <summary>
/// Configuração do nó, lida do ambiente (variáveis de ambiente / .env / Docker).
/// A validação acontece na inicialização: um valor inválido derruba o nó na hora,
/// com mensagem clara, em vez de deixá-lo minerando para sempre.
/// </summary>
public record NodeOptions(string NodeId, byte Difficulty, IReadOnlyList<string> Peers)
{
    public const string DefaultNodeId = "node-local";

    public static NodeOptions FromConfiguration(IConfiguration configuration) => new(
        configuration["NODE_ID"] ?? DefaultNodeId,
        ParseDifficulty(configuration["DIFFICULTY"]),
        ParsePeers(configuration["PEERS"]));

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
    /// Peers estáticos (PEERS), separados por vírgula — ex.:
    /// <c>http://node2:8080,http://node3:8080</c>. São os destinos do push de blocos.
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
            // TrimEnd('/') evita "http://node2:8080//blocks/receive" na hora do push.
            var peer = entry.Trim().TrimEnd('/');
            if (peer.Length == 0)
                continue;

            if (!Uri.TryCreate(peer, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
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
}
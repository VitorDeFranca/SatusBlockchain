using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node;

/// <summary>
/// Configuração do nó, lida do ambiente (variáveis de ambiente / .env / Docker).
/// A validação acontece na inicialização: um valor inválido derruba o nó na hora,
/// com mensagem clara, em vez de deixá-lo minerando para sempre.
/// </summary>
public record NodeOptions(string NodeId, byte Difficulty)
{
    public const string DefaultNodeId = "node-local";

    public static NodeOptions FromConfiguration(IConfiguration configuration) => new(
        configuration["NODE_ID"] ?? DefaultNodeId,
        ParseDifficulty(configuration["DIFFICULTY"]));

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
}
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SatusBlockchain.Node.Core;

/// <summary>
/// Calcula o SHA-256 de um bloco a partir de uma SERIALIZAÇÃO CANÔNICA:
/// JSON com campos em ordem fixa e timestamp em formato numérico.
///
/// Sem canonicalização, dois nós poderiam serializar o mesmo bloco de formas
/// diferentes (ordem de campos, formato de data) e obter hashes diferentes,
/// quebrando o consenso. O campo Hash nunca participa do próprio cálculo.
/// </summary>
public static class Hasher
{
    public static string ComputeHash(Block block)
    {
        var canonical = new
        {
            block.Index,
            Timestamp = block.Timestamp.ToUnixTimeMilliseconds(),
            block.Transactions,
            block.PreviousHash,
            block.Nonce
        };

        var json = JsonSerializer.Serialize(canonical);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));

        // Hex minúsculo: facilita inspecionar o prefixo de zeros do Proof of Work.
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
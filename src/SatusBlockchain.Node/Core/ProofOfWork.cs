namespace SatusBlockchain.Node.Core;

/// <summary>
/// Proof of Work simplificado: um bloco só é aceito se o seu hash começar com
/// N zeros hexadecimais (<c>difficulty</c>).
///
/// Minerar é caro (testa nonces até acertar); verificar é barato (um único hash).
/// </summary>
public static class ProofOfWork
{
    /// <summary>Dificuldade padrão: ~65.536 tentativas (16⁴) — cerca de 1 segundo.</summary>
    public const byte DefaultDifficulty = 4;

    /// <summary>
    /// Limite superior: um hash SHA-256 tem 64 caracteres hexadecimais, portanto
    /// mais de 64 zeros hexadecimais é impossível de satisfazer (a mineração
    /// ficaria em laço infinito). O tipo byte é escolhido porque o domínio real
    /// da dificuldade é pequeno e contíguo — e a validação explícita impede
    /// configurações absurdas vindas do ambiente.
    /// </summary>
    public const byte MaxDifficulty = 64;

    /// <summary>
    /// Minera o bloco: varia o <c>Nonce</c> até o hash satisfazer a dificuldade.
    /// Ao final, <c>block.Hash</c> e <c>block.Nonce</c> contêm a solução.
    /// </summary>
    public static void Mine(Block block, byte difficulty)
    {
        for (long nonce = 0; ; nonce++)
        {
            block.Nonce = nonce;
            block.Hash = Hasher.ComputeHash(block);

            if (SatisfiesDifficulty(block.Hash, difficulty))
                return;
        }
    }

    /// <summary>
    /// Confere se a solução do bloco é válida: recalcula o hash a partir do nonce
    /// informado e verifica o prefixo de zeros. Custo O(1), independente da dificuldade.
    /// </summary>
    public static bool Verify(Block block, byte difficulty) =>
        SatisfiesDifficulty(Hasher.ComputeHash(block), difficulty);

    /// <summary>
    /// Dificuldade = número de zeros hexadecimais iniciais exigidos no hash.
    /// Usa <c>Span</c> para não alocar memória a cada verificação (o método é
    /// chamado milhares de vezes por bloco durante a mineração).
    /// </summary>
    public static bool SatisfiesDifficulty(string hash, byte difficulty)
    {
        if (difficulty == 0)
            return true;

        if (difficulty > hash.Length)
            return false; // impossível: o hash não tem zeros suficientes

        return hash.AsSpan(0, difficulty).IndexOfAnyExcept('0') < 0;
    }
}
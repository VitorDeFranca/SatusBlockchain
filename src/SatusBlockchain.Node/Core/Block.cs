namespace SatusBlockchain.Node.Core;

/// <summary>
/// Um bloco da cadeia. O Hash é a "impressão digital" (SHA-256) de todos os
/// demais campos; o PreviousHash é o elo com o bloco anterior.
///
/// Nonce e Hash são mutáveis (set) porque são o resultado da mineração:
/// o Proof of Work varia o Nonce até o Hash satisfazer a dificuldade.
/// Os demais campos são imutáveis (init) após a criação do bloco.
/// </summary>
public class Block
{
    public long Index { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public IReadOnlyList<Transaction> Transactions { get; init; } = [];
    public string PreviousHash { get; init; } = string.Empty;
    public long Nonce { get; set; }
    public string Hash { get; set; } = string.Empty;
}
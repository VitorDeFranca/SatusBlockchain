namespace SatusBlockchain.Node.Core;

/// <summary>
/// Desfecho da tentativa de adotar uma cadeia vinda de um peer
/// (etapa 8, <c>POST /sync</c>). Um estado por motivo — assim o relatório do sync
/// não precisa de um <c>bool</c> mais uma string para explicar o que houve.
/// </summary>
public enum ChainUpdate
{
    /// <summary>Recebida, válida e estritamente mais longa: foi adotada (reorg).</summary>
    Adopted,

    /// <summary>
    /// Recebida, mas não mais longa que a local (empate ou menor): a local foi mantida.
    /// É a regra "first seen" — sem ela, dois nós com cadeias iguais poderiam ficar
    /// trocando de versão indefinidamente.
    /// </summary>
    Kept,

    /// <summary>
    /// Recebida, mas inválida: elo/hash/PoW quebrados, ou genesis diferente do nosso
    /// (dificuldade outra = outro "universo"; o PoW de lá não valeria aqui).
    /// </summary>
    Invalid,

    /// <summary>
    /// O peer não respondeu (fora do ar, DNS, timeout) ou respondeu com erro.
    /// Não é erro do nó: um peer ausente é situação normal numa rede parcial.
    /// </summary>
    Unreachable
}

/// <summary>
/// Resultado de <see cref="Blockchain.TryReplaceChain"/>: o que aconteceu com a cadeia
/// local e quais transações ficaram órfãs.
/// </summary>
/// <param name="Outcome">O que aconteceu com a cadeia local.</param>
/// <param name="Length">Tamanho da cadeia local depois da tentativa.</param>
/// <param name="OrphanTransactions">
/// Transações dos blocos locais descartados que NÃO estão na cadeia adotada — é o que
/// volta para a mempool. As transações que a cadeia vencedora reusou não aparecem aqui:
/// elas já estão confirmadas.
/// </param>
public sealed record ChainUpdateResult(
    ChainUpdate Outcome,
    int Length,
    IReadOnlyList<Transaction> OrphanTransactions);
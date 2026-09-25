namespace SatusBlockchain.Node.Core;

/// <summary>
/// Fila de transações pendentes: aguardam a próxima mineração para entrar em um bloco.
/// Thread-safe por <c>lock</c>, pois pode ser alterada por requisições simultâneas.
/// </summary>
public class Mempool
{
    private readonly List<Transaction> _pending = [];
    private readonly Lock _lock = new();

    public void Add(Transaction transaction)
    {
        lock (_lock)
            _pending.Add(transaction);
    }

    /// <summary>Cópia das transações pendentes — protege a fila interna.</summary>
    public IReadOnlyList<Transaction> GetPending()
    {
        lock (_lock)
            return [.. _pending];
    }

    /// <summary>
    /// Remove exatamente as transações que entraram em um bloco.
    /// Usar "remover o que foi minerado" (em vez de "limpar tudo") mantém a
    /// fila correta mesmo que uma transação tenha sido criada durante a mineração.
    /// </summary>
    public void Remove(IReadOnlyCollection<Transaction> transactions)
    {
        lock (_lock)
            _pending.RemoveAll(transactions.Contains);
    }
}
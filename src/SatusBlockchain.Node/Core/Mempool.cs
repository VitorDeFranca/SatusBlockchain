namespace SatusBlockchain.Node.Core;

/// <summary>
/// Fila de transações pendentes: aguardam a próxima mineração para entrar em um bloco.
/// Thread-safe por <c>lock</c>, pois pode ser alterada por requisições simultâneas —
/// na etapa 7 são DOIS escritores possíveis: o cliente (`POST /transactions`) e os
/// peers (gossip, `POST /transactions/receive`).
/// </summary>
public class Mempool
{
    private readonly List<Transaction> _pending = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Adiciona a transação à fila, **sem duplicar**: com o gossip, a mesma transação
    /// pode chegar por caminhos diferentes (cliente e peers) e a fila deve continuar
    /// com uma única cópia.
    /// </summary>
    /// <returns><c>true</c> se entrou na fila; <c>false</c> se já estava pendente.</returns>
    public bool Add(Transaction transaction)
    {
        lock (_lock)
        {
            // Transaction é record: a comparação é por valor (From/To/Amount).
            if (_pending.Contains(transaction))
                return false;

            _pending.Add(transaction);
            return true;
        }
    }

    /// <summary>Cópia das transações pendentes — protege a fila interna.</summary>
    public IReadOnlyList<Transaction> GetPending()
    {
        lock (_lock)
            return [.. _pending];
    }

    /// <summary>
    /// Quantidade de transações pendentes — leitura rápida usada pelo gauge
    /// <c>satus.mempool.size</c> (etapa 9), sem copiar a fila inteira.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _pending.Count;
        }
    }

    /// <summary>
    /// Adiciona várias transações de uma vez, sem duplicar. Usado no <c>POST /sync</c> para
    /// devolver à fila as transações dos blocos que perderam o reorg: elas não foram
    /// invalidadas, apenas saíram da cadeia vencedora.
    /// </summary>
    /// <returns>Quantas transações entraram na fila (as repetidas são ignoradas).</returns>
    public int AddRange(IEnumerable<Transaction> transactions)
    {
        lock (_lock)
        {
            var added = 0;

            // Add trava o mesmo _lock de novo: Lock é reentrante, então aninhar é seguro.
            foreach (var transaction in transactions)
            {
                if (Add(transaction))
                    added++;
            }

            return added;
        }
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
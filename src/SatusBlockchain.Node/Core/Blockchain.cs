namespace SatusBlockchain.Node.Core;

/// <summary>
/// Uma cadeia de blocos em memória. Cada nó do SatusBlockchain possui a sua própria
/// instância (estado replicado).
///
/// Thread-safe por <c>lock</c>: blocos podem chegar de três fontes — mineração local,
/// propagação de outro nó (etapa 6) e substituição pela cadeia mais longa que um peer
/// manda no <c>POST /sync</c> (etapa 8).
/// </summary>
public class Blockchain
{
    /// <summary>Por convenção, o "PreviousHash" do bloco genesis é "0".</summary>
    public const string GenesisPreviousHash = "0";

    /// <summary>
    /// Timestamp FIXO do genesis: garante que todos os nós criem exatamente o mesmo
    /// bloco genesis (e portanto o mesmo hash), pré-requisito para que as cadeias
    /// de nós diferentes sejam compatíveis entre si.
    /// </summary>
    private static readonly DateTimeOffset GenesisTimestamp =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly List<Block> _chain = [];
    private readonly Lock _lock = new();

    /// <summary>Dificuldade do Proof of Work usada por este nó.</summary>
    public byte Difficulty { get; }

    public Blockchain(byte difficulty = ProofOfWork.DefaultDifficulty)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(difficulty, ProofOfWork.MaxDifficulty);

        Difficulty = difficulty;
        _chain.Add(CreateGenesisBlock(difficulty));
    }

    public int Length
    {
        get
        {
            lock (_lock)
                return _chain.Count;
        }
    }

    /// <summary>Cópia da cadeia — protege a lista interna de mutações externas.</summary>
    public IReadOnlyList<Block> GetChain()
    {
        lock (_lock)
            return [.. _chain];
    }

    public Block GetLatestBlock()
    {
        lock (_lock)
            return _chain[^1];
    }

    /// <summary>
    /// Tenta anexar um bloco à cadeia. Só aceita blocos que estendam corretamente
    /// a cadeia atual (índice e elo corretos, hash consistente).
    /// </summary>
    /// <returns><c>true</c> se o bloco foi anexado; <c>false</c> se foi rejeitado.</returns>
    public bool AddBlock(Block block)
    {
        lock (_lock)
            return TryAppend(block);
    }

    /// <summary>
    /// Minera um novo bloco com as transações informadas e tenta anexá-lo à cadeia.
    ///
    /// Ponto importante de concorrência: a mineração (lenta, ~1s) roda FORA do lock,
    /// para não bloquear as demais requisições do nó. Depois de minerar, o bloco é
    /// revalidado sob o lock — se outro bloco já tiver entrado na cadeia nesse
    /// intervalo, a mineração é descartada (retorna null) e o chamador pode repetir.
    /// </summary>
    public Block? MineBlock(IReadOnlyList<Transaction> transactions)
    {
        Block candidate;
        lock (_lock)
        {
            var previous = _chain[^1];
            candidate = new Block
            {
                Index = previous.Index + 1,
                Timestamp = DateTimeOffset.UtcNow,
                Transactions = [.. transactions],
                PreviousHash = previous.Hash
            };
        }

        // Mineração FORA do lock: pode levar segundos.
        ProofOfWork.Mine(candidate, Difficulty);

        lock (_lock)
            return TryAppend(candidate) ? candidate : null;
    }

    /// <summary>
    /// A transação já está confirmada em algum bloco da cadeia?
    ///
    /// Usado pelo gossip (etapa 7): o bloco pode chegar ANTES da transação, dependendo de
    /// quem responde primeiro. Sem esta checagem, uma transação já confirmada voltaria a
    /// ficar "pendente para sempre" na mempool local.
    /// </summary>
    public bool ContainsTransaction(Transaction transaction)
    {
        lock (_lock)
            return _chain.Any(block => block.Transactions.Contains(transaction));
    }

    /// <summary>Exige o lock adquirido. Encadeia e anexa o bloco, se válido.</summary>
    private bool TryAppend(Block block)
    {
        if (!IsValidBlock(block, expectedIndex: _chain.Count,
                expectedPreviousHash: _chain[^1].Hash, Difficulty))
            return false;

        _chain.Add(block);
        return true;
    }

    /// <summary>
    /// Valida a cadeia inteira: índices sequenciais, elos de PreviousHash
    /// consistentes e hash de cada bloco igual ao hash recalculado do seu conteúdo
    /// (é isso que detecta adulteração). Delega para <see cref="IsValidChain"/>, a mesma
    /// regra que julga a cadeia recebida de um peer.
    /// </summary>
    public bool IsValid()
    {
        lock (_lock)
            return IsValidChain(_chain, Difficulty);
    }

    /// <summary>
    /// Regra única de "cadeia válida": índices sequenciais a partir do 0, elos de
    /// PreviousHash consistentes, hash recalculado igual ao armazenado e PoW válido na
    /// dificuldade informada. Vale tanto para a cadeia local (<see cref="IsValid"/>) quanto
    /// para a que um peer envia no <c>POST /sync</c> — um código só, para não haver duas
    /// verdades sobre o que é uma cadeia legítima.
    /// </summary>
    private static bool IsValidChain(IReadOnlyList<Block> chain, byte difficulty)
    {
        if (chain.Count == 0)
            return false;

        for (var position = 0; position < chain.Count; position++)
        {
            var expectedPreviousHash = position == 0
                ? GenesisPreviousHash
                : chain[position - 1].Hash;

            if (!IsValidBlock(chain[position], expectedIndex: position,
                    expectedPreviousHash, difficulty))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Tenta adotar a cadeia que um peer mandou no <c>POST /sync</c> (etapa 8) —
    /// o lado do **pull** do sistema. Consenso aqui é o mais simples possível: a
    /// <b>cadeia válida mais longa vence</b>.
    ///
    /// Três regras, nesta ordem:
    /// <list type="number">
    ///   <item><b>Mesmo genesis</b>: dificuldade diferente gera genesis diferente, e aí
    ///   são dois "universos" — o PoW de lá não valeria aqui.</item>
    ///   <item><b>Cadeia válida</b>: a mesma regra de <see cref="IsValid"/>, aplicada
    ///   localmente. Não confiamos no julgamento do peer: validamos nós mesmos.</item>
    ///   <item><b>Estritamente mais longa</b>: empate mantém a local (regra "first seen").</item>
    /// </list>
    ///
    /// Devolve as transações órfãs, mas NÃO as repõe na mempool: a Mempool é outra
    /// camada (estado local), e quem coordena as duas é a API.
    /// </summary>
    public ChainUpdateResult TryReplaceChain(IReadOnlyList<Block> candidate)
    {
        lock (_lock)
        {
            if (candidate.Count == 0 || candidate[0].Hash != _chain[0].Hash)
                return new ChainUpdateResult(ChainUpdate.Invalid, _chain.Count, []);

            if (!IsValidChain(candidate, Difficulty))
                return new ChainUpdateResult(ChainUpdate.Invalid, _chain.Count, []);

            if (candidate.Count <= _chain.Count)
                return new ChainUpdateResult(ChainUpdate.Kept, _chain.Count, []);

            var orphans = GetOrphanTransactions(candidate);
            _chain.Clear();
            _chain.AddRange(candidate);

            return new ChainUpdateResult(ChainUpdate.Adopted, _chain.Count, orphans);
        }
    }

    /// <summary>
    /// Transações que estavam nos blocos locais descartados e <b>não</b> estão na cadeia
    /// adotada — é o que volta para a mempool, como no Bitcoin: o bloco órfão é
    /// descartado, mas as transações dele continuam válidas e podem ser mineradas de novo.
    ///
    /// As transações que a cadeia vencedora reusou ficam de fora de propósito: elas já
    /// estão confirmadas, e voltar à fila as mineraria duas vezes.
    /// </summary>
    private List<Transaction> GetOrphanTransactions(IReadOnlyList<Block> candidate)
    {
        // Transaction é record: o HashSet compara por valor (From/To/Amount).
        var kept = candidate.SelectMany(block => block.Transactions).ToHashSet();

        return _chain
            .Where(local => candidate.All(block => block.Hash != local.Hash))
            .SelectMany(local => local.Transactions)
            .Where(transaction => !kept.Contains(transaction))
            .ToList();
    }

    public static Block CreateGenesisBlock(byte difficulty = ProofOfWork.DefaultDifficulty)
    {
        var block = new Block
        {
            Index = 0,
            Timestamp = GenesisTimestamp,
            Transactions = [],
            PreviousHash = GenesisPreviousHash
        };

        // O genesis também passa pelo Proof of Work (como no Bitcoin).
        // Como o timestamp é fixo, todos os nós obtêm exatamente o mesmo genesis.
        ProofOfWork.Mine(block, difficulty);
        return block;
    }

    /// <summary>
    /// Regras de aceitação de um bloco: índice, elo, integridade do hash
    /// e solução válida do Proof of Work.
    /// </summary>
    private static bool IsValidBlock(Block block, int expectedIndex,
        string expectedPreviousHash, byte difficulty)
    {
        if (block.Index != expectedIndex)
            return false;

        if (block.PreviousHash != expectedPreviousHash)
            return false;

        // Recalcular o hash e comparar com o armazenado detecta qualquer
        // alteração no conteúdo do bloco (adulteração).
        var recalculatedHash = Hasher.ComputeHash(block);
        if (block.Hash != recalculatedHash)
            return false;

        // O bloco precisa ter um Proof of Work válido para a dificuldade do nó.
        return ProofOfWork.SatisfiesDifficulty(block.Hash, difficulty);
    }
}
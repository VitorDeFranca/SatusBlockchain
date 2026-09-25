namespace SatusBlockchain.Node.Core;

/// <summary>
/// Uma cadeia de blocos em memória. Cada nó do SatusBlockchain possui a sua própria
/// instância (estado replicado).
///
/// Thread-safe por <c>lock</c>: no futuro, blocos podem chegar de duas fontes
/// simultâneas (mineração local e blocos propagados por outros nós).
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
    /// (é isso que detecta adulteração).
    /// </summary>
    public bool IsValid()
    {
        lock (_lock)
        {
            if (_chain.Count == 0)
                return false;

            for (var position = 0; position < _chain.Count; position++)
            {
                var expectedPreviousHash = position == 0
                    ? GenesisPreviousHash
                    : _chain[position - 1].Hash;

                if (!IsValidBlock(_chain[position], expectedIndex: position,
                        expectedPreviousHash, Difficulty))
                    return false;
            }

            return true;
        }
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
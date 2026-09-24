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

    public Blockchain()
    {
        _chain.Add(CreateGenesisBlock());
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
        {
            var previousHash = _chain[^1].Hash;
            if (!IsValidBlock(block, expectedIndex: _chain.Count, expectedPreviousHash: previousHash))
                return false;

            _chain.Add(block);
            return true;
        }
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

                if (!IsValidBlock(_chain[position], expectedIndex: position, expectedPreviousHash))
                    return false;
            }

            return true;
        }
    }

    public static Block CreateGenesisBlock()
    {
        var block = new Block
        {
            Index = 0,
            Timestamp = GenesisTimestamp,
            Transactions = [],
            PreviousHash = GenesisPreviousHash,
            Nonce = 0
        };

        block.Hash = Hasher.ComputeHash(block);
        return block;
    }

    /// <summary>
    /// Regras de aceitação de um bloco. A verificação do Proof of Work
    /// entra aqui na etapa 3.
    /// </summary>
    private static bool IsValidBlock(Block block, int expectedIndex, string expectedPreviousHash)
    {
        if (block.Index != expectedIndex)
            return false;

        if (block.PreviousHash != expectedPreviousHash)
            return false;

        // Recalcular o hash e comparar com o armazenado detecta qualquer
        // alteração no conteúdo do bloco (adulteração).
        var recalculatedHash = Hasher.ComputeHash(block);
        return block.Hash == recalculatedHash;
    }
}
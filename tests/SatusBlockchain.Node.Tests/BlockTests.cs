using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests;

public class BlockTests
{
    [Fact]
    public void Block_ArmazenaTransacoesEEncadeamento()
    {
        var transactions = new List<Transaction>
        {
            new("alice", "bob", 10m),
            new("bob", "carol", 3m)
        };

        var block = new Block
        {
            Index = 7,
            Transactions = transactions,
            PreviousHash = "hash-do-bloco-6"
        };

        Assert.Equal(7, block.Index);
        Assert.Equal(2, block.Transactions.Count);
        Assert.Equal("hash-do-bloco-6", block.PreviousHash);
    }

    [Fact]
    public void Transaction_RecordComIgualdadePorValor()
    {
        // Importante para comparações em testes e, depois, na deduplicação da mempool.
        var a = new Transaction("alice", "bob", 10m);
        var b = new Transaction("alice", "bob", 10m);
        var c = new Transaction("alice", "bob", 99m);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
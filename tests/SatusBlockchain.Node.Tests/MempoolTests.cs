using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests;

public class MempoolTests
{
    [Fact]
    public void Mempool_IniciaVazia()
    {
        Assert.Empty(new Mempool().GetPending());
    }

    [Fact]
    public void Add_AdicionaTransacaoNaFila()
    {
        var mempool = new Mempool();
        var transacao = new Transaction("alice", "bob", 10m);

        mempool.Add(transacao);

        Assert.Equal([transacao], mempool.GetPending());
    }

    [Fact]
    public void GetPending_DevolveCopia_ProtegeAFilaInterna()
    {
        var mempool = new Mempool();
        mempool.Add(new Transaction("alice", "bob", 10m));

        var copia = mempool.GetPending();
        mempool.Add(new Transaction("bob", "carol", 5m));

        Assert.Single(copia); // a cópia não enxerga o que foi adicionado depois
        Assert.Equal(2, mempool.GetPending().Count);
    }

    [Fact]
    public void Remove_RetiraApenasAsTransacoesMineradas()
    {
        var mempool = new Mempool();
        var minerada = new Transaction("alice", "bob", 10m);
        mempool.Add(minerada);
        mempool.Add(new Transaction("bob", "carol", 5m));

        mempool.Remove([minerada]);

        var restante = Assert.Single(mempool.GetPending());
        Assert.Equal(new Transaction("bob", "carol", 5m), restante);
    }

    [Fact]
    public void AddConcorrente_NaoPerdeTransacoes()
    {
        var mempool = new Mempool();
        var quantidade = 200;

        Parallel.For(0, quantidade, i => mempool.Add(new Transaction("from", $"to{i}", i)));

        Assert.Equal(quantidade, mempool.GetPending().Count);
    }
}
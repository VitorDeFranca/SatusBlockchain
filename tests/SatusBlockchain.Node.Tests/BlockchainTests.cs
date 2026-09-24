using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests;

public class BlockchainTests
{
    // Cria um bloco válido que estende a cadeia a partir de previous.
    private static Block CreateNextBlock(Block previous, params Transaction[] transactions)
    {
        var block = new Block
        {
            Index = previous.Index + 1,
            Timestamp = DateTimeOffset.UtcNow,
            Transactions = transactions.ToList(),
            PreviousHash = previous.Hash
        };

        block.Hash = Hasher.ComputeHash(block);
        return block;
    }

    [Fact]
    public void Construtor_IniciaComSomenteOGenesis_IndiceZero()
    {
        var blockchain = new Blockchain();

        Assert.Equal(1, blockchain.Length);
        Assert.Equal(0, blockchain.GetChain()[0].Index);
        Assert.Equal(Blockchain.GenesisPreviousHash, blockchain.GetChain()[0].PreviousHash);
    }

    [Fact]
    public void Genesis_EhValido()
    {
        var blockchain = new Blockchain();

        Assert.True(blockchain.IsValid());
    }

    [Fact]
    public void Genesis_EhIdenticoEmTodosOsNos_MesmoHash()
    {
        // Dois nós independentes que criam o genesis devem obter o mesmo hash.
        var nodeA = new Blockchain();
        var nodeB = new Blockchain();

        Assert.Equal(nodeA.GetChain()[0].Hash, nodeB.GetChain()[0].Hash);
    }

    [Fact]
    public void AddBlock_AdicionaBlocoValido_EncadeandoNoAnterior()
    {
        var blockchain = new Blockchain();
        var novo = CreateNextBlock(blockchain.GetLatestBlock(), new Transaction("alice", "bob", 10m));

        Assert.True(blockchain.AddBlock(novo));
        Assert.Equal(2, blockchain.Length);
        Assert.True(blockchain.IsValid());
        Assert.Equal(blockchain.GetChain()[0].Hash, blockchain.GetChain()[1].PreviousHash);
    }

    [Fact]
    public void AddBlock_RejeitaIndiceIncorreto()
    {
        var blockchain = new Blockchain();
        var invalido = new Block
        {
            Index = 5, // deveria ser 1
            Timestamp = DateTimeOffset.UtcNow,
            Transactions = [],
            PreviousHash = blockchain.GetLatestBlock().Hash
        };
        invalido.Hash = Hasher.ComputeHash(invalido);

        Assert.False(blockchain.AddBlock(invalido));
        Assert.Equal(1, blockchain.Length);
    }

    [Fact]
    public void AddBlock_RejeitaPreviousHashIncorreto()
    {
        var blockchain = new Blockchain();
        var invalido = new Block
        {
            Index = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Transactions = [],
            PreviousHash = "hash-que-nao-e-o-do-bloco-anterior"
        };
        invalido.Hash = Hasher.ComputeHash(invalido);

        Assert.False(blockchain.AddBlock(invalido));
        Assert.Equal(1, blockchain.Length);
    }

    [Fact]
    public void AddBlock_RejeitaBlocoComHashInconsistente()
    {
        // O bloco diz ter um hash, mas o conteúdo não corresponde a ele.
        var blockchain = new Blockchain();
        var invalido = new Block
        {
            Index = 1,
            Timestamp = DateTimeOffset.UtcNow,
            Transactions = [new Transaction("alice", "bob", 10m)],
            PreviousHash = blockchain.GetLatestBlock().Hash,
            Hash = "0000hash-falsificado"
        };

        Assert.False(blockchain.AddBlock(invalido));
        Assert.Equal(1, blockchain.Length);
    }

    [Fact]
    public void IsValid_DetectaAdulteracaoDeTransacaoEmBlocoJaConfirmado()
    {
        var blockchain = new Blockchain();
        var bloco = CreateNextBlock(blockchain.GetLatestBlock(), new Transaction("alice", "bob", 10m));
        blockchain.AddBlock(bloco);
        Assert.True(blockchain.IsValid());

        // Simula adulteração direta no bloco já aceito (efeito "Alice in Wonderland").
        // O conteúdo muda, mas o Hash armazenado continua o antigo => elo quebrado.
        ((List<Transaction>)bloco.Transactions).Add(new Transaction("alice", "eve", 999m));

        Assert.False(blockchain.IsValid());
    }

    [Fact]
    public void AddBlock_Concorrente_ApenasUmBlocoIgualEAceito()
    {
        // Dois blocos idênticos chegando "ao mesmo tempo": um vence, o outro é rejeitado.
        var blockchain = new Blockchain();
        var bloco = CreateNextBlock(blockchain.GetLatestBlock(), new Transaction("alice", "bob", 10m));

        var resultados = new bool[2];
        Parallel.For(0, 2, i => resultados[i] = blockchain.AddBlock(bloco));

        Assert.Single(resultados, aceito => aceito);
        Assert.Equal(2, blockchain.Length);
        Assert.True(blockchain.IsValid());
    }
}
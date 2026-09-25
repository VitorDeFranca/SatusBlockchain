using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests;

public class BlockchainTests
{
    // Cria um bloco que estende a cadeia a partir de previous, SEM Proof of Work.
    // Usado nos testes de rejeição (índice, elo ou hash inválidos).
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

    // Cria um bloco já MINERADO na dificuldade do nó (necessário para ser aceito).
    private static Block CreateMinedNextBlock(Blockchain blockchain, params Transaction[] transactions)
    {
        var block = CreateNextBlock(blockchain.GetLatestBlock(), transactions);
        ProofOfWork.Mine(block, blockchain.Difficulty);
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
        var novo = CreateMinedNextBlock(blockchain, new Transaction("alice", "bob", 10m));

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
        var bloco = CreateMinedNextBlock(blockchain, new Transaction("alice", "bob", 10m));
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
        var bloco = CreateMinedNextBlock(blockchain, new Transaction("alice", "bob", 10m));

        var resultados = new bool[2];
        Parallel.For(0, 2, i => resultados[i] = blockchain.AddBlock(bloco));

        Assert.Single(resultados, aceito => aceito);
        Assert.Equal(2, blockchain.Length);
        Assert.True(blockchain.IsValid());
    }

    #region Proof of Work

    [Fact]
    public void Genesis_EhMineradoComProofOfWork()
    {
        var blockchain = new Blockchain(); // dificuldade padrão = 4

        var genesis = blockchain.GetChain()[0];
        Assert.StartsWith("0000", genesis.Hash);
        Assert.True(ProofOfWork.Verify(genesis, blockchain.Difficulty));
    }

    [Fact]
    public void AddBlock_RejeitaBlocoSemProofOfWorkValido()
    {
        // Elo e hash corretos, mas nenhum Proof of Work foi feito.
        // Dificuldade 5 => chance de "passar" por acaso ~1 em 1 milhão.
        var blockchain = new Blockchain(difficulty: 5);
        var semMinerar = CreateNextBlock(blockchain.GetLatestBlock(), new Transaction("alice", "bob", 10m));

        Assert.False(blockchain.AddBlock(semMinerar));
        Assert.Equal(1, blockchain.Length);
    }

    [Fact]
    public void DificuldadeEhConfiguravel()
    {
        var blockchain = new Blockchain(difficulty: 1);

        Assert.Equal(1, (int)blockchain.Difficulty);
        Assert.StartsWith("0", blockchain.GetChain()[0].Hash);
    }

    [Fact]
    public void Construtor_RejeitaDificuldadeAcimaDoMaximo()
    {
        // 65 zeros hexadecimais são impossíveis num hash de 64 caracteres:
        // a mineração ficaria em laço infinito.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Blockchain(difficulty: ProofOfWork.MaxDifficulty + 1));
    }

    [Fact]
    public void MineBlock_AdicionaBlocoComAsTransacoesInformadas()
    {
        var blockchain = new Blockchain();
        Transaction[] transacoes =
        [
            new Transaction("alice", "bob", 10m),
            new Transaction("bob", "carol", 5m)
        ];

        var minerado = blockchain.MineBlock(transacoes);

        Assert.NotNull(minerado);
        Assert.Equal(2, blockchain.Length);
        Assert.Equal(2, minerado.Transactions.Count);
        Assert.StartsWith("0000", minerado.Hash);
        Assert.True(ProofOfWork.Verify(minerado, blockchain.Difficulty));
        Assert.True(blockchain.IsValid());
    }

    [Fact]
    public async Task MineBlock_ConcorrenteComBlocoDePeer_NuncaCorrompeACadeia()
    {
        // O bloco do "peer" é minerado ANTES, para poder chegar em milissegundos
        // enquanto o nó pode estar minerando o bloco dele.
        var blockchain = new Blockchain();
        var doPeer = CreateMinedNextBlock(blockchain, new Transaction("peer", "peer", 1m));

        var minerando = Task.Run(() =>
            blockchain.MineBlock([new Transaction("eu", "eu", 2m)]));

        // O bloco do peer pode entrar antes, durante ou depois da mineração local.
        blockchain.AddBlock(doPeer);
        var local = await minerando;

        // Invariante: a cadeia continua consistente, venha o que vier.
        Assert.True(blockchain.IsValid());
        Assert.Contains(blockchain.GetChain(), b => b.Hash == doPeer.Hash);

        if (local is null)
        {
            // A mineração local foi descartada (o peer já tinha avançado a cadeia).
            Assert.Equal(2, blockchain.Length);
        }
        else
        {
            // A mineração local terminou primeiro e foi encadeada após o peer.
            Assert.Equal(3, blockchain.Length);
        }
    }
    #endregion
}
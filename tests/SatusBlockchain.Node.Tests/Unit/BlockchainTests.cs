using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests.Unit;

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

    #region Consultas

    [Fact]
    public void ContainsTransaction_TransacaoJaMinerada_DevolveTrue()
    {
        // Caso real do gossip (etapa 7): o bloco pode chegar ANTES da transação.
        var blockchain = new Blockchain(difficulty: 0);
        var transacao = new Transaction("alice", "bob", 10m);
        blockchain.AddBlock(CreateMinedNextBlock(blockchain, transacao));

        Assert.True(blockchain.ContainsTransaction(transacao));
    }

    [Fact]
    public void ContainsTransaction_TransacaoAindaNaoMinerada_DevolveFalse()
    {
        var blockchain = new Blockchain(difficulty: 0);

        Assert.False(blockchain.ContainsTransaction(new Transaction("alice", "bob", 10m)));
    }

    [Fact]
    public void ContainsTransaction_ProcuradaPorValor_NaoPorReferencia()
    {
        // Transaction é record: uma instância NOVA com os mesmos dados é a mesma transação.
        var blockchain = new Blockchain(difficulty: 0);
        blockchain.AddBlock(CreateMinedNextBlock(blockchain, new Transaction("alice", "bob", 10m)));

        Assert.True(blockchain.ContainsTransaction(new Transaction("alice", "bob", 10m)));
        Assert.False(blockchain.ContainsTransaction(new Transaction("alice", "bob", 10.01m)));
    }

    #endregion

    #region Sincronização e reorg (etapa 8)

    // Os testes desta região usam dificuldade 2: o PoW é instantâneo, mas o genesis
    // continua sendo o MESMO entre os nós (dificuldade igual => mesmo genesis).

    [Fact]
    public void TryReplaceChain_AdotaCadeiaValidaMaisLonga()
    {
        // O caso real da recuperação: o nó local ficou fora e o peer tem 3 blocos.
        var local = new Blockchain(difficulty: 2);
        var peer = new Blockchain(difficulty: 2);
        peer.AddBlock(CreateMinedNextBlock(peer, new Transaction("alice", "bob", 10m)));
        peer.AddBlock(CreateMinedNextBlock(peer, new Transaction("bob", "carol", 5m)));
        var esperada = peer.GetChain();

        var result = local.TryReplaceChain(esperada);

        Assert.Equal(ChainUpdate.Adopted, result.Outcome);
        Assert.Equal(3, result.Length);
        Assert.True(local.IsValid());
        Assert.Equal(esperada.Select(block => block.Hash), local.GetChain().Select(block => block.Hash));
    }

    [Fact]
    public void TryReplaceChain_Empate_MantemACadeiaLocal()
    {
        // Regra "first seen": no empate ninguém troca, senão dois nós trocariam de
        // versão indefinidamente sem nunca convergir.
        var local = new Blockchain(difficulty: 2);
        local.AddBlock(CreateMinedNextBlock(local, new Transaction("eu", "eu", 1m)));
        var meuHash = local.GetChain()[1].Hash;

        var peer = new Blockchain(difficulty: 2);
        peer.AddBlock(CreateMinedNextBlock(peer, new Transaction("peer", "peer", 2m)));

        var result = local.TryReplaceChain(peer.GetChain());

        Assert.Equal(ChainUpdate.Kept, result.Outcome);
        Assert.Equal(meuHash, local.GetChain()[1].Hash);
    }

    [Fact]
    public void TryReplaceChain_CadeiaMenor_MantemACadeiaLocal()
    {
        // Um peer que está ATRÁS não pode fazer este nó voltar atrás.
        var local = new Blockchain(difficulty: 2);
        local.AddBlock(CreateMinedNextBlock(local, new Transaction("a", "b", 1m)));
        local.AddBlock(CreateMinedNextBlock(local, new Transaction("c", "d", 2m)));
        var meuHash = local.GetLatestBlock().Hash;

        var atrasado = new Blockchain(difficulty: 2); // só o genesis

        var result = local.TryReplaceChain(atrasado.GetChain());

        Assert.Equal(ChainUpdate.Kept, result.Outcome);
        Assert.Equal(3, local.Length);
        Assert.Equal(meuHash, local.GetLatestBlock().Hash);
    }

    [Fact]
    public void TryReplaceChain_CadeiaAdulterada_Rejeita()
    {
        var local = new Blockchain(difficulty: 2);
        var peer = new Blockchain(difficulty: 2);
        peer.AddBlock(CreateMinedNextBlock(peer, new Transaction("alice", "bob", 10m)));

        // Adulteração pós-envio: o conteúdo muda, o Hash gravado continua o antigo.
        var adulterada = peer.GetChain();
        ((List<Transaction>)adulterada[1].Transactions).Add(new Transaction("eve", "eve", 999m));

        var result = local.TryReplaceChain(adulterada);

        Assert.Equal(ChainUpdate.Invalid, result.Outcome);
        Assert.Equal(1, local.Length);
        Assert.True(local.IsValid());
    }

    [Fact]
    public void TryReplaceChain_GenesisDiferente_Rejeita()
    {
        // Dificuldade diferente gera genesis diferente: são dois "universos" e o PoW
        // de lá não valeria aqui. Por isso a checagem de genesis vem ANTES do tamanho.
        var local = new Blockchain(difficulty: 2);
        var outro = new Blockchain(difficulty: 3);
        outro.AddBlock(CreateMinedNextBlock(outro, new Transaction("alice", "bob", 10m)));
        outro.AddBlock(CreateMinedNextBlock(outro, new Transaction("bob", "carol", 5m)));

        var result = local.TryReplaceChain(outro.GetChain());

        Assert.Equal(ChainUpdate.Invalid, result.Outcome);
        Assert.Equal(1, local.Length);
    }

    [Fact]
    public void TryReplaceChain_CadeiaVazia_Rejeita()
    {
        var local = new Blockchain(difficulty: 2);

        var result = local.TryReplaceChain([]);

        Assert.Equal(ChainUpdate.Invalid, result.Outcome);
        Assert.Equal(1, local.Length);
    }

    [Fact]
    public void TryReplaceChain_Orfas_DevolveSoAsTransacoesQuePerderam()
    {
        // Fork: as duas pontas compartilham o bloco 1. A local segue por txLocal; a
        // candidata segue por txPeer (e é mais longa, então vence).
        var local = new Blockchain(difficulty: 2);
        var comum = CreateMinedNextBlock(local, new Transaction("alice", "bob", 10m));
        local.AddBlock(comum);
        var txPerdida = new Transaction("local", "local", 5m);
        local.AddBlock(CreateMinedNextBlock(local, txPerdida));

        var candidata = new List<Block> { local.GetChain()[0], comum };
        var proximo = CreateNextBlock(comum, new Transaction("peer", "peer", 7m));
        ProofOfWork.Mine(proximo, local.Difficulty);
        var ultimo = CreateNextBlock(proximo, new Transaction("peer", "outro", 8m));
        ProofOfWork.Mine(ultimo, local.Difficulty);
        candidata.Add(proximo);
        candidata.Add(ultimo);

        var result = local.TryReplaceChain(candidata);

        Assert.Equal(ChainUpdate.Adopted, result.Outcome);
        // Só a transação do bloco órfão volta; a do bloco 1 está na cadeia adotada.
        Assert.Equal([txPerdida], result.OrphanTransactions);
        Assert.True(local.IsValid());
    }

    [Fact]
    public async Task TryReplaceChain_ConcorrenteComMineBlock_NuncaCorrompeACadeia()
    {
        // Três fontes disputam a cadeia ao mesmo tempo (etapa 8): mineração local,
        // push de outro nó e a substituição vinda do sync. Só a invariante importa.
        var local = new Blockchain(difficulty: 2);
        var peer = new Blockchain(difficulty: 2);
        for (var i = 0; i < 3; i++)
            peer.AddBlock(CreateMinedNextBlock(peer, new Transaction("peer", $"peer{i}", i + 1m)));

        var mineracao = Task.Run(() => local.MineBlock([new Transaction("eu", "eu", 1m)]));
        var sync = Task.Run(() => local.TryReplaceChain(peer.GetChain()));
        var push = Task.Run(() => local.AddBlock(CreateMinedNextBlock(local, new Transaction("push", "push", 2m))));

        await Task.WhenAll(mineracao, sync, push);

        Assert.True(local.IsValid());
        Assert.True(local.Length >= 4); // os 4 blocos do peer (genesis + 3) são o piso
    }

    #endregion
}
using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests;

public class ProofOfWorkTests
{
    private static Block CreateBlock() => new()
    {
        Index = 1,
        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000),
        Transactions = [new Transaction("alice", "bob", 10m)],
        PreviousHash = "abc123"
    };

    [Fact]
    public void SatisfiesDifficulty_ConfereOsZerosIniciais()
    {
        Assert.True(ProofOfWork.SatisfiesDifficulty("0000abc", 4));
        Assert.False(ProofOfWork.SatisfiesDifficulty("000abc", 4)); // só 3 zeros
        Assert.False(ProofOfWork.SatisfiesDifficulty("0000abc", 5)); // exige 5
    }

    [Fact]
    public void SatisfiesDifficulty_DificuldadeZero_AceitaQualquerHash()
    {
        Assert.True(ProofOfWork.SatisfiesDifficulty("abc123", 0));
    }

    [Fact]
    public void SatisfiesDifficulty_DificuldadeMaiorQueOHash_EsImpossivel()
    {
        // Hash de 4 caracteres não pode ter 5 zeros iniciais.
        Assert.False(ProofOfWork.SatisfiesDifficulty("0000", 5));
    }

    [Fact]
    public void Mine_HashSatisfazADificuldadeEVerifyConfere()
    {
        var block = CreateBlock();

        ProofOfWork.Mine(block, difficulty: 3);

        Assert.StartsWith("000", block.Hash);
        Assert.True(ProofOfWork.Verify(block, difficulty: 3));
    }

    [Fact]
    public void Mine_EhDeterministico_MesmoNonceParaMesmoBloco()
    {
        var a = CreateBlock();
        var b = CreateBlock();

        ProofOfWork.Mine(a, difficulty: 3);
        ProofOfWork.Mine(b, difficulty: 3);

        Assert.Equal(a.Nonce, b.Nonce);
        Assert.Equal(a.Hash, b.Hash);
    }

    [Fact]
    public void Verify_RejeitaBlocoComNonceAlterado()
    {
        var block = CreateBlock();
        ProofOfWork.Mine(block, difficulty: 4);
        Assert.True(ProofOfWork.Verify(block, difficulty: 4));

        block.Nonce += 1; // solução invalidada (o Hash armazenado é o antigo)

        Assert.False(ProofOfWork.Verify(block, difficulty: 4));
    }

    [Fact]
    public void Verify_RejeitaBlocoNaoMinerado()
    {
        // Dificuldade 5: chance de um bloco não minerado "passar" por acaso ~1 em 1 milhão.
        var block = CreateBlock();
        block.Nonce = 0;
        block.Hash = Hasher.ComputeHash(block);

        Assert.False(ProofOfWork.Verify(block, difficulty: 5));
    }
}
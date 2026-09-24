using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests;

public class HasherTests
{
    private static Block CreateSampleBlock() => new()
    {
        Index = 1,
        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000),
        Transactions = [new Transaction("alice", "bob", 10m)],
        PreviousHash = "abc123",
        Nonce = 0
    };

    [Fact]
    public void ComputeHash_EhDeterministico_MesmoDadosMesmoHash()
    {
        // Propriedade fundamental: qualquer nó recalcula o hash e obtém o mesmo valor.
        var block = CreateSampleBlock();

        var hash1 = Hasher.ComputeHash(block);
        var hash2 = Hasher.ComputeHash(block);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_Retorna64CaracteresHexMinúsculos()
    {
        var hash = Hasher.ComputeHash(CreateSampleBlock());

        Assert.Equal(64, hash.Length); // SHA-256 = 256 bits = 64 chars hex
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void ComputeHash_EfeitoAvalanche_QualquerMudancaAlteraOHashInteiro()
    {
        var original = Hasher.ComputeHash(CreateSampleBlock());

        var nonceDiferente = Hasher.ComputeHash(CloneWithNonce(CreateSampleBlock(), 1));
        var transacaoDiferente = Hasher.ComputeHash(new Block
        {
            Index = 1,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000),
            Transactions = [new Transaction("alice", "bob", 11m)], // só o valor mudou
            PreviousHash = "abc123",
            Nonce = 0
        });

        Assert.NotEqual(original, nonceDiferente);
        Assert.NotEqual(original, transacaoDiferente);
    }

    [Fact]
    public void ComputeHash_IgnoraOCampoHash_HashNaoParticipaDoProprioCalculo()
    {
        var block = CreateSampleBlock();
        var semHash = Hasher.ComputeHash(block);

        block.Hash = "qualquer-coisa";
        var comHash = Hasher.ComputeHash(block);

        Assert.Equal(semHash, comHash);
    }

    // Helper local: como Block é uma classe (Nonce é mutável), clonamos manualmente.
    private static Block CloneWithNonce(Block block, long nonce) => new()
    {
        Index = block.Index,
        Timestamp = block.Timestamp,
        Transactions = block.Transactions,
        PreviousHash = block.PreviousHash,
        Nonce = nonce
    };
}
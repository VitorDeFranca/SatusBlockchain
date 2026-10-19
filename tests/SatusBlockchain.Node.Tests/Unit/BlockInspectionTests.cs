using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests.Unit;

/// <summary>
/// <c>Blockchain.Inspect</c> (etapa 12): a régua do <c>AddBlock</c>, explicada item a item e
/// sem anexar nada — é ela que mostra no console por que um bloco adulterado é recusado.
/// </summary>
public class BlockInspectionTests
{
    [Fact]
    public void BlocoQueEncaixaNoTopo_SeriaAceito()
    {
        // Outra cadeia com o mesmo genesis (timestamp fixo) minera o próximo bloco desta.
        var local = new Blockchain(2);
        var block = new Blockchain(2).MineBlock([new Transaction("alice", "bob", 10m)])!;

        var inspection = local.Inspect(block);

        Assert.True(inspection.WouldAccept);
        Assert.True(inspection.HashMatches);
        Assert.True(inspection.ProofOfWork);
        Assert.True(inspection.ExtendsTip);
        Assert.False(inspection.InChain);
        Assert.Equal(block.Hash, inspection.RecalculatedHash);
    }

    [Fact]
    public void BlocoAdulterado_HashRecalculadoNaoBate()
    {
        var local = new Blockchain(2);
        var mined = new Blockchain(2).MineBlock([new Transaction("alice", "bob", 10m)])!;

        // Mesmo bloco, valor trocado depois da mineração: o Hash declarado é o antigo.
        var tampered = new Block
        {
            Index = mined.Index,
            Timestamp = mined.Timestamp,
            Transactions = [new Transaction("alice", "bob", 999m)],
            PreviousHash = mined.PreviousHash,
            Nonce = mined.Nonce,
            Hash = mined.Hash
        };

        var inspection = local.Inspect(tampered);

        Assert.False(inspection.HashMatches);
        Assert.False(inspection.WouldAccept);
        Assert.NotEqual(tampered.Hash, inspection.RecalculatedHash);
        Assert.StartsWith("Adulterado", inspection.Verdict);
    }

    [Fact]
    public void BlocoQueJaEstaNaCadeia_NaoSeriaAceitoDeNovo()
    {
        var local = new Blockchain(2);
        var mined = local.MineBlock([])!;

        var inspection = local.Inspect(mined);

        Assert.True(inspection.InChain);
        Assert.False(inspection.ExtendsTip);
        Assert.False(inspection.WouldAccept);
    }

    [Fact]
    public void BlocoOrfao_IntegroMasNaoEncaixa()
    {
        var local = new Blockchain(2);
        var other = new Blockchain(2);
        other.MineBlock([]);
        var second = other.MineBlock([])!; // índice 2: falta o 1 aqui

        var inspection = local.Inspect(second);

        Assert.True(inspection.HashMatches);
        Assert.False(inspection.ExtendsTip);
        Assert.Contains("não encaixa", inspection.Verdict);
    }

    [Fact]
    public void Inspecionar_NaoAnexa()
    {
        var local = new Blockchain(2);
        local.Inspect(new Blockchain(2).MineBlock([])!);

        Assert.Equal(1, local.Length);
    }
}

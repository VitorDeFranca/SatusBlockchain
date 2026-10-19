using SatusBlockchain.Gateway.Network;

namespace SatusBlockchain.Gateway.Tests.Unit;

/// <summary>A regra única de "quem está na cadeia líder" (convergida, atrasada, fork).</summary>
public class StatusClassifierTests
{
    [Fact]
    public void MesmaCadeia_TodosOk()
    {
        var statuses = StatusClassifier.Classify([Obs("a", 3, "x"), Obs("b", 3, "x")]);

        Assert.All(statuses.Values, status => Assert.Equal(NodeStatus.Ok, status));
    }

    [Fact]
    public void CadeiaMaisCurta_Atrasado()
    {
        var statuses = StatusClassifier.Classify([Obs("a", 3, "x"), Obs("b", 1, "g")]);

        Assert.Equal(NodeStatus.Ok, statuses["a"]);
        Assert.Equal(NodeStatus.Behind, statuses["b"]);
    }

    [Fact]
    public void CadeiaMaisCurta_ComOTopoNaLider_SoAtrasada()
    {
        // Etapa 12: o topo do nó (posição 1) está na cadeia líder — ele só perdeu blocos.
        var statuses = StatusClassifier.Classify([Obs("a", 3, "x2"), Obs("b", 2, "x1")], leaderChain: ["g", "x1", "x2"]);

        Assert.Equal(NodeStatus.Behind, statuses["b"]);
    }

    [Fact]
    public void CadeiaMaisCurta_ComOTopoForaDaLider_Fork()
    {
        // O topo "y1" não está na líder: o nó minerou num ramo próprio (partição) — fork.
        var statuses = StatusClassifier.Classify([Obs("a", 3, "x2"), Obs("b", 2, "y1")], leaderChain: ["g", "x1", "x2"]);

        Assert.Equal(NodeStatus.Diverged, statuses["b"]);
        Assert.Equal(NodeStatus.Ok, statuses["a"]);
    }

    [Fact]
    public void SemACadeiaLider_CadeiaMaisCurtaFicaAtrasada()
    {
        var statuses = StatusClassifier.Classify([Obs("a", 3, "x2"), Obs("b", 2, "y1")]);

        Assert.Equal(NodeStatus.Behind, statuses["b"]);
        Assert.Equal("a", StatusClassifier.LeaderId([Obs("a", 3, "x2"), Obs("b", 2, "y1")], statuses));
    }

    [Fact]
    public void MesmoTamanhoOutroUltimoBloco_Divergente_AMinoria()
    {
        // Fork: o hash mais comum entre as cadeias mais longas é o da líder.
        var statuses = StatusClassifier.Classify([Obs("a", 3, "x"), Obs("b", 3, "x"), Obs("c", 3, "y")]);

        Assert.Equal(NodeStatus.Ok, statuses["a"]);
        Assert.Equal(NodeStatus.Diverged, statuses["c"]);
    }

    [Fact]
    public void EmpateNoFork_DesempateEstavel()
    {
        // 1 x 1: a escolha é arbitrária, mas a mesma a cada poll (a tela não pisca).
        var first = StatusClassifier.Classify([Obs("a", 2, "y"), Obs("b", 2, "x")]);
        var second = StatusClassifier.Classify([Obs("b", 2, "x"), Obs("a", 2, "y")]);

        Assert.Equal(first["a"], second["a"]);
        Assert.Equal(NodeStatus.Ok, first["b"]);   // "x" < "y"
        Assert.Equal(NodeStatus.Diverged, first["a"]);
    }

    [Fact]
    public void GenesisDaMinoria_Incompativel_ENaoDefineALider()
    {
        // O nó de outro universo tem a cadeia mais longa, mas não é "a rede".
        var statuses = StatusClassifier.Classify(
            [Obs("a", 2, "x"), Obs("b", 2, "x"), Obs("c", 9, "z", genesis: "outro")]);

        Assert.Equal(NodeStatus.Incompatible, statuses["c"]);
        Assert.Equal(NodeStatus.Ok, statuses["a"]);
    }

    [Fact]
    public void CadeiaInvalida_Invalido()
    {
        var statuses = StatusClassifier.Classify([Obs("a", 2, "x"), Obs("b", 5, "w", valid: false)]);

        Assert.Equal(NodeStatus.Invalid, statuses["b"]);
        Assert.Equal(NodeStatus.Ok, statuses["a"]);
    }

    private static ChainObservation Obs(string id, int length, string hash, string genesis = "g", bool valid = true) =>
        new(id, genesis, new ChainView(length, hash, valid));
}

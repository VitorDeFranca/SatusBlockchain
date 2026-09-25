using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests;

public class NodeOptionsTests
{
    [Fact]
    public void ParseDifficulty_NaoInformada_UsaOPadrao()
    {
        Assert.Equal(ProofOfWork.DefaultDifficulty, NodeOptions.ParseDifficulty(null));
        Assert.Equal(ProofOfWork.DefaultDifficulty, NodeOptions.ParseDifficulty("  "));
    }

    [Fact]
    public void ParseDifficulty_ValorValido_UsaOValorInformado()
    {
        Assert.Equal(0, NodeOptions.ParseDifficulty("0"));
        Assert.Equal(6, NodeOptions.ParseDifficulty("6"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("65")]   // acima do máximo (hash tem 64 caracteres)
    [InlineData("999")]
    public void ParseDifficulty_ValorInvalido_FalhaComMensagemClara(string valor)
    {
        // Falhar na inicialização é melhor do que um nó minerando para sempre.
        var excecao = Assert.Throws<InvalidOperationException>(() => NodeOptions.ParseDifficulty(valor));
        Assert.Contains("DIFFICULTY inválida", excecao.Message);
    }
}
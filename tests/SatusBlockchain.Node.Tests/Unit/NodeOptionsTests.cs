using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Tests.Unit;

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

    [Fact]
    public void ParsePeers_NaoInformado_DevolveListaVazia()
    {
        // Nó sem peers é o caso normal do `dotnet run` e dos testes de nó único.
        Assert.Empty(NodeOptions.ParsePeers(null));
        Assert.Empty(NodeOptions.ParsePeers("  "));
        Assert.Empty(NodeOptions.ParsePeers(", ,"));
    }

    [Fact]
    public void ParsePeers_SeparadosPorVirgula_NormalizaEspacosEBarraFinal()
    {
        // A barra final sai para o push não montar "http://node2:8080//blocks/receive".
        var peers = NodeOptions.ParsePeers(" http://node2:8080 , http://node3:8080/ ");

        Assert.Equal(new[] { "http://node2:8080", "http://node3:8080" }, peers);
    }

    [Fact]
    public void ParsePeers_Repetido_EntraUmaVezSo()
    {
        // Um peer repetido geraria dois POSTs para o mesmo nó.
        var peers = NodeOptions.ParsePeers("http://node2:8080,HTTP://NODE2:8080");

        Assert.Single(peers);
    }

    [Theory]
    [InlineData("node2:8080")]        // sem esquema http/https
    [InlineData("ftp://node2:8080")]  // esquema que o push não usa
    [InlineData("http://")]           // sem host
    public void ParsePeers_ValorInvalido_FalhaComMensagemClara(string valor)
    {
        // Mesmo princípio do DIFFICULTY: configuração inválida derruba o nó na subida,
        // em vez de virar um push que nunca sai.
        var excecao = Assert.Throws<InvalidOperationException>(() => NodeOptions.ParsePeers(valor));
        Assert.Contains("PEERS inválida", excecao.Message);
    }

    [Fact]
    public void ParseAdvertiseUrl_NaoInformado_NoSemAnuncio()
    {
        // Sem ADVERTISE_URL o nó fica como nas etapas 6–9: não se anuncia nem se despede.
        Assert.Null(NodeOptions.ParseAdvertiseUrl(null));
        Assert.Null(NodeOptions.ParseAdvertiseUrl("  "));
    }

    [Fact]
    public void ParseAdvertiseUrl_NormalizaComoOsPeers()
    {
        // Mesma regra do PEERS: é por este texto que os outros nós vão reconhecer este.
        Assert.Equal("http://node4:8080", NodeOptions.ParseAdvertiseUrl(" http://node4:8080/ "));
    }

    [Theory]
    [InlineData("node4:8080")]
    [InlineData("ftp://node4:8080")]
    public void ParseAdvertiseUrl_ValorInvalido_FalhaComMensagemClara(string valor)
    {
        var excecao = Assert.Throws<InvalidOperationException>(() => NodeOptions.ParseAdvertiseUrl(valor));
        Assert.Contains("ADVERTISE_URL inválida", excecao.Message);
    }

    [Theory]
    [InlineData("http://node4:8080/", true, "http://node4:8080")]
    [InlineData("  https://node4  ", true, "https://node4")]
    [InlineData("node4:8080", false, null)]
    [InlineData("", false, null)]
    [InlineData(null, false, null)]
    public void TryNormalizeUrl_RegraUnicaDeEnderecoDeNo(string? valor, bool valido, string? esperado)
    {
        // A mesma regra vale para PEERS, ADVERTISE_URL e o corpo de POST /peers.
        Assert.Equal(valido, NodeOptions.TryNormalizeUrl(valor, out var url));
        if (valido)
            Assert.Equal(esperado, url);
    }
}
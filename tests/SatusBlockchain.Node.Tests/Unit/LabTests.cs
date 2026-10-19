using System.Net;
using SatusBlockchain.Node.Lab;
using SatusBlockchain.Node.Networking;

namespace SatusBlockchain.Node.Tests.Unit;

/// <summary>Estado do laboratório (etapa 12) e a marcação do tráfego entre nós.</summary>
public class LabTests
{
    [Fact]
    public void ComecaSemFalhas()
    {
        var lab = new LabState();

        Assert.Null(lab.Partition);
        Assert.Equal(0, lab.DelayMs);
    }

    [Theory]
    [InlineData("B")]
    [InlineData("lado-isolado_2")]
    public void Particao_GrupoValido(string group)
    {
        var lab = new LabState();
        lab.SetPartition(group);
        Assert.Equal(group, lab.Partition);
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("grupo-com-mais-de-vinte")]
    [InlineData("ç")]
    public void Particao_GrupoInvalido(string group) =>
        Assert.Throws<ArgumentException>(() => new LabState().SetPartition(group));

    [Fact]
    public void Particao_VaziaOuNula_SaiDaParticao()
    {
        var lab = new LabState();
        lab.SetPartition("B");
        lab.SetPartition("  ");
        Assert.Null(lab.Partition);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(LabState.MaxDelayMs + 1)]
    public void Atraso_ForaDaFaixa(int ms) =>
        Assert.Throws<ArgumentException>(() => new LabState().SetDelay(ms));

    [Theory]
    [InlineData(null, null, true)]   // ninguém particionado: a rede inteira
    [InlineData(null, "", true)]     // cabeçalho vazio = sem grupo
    [InlineData("B", "B", true)]
    [InlineData("B", null, false)]   // eu isolado, ele não
    [InlineData(null, "B", false)]   // ele isolado, eu não
    [InlineData("A", "B", false)]
    public void MesmoLado(string? mine, string? sender, bool expected)
    {
        var lab = new LabState();
        lab.SetPartition(mine);
        Assert.Equal(expected, lab.SameSide(sender));
    }

    [Fact]
    public void Reset_VoltaAoNormal()
    {
        var lab = new LabState();
        lab.SetPartition("B");
        lab.SetDelay(3000);

        lab.Reset();

        Assert.Null(lab.Partition);
        Assert.Equal(0, lab.DelayMs);
    }

    [Fact]
    public async Task PeerHeadersHandler_MarcaOTrafegoEOGrupo_NoMomentoDoEnvio()
    {
        var lab = new LabState();
        var captured = new List<HttpRequestMessage>();
        var handler = new PeerHeadersHandler(lab) { InnerHandler = new Capture(captured) };
        using var http = new HttpClient(handler);

        await http.GetAsync("http://node2:8080/chain");
        lab.SetPartition("B");
        await http.GetAsync("http://node2:8080/chain");

        Assert.Equal("1", captured[0].Headers.GetValues(PeerTraffic.PeerHeader).Single());
        Assert.False(captured[0].Headers.Contains(PeerTraffic.PartitionHeader)); // sem partição, sem cabeçalho
        Assert.Equal("B", captured[1].Headers.GetValues(PeerTraffic.PartitionHeader).Single());
    }

    private sealed class Capture(List<HttpRequestMessage> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}

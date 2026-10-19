using Microsoft.Extensions.Configuration;

namespace SatusBlockchain.Gateway.Tests.Unit;

public class GatewayOptionsTests
{
    [Fact]
    public void ParseNodes_NormalizaEDeduplica()
    {
        var nodes = GatewayOptions.ParseNodes(" http://localhost:5165/ , http://localhost:5166,HTTP://LOCALHOST:5165, ");

        Assert.Equal(new[] { "http://localhost:5165", "http://localhost:5166" }, nodes);
    }

    [Fact]
    public void ParseNodes_NaoInformado_ListaVazia()
    {
        // No Docker a lista vem da plataforma: GATEWAY_NODES é opcional.
        Assert.Empty(GatewayOptions.ParseNodes(null));
        Assert.Empty(GatewayOptions.ParseNodes("  "));
    }

    [Theory]
    [InlineData("localhost:5165")]
    [InlineData("ftp://node1:8080")]
    public void ParseNodes_Invalida_FalhaNaSubida(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => GatewayOptions.ParseNodes(value));
        Assert.Contains("GATEWAY_NODES inválida", error.Message);
    }

    [Theory]
    [InlineData(null, RuntimeMode.Auto)]
    [InlineData("docker", RuntimeMode.Docker)]
    [InlineData(" NONE ", RuntimeMode.None)]
    public void ParseRuntime_AceitaOsModos(string? value, RuntimeMode expected) =>
        Assert.Equal(expected, GatewayOptions.ParseRuntime(value));

    [Fact]
    public void ParseRuntime_Invalido_FalhaNaSubida() =>
        Assert.Throws<InvalidOperationException>(() => GatewayOptions.ParseRuntime("kubernetes"));

    [Fact]
    public void FromConfiguration_LinksTemPadraoDaStackDeObservabilidade()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JAEGER_URL"] = "http://jaeger.local" })
            .Build();

        var options = GatewayOptions.FromConfiguration(configuration);

        Assert.Equal("http://localhost:3000", options.Links.Grafana);
        Assert.Equal("http://jaeger.local", options.Links.Jaeger);
        Assert.Equal(GatewayOptions.DefaultDockerSocket, options.DockerSocket);
    }
}

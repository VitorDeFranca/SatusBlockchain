using System.Net;
using System.Text.RegularExpressions;

namespace SatusBlockchain.Node.Tests.Integration;

/// <summary>
/// Métricas do nó (etapa 9): <c>GET /metrics</c> no formato Prometheus, raspável sem
/// infraestrutura alguma (só HTTP). O nó de teste sobe SEM
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> — e os testes também cobrem que a ausência do
/// Jaeger não atrasa nem polui nada: o OTLP é condicional, o /metrics é pull e
/// sempre está no ar.
/// </summary>
public class MetricsTests
{
    [Fact]
    public async Task Metrics_SaoExpostosEmFormatoPrometheus()
    {
        using var node = await NodeServer.StartAsync();

        // Espera a série aparecer: o exportador tem um pequeno atraso assíncrono entre
        // a publicação do instrumento e a série ficar visível no /metrics (o Prometheus
        // raspando a cada 5s não percebe; um curl imediato pode ver o snapshot anterior).
        var body = await WaitForSeriesAsync(node, @"satus_chain_length\{[^}]*\} 1");

        // Instrumentos do domínio (satus.*) e os automáticos do ASP.NET convivem.
        Assert.Contains("satus_mempool_size", body);

        // Ainda sem fato algum, só os GAUGES têm série (eles medem o estado atual):
        // counters sem nenhuma medição não aparecem no scrape — por isso os contadores
        // são testados no teste seguinte, depois de minerar de verdade.
        Assert.Matches(@"satus_mempool_size\{[^}]*\} 0", body);

        // Valores vêm COM LABELS no meio (satus_chain_length{otel_scope_name=...} 1),
        // então a asserção casa nome, labels quaisquer e valor — não o "nome valor" puro.
        Assert.Matches(@"satus_chain_length\{[^}]*\} 1", body);
        Assert.Contains("# TYPE satus_chain_length gauge", body);
    }

    [Fact]
    public async Task Metrics_RefletemMinERecebimento()
    {
        using var node = await NodeServer.StartAsync();

        using var response = await node.Client.PostAsync("/blocks/mine", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Espera a série com o valor JÁ ATUALIZADO da mineração (mesma janela
        // assíncrona do teste anterior).
        var body = await WaitForSeriesAsync(node, @"satus_blocks_mined_total\{[^}]*\} 1");

        // O corpo é um snapshot único: se o contador já mostra a mineração, a gauge
        // do mesmo scrape mostra a cadeia com genesis + bloco = 2.
        Assert.Matches(@"satus_chain_length\{[^}]*\} 2", body);
    }

    [Fact]
    public async Task Metrics_SemEndpointOtlp_NoSobeERespondeNormalmente()
    {
        // NodeServer nunca define OTEL_EXPORTER_OTLP_ENDPOINT (e o remove do ambiente
        // herdado): sem Jaeger nenhum exportador de trace é registrado — regressão da
        // decisão de projeto "OTLP condicional". Se alguém voltar a registrar o OTLP
        // incondicionalmente, o nó passa a tentar 127.0.0.1:4317 e este teste falha.
        using var node = await NodeServer.StartAsync();

        var body = await WaitForSeriesAsync(node, "satus_blocks_mined_total");

        Assert.Contains("satus_chain_length", body);
        Assert.DoesNotContain("4317", node.Output);
    }

    /// <summary>
    /// Espera o <c>/metrics</c> mostrar a série procurada, raspando a cada 250ms — é
    /// o mesmo papel do Prometheus (que rasparia a cada 5s e nem notaria o atraso).
    /// Devolve o corpo FINAL, para que a falha de um Assert mostre o que existia.
    /// </summary>
    private static async Task<string> WaitForSeriesAsync(NodeServer node, string pattern,
        int timeoutSeconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var body = string.Empty;

        while (DateTime.UtcNow < deadline)
        {
            body = await node.Client.GetStringAsync("/metrics");
            if (Regex.IsMatch(body, pattern))
                return body;

            await Task.Delay(250);
        }

        return body;
    }
}
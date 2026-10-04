using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SatusBlockchain.Node;
using SatusBlockchain.Node.Api;
using SatusBlockchain.Node.Core;
using SatusBlockchain.Node.Networking;
using SatusBlockchain.Node.Observability;

var builder = WebApplication.CreateBuilder(args);

// Configuração do nó (NODE_ID, DIFFICULTY, PEERS), validada na inicialização.
var nodeOptions = NodeOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(nodeOptions);


builder.Services.AddSingleton(_ => new Blockchain(nodeOptions.Difficulty));
builder.Services.AddSingleton<Mempool>();
builder.Services.AddSingleton<NodeTelemetry>();

builder.Services.AddHttpClient<PeerClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(2);
});

// ---- Observabilidade: OpenTelemetry ----
//   - MÉTRICAS → Prometheus, via pull: o AddPrometheusExporter() publica GET /metrics
//     e o Prometheus raspa.;
//
//   - TRACES → Jaeger, via push OTLP: só quando OTEL_EXPORTER_OTLP_ENDPOINT existe
//     (sob o overlay de observabilidade).

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(nodeOptions.NodeId))
    .WithTracing(tracing =>
    {
        // Traces próprios (NodeTelemetry) + automáticos (ASP.NET Core e HttpClient)
        tracing.AddSource(NodeTelemetry.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options =>
                options.BatchExportProcessorOptions.ScheduledDelayMilliseconds = 1000);
        }
    })
    .WithMetrics(metrics => metrics
        // Métricas próprias (satus.*) + automáticas (duração de requisições/HTTP),
        // exportadas por pull — o /metrics responde mesmo sem Jaeger/Prometheus.
        .AddMeter(NodeTelemetry.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddPrometheusExporter());

var app = builder.Build();

// Materializa a telemetria já na inicialização do nó.
app.Services.GetRequiredService<NodeTelemetry>();

// Identidade e situação do nó — útil para conferir a demo.
app.MapGet("/", (NodeOptions node, Blockchain blockchain) => Results.Ok(new
{
    node = node.NodeId,
    difficulty = node.Difficulty,
    blocks = blockchain.Length,
    valid = blockchain.IsValid()
}));

app.MapChainEndpoints();
app.MapTransactionEndpoints();
app.MapBlockEndpoints();
app.MapPeerEndpoints();
app.MapSyncEndpoints();

app.MapPrometheusScrapingEndpoint("/metrics");

app.Run();


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

// Configuração do nó (NODE_ID, DIFFICULTY, PEERS, ADVERTISE_URL), validada na inicialização.
var nodeOptions = NodeOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(nodeOptions);


builder.Services.AddSingleton(_ => new Blockchain(nodeOptions.Difficulty));
builder.Services.AddSingleton<Mempool>();
builder.Services.AddSingleton<NodeTelemetry>();

// Etapa 10: a lista VIVA de peers (sementes de PEERS + quem entra/sai em execução).
builder.Services.AddSingleton<PeerRegistry>();

builder.Services.AddHttpClient<PeerClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(2);
});

// O pull (etapa 8), compartilhado pelo POST /sync e pela entrada do nó na rede.
builder.Services.AddTransient<ChainSynchronizer>();

// Entrada (ao subir) e despedida (ao desligar). Singleton registrado também como hosted
// service: o mesmo objeto atende o ciclo de vida do processo e o reanúncio do POST /sync.
builder.Services.AddSingleton<MembershipService>();
builder.Services.AddHostedService(services => services.GetRequiredService<MembershipService>());

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

// Identidade e situação do nó — útil para conferir a demo. É também o que o handshake da
// etapa 10 lê (NodeIdentity): o genesis diz se um nó novo pertence a esta rede.
app.MapGet("/", (NodeOptions node, Blockchain blockchain) => Results.Ok(new
{
    node = node.NodeId,
    difficulty = node.Difficulty,
    genesis = blockchain.GenesisHash,
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


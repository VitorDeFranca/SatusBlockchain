using System.Diagnostics;
using System.Diagnostics.Metrics;
using SatusBlockchain.Node.Core;

namespace SatusBlockchain.Node.Observability;

/// <summary>
/// Telemetria do nós: os DOIS pilares de instrumentação que faltavam —
/// <b>métricas</b> (contadores + gauges via <c>Meter</c>) e <b>traces</b> (spans via
/// <c>ActivitySource</c>). Os logs (terceiro pilar) já existem via <c>ILogger</c>.
///
/// As APIs usadas aqui são da BCL (.NET), não do SDK do OpenTelemetry: instrumentar
/// fica desacoplado de exportar. Quem coleta e exporta é o pipeline configurado no
/// <c>Program.cs</c> — Prometheus para métricas (pull em <c>GET /metrics</c>) e
/// OTLP/Jaeger para traces (push, só quando <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> existe).
/// </summary>
public class NodeTelemetry
{
    /// <summary>Nome da fonte de spans próprios — registrado no Program.cs via AddSource.</summary>
    public const string ActivitySourceName = "SatusBlockchain.Node";

    /// <summary>Nome do Meter — registrado no Program.cs via AddMeter.</summary>
    public const string MeterName = "SatusBlockchain.Node";

    /// <summary>
    /// O <c>Meter</c> DESTA instância. Exposto para os testes de unidade filtrarem os
    /// instrumentos da instância sob teste (várias coexistem no processo de teste com
    /// o mesmo nome). Em produção ninguém usa a propriedade: o Program.cs registra
    /// <see cref="MeterName"/> no pipeline do OpenTelemetry, que coleta por nome.
    /// </summary>
    public Meter Meter => _meter;

    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly ActivitySource _activitySource = new(ActivitySourceName, "1.0.0");

    private readonly Counter<long> _blocksMined;
    private readonly Counter<long> _blocksReceived;
    private readonly Counter<long> _peerPushes;
    private readonly Counter<long> _peerPulls;
    private readonly Counter<long> _syncs;
    private readonly Counter<long> _txsReceived;

    public NodeTelemetry(Blockchain blockchain, Mempool mempool)
    {
        // ---- Counters (acumulam ao longo da vida do processo) ----

        _blocksMined = _meter.CreateCounter<long>(
            "satus.blocks.mined", description: "Blocos minerados por este nó.");

        _blocksReceived = _meter.CreateCounter<long>(
            "satus.blocks.received",
            description: "Blocos recebidos de peers (outcome = accepted | rejected).");

        _peerPushes = _meter.CreateCounter<long>(
            "satus.peer.push",
            description: "Envios de bloco/transação para peers " +
                         "(kind = block | transaction; outcome = accepted | refused | unreachable).");

        _peerPulls = _meter.CreateCounter<long>(
            "satus.peer.pull",
            description: "Leituras de cadeia de peers no POST /sync " +
                         "(outcome = read | unreachable | invalid).");

        _syncs = _meter.CreateCounter<long>(
            "satus.sync",
            description: "Execuções de POST /sync (outcome = adopted | kept).");

        _txsReceived = _meter.CreateCounter<long>(
            "satus.txs.received",
            description: "Transações aceitas/recusadas na mempool " +
                         "(source = client | peer; outcome = accepted | rejected).");

        // ---- Gauges
        _meter.CreateObservableGauge(
            "satus.chain.length", () => blockchain.Length,
            description: "Tamanho atual da cadeia local (inclui o genesis).");

        _meter.CreateObservableGauge(
            "satus.mempool.size", () => mempool.Count,
            description: "Transações pendentes na mempool local.");
    }

    /// <summary>POST /blocks/mine concluiu com sucesso.</summary>
    public void BlockMined() => _blocksMined.Add(1);

    /// <summary>POST /blocks/receive avaliou um bloco recebido de um peer.</summary>
    public void BlockReceived(bool accepted) =>
        _blocksReceived.Add(1, Tag("outcome", accepted ? "accepted" : "rejected"));

    /// <summary>Envio de bloco (push) ou transação (gossip) para um peer, com desfecho.</summary>
    public void PeerPush(string kind, bool accepted, bool unreachable) =>
        _peerPushes.Add(1,
            new("kind", kind),
            new("outcome", accepted ? "accepted" : unreachable ? "unreachable" : "refused"));

    /// <summary>Leitura da cadeia de um peer dentro do POST /sync.</summary>
    public void PeerPull(string outcome) =>
        _peerPulls.Add(1, Tag("outcome", outcome));

    /// <summary>POST /sync terminou: adopted (trocou de cadeia) ou kept (local era a mais longa).</summary>
    public void Sync(string outcome) =>
        _syncs.Add(1, Tag("outcome", outcome));

    /// <summary>Transação avaliada na mempool, vinda do cliente ou de um peer (gossip).</summary>
    public void TransactionReceived(bool fromPeer, bool accepted) =>
        _txsReceived.Add(1,
            new("source", fromPeer ? "peer" : "client"),
            new("outcome", accepted ? "accepted" : "rejected"));

    /// <summary>
    /// Cria um span próprio (ex.: <c>mine</c>, <c>sync</c>). Vira filho automático do span
    /// da requisição em andamento e vira PAI dos spans de saída (HttpClient) — daí o
    /// trace de um <c>POST /blocks/mine</c> encadear os três nós pelo traceparent.
    /// </summary>
    public Activity? StartActivity(string name) => _activitySource.StartActivity(name);

    /// <summary>
    /// Par (chave, valor) de uma tag. O tipo explícito evita a ambiguidade do C# entre
    /// <c>Add(1, umaTag)</c> e <c>Add(1, params tags[])</c> quando o argumento é um
    /// <c>new()</c> sem tipo declarado (target-typed new não desempata sobrecargas).
    /// </summary>
    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);
}
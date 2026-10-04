using System.Diagnostics;
using SatusBlockchain.Node.Core;
using SatusBlockchain.Node.Observability;

namespace SatusBlockchain.Node.Tests.Unit;

/// <summary>
/// Testes de unidade da telemetria (etapa 9): contadores e gauges observados com
/// <c>MeterListener</c>, spans com <c>ActivityListener</c> — as MESMAS APIs da BCL que
/// o OpenTelemetry consome, sem subir servidor e sem exporter.
/// </summary>
public class NodeTelemetryTests
{
    [Fact]
    public void Contadores_AcumulamPorInstrumento()
    {
        var telemetry = Telemetry();
        using var spy = new MetricSpy(telemetry);

        telemetry.BlockMined();
        telemetry.BlockMined();
        telemetry.BlockReceived(accepted: true);
        telemetry.BlockReceived(accepted: false);

        Assert.Equal(2, spy.Total("satus.blocks.mined"));
        Assert.Equal(1, spy.TotalWithTags("satus.blocks.received", ("outcome", "accepted")));
        Assert.Equal(1, spy.TotalWithTags("satus.blocks.received", ("outcome", "rejected")));
    }

    [Fact]
    public void Contadores_CarregamTagsDeDominioFechado()
    {
        var telemetry = Telemetry();
        using var spy = new MetricSpy(telemetry);

        telemetry.PeerPush("block", accepted: false, unreachable: false);
        telemetry.PeerPush("transaction", accepted: false, unreachable: true);
        telemetry.PeerPull("unreachable");
        telemetry.Sync("adopted");
        telemetry.TransactionReceived(fromPeer: true, accepted: false);

        // Cada tag é um domínio fechado (outcome/kind/source) — a lição da
        // cardinalidade baixa do Prometheus virasse teste se alguém a quebrasse.
        Assert.Equal(1, spy.TotalWithTags("satus.peer.push",
            ("kind", "block"), ("outcome", "refused")));
        Assert.Equal(1, spy.TotalWithTags("satus.peer.push",
            ("kind", "transaction"), ("outcome", "unreachable")));
        Assert.Equal(1, spy.TotalWithTags("satus.peer.pull", ("outcome", "unreachable")));
        Assert.Equal(1, spy.TotalWithTags("satus.sync", ("outcome", "adopted")));
        Assert.Equal(1, spy.TotalWithTags("satus.txs.received",
            ("source", "peer"), ("outcome", "rejected")));
    }

    [Fact]
    public void Gauges_LenCadeiaEMempoolAtuais()
    {
        var blockchain = new Blockchain(2);
        var mempool = new Mempool();
        var telemetry = new NodeTelemetry(blockchain, mempool);
        using var spy = new MetricSpy(telemetry);

        mempool.Add(new Transaction("alice", "bob", 10m));
        mempool.Add(new Transaction("bob", "carol", 5m));
        spy.RecordObservables();

        // Diferente dos contadores, gauge não acumula: devolve o estado ATUAL
        // (1 = só o genesis; 2 = as duas transações pendentes).
        Assert.Equal(1, spy.Latest("satus.chain.length"));
        Assert.Equal(2, spy.Latest("satus.mempool.size"));
    }

    [Fact]
    public void StartActivity_CriaSpanProprioComONomeDaOperacao()
    {
        var telemetry = Telemetry();
        Activity? started = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NodeTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => started = activity,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = telemetry.StartActivity("mine");

        Assert.NotNull(activity);
        Assert.Equal("mine", activity!.OperationName);
        Assert.Same(activity, started);
    }

    /// <summary>
    /// Instância barata: genesis na dificuldade 2 e sem exportador — suficiente para
    /// exercitar contadores/gauges sem I/O algum.
    /// </summary>
    private static NodeTelemetry Telemetry() => new(new Blockchain(2), new Mempool());
}
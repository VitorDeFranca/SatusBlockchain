using System.Diagnostics.Metrics;
using SatusBlockchain.Node.Observability;

namespace SatusBlockchain.Node.Tests.Unit;

/// <summary>
/// Espião de métricas para testes de unidade (etapa 9): assina o <c>Meter</c> de UMA
/// instância de <see cref="NodeTelemetry"/> e grava cada medição publicada. Sem pacote
/// extra — <c>MeterListener</c> é a API da BCL feita exatamente para isso (é o mesmo
/// mecanismo que o OpenTelemetry usa por baixo para coletar).
///
/// O filtro é por <b>instância</b> (referência do Meter) e não por nome: várias
/// instâncias de NodeTelemetry coexistem no processo de teste com o mesmo nome de
/// Meter, e classes de teste rodam em paralelo — filtrar por nome cruzaria medições.
/// </summary>
internal sealed class MetricSpy : IDisposable
{
    private readonly MeterListener _listener = new();

    public List<Measurement> Measurements { get; } = [];

    public MetricSpy(NodeTelemetry telemetry)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, telemetry.Meter))
                listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            Capture(instrument, value, tags));

        // Gauges criados com () => blockchain.Length inferem T=int (Length é int):
        // sem este callback, medições inteiras jamais chegariam ao espião.
        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            Capture(instrument, value, tags));

        // Start() notifica os instrumentos já publicados — por isso o spy pode ser
        // criado DEPOIS da telemetria existir.
        _listener.Start();
    }

    private void Capture(Instrument instrument, long value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
            map[tag.Key] = tag.Value;

        lock (Measurements)
            Measurements.Add(new Measurement(instrument.Name, value, map));
    }

    /// <summary>Soma de todas as medições de um instrumento (contadores somam por chamada).</summary>
    public long Total(string instrumentName)
    {
        lock (Measurements)
            return Measurements.Where(m => m.Name == instrumentName).Sum(m => m.Value);
    }

    /// <summary>Soma apenas as medições que batem com TODAS as tags informadas (igualdade de string).</summary>
    public long TotalWithTags(string instrumentName, params (string Key, string Expected)[] tags)
    {
        lock (Measurements)
        {
            return Measurements
                .Where(m => m.Name == instrumentName
                    && tags.All(t => m.Tags.TryGetValue(t.Key, out var value)
                        && string.Equals(value?.ToString(), t.Expected, StringComparison.Ordinal)))
                .Sum(m => m.Value);
        }
    }

    /// <summary>Última medição registrada — é o que se espera dos gauges observáveis.</summary>
    public long Latest(string instrumentName)
    {
        lock (Measurements)
            return Measurements.Last(m => m.Name == instrumentName).Value;
    }

    /// <summary>Força a coleta dos instrumentos observáveis (gauges) agora.</summary>
    public void RecordObservables() => _listener.RecordObservableInstruments();

    public void Dispose() => _listener.Dispose();

    public sealed record Measurement(string Name, long Value, IReadOnlyDictionary<string, object?> Tags);
}
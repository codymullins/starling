namespace Tessera.Telemetry;

/// <summary>
/// First-paint snapshot returned by <see cref="TelemetryStream.Snapshot"/>.
/// DevTools panels render this immediately on open, then subscribe to live
/// records via the matching channel-reader on <see cref="TelemetryStream"/>.
/// </summary>
public sealed record TelemetrySnapshot(
    IReadOnlyList<LogRecord> Logs,
    IReadOnlyList<ActivityRecord> Activities,
    IReadOnlyList<MeterRecord> Metrics);

/// <summary>
/// DI-resolved facade over the three in-memory sinks. DevTools resolves this
/// from the service provider; tests and headless callers can construct it
/// directly with custom sinks. Disposal flows to the underlying sinks.
/// </summary>
public sealed class TelemetryStream : IDisposable
{
    public InMemoryLogSink Logs { get; }
    public InMemoryActivitySink Activities { get; }
    public InMemoryMeterSink Metrics { get; }

    public TelemetryStream(InMemoryLogSink logs, InMemoryActivitySink activities, InMemoryMeterSink metrics)
    {
        Logs = logs;
        Activities = activities;
        Metrics = metrics;
    }

    public TelemetrySnapshot Snapshot()
        => new(Logs.Snapshot(), Activities.Snapshot(), Metrics.Snapshot());

    public void Dispose()
    {
        Logs.Dispose();
        Activities.Dispose();
        Metrics.Dispose();
    }
}

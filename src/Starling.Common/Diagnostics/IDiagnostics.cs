namespace Tessera.Common.Diagnostics;

public enum DiagLevel
{
    Trace,
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// Single sink for engine logs, traces, counters, and snapshots. Concrete
/// implementations live in their consumers (Console for dev, Noop for perf,
/// DevTools later). See 01_ARCHITECTURE.md §H.
/// </summary>
public interface IDiagnostics
{
    void Log(DiagLevel level, string area, string message);
    IDisposable Span(string area, string operation);
    void Counter(string name, double value);
    void Snapshot(string label, ReadOnlySpan<byte> bytes);
}

public sealed class NoopDiagnostics : IDiagnostics
{
    public static readonly NoopDiagnostics Instance = new();

    public void Log(DiagLevel level, string area, string message) { }
    public IDisposable Span(string area, string operation) => NoopSpan.Instance;
    public void Counter(string name, double value) { }
    public void Snapshot(string label, ReadOnlySpan<byte> bytes) { }

    private sealed class NoopSpan : IDisposable
    {
        public static readonly NoopSpan Instance = new();
        public void Dispose() { }
    }
}

public sealed class ConsoleDiagnostics : IDiagnostics
{
    public DiagLevel MinLevel { get; init; } = DiagLevel.Info;

    public void Log(DiagLevel level, string area, string message)
    {
        if (level < MinLevel) return;
        Console.Error.WriteLine($"[{level,-5}] {area}: {message}");
    }

    public IDisposable Span(string area, string operation)
    {
        Log(DiagLevel.Trace, area, $"+ {operation}");
        return new TraceSpan(this, area, operation);
    }

    public void Counter(string name, double value)
        => Log(DiagLevel.Debug, "counter", $"{name}={value}");

    public void Snapshot(string label, ReadOnlySpan<byte> bytes)
        => Log(DiagLevel.Debug, "snapshot", $"{label} ({bytes.Length} bytes)");

    private sealed class TraceSpan(ConsoleDiagnostics owner, string area, string operation) : IDisposable
    {
        private readonly long _started = Environment.TickCount64;
        public void Dispose()
        {
            var elapsed = Environment.TickCount64 - _started;
            owner.Log(DiagLevel.Trace, area, $"- {operation} ({elapsed}ms)");
        }
    }
}

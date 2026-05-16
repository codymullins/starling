using Tessera.Common.Diagnostics;

namespace Tessera.Telemetry;

/// <summary>
/// Fans <see cref="IDiagnostics"/> calls to multiple inner sinks. Used by the
/// Headless CLI to tee output to <see cref="ConsoleDiagnostics"/> (so plain
/// <c>dotnet run</c> still shows pipeline traces on stderr) and
/// <see cref="OtelDiagnostics"/> (so the Aspire dashboard sees them too).
/// </summary>
public sealed class CompositeDiagnostics : IDiagnostics
{
    private readonly IDiagnostics[] _inner;

    public CompositeDiagnostics(params IDiagnostics[] inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public void Log(DiagLevel level, string area, string message)
    {
        foreach (var d in _inner) d.Log(level, area, message);
    }

    public IDisposable Span(string area, string operation)
    {
        var spans = new IDisposable[_inner.Length];
        for (var i = 0; i < _inner.Length; i++)
            spans[i] = _inner[i].Span(area, operation);
        return new CompositeSpan(spans);
    }

    public void Counter(string name, double value)
    {
        foreach (var d in _inner) d.Counter(name, value);
    }

    public void Snapshot(string label, ReadOnlySpan<byte> bytes)
    {
        // ReadOnlySpan can't cross the foreach lambda barrier; manual loop.
        for (var i = 0; i < _inner.Length; i++)
            _inner[i].Snapshot(label, bytes);
    }

    private sealed class CompositeSpan(IDisposable[] spans) : IDisposable
    {
        public void Dispose()
        {
            // Dispose in reverse order so the outermost OTel-style scope
            // closes after any console end-of-span print.
            for (var i = spans.Length - 1; i >= 0; i--)
                spans[i].Dispose();
        }
    }
}

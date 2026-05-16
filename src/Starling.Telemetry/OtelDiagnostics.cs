using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Tessera.Common.Diagnostics;

namespace Tessera.Telemetry;

/// <summary>
/// Bridges <see cref="IDiagnostics"/> calls (Span/Log/Counter/Snapshot) to the
/// OpenTelemetry primitives that <see cref="OtelBootstrap"/> wires up:
/// <see cref="ActivitySource"/>, <see cref="Meter"/>, and
/// <see cref="ILogger"/>. Aspire (or any OTLP collector) sees the result as
/// proper traces, metrics, and structured logs without engine code referencing
/// OpenTelemetry types directly.
/// </summary>
public sealed class OtelDiagnostics : IDiagnostics
{
    /// <summary>Single source name for engine/layout/paint/css spans. The
    /// <see cref="OtelBootstrap"/> tracer registers this via AddSource so the
    /// SDK actually exports them.</summary>
    public const string SourceName = "Starling.Engine";

    internal static readonly ActivitySource Source = new(SourceName);
    internal static readonly Meter Meter = new(SourceName);

    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Counter<double>> _counters = new(StringComparer.Ordinal);

    public OtelDiagnostics(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
    }

    public IDisposable Span(string area, string operation)
    {
        // Activity is itself IDisposable; when no listener is attached
        // StartActivity returns null so we hand back a no-op disposable to
        // keep the IDiagnostics contract non-null.
        var activity = Source.StartActivity($"{area}.{operation}", ActivityKind.Internal);
        return activity ?? (IDisposable)NoopSpan.Instance;
    }

    public void Log(DiagLevel level, string area, string message)
    {
        var logger = _loggers.GetOrAdd(area, a => _loggerFactory.CreateLogger($"Starling.{a}"));
        logger.Log(MapLevel(level), message);

        var current = Activity.Current;
        if (current is not null)
        {
            // Span events show inline in the Aspire trace timeline; useful
            // for warnings/info that happen mid-phase.
            var tags = new ActivityTagsCollection
            {
                { "level", level.ToString() },
                { "area", area },
            };
            current.AddEvent(new ActivityEvent(message, tags: tags));

            if (level == DiagLevel.Error)
                current.SetStatus(ActivityStatusCode.Error, message);
        }
    }

    public void Counter(string name, double value)
    {
        var counter = _counters.GetOrAdd(name, n => Meter.CreateCounter<double>(n));
        counter.Add(value);
    }

    public void Snapshot(string label, ReadOnlySpan<byte> bytes)
    {
        var current = Activity.Current;
        if (current is null) return;
        var tags = new ActivityTagsCollection { { "bytes", bytes.Length } };
        current.AddEvent(new ActivityEvent($"snapshot:{label}", tags: tags));
    }

    private static LogLevel MapLevel(DiagLevel level) => level switch
    {
        DiagLevel.Trace => LogLevel.Trace,
        DiagLevel.Debug => LogLevel.Debug,
        DiagLevel.Info => LogLevel.Information,
        DiagLevel.Warn => LogLevel.Warning,
        DiagLevel.Error => LogLevel.Error,
        _ => LogLevel.Information,
    };

    private sealed class NoopSpan : IDisposable
    {
        public static readonly NoopSpan Instance = new();
        public void Dispose() { }
    }
}

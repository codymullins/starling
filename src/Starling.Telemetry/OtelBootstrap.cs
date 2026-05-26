using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Starling.Common.Diagnostics;

namespace Starling.Telemetry;

/// <summary>
/// One-stop OpenTelemetry wiring for Starling host processes. Both flavours
/// export traces, metrics, and logs over OTLP when
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (Aspire's AppHost sets it
/// automatically for every project resource it launches); without the env
/// var the providers are wired but exporters drop on the floor, so this
/// is safe to call unconditionally.
/// </summary>
public static class OtelBootstrap
{
    /// <summary>
    /// Wire OpenTelemetry into a <see cref="IHostApplicationBuilder"/>-shaped
    /// host (<c>MauiAppBuilder</c>, <c>HostApplicationBuilder</c>,
    /// <c>WebApplicationBuilder</c>). Uses the framework's logging/metrics
    /// builders so anything the app already logs through
    /// <see cref="ILogger"/> flows out as OTel log records, and registers
    /// <see cref="IDiagnostics"/> as a singleton so engine code can resolve
    /// it via DI.
    /// </summary>
    public static TBuilder AddStarlingTelemetry<TBuilder>(this TBuilder builder, string serviceName)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        // The in-memory log sink is its own ILoggerProvider, registered
        // alongside OpenTelemetry's so DevTools' ConsolePanel can read recent
        // entries even when no OTLP endpoint is configured.
        var logSink = new InMemoryLogSink();
        builder.Logging.AddProvider(logSink);
        // DevTools' ConsolePanel shows page console.* output (routed via the
        // engine under "Starling.engine.js"). Open that category down to Debug
        // for the in-memory sink only so console.debug survives the default
        // Information floor; other providers/categories keep their defaults.
        builder.Logging.AddFilter<InMemoryLogSink>("Starling.engine.js", LogLevel.Debug);
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(rb => rb.AddService(serviceName))
            .WithTracing(t => t
                .AddSource(serviceName)
                .AddSource(OtelDiagnostics.SourceName)
                .AddHttpClientInstrumentation())
            .WithMetrics(m => m
                .AddMeter(OtelDiagnostics.SourceName)
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (HasOtlpEndpoint())
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        builder.Services.AddSingleton<IDiagnostics>(sp =>
            new OtelDiagnostics(sp.GetRequiredService<ILoggerFactory>()));

        // In-memory sinks feeding DevTools' ConsolePanel / PerformancePanel /
        // InternalsPanel. The activity and meter listeners self-register at
        // construction; coexists with the OTel SDK's own listeners (multiple
        // ActivityListeners per source are allowed). One TelemetryStream
        // facade aggregates the three for DevTools to subscribe to.
        builder.Services.AddSingleton(logSink);
        builder.Services.AddSingleton(_ =>
            new InMemoryActivitySink(serviceName, OtelDiagnostics.SourceName));
        builder.Services.AddSingleton(_ =>
            new InMemoryMeterSink(OtelDiagnostics.SourceName));
        builder.Services.AddSingleton<TelemetryStream>();

        return builder;
    }

    /// <summary>
    /// Wire OpenTelemetry for a plain <c>Main</c>-style console app that
    /// doesn't go through <see cref="IHostApplicationBuilder"/>. Returns a
    /// disposable handle that flushes and shuts down the providers and
    /// exposes both the <see cref="ILoggerFactory"/> and a ready-to-use
    /// <see cref="IDiagnostics"/> — store it in a <c>using</c> at the top
    /// of <c>Main</c> so traces/metrics aren't dropped on exit.
    ///
    /// Pass <paramref name="withInMemorySinks"/> = true to additionally
    /// build the same three ring-buffer sinks <see cref="AddStarlingTelemetry"/>
    /// registers via DI — needed when the host wants to read its own
    /// telemetry back (e.g. to serve it over MCP).
    /// </summary>
    public static OtelHandle Initialize(string serviceName, bool withInMemorySinks = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var resource = ResourceBuilder.CreateDefault().AddService(serviceName);
        var hasOtlp = HasOtlpEndpoint();

        InMemoryActivitySink? activitySink = null;
        InMemoryMeterSink? meterSink = null;
        InMemoryLogSink? logSink = null;
        if (withInMemorySinks)
        {
            // Construct the sinks before the providers. ActivitySink and
            // MeterSink self-register their listeners at construction; the
            // log sink is wired as an ILoggerProvider on the factory below.
            activitySink = new InMemoryActivitySink(serviceName, OtelDiagnostics.SourceName);
            meterSink = new InMemoryMeterSink(OtelDiagnostics.SourceName);
            logSink = new InMemoryLogSink();
        }

        var tracerBuilder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(serviceName)
            .AddSource(OtelDiagnostics.SourceName)
            .AddHttpClientInstrumentation();
        if (hasOtlp) tracerBuilder.AddOtlpExporter();
        var tracerProvider = tracerBuilder.Build();

        var meterBuilder = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter(OtelDiagnostics.SourceName)
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();
        if (hasOtlp) meterBuilder.AddOtlpExporter();
        var meterProvider = meterBuilder.Build();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            if (logSink is not null)
            {
                builder.AddProvider(logSink);
                // Mirror AddStarlingTelemetry: open Starling.engine.js (page
                // console output) down to Debug for the in-memory sink only.
                builder.AddFilter<InMemoryLogSink>("Starling.engine.js", LogLevel.Debug);
            }
            builder.AddOpenTelemetry(logging =>
            {
                logging.SetResourceBuilder(resource);
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
                if (hasOtlp) logging.AddOtlpExporter();
            });
        });

        TelemetryStream? telemetryStream = null;
        if (withInMemorySinks)
        {
            // Non-null when withInMemorySinks is true (assigned above).
            telemetryStream = new TelemetryStream(logSink!, activitySink!, meterSink!);
        }

        return new OtelHandle(tracerProvider, meterProvider, loggerFactory, telemetryStream);
    }

    private static bool HasOtlpEndpoint()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

    public sealed class OtelHandle : IDisposable
    {
        private readonly TracerProvider _tracer;
        private readonly MeterProvider _meter;
        private readonly TelemetryStream? _telemetryStream;

        internal OtelHandle(
            TracerProvider tracer,
            MeterProvider meter,
            ILoggerFactory loggerFactory,
            TelemetryStream? telemetryStream)
        {
            _tracer = tracer;
            _meter = meter;
            _telemetryStream = telemetryStream;
            LoggerFactory = loggerFactory;
            Diagnostics = new OtelDiagnostics(loggerFactory);
        }

        public ILoggerFactory LoggerFactory { get; }
        public IDiagnostics Diagnostics { get; }

        /// <summary>
        /// Non-null when <see cref="Initialize"/> was called with
        /// <c>withInMemorySinks: true</c>. Hosts an in-memory ring-buffer
        /// snapshot of recent spans/logs/metrics — useful for serving the
        /// process's own telemetry over MCP without a separate exporter.
        /// </summary>
        public TelemetryStream? TelemetryStream => _telemetryStream;

        public void Dispose()
        {
            // Disposal order matters: flush exporters via the providers'
            // disposal hooks before tearing the logger factory down.
            _tracer.Dispose();
            _meter.Dispose();
            _telemetryStream?.Dispose();
            LoggerFactory.Dispose();
        }
    }
}

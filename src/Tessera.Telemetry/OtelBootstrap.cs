using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Tessera.Telemetry;

/// <summary>
/// One-stop OpenTelemetry wiring for Tessera host processes. Both flavours
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
    /// <see cref="ILogger"/> flows out as OTel log records.
    /// </summary>
    public static TBuilder AddTesseraTelemetry<TBuilder>(this TBuilder builder, string serviceName)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(rb => rb.AddService(serviceName))
            .WithTracing(t => t
                .AddSource(serviceName)
                .AddHttpClientInstrumentation())
            .WithMetrics(m => m
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (HasOtlpEndpoint())
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        return builder;
    }

    /// <summary>
    /// Wire OpenTelemetry for a plain <c>Main</c>-style console app that
    /// doesn't go through <see cref="IHostApplicationBuilder"/>. Returns a
    /// disposable that flushes and shuts down the providers — store it in a
    /// <c>using</c> at the top of <c>Main</c> so traces/metrics aren't
    /// dropped on exit.
    /// </summary>
    public static IDisposable Initialize(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var resource = ResourceBuilder.CreateDefault().AddService(serviceName);
        var hasOtlp = HasOtlpEndpoint();

        var tracerBuilder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(serviceName)
            .AddHttpClientInstrumentation();
        if (hasOtlp) tracerBuilder.AddOtlpExporter();
        var tracerProvider = tracerBuilder.Build();

        var meterBuilder = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();
        if (hasOtlp) meterBuilder.AddOtlpExporter();
        var meterProvider = meterBuilder.Build();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddOpenTelemetry(logging =>
            {
                logging.SetResourceBuilder(resource);
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
                if (hasOtlp) logging.AddOtlpExporter();
            });
        });

        return new ProviderHandle(tracerProvider, meterProvider, loggerFactory);
    }

    private static bool HasOtlpEndpoint()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));

    private sealed class ProviderHandle(
        TracerProvider tracer,
        MeterProvider meter,
        ILoggerFactory loggerFactory) : IDisposable
    {
        public void Dispose()
        {
            // Disposal order matters: flush exporters via the providers'
            // disposal hooks before tearing the logger factory down.
            tracer.Dispose();
            meter.Dispose();
            loggerFactory.Dispose();
        }
    }
}

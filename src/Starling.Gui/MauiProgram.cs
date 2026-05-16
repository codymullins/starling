using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Tessera.Gui.Imaging;
using Tessera.Gui.Mcp;
using Tessera.Gui.Theme;
using Tessera.Telemetry;
#if MACCATALYST
using Tessera.Gui.Platforms.MacCatalyst;
#endif

namespace Tessera.Gui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        // Logs/traces/metrics flow to the OTLP endpoint Aspire passes in
        // via OTEL_EXPORTER_OTLP_ENDPOINT when this app is launched as an
        // Aspire resource. Running directly (without Aspire) silently
        // drops the exports — same wiring, no-op exporter.
        builder.AddTesseraTelemetry("starling-gui");

#if MACCATALYST
        // Page-bitmap path: render at physical pixels and hand the result to
        // UIImage with the correct .Scale so MAUI doesn't resample on display.
        builder.ConfigureImageSources(c =>
            c.AddService<IRgbaImageSource, RgbaImageSourceService>());
#endif

        // The active theme / density / type selection is process-wide — one
        // instance drives every chrome and devtools widget.
        builder.Services.AddSingleton<ThemeManager>();
        builder.Services.AddSingleton<BrowserControlBridge>();
        builder.Services.AddSingleton<GuiMcpServer>();

        // MainPage resolves IDiagnostics + ThemeManager from DI; register it as
        // transient so App's CreateWindow can pull a fresh instance each time.
        builder.Services.AddTransient<MainPage>();

        var app = builder.Build();

        // MAUI's IHost on Mac Catalyst does NOT auto-start hosted services
        // (long-standing limitation — see dotnet/maui#2244). The OTel
        // TracerProvider and MeterProvider are registered as IHostedService
        // by OpenTelemetry.Extensions.Hosting and only build inside
        // StartAsync — so without a nudge here, no ActivityListener attaches
        // to our ActivitySource and every span goes to /dev/null. (Logs
        // still flow because OpenTelemetryLoggerProvider is a plain
        // ILoggerProvider and builds eagerly when ILoggerFactory does.)
        // Resolving the providers from DI forces their build closures to
        // run, attaching the SDK and the OTLP exporter for real.
        _ = app.Services.GetService<TracerProvider>();
        _ = app.Services.GetService<MeterProvider>();

        // Diagnostic line written on first launch — gives an immediate signal
        // in stdout (and Aspire's resource console) whether OTLP wiring is
        // actually live or whether the exporter is a no-op for this run.
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        var startupLog = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Starling.Gui.Startup");
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            startupLog.LogInformation("OTLP exporter live → {Endpoint}", otlpEndpoint);
        else
            startupLog.LogInformation("OTEL_EXPORTER_OTLP_ENDPOINT not set; telemetry exporter is a no-op.");

        app.Services.GetRequiredService<GuiMcpServer>()
            .StartAsync()
            .GetAwaiter()
            .GetResult();
        return app;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Tessera.Gui.Theme;
using Tessera.Telemetry;

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
        builder.AddTesseraTelemetry("tessera-gui");

        // The active theme / density / type selection is process-wide — one
        // instance drives every chrome and devtools widget.
        builder.Services.AddSingleton<ThemeManager>();

        // MainPage resolves IDiagnostics + ThemeManager from DI; register it as
        // transient so App's CreateWindow can pull a fresh instance each time.
        builder.Services.AddTransient<MainPage>();

        return builder.Build();
    }
}

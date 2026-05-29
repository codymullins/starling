using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Starling.Gui.Theme;
// Telemetry assembly is now Starling.Telemetry on disk but the in-file
// namespace was not renamed in the Starling→Starling pass.
using Starling.Telemetry;

namespace Starling.Gui;

internal static class Program
{
    public static IServiceProvider Services { get; private set; } = null!;

    [STAThread]
    public static int Main(string[] args)
    {
        Services = BuildServices();

        var otlp = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        var log = Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Starling.Gui.Startup");
        if (!string.IsNullOrWhiteSpace(otlp))
            log.LogInformation("OTLP exporter live → {Endpoint}", otlp);
        else
            log.LogInformation("OTEL_EXPORTER_OTLP_ENDPOINT not set; telemetry exporter is a no-op.");

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static IServiceProvider BuildServices()
    {
        // AddStarlingTelemetry's TBuilder constraint is IHostApplicationBuilder
        // (so the call site can chain on `WebApplication.CreateBuilder()` or
        // `Host.CreateApplicationBuilder()`). Use a HostApplicationBuilder to
        // satisfy the constraint, mutate its Services collection, then build a
        // plain ServiceProvider — we don't actually want the host's lifetime.
        var host = Host.CreateApplicationBuilder();
        host.Services.AddSingleton<ThemeManager>();
        host.AddStarlingTelemetry("starling-gui");

        var provider = host.Services.BuildServiceProvider();

        // Mirror the MAUI workaround: the OTel TracerProvider and
        // MeterProvider register as IHostedService and only attach the
        // ActivityListener inside StartAsync. We're not running the host, so
        // resolve them by hand to force the build closure.
        _ = provider.GetService<TracerProvider>();
        _ = provider.GetService<MeterProvider>();
        return provider;
    }
}

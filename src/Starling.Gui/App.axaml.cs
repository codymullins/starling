using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;
using Starling.Gui.Mcp;

namespace Starling.Gui;

public sealed class App : Application
{
    private GuiMcpServer? _mcpServer;
    private BrowserControlBridge? _mcpBridge;
    private MainWindow? _mainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;
            desktop.Exit += (_, _) => ShutdownMcpAsync().GetAwaiter().GetResult();

            StartMcpServer(_mainWindow);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void StartMcpServer(MainWindow window)
    {
        var log = Program.Services.GetService(typeof(ILoggerFactory)) is ILoggerFactory lf
            ? lf.CreateLogger("Starling.Gui.Mcp")
            : null;
        try
        {
            _mcpBridge = new BrowserControlBridge();
            _mcpBridge.Attach(window);
            _mcpServer = new GuiMcpServer(_mcpBridge);
            // StartAsync returns the listener-spawned task — fire-and-forget;
            // the accept loop runs until disposal.
            _ = _mcpServer.StartAsync();
            log?.LogInformation("MCP server listening on {Endpoint}", _mcpServer.Endpoint);
        }
        catch (Exception ex)
        {
            // Port collision (e.g. MAUI Starling.Gui already bound 3077) shows
            // up here; log + swallow so the window still opens.
            log?.LogWarning(ex, "MCP server failed to start; browser_* tools unavailable");
        }
    }

    private async Task ShutdownMcpAsync()
    {
        if (_mainWindow is not null && _mcpBridge is not null)
            _mcpBridge.Detach(_mainWindow);
        if (_mcpServer is not null)
            await _mcpServer.DisposeAsync().ConfigureAwait(false);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace Tessera.Shell;

/// <summary>
/// M0 Avalonia shell. Compiles and launches a single window with a "coming soon"
/// label. The real shell — tabs, address bar, history, downloads — is M5 work
/// per browser-plan/11_AVALONIA_SHELL.md.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TesseraApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

internal sealed class TesseraApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "Tessera";
        Width = 1024;
        Height = 720;
        Content = new TextBlock
        {
            Text = "Tessera — M0 shell.\nReal UI lands in M5 (browser-plan/11_AVALONIA_SHELL.md).",
            FontSize = 18,
            Margin = new Avalonia.Thickness(24),
            Foreground = Brushes.Black,
        };
    }
}

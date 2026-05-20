using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(Starling.Gui.Headless.Tests.TestAppBuilder))]

namespace Starling.Gui.Headless.Tests;

/// <summary>Minimal Avalonia app hosting the headless render session.</summary>
public sealed class HeadlessTestApp : Application
{
    public HeadlessTestApp() => Styles.Add(new FluentTheme());
}

public static class TestAppBuilder
{
    // Skia + UseHeadlessDrawing=false so CaptureRenderedFrame() rasterizes a
    // real frame whose pixels we can inspect.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<HeadlessTestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

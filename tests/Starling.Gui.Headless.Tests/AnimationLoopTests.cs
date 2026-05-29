using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AwesomeAssertions;
using Starling.Common.Diagnostics;
using Starling.Engine;
using Starling.Gui.Controls;
using Starling.Gui.Theme;
using EngineSize = SixLabors.ImageSharp.Size;

namespace Starling.Gui.Headless.Tests;

/// <summary>
/// Verifies the live GUI animation loop: a page that calls
/// <c>element.animate()</c> drives the WebviewPanel's own renderer (via the
/// engine's PrepareAnimationFrame + the animated-style override) so the painted
/// pixels of the animated element change across frame timestamps. This is the
/// GUI-side counterpart to the engine's RenderFrame pixel-diff test.
/// </summary>
public class AnimationLoopTests
{
    [AvaloniaFact]
    public async Task WebviewPanel_repaints_a_waapi_animation_across_frames()
    {
        const string html = """
            <!doctype html><html><body>
              <div id='t' style='width:200px;height:200px;background:rgb(255,0,0)'></div>
              <script>
                document.getElementById('t').animate(
                  [{backgroundColor:'rgb(255,0,0)'},{backgroundColor:'rgb(0,0,255)'}],
                  {duration:1000, easing:'linear'});
              </script>
            </body></html>
            """;
        var (engine, page) = await LoadInteractiveAsync(html);

        using var panel = new WebviewPanel(
            new ThemeManager(), NoopDiagnostics.Instance, _ => { }, (_, _) => { },
            (p, vp) => engine.RelayoutPage(p, new RenderOptions(vp, FontSize: 16f)),
            prepareAnimationFrame: engine.PrepareAnimationFrame,
            hasActiveAnimations: engine.HasActiveAnimations);

        var window = new Window { Content = panel, Width = 400, Height = 400 };
        window.Show();
        panel.ShowPage(page);
        window.CaptureRenderedFrame(); // force measure/arrange so the page bitmap materializes

        // The animation should be in flight (script anim imported into the page).
        engine.HasActiveAnimations(page).Should().BeTrue("element.animate registered a script animation");

        var c0 = PixelAtFrame(engine, panel, page, nowMs: 0, x: 20, y: 20);
        var c500 = PixelAtFrame(engine, panel, page, nowMs: 500, x: 20, y: 20);
        var c999 = PixelAtFrame(engine, panel, page, nowMs: 999, x: 20, y: 20);

        // Linear red→blue fade: red falls, blue rises monotonically.
        c0.R.Should().BeGreaterThan(c500.R, "the red channel fades out over the animation");
        c500.R.Should().BeGreaterThan(c999.R);
        c0.B.Should().BeLessThan(c500.B, "the blue channel fades in over the animation");
        c500.B.Should().BeLessThan(c999.B);
    }

    [AvaloniaFact]
    public async Task WebviewPanel_repaints_a_declarative_keyframes_animation()
    {
        // No script — a pure @keyframes animation. The engine primes it into the
        // live AnimationEngine from the element's static animation-* cascade, so
        // the GUI loop renders it moving.
        const string html = """
            <!doctype html><html><head><style>
              @keyframes fade { 0% { background-color: rgb(255,0,0) } 100% { background-color: rgb(0,0,255) } }
              #t { width:200px; height:200px; background:rgb(255,0,0); animation: fade 1000ms linear; }
            </style></head><body><div id='t'></div></body></html>
            """;
        var (engine, page) = await LoadInteractiveAsync(html);

        using var panel = new WebviewPanel(
            new ThemeManager(), NoopDiagnostics.Instance, _ => { }, (_, _) => { },
            (p, vp) => engine.RelayoutPage(p, new RenderOptions(vp, FontSize: 16f)),
            prepareAnimationFrame: engine.PrepareAnimationFrame,
            hasActiveAnimations: engine.HasActiveAnimations);

        var window = new Window { Content = panel, Width = 400, Height = 400 };
        window.Show();
        panel.ShowPage(page);
        window.CaptureRenderedFrame();

        engine.HasActiveAnimations(page).Should().BeTrue("a declarative @keyframes animation should be primed and in flight");

        var c0 = PixelAtFrame(engine, panel, page, nowMs: 0, x: 20, y: 20);
        var c500 = PixelAtFrame(engine, panel, page, nowMs: 500, x: 20, y: 20);
        var c999 = PixelAtFrame(engine, panel, page, nowMs: 999, x: 20, y: 20);

        c0.R.Should().BeGreaterThan(c500.R, "the red channel fades out");
        c500.R.Should().BeGreaterThan(c999.R);
        c0.B.Should().BeLessThan(c500.B, "the blue channel fades in");
        c500.B.Should().BeLessThan(c999.B);
    }

    // ---------------------------------------------------------------- helpers

    private static (byte R, byte G, byte B, byte A) PixelAtFrame(
        StarlingEngine engine, WebviewPanel panel, LaidOutPage page, long nowMs, int x, int y)
    {
        // Advance the page's animation clock, then drive the panel's own render
        // path the way the live timer would (without depending on the timer).
        engine.PrepareAnimationFrame(page, nowMs);
        SetField(panel, "_animClockMs", nowMs);
        SetField(panel, "_animating", true);
        typeof(WebviewPanel).GetMethod("RenderViewportRegion", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, null);

        var image = (Image)typeof(WebviewPanel)
            .GetField("_pageImage", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(panel)!;
        var bmp = (WriteableBitmap)image.Source!;
        return ReadPixel(bmp, x, y);
    }

    private static void SetField(object obj, string name, object value)
        => typeof(WebviewPanel).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(obj, value);

    private static (byte R, byte G, byte B, byte A) ReadPixel(WriteableBitmap bmp, int x, int y)
    {
        using var fb = bmp.Lock();
        var bytesPerPixel = 4;
        var offset = y * fb.RowBytes + x * bytesPerPixel;
        var px = new byte[4];
        Marshal.Copy(fb.Address + offset, px, 0, 4);
        // Rgba8888 channel order.
        return fb.Format == PixelFormat.Bgra8888
            ? (px[2], px[1], px[0], px[3])
            : (px[0], px[1], px[2], px[3]);
    }

    private static async Task<(StarlingEngine Engine, LaidOutPage Page)> LoadInteractiveAsync(string html)
    {
        var fixture = Path.Combine(Path.GetTempPath(), $"starling-animloop-{Guid.NewGuid():N}.html");
        File.WriteAllText(fixture, html);
        var engine = new StarlingEngine();
        var result = await engine.LayoutPageAsync(
            "file://" + fixture.Replace('\\', '/'),
            new RenderOptions(new EngineSize(400, 400), FontSize: 16f),
            CancellationToken.None, onFirstPaint: _ => { });
        result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
        return (engine, result.Value);
    }
}

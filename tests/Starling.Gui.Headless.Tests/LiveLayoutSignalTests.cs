using System.Reflection;
using AwesomeAssertions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
using Starling.Common.Diagnostics;
using Starling.Engine;
using Starling.Gui.Controls;
using Starling.Gui.Theme;
using EngineSize = SixLabors.ImageSharp.Size;

namespace Starling.Gui.Headless.Tests;

/// <summary>
/// Phase 0c of the incremental-layout plan: the live loop relayouts only when a
/// change a built-in style/layout pass cares about lands
/// (<c>Document.LayoutInvalidationVersion</c>), not on every DOM mutation. A
/// <c>requestAnimationFrame</c> that writes only <c>data-*</c> / <c>aria-*</c> /
/// <c>js*</c> attributes must no longer force a reflow, while a layout-relevant
/// write (a class change, text) still must.
/// </summary>
public class LiveLayoutSignalTests
{
    private static readonly MethodInfo LiveTick =
        typeof(WebviewPanel).GetMethod("LiveTick", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [AvaloniaFact]
    public async Task Live_loop_relayouts_on_layout_relevant_changes_only()
    {
        // A page with a one-shot script so a live scripting context exists (the
        // live tick early-returns without one), but no rAF/timer activity, so the
        // pump is inert between our controlled mutations.
        var url = WritePage("""
            <body><div id="box">hello</div>
            <script>window.__ready = 1;</script></body>
            """);

        var engine = new StarlingEngine();
        var result = await engine.LayoutPageAsync(
            url, new RenderOptions(new EngineSize(800, 600), FontSize: 16f),
            CancellationToken.None, onFirstPaint: _ => { });
        Assert.True(result.IsOk, result.IsErr ? result.Error.Message : "");
        var page = result.Value;
        var box = page.Document.GetElementById("box")!;

        // Count relayout-hook invocations. Returning null skips the re-show/paint
        // path, so the test stays independent of the paint backend on relayout.
        var relayouts = 0;
        using var panel = new WebviewPanel(
            new ThemeManager(), NoopDiagnostics.Instance, _ => { }, (_, _) => { },
            (_, _) => { relayouts++; return null; });
        var window = new Window { Content = panel, Width = 800, Height = 600 };
        window.Show();
        panel.ShowPage(page);
        // Force the deferred layout/render pass to complete now, while the page
        // bitmap is alive — otherwise it runs during teardown after Dispose frees
        // the bitmap and measures a disposed Image.
        window.CaptureRenderedFrame();
        panel.BindLiveScripting();

        // Let the load script settle so its mutations don't count against us.
        LiveTick.Invoke(panel, null);
        var baseline = relayouts;

        // A data-* write bumps MutationVersion but not LayoutInvalidationVersion.
        box.SetAttribute("data-frame", "1");
        LiveTick.Invoke(panel, null);
        relayouts.Should().Be(baseline, "a data-* write is not layout-relevant");

        box.SetAttribute("data-frame", "2");
        LiveTick.Invoke(panel, null);
        relayouts.Should().Be(baseline, "repeated data-* writes still don't relayout");

        // aria-* is likewise suppressed.
        box.SetAttribute("aria-label", "x");
        LiveTick.Invoke(panel, null);
        relayouts.Should().Be(baseline, "an aria-* write is not layout-relevant");

        // A class change is layout-relevant — it must relayout exactly once.
        box.SetAttribute("class", "highlighted");
        LiveTick.Invoke(panel, null);
        relayouts.Should().Be(baseline + 1, "a class change can shift cascade/layout");

        // And another data-* write after that still doesn't.
        box.SetAttribute("data-frame", "3");
        LiveTick.Invoke(panel, null);
        relayouts.Should().Be(baseline + 1, "data-* after a real change still doesn't relayout");
    }

    private static string WritePage(string html)
    {
        var path = Path.Combine(Path.GetTempPath(), $"starling-0c-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, html);
        return "file://" + path.Replace('\\', '/');
    }
}

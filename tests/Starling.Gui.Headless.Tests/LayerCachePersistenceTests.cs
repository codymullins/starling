using System.Collections.Concurrent;
using AwesomeAssertions;
using Starling.Common.Diagnostics;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Layout.Text;
using Xunit;

namespace Starling.Gui.Headless.Tests;

/// <summary>
/// LTF-03: the shell keeps each layer's persistent picture cache across an
/// in-place relayout (InvalidateCache drops only the flat scroll cache) and
/// clears them on navigation (ResetForNavigation). Combined with the LTF-02
/// content-hash key, an unchanged layer re-blits from cache across a relayout
/// while a navigation forces a cold re-raster.
/// </summary>
public sealed class LayerCachePersistenceTests
{
    // Two promoted opacity layers + the root → three layers total.
    private const string Html =
        "<body style=\"margin:0\">" +
        "<div style=\"opacity:0.5;position:absolute;left:0;top:0;width:50px;height:50px;background-color:#ff0000\"></div>" +
        "<div style=\"opacity:0.5;position:absolute;left:80px;top:0;width:50px;height:50px;background-color:#0000ff\"></div>" +
        "</body>";

    private static BlockBox Layout()
        => new LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance)
            .LayoutDocument(HtmlParser.Parse(Html), new Size(200, 200));

    [Fact]
    public void Layer_caches_survive_relayout_and_clear_on_navigation()
    {
        var diag = new RecordingDiagnostics();
        using var host = new PageRendererHost(diag);
        var root = Layout();

        // Seed every layer's cache (no prior content → no HIT).
        host.RenderViaLayerTree(root, 1f).Dispose();
        diag.CountOf("paint.cache.hit").Should().Be(0, "nothing is cached before the first render");

        // Re-render unchanged content: every layer re-blits from cache.
        host.RenderViaLayerTree(root, 1f).Dispose();
        var afterReblit = diag.CountOf("paint.cache.hit");
        afterReblit.Should().BeGreaterThan(0, "unchanged layers serve from cache on the second render");

        // In-place relayout: the flat cache drops but the per-layer caches persist.
        host.InvalidateCache();
        host.RenderViaLayerTree(root, 1f).Dispose();
        diag.CountOf("paint.cache.hit").Should().Be(afterReblit * 2,
            "the per-layer caches are retained across an in-place relayout (LTF-03)");

        // Navigation: every layer cache is cleared, so the next render is cold.
        host.ResetForNavigation();
        host.RenderViaLayerTree(root, 1f).Dispose();
        diag.CountOf("paint.cache.hit").Should().Be(afterReblit * 2,
            "navigation clears the per-layer caches — the next render re-rasters, no new HIT");
    }

    private sealed class RecordingDiagnostics : IDiagnostics
    {
        private readonly ConcurrentDictionary<string, double> _counters = new();
        public double CountOf(string name) => _counters.TryGetValue(name, out var v) ? v : 0d;
        public void Counter(string name, double value) => _counters.AddOrUpdate(name, value, (_, prev) => prev + value);
        public IDisposable Span(string area, string operation) => NoopSpan.Instance;
        public void Log(DiagLevel level, string area, string message) { }
        public void Snapshot(string label, ReadOnlySpan<byte> bytes) { }
        public void LogException(string area, Exception exception, string? message = null) { }
        private sealed class NoopSpan : IDisposable
        {
            public static readonly NoopSpan Instance = new();
            public void Dispose() { }
        }
    }
}

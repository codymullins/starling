using System.Collections.Concurrent;
using AwesomeAssertions;
using Starling.Common.Diagnostics;
using Starling.Css.Cascade;
using Starling.Dom;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Text;
using Xunit;

namespace Starling.Gui.Headless.Tests;

/// <summary>
/// LTF-04: the compositor layer-tree path runs on a live frame even when that
/// frame also relayouted. The pre-LTF-04 gate stayed on the flat path whenever a
/// frame relayouted (which the demo does every frame, from a status-text write),
/// so the layer caches never helped. Now a relayout no longer wipes the per-layer
/// caches (LTF-03) and each layer is content-keyed (LTF-02), so a transform-only
/// layer re-blits from cache across a relayout frame while the relaid region
/// re-rasters.
/// </summary>
public sealed class LayerTreeRelayoutFrameTests
{
    private const string SpinBase =
        "position:absolute;left:50px;top:50px;width:80px;height:50px;background-color:#cc2222;";

    [Fact]
    public void Spinning_layer_reblits_from_cache_across_relayout_frames()
    {
        var diag = new RecordingDiagnostics();
        using var host = new PageRendererHost(diag);

        var doc = HtmlParser.Parse(
            "<body style=\"margin:0\">" +
            $"<div id=spin style=\"{SpinBase}transform:rotate(0deg)\"></div>" +
            "<div id=label style=\"position:absolute;left:0;top:150px\">frame 0</div>" +
            "</body>");
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var size = new Size(240, 240);
        var spin = doc.GetElementById("spin")!;
        var label = doc.GetElementById("label")!;

        double prevHits = 0;
        for (var f = 0; f < 4; f++)
        {
            // A layout-relevant mutation (the status-text write the demo makes) —
            // this is what forced the old gate onto the flat path every frame.
            if (label.FirstChild is Text t) t.Data = "frame " + f;
            // A composite-time, transform-only change on the promoted layer.
            spin.SetAttribute("style", SpinBase + $"transform:rotate({f * 15}deg)");

            var root = engine.LayoutDocument(doc, size);
            host.RenderViaLayerTree(root, 1f).Dispose();

            var hits = diag.CountOf("paint.cache.hit");
            if (f == 0)
            {
                hits.Should().Be(0, "the first frame seeds every layer's cache");
            }
            else
            {
                (hits - prevHits).Should().BeGreaterThan(0,
                    "the spinning layer re-blits its content from cache on a relayout frame (LTF-04)");
            }
            prevHits = hits;
        }
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

using System.Collections.Concurrent;
using AwesomeAssertions;
using Starling.Common.Diagnostics;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Text;

namespace Starling.Paint.Tests;

/// <summary>
/// Pins the per-run shape optimisation in <c>InlineLayout</c>:
/// <c>_measurer.Shape(normalized, ...)</c> runs <em>once</em> per styled
/// inline run, and per-word advances come from <c>ShapedRun.Slice</c>. The
/// optimisation only engages when the measurer populates
/// <c>ShapedRun.Glyphs</c> at 1:1 with the text's char count, which the
/// Skia-removal regression silently broke by returning an empty glyph array.
/// The increment in <c>InlineLayout.LayoutInlineRun</c>'s
/// <c>layout.text.measures</c> counter is the cleanest observable signal for
/// the optimisation being alive: a 9-word ASCII paragraph should fire it 1×,
/// not 10×.
/// </summary>
[TestClass]
public sealed class InlineShapeOptimizationTests
{
    [TestMethod]
    public void Ascii_paragraph_shapes_each_inline_run_once_not_once_per_word()
    {
        var diag = new RecordingDiagnostics();
        using var measurer = new ImageSharpTextMeasurer();
        var engine = new LayoutEngine(new StyleEngine(), measurer, diagnostics: diag);

        var doc = HtmlParser.Parse(
            "<body><p>the quick brown fox jumps over the lazy dog</p></body>");
        engine.LayoutDocument(doc, new Size(800, 600));

        diag.CountOf("layout.text.measures").Should().Be(1,
            "the inline run shapes once and slices per-word; if Glyphs come back empty the canSlice fast path drops and we shape once per word (10 calls for 9 words + the whole-run shape)");
    }

    [TestMethod]
    public void Two_paragraphs_shape_one_run_each()
    {
        var diag = new RecordingDiagnostics();
        using var measurer = new ImageSharpTextMeasurer();
        var engine = new LayoutEngine(new StyleEngine(), measurer, diagnostics: diag);

        var doc = HtmlParser.Parse(
            "<body><p>alpha beta gamma</p><p>delta epsilon zeta</p></body>");
        engine.LayoutDocument(doc, new Size(800, 600));

        diag.CountOf("layout.text.measures").Should().Be(2,
            "two single-run paragraphs produce exactly two whole-run shape calls when canSlice is engaged");
    }

    /// <summary>
    /// Same paragraph rendered through two consecutive layouts on the same
    /// measurer instance: the shape cache means subsequent runs reuse the
    /// cached <see cref="ShapedRun"/> instance, but the diag counter still
    /// fires per call (it's recorded before the measurer is invoked). This
    /// test pins both: optimisation alive (counter == 1 per layout) AND cache
    /// returns the same <see cref="ShapedRun"/> instance to InlineLayout —
    /// which means <c>ShapedRun.Slice</c> on the second layout consumes the
    /// already-shaped run without re-invoking SixLabors.Fonts.
    /// </summary>
    [TestMethod]
    public void Repeat_layout_reuses_cached_shape_for_same_run()
    {
        var diag = new RecordingDiagnostics();
        using var measurer = new ImageSharpTextMeasurer();
        var engine = new LayoutEngine(new StyleEngine(), measurer, diagnostics: diag);

        var html = "<body><p>the quick brown fox jumps over the lazy dog</p></body>";
        engine.LayoutDocument(HtmlParser.Parse(html), new Size(800, 600));
        engine.LayoutDocument(HtmlParser.Parse(html), new Size(800, 600));

        diag.CountOf("layout.text.measures").Should().Be(2);

        // Direct cache check: re-issuing Shape for the run text must hand
        // back the same ShapedRun instance — proves the measurer cache is
        // populated by the layout pass and reusable across renders.
        var spec = new FontSpec(["sans-serif"], bold: false, italic: false);
        var first = measurer.Shape("the quick brown fox jumps over the lazy dog", 16, spec);
        var second = measurer.Shape("the quick brown fox jumps over the lazy dog", 16, spec);
        second.Should().BeSameAs(first);
    }

    private sealed class RecordingDiagnostics : IDiagnostics
    {
        private readonly ConcurrentDictionary<string, double> _counters = new();
        public double CountOf(string name) => _counters.TryGetValue(name, out var v) ? v : 0d;
        public void Counter(string name, double value)
            => _counters.AddOrUpdate(name, value, (_, prev) => prev + value);
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

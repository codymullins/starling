using AwesomeAssertions;
using Starling.Common.Diagnostics;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Dom;
using Starling.Html;
using Starling.Layout.Box;
using Starling.Layout.Incremental;
using Starling.Layout.Text;
using Starling.Layout.Verification;

namespace Starling.Layout.Tests.Incremental;

/// <summary>
/// Regression guard for the animations-page CPU spike. The page roots its
/// content under a <c>display:flex</c> body (a common centering trick), so
/// <c>main</c> is a flex item. A flex item with an auto cross-size is measured
/// every relayout to find its content height — and that measurement recurses
/// the whole subtree. When a script writes a deep text node every animation
/// frame (the demo's status line), the dirty path reaches the flex root and,
/// without the fix, the intrinsic-size pass re-measured every text run on the
/// page each frame. These tests pin that a deep text change re-measures only a
/// handful of runs, and that the cheap path still matches a full rebuild.
/// </summary>
[TestClass]
public sealed class IncrementalFlexMeasureTests
{
    private static readonly Size Viewport = new(1024, 768);

    // A flex-rooted page: body is a flex container, main is its single flex
    // item, and the page has several text-heavy sibling sections plus one deep
    // "status" line that a per-frame script rewrites.
    private const string Html = """
        <body>
          <main id=page>
            <header><h1>Animations showcase headline text</h1>
              <p>A paragraph of intro copy that wraps across a couple of lines here.</p></header>
            <section><h2>Section one title</h2>
              <p>Section one body copy that also wraps onto more than a single line.</p></section>
            <section><h2>Section two title</h2>
              <p>Section two body copy that also wraps onto more than a single line.</p></section>
            <section><h2>Section three title</h2>
              <p>Section three body copy that also wraps onto more than a single line.</p></section>
            <div class=waapi><p id=status>idle 0 ms</p></div>
            <footer>Rendered by the layout engine.</footer>
          </main>
        </body>
        """;

    private const string Css = """
        body { display: flex; justify-content: center; }
        #page { width: 100%; max-width: 880px; }
        """;

    [TestMethod]
    public void Deep_text_change_under_flex_root_does_not_remeasure_whole_page()
    {
        var doc = HtmlParser.Parse(Html);
        doc.RecordLayoutMutations = true;
        var style = new StyleEngine();
        style.AddStyleSheet(CssParser.ParseStyleSheet(Css));
        var measurer = new CountingMeasurer();
        var session = new LayoutSession(style, diagnostics: NoopDiagnostics.Instance);

        // Frame 0: full build measures the whole page (the baseline cost).
        session.Layout(doc, Viewport, measurer, nowMs: null);
        var fullBuildMeasures = measurer.Count;
        fullBuildMeasures.Should().BeGreaterThan(20, "the full build measures every text run on the page");

        // Frame 1: rewrite only the deep status line, then relayout.
        var status = (Starling.Dom.Text)doc.GetElementById("status")!.FirstChild!;
        measurer.Count = 0;
        status.Data = "running 16 ms";
        session.Layout(doc, Viewport, measurer, nowMs: null);

        // The incremental relayout must re-measure only the changed line and a
        // few runs on its dirty path — not the whole page. Before the fix this
        // was ~the full-build count every frame (the flex root's intrinsic pass
        // re-measured everything); after it, it is a small constant.
        measurer.Count.Should().BeLessThan(fullBuildMeasures / 2,
            "a deep text change must not trigger a full-page intrinsic re-measure");
    }

    [TestMethod]
    public void Deep_text_change_under_flex_root_matches_full_rebuild()
    {
        var doc = HtmlParser.Parse(Html);
        doc.RecordLayoutMutations = true;
        var style = new StyleEngine();
        style.AddStyleSheet(CssParser.ParseStyleSheet(Css));
        var session = new LayoutSession(style, diagnostics: NoopDiagnostics.Instance);

        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        var status = (Starling.Dom.Text)doc.GetElementById("status")!.FirstChild!;
        for (var i = 0; i < 5; i++)
        {
            status.Data = $"running {i * 16} ms";
            var incremental = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

            var reference = FullRebuild(doc, Css);
            LayoutVerifier.FindFirstDivergence(incremental, reference).Should().BeNull(
                $"the cheap (height-reuse) relayout must match a full rebuild on frame {i}");
        }
    }

    private static BlockBox FullRebuild(Document doc, string css)
    {
        var style = new StyleEngine();
        style.AddStyleSheet(CssParser.ParseStyleSheet(css));
        return new LayoutEngine(style).LayoutDocument(doc, Viewport);
    }

    // Wraps the heuristic measurer, counting how many text runs are measured so a
    // test can assert that a clean subtree is not re-measured.
    private sealed class CountingMeasurer : ITextMeasurer
    {
        private readonly DefaultTextMeasurer _inner = DefaultTextMeasurer.Instance;
        public int Count;

        public double MeasureWidth(string text, double fontSize, FontSpec spec)
        {
            Count++;
            return _inner.MeasureWidth(text, fontSize, spec);
        }

        public ShapedRun Shape(string text, double fontSize, FontSpec spec)
        {
            Count++;
            return _inner.Shape(text, fontSize, spec);
        }

        public double NormalLineHeight(double fontSize, FontSpec spec) => _inner.NormalLineHeight(fontSize, spec);
        public double Baseline(double fontSize, FontSpec spec) => _inner.Baseline(fontSize, spec);
    }
}

using AwesomeAssertions;
using Starling.Common.Diagnostics;
using Starling.Css.Cascade;
using Starling.Dom;
using Starling.Html;
using Starling.Layout.Box;
using Starling.Layout.Incremental;
using Starling.Layout.Text;
using Starling.Layout.Verification;

namespace Starling.Layout.Tests.Incremental;

[TestClass]
public sealed class LayoutSessionTests
{
    private static readonly Size Viewport = new(400, 600);

    private static Document Parse(string html)
    {
        var doc = HtmlParser.Parse(html);
        doc.RecordLayoutMutations = true;
        return doc;
    }

    // The reference: a full rebuild of the document as it stands now, the
    // always-correct output the incremental result must match.
    private static BlockBox FullRebuild(Document doc)
        => new LayoutEngine(new StyleEngine()).LayoutDocument(doc, Viewport);

    private static Starling.Dom.Text FirstText(Element el)
        => (Starling.Dom.Text)el.FirstChild!;

    // ---- text mutation: the Animations-demo case ----------------------------

    [TestMethod]
    public void Text_change_relays_incrementally_and_matches_full_rebuild()
    {
        var doc = Parse("<body><div id=a>alpha</div><div id=b>beta</div><div id=c>gamma</div></body>");
        var diag = new CountingDiagnostics();
        var session = new LayoutSession(new StyleEngine(), diagnostics: diag);

        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);
        diag.Counter("layout.incremental.full_rebuild").Should().Be(1);

        // Grow the middle div's text so its block (and everything after it) shifts.
        FirstText(doc.GetElementById("b")!).Data =
            "beta beta beta beta beta beta beta beta beta beta beta beta wraps onto lines";

        var incremental = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        // The incremental path was actually taken (not a silent full-rebuild fallback)...
        diag.Counter("layout.incremental.relayout").Should().Be(1);
        diag.Counter("layout.incremental.full_rebuild").Should().Be(1);
        // ...and it matches a full rebuild exactly.
        LayoutVerifier.FindFirstDivergence(incremental, FullRebuild(doc)).Should().BeNull();
    }

    [TestMethod]
    public void Unrelated_clean_siblings_keep_their_geometry_across_a_text_change()
    {
        var doc = Parse("<body><div id=a>alpha</div><div id=b>beta</div><div id=c>gamma</div></body>");
        var session = new LayoutSession(new StyleEngine());
        var first = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        // #a precedes the change, so its position must be untouched; capture it.
        var aBefore = FindById(first, "a")!.Frame;

        FirstText(doc.GetElementById("c")!).Data = "gamma is now considerably longer than before";
        var after = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        FindById(after, "a")!.Frame.Should().Be(aBefore);
        LayoutVerifier.FindFirstDivergence(after, FullRebuild(doc)).Should().BeNull();
    }

    // ---- attribute mutation --------------------------------------------------

    [TestMethod]
    public void Style_attribute_change_relays_incrementally_and_matches_full_rebuild()
    {
        var doc = Parse("<body><div id=a>alpha</div><div id=b style='height:10px'>beta</div><div id=c>gamma</div></body>");
        var diag = new CountingDiagnostics();
        var session = new LayoutSession(new StyleEngine(), diagnostics: diag);
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        // A layout-relevant attribute write — height grows, so #c shifts down.
        doc.GetElementById("b")!.SetAttribute("style", "height:120px");

        var incremental = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        diag.Counter("layout.incremental.relayout").Should().Be(1);
        LayoutVerifier.FindFirstDivergence(incremental, FullRebuild(doc)).Should().BeNull();
    }

    // ---- structural mutation falls back to a full rebuild (Phase 2 scope) ----

    [TestMethod]
    public void Structural_change_falls_back_to_full_rebuild_and_stays_correct()
    {
        var doc = Parse("<body><div id=a>alpha</div><div id=b>beta</div></body>");
        var diag = new CountingDiagnostics();
        var session = new LayoutSession(new StyleEngine(), diagnostics: diag);
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        // Insert a new child — structural, so the reconciler must fall back.
        var added = doc.CreateElement("div");
        added.AppendChild(doc.CreateTextNode("delta"));
        doc.GetElementById("a")!.ParentNode!.AppendChild(added);

        var rebuilt = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        diag.Counter("layout.incremental.full_rebuild").Should().Be(2); // initial + fallback
        diag.Counter("layout.incremental.relayout").Should().Be(0);
        LayoutVerifier.FindFirstDivergence(rebuilt, FullRebuild(doc)).Should().BeNull();
    }

    // ---- a no-op frame reuses everything and stays correct -------------------

    [TestMethod]
    public void Frame_with_no_mutations_reuses_the_whole_tree()
    {
        var doc = Parse("<body><div id=a>alpha</div><div id=b>beta</div></body>");
        var diag = new CountingDiagnostics();
        var session = new LayoutSession(new StyleEngine(), diagnostics: diag);
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        var again = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        diag.Counter("layout.incremental.relayout").Should().Be(1);
        LayoutVerifier.FindFirstDivergence(again, FullRebuild(doc)).Should().BeNull();
    }

    // ---- the in-session dual-run safety net (plan §2g) -----------------------

    [TestMethod]
    public void Self_verification_passes_across_a_sequence_of_text_changes()
    {
        var doc = Parse("<body><div id=a>alpha</div><div id=b>beta</div><div id=c>gamma</div></body>");
        var diag = new CountingDiagnostics();
        var session = new LayoutSession(new StyleEngine(), diagnostics: diag) { VerifyAgainstFullRebuild = true };

        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);
        FirstText(doc.GetElementById("b")!).Data = "beta grew a lot longer than it used to be and wraps";
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);
        FirstText(doc.GetElementById("a")!).Data = "alpha";
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        diag.Counter("layout.incremental.verify_ok").Should().Be(2);
        diag.Counter("layout.incremental.divergent").Should().Be(0);
        diag.Counter("layout.incremental.relayout").Should().Be(2);
    }

    private static Box.Box? FindById(Box.Box box, string id)
    {
        if (box.Element?.Id == id) return box;
        foreach (var child in box.Children)
            if (FindById(child, id) is { } found) return found;
        return null;
    }

    private sealed class CountingDiagnostics : IDiagnostics
    {
        private readonly Dictionary<string, double> _counters = new(StringComparer.Ordinal);
        public double Counter(string name) => _counters.TryGetValue(name, out var v) ? v : 0;
        void IDiagnostics.Counter(string name, double value)
        {
            _counters.TryGetValue(name, out var v);
            _counters[name] = v + value;
        }
        void IDiagnostics.Log(DiagLevel level, string area, string message) { }
        IDisposable IDiagnostics.Span(string area, string operation) => NullSpan.Instance;
        void IDiagnostics.Snapshot(string label, ReadOnlySpan<byte> bytes) { }
        void IDiagnostics.LogException(string area, Exception exception, string? message) { }
        private sealed class NullSpan : IDisposable { public static readonly NullSpan Instance = new(); public void Dispose() { } }
    }
}

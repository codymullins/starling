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

    private static BlockBox FullRebuildWith(Document doc, string css)
    {
        var style = new StyleEngine();
        style.AddStyleSheet(CssParser.ParseStyleSheet(css));
        return new LayoutEngine(style).LayoutDocument(doc, Viewport);
    }

    private static (LayoutSession Session, CountingDiagnostics Diag) SessionWith(string css)
    {
        var style = new StyleEngine();
        style.AddStyleSheet(CssParser.ParseStyleSheet(css));
        var diag = new CountingDiagnostics();
        return (new LayoutSession(style, diagnostics: diag), diag);
    }

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

    // ---- structural mutations: reconciled incrementally (Phase 3) ------------

    [TestMethod]
    public void Child_insert_relays_incrementally_and_matches_full_rebuild()
    {
        var doc = Parse("<body><div id=a>alpha</div><div id=b>beta</div></body>");
        var diag = new CountingDiagnostics();
        var session = new LayoutSession(new StyleEngine(), diagnostics: diag);
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        var added = doc.CreateElement("div");
        added.AppendChild(doc.CreateTextNode("delta"));
        doc.GetElementById("a")!.ParentNode!.AppendChild(added);

        var rebuilt = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        diag.Counter("layout.incremental.relayout").Should().Be(1);
        diag.Counter("layout.incremental.full_rebuild").Should().Be(1); // initial only
        LayoutVerifier.FindFirstDivergence(rebuilt, FullRebuild(doc)).Should().BeNull();
    }

    [TestMethod]
    public void Child_remove_relays_incrementally_and_matches_full_rebuild()
    {
        var doc = Parse("<body><div id=a>alpha</div><div id=b>beta</div><div id=c>gamma</div></body>");
        var diag = new CountingDiagnostics();
        var session = new LayoutSession(new StyleEngine(), diagnostics: diag);
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        var b = doc.GetElementById("b")!;
        b.ParentNode!.RemoveChild(b);

        var rebuilt = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        diag.Counter("layout.incremental.relayout").Should().Be(1);
        LayoutVerifier.FindFirstDivergence(rebuilt, FullRebuild(doc)).Should().BeNull();
    }

    [TestMethod]
    public void Insert_reuses_unchanged_sibling_boxes_by_identity()
    {
        // §3a: a structural insert must reuse the unchanged siblings' box objects,
        // not rebuild them. Reference-identity across the relayout proves it.
        var doc = Parse("<body><main id=main><section id=a>alpha</section><section id=b>beta</section></main></body>");
        var session = new LayoutSession(new StyleEngine());
        var first = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);
        var aBox = FindById(first, "a");
        var bBox = FindById(first, "b");

        var added = doc.CreateElement("section");
        added.AppendChild(doc.CreateTextNode("gamma"));
        doc.GetElementById("main")!.AppendChild(added);

        var after = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        ReferenceEquals(FindById(after, "a"), aBox).Should().BeTrue("unchanged sibling #a is reused, not rebuilt");
        ReferenceEquals(FindById(after, "b"), bBox).Should().BeTrue("unchanged sibling #b is reused, not rebuilt");
        FindById(after, "a")!.Element!.Id.Should().Be("a"); // sanity
        LayoutVerifier.FindFirstDivergence(after, FullRebuild(doc)).Should().BeNull();
    }

    [TestMethod]
    public void Positional_selector_restyle_on_insert_is_detected_and_matches_full_rebuild()
    {
        // A geometry-affecting :nth-child rule: inserting at the front shifts every
        // sibling's parity, so the re-cascade + style-equality gate must rebuild the
        // shifted siblings rather than reuse them stale.
        const string css = "section:nth-child(even) { padding-left: 40px; }";
        var doc = Parse("<body><main id=main><section id=a>a</section><section id=b>b</section><section id=c>c</section></main></body>");
        var (session, _) = SessionWith(css);
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        var lead = doc.CreateElement("section");
        lead.AppendChild(doc.CreateTextNode("lead"));
        var main = doc.GetElementById("main")!;
        main.InsertBefore(lead, main.FirstChild);

        var after = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);
        LayoutVerifier.FindFirstDivergence(after, FullRebuildWith(doc, css))
            .Should().BeNull("positional restyle must be re-cascaded, not reused stale");
    }

    // ---- selector-aware invalidation (plan §7) -------------------------------

    [TestMethod]
    public void Selector_referenced_data_attribute_change_relays_incrementally()
    {
        // Author CSS keyed on a data-* attribute: flipping it must invalidate and
        // relayout, even though the static heuristic treats data-* as cosmetic.
        const string css = "div[data-state=\"open\"] { padding-left: 40px; }";
        var doc = Parse("<body><div id=a data-state=\"closed\">alpha</div><div id=b>beta</div></body>");
        var (session, diag) = SessionWith(css);
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        // The layout pass taught the document which attributes the CSS selects on.
        doc.StyleReferencedAttributes.Should().Contain("data-state");

        var before = doc.LayoutInvalidationVersion;
        doc.GetElementById("a")!.SetAttribute("data-state", "open");
        doc.LayoutInvalidationVersion.Should().Be(before + 1, "a write to a selector-referenced attribute is layout-relevant");

        var incremental = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);
        diag.Counter("layout.incremental.relayout").Should().Be(1);
        LayoutVerifier.FindFirstDivergence(incremental, FullRebuildWith(doc, css)).Should().BeNull();
    }

    [TestMethod]
    public void Has_selector_forces_full_rebuild_on_structural_change()
    {
        const string css = "main:has(section) { padding: 10px; }";
        var doc = Parse("<body><main id=main><section id=a>a</section></main></body>");
        var (session, diag) = SessionWith(css);
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        var added = doc.CreateElement("section");
        added.AppendChild(doc.CreateTextNode("b"));
        doc.GetElementById("main")!.AppendChild(added);

        var after = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        diag.Counter("layout.incremental.full_rebuild").Should().Be(2, "a :has rule forces structural full rebuild");
        diag.Counter("layout.incremental.relayout").Should().Be(0);
        LayoutVerifier.FindFirstDivergence(after, FullRebuildWith(doc, css)).Should().BeNull();
    }

    [TestMethod]
    public void Insert_inline_between_blocks_rewraps_anonymous_correctly()
    {
        // Mixed content: a block, then text — the text is in an anonymous block.
        var doc = Parse("<body><div id=host><p>block</p>tail text</div></body>");
        var session = new LayoutSession(new StyleEngine(), diagnostics: new CountingDiagnostics());
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        // Insert a bare text node before the <p> — changes how inline runs bucket.
        var host = doc.GetElementById("host")!;
        host.InsertBefore(doc.CreateTextNode("lead text "), host.FirstChild);

        var rebuilt = session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);
        LayoutVerifier.FindFirstDivergence(rebuilt, FullRebuild(doc))
            .Should().BeNull("localized anonymous re-wrapping must match a full build");
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

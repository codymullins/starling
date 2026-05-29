using AwesomeAssertions;

namespace Starling.Dom.Tests;

/// <summary>
/// Selector-aware layout invalidation (incremental-layout plan §7): a write to a
/// normally-cosmetic attribute (data-* / aria-*) must still invalidate layout
/// when the page's own stylesheets select on that attribute, so author CSS keyed
/// on it doesn't miss a recompute when a script flips it.
/// </summary>
[TestClass]
public sealed class SelectorAwareInvalidationTests
{
    private static (Document Doc, Element Div) MakeTree()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        doc.AppendChild(html);
        var div = doc.CreateElement("div");
        html.AppendChild(div);
        return (doc, div);
    }

    [TestMethod]
    public void Unreferenced_data_attribute_does_not_invalidate_layout()
    {
        var (doc, div) = MakeTree();
        Document.IsLayoutRelevantAttribute("data-state").Should().BeFalse("the static heuristic treats data-* as cosmetic");

        var before = doc.LayoutInvalidationVersion;
        div.SetAttribute("data-state", "open");
        doc.LayoutInvalidationVersion.Should().Be(before, "no stylesheet selects on data-state");
    }

    [TestMethod]
    public void Selector_referenced_data_attribute_invalidates_layout()
    {
        var (doc, div) = MakeTree();
        // The page's CSS selects on [data-state] — recorded by the layout pass.
        doc.StyleReferencedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "data-state" };

        var before = doc.LayoutInvalidationVersion;
        div.SetAttribute("data-state", "open");
        doc.LayoutInvalidationVersion.Should().Be(before + 1, "a write to a selector-referenced attribute invalidates layout");

        // ...and is recorded for the incremental reconciler when batching is on.
        doc.RecordLayoutMutations = true;
        div.SetAttribute("data-state", "closed");
        var batch = doc.DrainLayoutMutations();
        batch.Should().ContainSingle(m => m.Kind == LayoutChangeKind.LayoutRelevantAttr && ReferenceEquals(m.Target, div));
    }

    [TestMethod]
    public void Reference_check_is_case_insensitive_and_leaves_other_attributes_alone()
    {
        var (doc, div) = MakeTree();
        doc.StyleReferencedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "data-state" };

        var v0 = doc.LayoutInvalidationVersion;
        div.SetAttribute("DATA-STATE", "x"); // attribute names lower-case; match is case-insensitive
        doc.LayoutInvalidationVersion.Should().Be(v0 + 1);

        var v1 = doc.LayoutInvalidationVersion;
        div.SetAttribute("data-unrelated", "y"); // not referenced → still cosmetic
        doc.LayoutInvalidationVersion.Should().Be(v1);
    }
}

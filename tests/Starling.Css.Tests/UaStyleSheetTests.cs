using FluentAssertions;
using Starling.Css.Cascade;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;
namespace Starling.Css.Tests;

/// <summary>
/// Sanity checks for the UA stylesheet's handling of legacy HTML4 elements
/// (center, font, nobr, tt, code, etc.) that real-world pages — notably
/// google.com — still rely on. The cascade engine wires these in by default,
/// so each test just sticks an element in a document, lets the engine compute
/// the style, and asserts the UA defaults made it through.
/// </summary>
[TestClass]
public sealed class UaStyleSheetTests
{
    [TestMethod]
    public void Center_element_gets_block_display_and_centered_text()
    {
        var doc = new Document();
        var center = doc.CreateElement("center");
        var p = doc.CreateElement("p");
        doc.AppendChild(center);
        center.AppendChild(p);

        var engine = new StyleEngine();
        var style = engine.Compute(center);

        style.Get(PropertyId.Display).Should().Be(new CssKeyword("block"));
        style.Get(PropertyId.TextAlign).Should().Be(new CssKeyword("center"));
    }

    [TestMethod]
    public void Center_text_alignment_inherits_to_descendants()
    {
        // <center> works by inheritance: its text-align: center cascades into
        // the inline content the legacy page placed inside it.
        var doc = new Document();
        var center = doc.CreateElement("center");
        var p = doc.CreateElement("p");
        doc.AppendChild(center);
        center.AppendChild(p);

        var style = new StyleEngine().Compute(p);

        style.Get(PropertyId.TextAlign).Should().Be(new CssKeyword("center"));
    }

    [TestMethod]
    public void Anchor_gets_blue_color_and_underline()
    {
        var doc = new Document();
        var body = doc.CreateElement("body");
        var a = doc.CreateElement("a");
        doc.AppendChild(body);
        body.AppendChild(a);

        var style = new StyleEngine().Compute(a);

        // The `a` rule wins over `body { color: black }` because the body's
        // color reaches the anchor only through inheritance, which is weaker
        // than a direct rule on the element itself.
        style.GetColor(PropertyId.Color).Should().Be(new CssColor(0, 0, 255));
        style.Get(PropertyId.TextDecorationLine).Should().Be(new CssKeyword("underline"));
    }

    [TestMethod]
    public void Nobr_element_disables_wrapping()
    {
        var doc = new Document();
        var nobr = doc.CreateElement("nobr");
        doc.AppendChild(nobr);

        var style = new StyleEngine().Compute(nobr);

        style.Get(PropertyId.WhiteSpace).Should().Be(new CssKeyword("nowrap"));
    }

    [TestMethod]
    public void Font_element_stays_inline()
    {
        var doc = new Document();
        var font = doc.CreateElement("font");
        doc.AppendChild(font);

        var style = new StyleEngine().Compute(font);

        style.Get(PropertyId.Display).Should().Be(new CssKeyword("inline"));
    }

    [TestMethod]
    [DataRow("tt")]
    [DataRow("code")]
    [DataRow("kbd")]
    [DataRow("samp")]
    public void Monospace_legacy_tags_use_monospace_font(string tag)
    {
        var doc = new Document();
        var el = doc.CreateElement(tag);
        doc.AppendChild(el);

        var style = new StyleEngine().Compute(el);

        style.Get(PropertyId.FontFamily).Should().Be(new CssKeyword("monospace"));
    }
}

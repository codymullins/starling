using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.CssOverscroll1;

/// <summary>
/// Property + cascade conformance for
/// <see href="https://www.w3.org/TR/css-overscroll-1/">CSS Overscroll Behavior Module Level 1</see>.
/// </summary>
[TestClass]
[Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/")]
public sealed class OverscrollBehaviorTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    // ---- Longhand parsing: each accepted keyword (auto | contain | none) ----

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior-x", section: "3")]
    [SpecFact]
    public void OverscrollBehaviorX_parses_auto()
        => Expand("overscroll-behavior-x: auto;")
            .Single(d => d.Id == PropertyId.OverscrollBehaviorX).Value
            .Should().Be(new CssKeyword("auto"));

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior-x", section: "3")]
    [SpecFact]
    public void OverscrollBehaviorX_parses_contain()
        => Expand("overscroll-behavior-x: contain;")
            .Single(d => d.Id == PropertyId.OverscrollBehaviorX).Value
            .Should().Be(new CssKeyword("contain"));

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior-x", section: "3")]
    [SpecFact]
    public void OverscrollBehaviorX_parses_none()
        => Expand("overscroll-behavior-x: none;")
            .Single(d => d.Id == PropertyId.OverscrollBehaviorX).Value
            .Should().Be(new CssKeyword("none"));

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior-y", section: "3")]
    [SpecFact]
    public void OverscrollBehaviorY_parses_auto()
        => Expand("overscroll-behavior-y: auto;")
            .Single(d => d.Id == PropertyId.OverscrollBehaviorY).Value
            .Should().Be(new CssKeyword("auto"));

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior-y", section: "3")]
    [SpecFact]
    public void OverscrollBehaviorY_parses_contain()
        => Expand("overscroll-behavior-y: contain;")
            .Single(d => d.Id == PropertyId.OverscrollBehaviorY).Value
            .Should().Be(new CssKeyword("contain"));

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior-y", section: "3")]
    [SpecFact]
    public void OverscrollBehaviorY_parses_none()
        => Expand("overscroll-behavior-y: none;")
            .Single(d => d.Id == PropertyId.OverscrollBehaviorY).Value
            .Should().Be(new CssKeyword("none"));

    // ---- Shorthand: one value sets both longhands, two values set x then y ----

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior", section: "2")]
    [SpecFact]
    public void Shorthand_one_value_sets_both_longhands()
    {
        var decls = Expand("overscroll-behavior: contain;");
        decls.Single(d => d.Id == PropertyId.OverscrollBehaviorX).Value.Should().Be(new CssKeyword("contain"));
        decls.Single(d => d.Id == PropertyId.OverscrollBehaviorY).Value.Should().Be(new CssKeyword("contain"));
    }

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior", section: "2")]
    [SpecFact]
    public void Shorthand_two_values_set_x_then_y()
    {
        var decls = Expand("overscroll-behavior: contain none;");
        decls.Single(d => d.Id == PropertyId.OverscrollBehaviorX).Value.Should().Be(new CssKeyword("contain"));
        decls.Single(d => d.Id == PropertyId.OverscrollBehaviorY).Value.Should().Be(new CssKeyword("none"));
    }

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior", section: "2")]
    [SpecFact]
    public void Shorthand_none_auto_sets_x_none_y_auto()
    {
        var decls = Expand("overscroll-behavior: none auto;");
        decls.Single(d => d.Id == PropertyId.OverscrollBehaviorX).Value.Should().Be(new CssKeyword("none"));
        decls.Single(d => d.Id == PropertyId.OverscrollBehaviorY).Value.Should().Be(new CssKeyword("auto"));
    }

    // ---- Initial values are `auto` for both longhands. ----

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior-x", section: "3")]
    [SpecFact]
    public void OverscrollBehaviorX_initial_is_auto()
        => PropertyRegistry.InitialValue(PropertyId.OverscrollBehaviorX).Should().Be(new CssKeyword("auto"));

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior-y", section: "3")]
    [SpecFact]
    public void OverscrollBehaviorY_initial_is_auto()
        => PropertyRegistry.InitialValue(PropertyId.OverscrollBehaviorY).Should().Be(new CssKeyword("auto"));

    // ---- Not inherited. The spec lists Inherited: no for both longhands. ----

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior-x", section: "3")]
    [SpecFact]
    public void OverscrollBehaviorX_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.OverscrollBehaviorX).Should().BeFalse();

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior-y", section: "3")]
    [SpecFact]
    public void OverscrollBehaviorY_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.OverscrollBehaviorY).Should().BeFalse();

    // ---- Cascade: an authored declaration becomes the computed value. ----

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior", section: "2")]
    [SpecFact]
    public void Cascade_authored_shorthand_computes_on_element()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        var div = doc.CreateElement("div");
        doc.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(div);

        var engine = new StyleEngine();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("div { overscroll-behavior: contain none; }"));

        var style = engine.Compute(div);
        style.Get(PropertyId.OverscrollBehaviorX).Should().Be(new CssKeyword("contain"));
        style.Get(PropertyId.OverscrollBehaviorY).Should().Be(new CssKeyword("none"));
    }

    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior-x", section: "3")]
    [SpecFact]
    public void Cascade_child_does_not_inherit_overscroll_from_parent()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        var child = doc.CreateElement("span");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        var engine = new StyleEngine();
        // Only the parent sets overscroll-behavior. Because the longhands are
        // not inherited, the child must fall back to the initial `auto`.
        engine.AddStyleSheet(CssParser.ParseStyleSheet("div { overscroll-behavior: none; }"));

        var childStyle = engine.Compute(child);
        childStyle.Get(PropertyId.OverscrollBehaviorX).Should().Be(new CssKeyword("auto"));
        childStyle.Get(PropertyId.OverscrollBehaviorY).Should().Be(new CssKeyword("auto"));
    }

    // ---- Invalid values must be dropped at parse time (CSS Syntax). ----
    // The longhand parse path keeps any ident as a CssKeyword without checking
    // it against the accepted value set, so a bogus keyword survives instead of
    // being dropped. Tracked as a gap.
    [Spec("css-overscroll-1", "https://www.w3.org/TR/css-overscroll-1/#propdef-overscroll-behavior-x", section: "3")]
    [PendingFact("overscroll-behavior-x keeps invalid keyword 'scroll' instead of dropping the declaration", trackingWp: "wp:spec-css-overscroll-1")]
    public void OverscrollBehaviorX_drops_invalid_keyword()
        => Expand("overscroll-behavior-x: scroll;")
            .Should().NotContain(d => d.Id == PropertyId.OverscrollBehaviorX);
}

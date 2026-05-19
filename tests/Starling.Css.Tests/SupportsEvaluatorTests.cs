using FluentAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;
using Starling.Spec;

namespace Starling.Css.Tests;

[TestClass]
[Spec("css-conditional-3", "https://www.w3.org/TR/css-conditional-3/")]
public sealed class SupportsEvaluatorTests
{
    private static bool Evaluate(string condition)
    {
        var sheet = CssParser.ParseStyleSheet($"@supports {condition} {{ }}");
        var at = sheet.Rules.OfType<AtRule>().Single();
        return SupportsEvaluator.Evaluate(at.Prelude);
    }

    [TestMethod]
    public void Property_value_supported_when_registry_parses()
    {
        // `color: red` is registered. TODO(lane-B): re-test with `(display: grid)` after grid lands.
        Evaluate("(color: red)").Should().BeTrue();
    }

    [TestMethod]
    public void Unknown_property_is_unsupported()
    {
        Evaluate("(does-not-exist: 1)").Should().BeFalse();
    }

    [TestMethod]
    public void Not_inverts_support()
    {
        Evaluate("not (color: red)").Should().BeFalse();
        Evaluate("not (does-not-exist: 1)").Should().BeTrue();
    }

    [TestMethod]
    public void And_requires_both()
    {
        Evaluate("(color: red) and (margin: 1px)").Should().BeTrue();
        Evaluate("(color: red) and (does-not-exist: 1)").Should().BeFalse();
    }

    [TestMethod]
    public void Or_takes_either()
    {
        Evaluate("(does-not-exist: 1) or (color: red)").Should().BeTrue();
        Evaluate("(does-not-exist: 1) or (also-fake: 2)").Should().BeFalse();
    }

    [TestMethod]
    public void Selector_function_returns_true_for_parseable_selectors()
    {
        Evaluate("selector(:has(*))").Should().BeTrue();
        Evaluate("selector(.foo)").Should().BeTrue();
    }

    [TestMethod]
    public void Font_tech_returns_false_for_now()
    {
        Evaluate("font-tech(color-COLRv1)").Should().BeFalse();
    }

    [TestMethod]
    public void StyleEngine_skips_unsupported_block()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @supports (does-not-exist: 1) { p { color: red; } }
            """));

        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(CssColor.Black);
    }

    [TestMethod]
    public void StyleEngine_applies_supported_block()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @supports (color: red) { p { color: red; } }
            """));

        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(new CssColor(255, 0, 0));
    }
}

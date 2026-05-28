using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Dom;
using CssColorValue = Starling.Css.Values.CssColor;

namespace Starling.Css.Spec.Tests.CssConditional5;

/// <summary>
/// Conformance suite for <see href="https://www.w3.org/TR/css-conditional-5/">CSS Conditional Rules Level 5</see>
/// — <c>@supports</c> evaluation.
/// </summary>
[TestClass]
[Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/")]
public sealed class SupportsTests
{
    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private static bool Evaluate(string condition)
    {
        var sheet = CssParser.ParseStyleSheet($"@supports {condition} {{ }}");
        var at = sheet.Rules.OfType<AtRule>().Single();
        return SupportsEvaluator.Evaluate(at.Prelude);
    }

    // =========================================================================
    // §3  Declaration tests  (prop: value)
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-supports-ext"/>
    /// <para>A declaration for a known property with a valid value evaluates to true.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Known_property_with_valid_value_is_supported()
    {
        Evaluate("(color: red)").Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-supports-ext"/>
    /// <para><c>display: block</c> is a known property/value pair.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Display_block_is_supported()
    {
        Evaluate("(display: block)").Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-supports-ext"/>
    /// <para><c>margin: 0</c> via shorthand expansion is supported.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Margin_shorthand_is_supported()
    {
        Evaluate("(margin: 0)").Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-supports-ext"/>
    /// <para>An unknown property evaluates to false.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Unknown_property_is_not_supported()
    {
        Evaluate("(does-not-exist: 1)").Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-supports-ext"/>
    /// <para>A known property with an invalid value evaluates to false.
    /// Currently the evaluator does not fully validate values — it accepts any token stream
    /// that yields at least one parsed longhand, even with bogus value tokens.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Known_property_invalid_value_is_not_supported()
    {
        // The spec requires that `@supports (color: not-a-valid-color-ever-12345)` evaluates to false
        // because the value is not a valid CSS color. The current evaluator returns true because
        // PropertyRegistry.Parse() returns some result even for unrecognized value tokens.
        Evaluate("(color: not-a-valid-color-ever-12345)").Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-supports-ext"/>
    /// <para>Custom properties (<c>--*</c>) are always considered supported per spec.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Custom_property_is_always_supported()
    {
        Evaluate("(--my-custom: anything)").Should().BeTrue();
    }

    // =========================================================================
    // §3.1  not
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-supports-ext"/>
    /// <para><c>not (prop: value)</c> negates the result.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Not_inverts_a_supported_property()
    {
        Evaluate("not (color: red)").Should().BeFalse();
    }

    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Not_inverts_an_unsupported_property()
    {
        Evaluate("not (does-not-exist: 1)").Should().BeTrue();
    }

    // =========================================================================
    // §3.2  and
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-supports-ext"/>
    /// <para><c>and</c> is true only when both operands are supported.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void And_both_supported_is_true()
    {
        Evaluate("(color: red) and (margin: 1px)").Should().BeTrue();
    }

    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void And_one_unsupported_is_false()
    {
        Evaluate("(color: red) and (does-not-exist: 1)").Should().BeFalse();
    }

    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void And_both_unsupported_is_false()
    {
        Evaluate("(no-such: 1) and (also-fake: 2)").Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-supports-ext"/>
    /// <para>Three-arm <c>and</c> chain: all must be supported.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void And_three_arms_all_supported()
    {
        Evaluate("(color: red) and (margin: 0) and (display: block)").Should().BeTrue();
    }

    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void And_three_arms_one_unsupported_is_false()
    {
        Evaluate("(color: red) and (no-such: 1) and (display: block)").Should().BeFalse();
    }

    // =========================================================================
    // §3.3  or
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-supports-ext"/>
    /// <para><c>or</c> is true when at least one operand is supported.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Or_one_supported_is_true()
    {
        Evaluate("(does-not-exist: 1) or (color: red)").Should().BeTrue();
    }

    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Or_both_supported_is_true()
    {
        Evaluate("(color: red) or (margin: 1px)").Should().BeTrue();
    }

    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Or_both_unsupported_is_false()
    {
        Evaluate("(does-not-exist: 1) or (also-fake: 2)").Should().BeFalse();
    }

    // =========================================================================
    // §3.4  Parenthesization / nesting
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-supports-ext"/>
    /// <para>A condition nested inside extra parentheses is evaluated correctly.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Nested_not_inside_parens()
    {
        // `not` inside a parens block: inner condition is `not (color: red)` = false
        Evaluate("(not (color: red))").Should().BeFalse();
    }

    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Nested_and_inside_parens()
    {
        // Outer `and` between a nested block and a declaration.
        Evaluate("((color: red) and (margin: 0)) or (does-not-exist: 1)").Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-supports-ext"/>
    /// <para><c>not and</c>: <c>not</c> applied to an <c>and</c> sub-expression.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void Not_of_and_expression()
    {
        // not ( (color: red) and (does-not-exist: 1) ) = not (true and false) = not false = true
        Evaluate("not ((color: red) and (does-not-exist: 1))").Should().BeTrue();
    }

    // =========================================================================
    // §4  selector() function
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#supports-selector"/>
    /// <para><c>selector(:has(*))</c> is true because the selector is parseable.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#supports-selector")]
    [SpecFact]
    public void Selector_function_valid_selector_is_true()
    {
        Evaluate("selector(:has(*))").Should().BeTrue();
    }

    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#supports-selector")]
    [SpecFact]
    public void Selector_function_class_selector_is_true()
    {
        Evaluate("selector(.foo)").Should().BeTrue();
    }

    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#supports-selector")]
    [SpecFact]
    public void Selector_function_element_selector_is_true()
    {
        Evaluate("selector(div > span)").Should().BeTrue();
    }

    // =========================================================================
    // §5  font-tech() / font-format() functions (unsupported in v1)
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-supports-ext"/>
    /// <para><c>font-tech()</c> always returns false in the current implementation.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void FontTech_returns_false()
    {
        Evaluate("font-tech(color-COLRv1)").Should().BeFalse();
    }

    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-supports-ext")]
    [SpecFact]
    public void FontFormat_returns_false()
    {
        Evaluate("font-format(woff2)").Should().BeFalse();
    }

    // =========================================================================
    // §6  StyleEngine integration
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-ruledef-supports"/>
    /// <para>Rules inside an unsupported <c>@supports</c> block are not applied.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-ruledef-supports")]
    [SpecFact]
    public void StyleEngine_skips_unsupported_block()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet(
            "@supports (does-not-exist: 1) { p { color: red; } }"));
        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(CssColorValue.Black);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-ruledef-supports"/>
    /// <para>Rules inside a supported <c>@supports</c> block are applied.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-ruledef-supports")]
    [SpecFact]
    public void StyleEngine_applies_supported_block()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet(
            "@supports (color: red) { p { color: red; } }"));
        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(new CssColorValue(255, 0, 0));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-ruledef-supports"/>
    /// <para>A <c>not</c>-guarded unsupported block flips to supported; rules apply.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-ruledef-supports")]
    [SpecFact]
    public void StyleEngine_not_unsupported_block_applies()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet(
            "@supports not (does-not-exist: 1) { p { color: blue; } }"));
        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(new CssColorValue(0, 0, 255));
    }

    // =========================================================================
    // §7  Pending: selector() for an unparseable selector
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#supports-selector"/>
    /// <para>An unparseable selector inside <c>selector()</c> should yield false per spec.
    /// This probes whether the implementation rejects a genuinely invalid selector.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#supports-selector")]
    [SpecFact]
    public void Selector_function_invalid_selector_is_false()
    {
        // A bare `&` is not valid outside nesting context; the parser should fail on it.
        Evaluate("selector(&invalid-syntax)").Should().BeFalse();
    }
}

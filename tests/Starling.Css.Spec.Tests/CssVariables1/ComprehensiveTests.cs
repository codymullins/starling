using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.CssVariables1;

/// <summary>
/// Comprehensive gap-filling conformance tests for
/// <see href="https://www.w3.org/TR/css-variables-1/">CSS Custom Properties Level 1</see>.
/// Covers §2 naming, §3 substitution variants, §3.1 invalid-at-computed-value-time,
/// §3.2 guaranteed-invalid value, and substitution chains. Does not duplicate names
/// from <see cref="CustomPropertyParsingTests"/>, <see cref="VarSubstitutionTests"/>,
/// or <see cref="VarCycleDetectionTests"/>.
/// </summary>
[TestClass]
[Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/")]
public sealed class ComprehensiveTests
{
    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>Compute style for a single div with no parent context.</summary>
    private static ComputedStyle Compute(string divCss)
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        doc.AppendChild(el);
        var engine = new StyleEngine();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("div { " + divCss + " }"));
        return engine.Compute(el);
    }

    /// <summary>
    /// Compute style for a child div nested inside a section.
    /// Both rules are in the same stylesheet. Useful for inheritance tests.
    /// </summary>
    private static ComputedStyle ComputeChild(string parentCss, string childCss)
    {
        var doc = new Document();
        var parent = doc.CreateElement("section");
        var child = doc.CreateElement("div");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        var engine = new StyleEngine();
        engine.AddStyleSheet(CssParser.ParseStyleSheet(
            "section { " + parentCss + " } div { " + childCss + " }"));
        return engine.Compute(child);
    }

    // ---------------------------------------------------------------
    // §2 — Custom property naming
    // ---------------------------------------------------------------

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#defining-variables", section: "2")]
    [SpecFact]
    public void Custom_property_names_are_case_sensitive_uppercase_differs_from_lowercase()
    {
        // §2: custom property names are case-sensitive identifiers.
        // --Foo and --foo are two distinct properties.
        var style = Compute("--Foo: upper; --foo: lower;");
        style.GetPropertyValue("--Foo").Should().Be("upper");
        style.GetPropertyValue("--foo").Should().Be("lower");
        style.GetPropertyValue("--Foo").Should().NotBe(style.GetPropertyValue("--foo"));
    }

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#defining-variables", section: "2")]
    [SpecFact]
    public void Custom_property_accepts_empty_value()
    {
        // §2: a custom property may be set to an empty value ("").
        // The stored token list is empty; GetPropertyValue returns "".
        var style = Compute("--empty: ;");
        style.GetPropertyValue("--empty").Should().BeEmpty();
    }

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#defining-variables", section: "2")]
    [SpecFact]
    public void Custom_property_value_is_whitespace_trimmed()
    {
        // §2 / CSSOM §6.7.4: leading and trailing whitespace is stripped when
        // serializing the computed value of a custom property.
        var style = Compute("--spaced:   hello   ;");
        style.GetPropertyValue("--spaced").Should().Be("hello");
    }

    // ---------------------------------------------------------------
    // §3 — var() substitution variants
    // ---------------------------------------------------------------

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#using-variables", section: "3")]
    [SpecFact]
    public void Var_used_in_shorthand_margin_all_sides()
    {
        // §3: var() may appear anywhere a value is expected, including shorthands.
        // margin: var(--m) should set all four margin longhands to the resolved value.
        var style = Compute("--m: 8px; margin: var(--m);");

        var expected = new CssLength(8, CssLengthUnit.Px);
        style.GetLength(PropertyId.MarginTop).Should().Be(expected);
        style.GetLength(PropertyId.MarginRight).Should().Be(expected);
        style.GetLength(PropertyId.MarginBottom).Should().Be(expected);
        style.GetLength(PropertyId.MarginLeft).Should().Be(expected);
    }

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#using-variables", section: "3")]
    [SpecFact]
    public void Var_provides_only_part_of_value_two_value_padding()
    {
        // §3: var() may supply one token within a multi-token value.
        // padding: 0 var(--x) → top/bottom = 0, right/left = resolved value.
        var style = Compute("--x: 12px; padding: 0 var(--x);");

        style.GetLength(PropertyId.PaddingTop).Should().Be(CssLength.Zero);
        style.GetLength(PropertyId.PaddingBottom).Should().Be(CssLength.Zero);
        var side = new CssLength(12, CssLengthUnit.Px);
        style.GetLength(PropertyId.PaddingRight).Should().Be(side);
        style.GetLength(PropertyId.PaddingLeft).Should().Be(side);
    }

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#using-variables", section: "3")]
    [SpecFact]
    public void Var_fallback_with_internal_commas_uses_whole_remainder()
    {
        // §3: the fallback of var(--x, a, b) is everything after the first comma —
        // i.e. the fallback value is "a, b" not just "a". For a custom property
        // this round-trips as the stored token stream "a, b".
        var style = Compute("--val: var(--missing, hello, world);");
        // GetPropertyValue on a custom property that itself contains var() will
        // substitute and serialize. --val's value stream after substitution is
        // the fallback tokens "hello, world".
        style.GetPropertyValue("--val").Should().Contain(",");
    }

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#using-variables", section: "3")]
    [SpecFact]
    public void Custom_property_inherits_to_child_element()
    {
        // §3: custom properties are inherited by default — a child element
        // sees its parent's custom property value even without its own declaration.
        var style = ComputeChild("--brand: red;", "color: var(--brand);");
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(255, 0, 0));
    }

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#using-variables", section: "3")]
    [SpecFact]
    public void Child_can_override_inherited_custom_property()
    {
        // §3 / cascade: a child's declaration of the same custom property wins
        // over the inherited value because it is an explicit declaration.
        var style = ComputeChild("--c: blue;", "--c: green; color: var(--c);");
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0));
    }

    // ---------------------------------------------------------------
    // §3 — substitution chain (--a → --b → --c → value)
    // ---------------------------------------------------------------

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#using-variables", section: "3")]
    [SpecFact]
    public void Substitution_chains_across_three_hops()
    {
        // var() that references another var() that references a concrete value.
        var style = Compute("--a: var(--b); --b: var(--c); --c: blue; color: var(--a);");
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 0, 255));
    }

    // ---------------------------------------------------------------
    // §3.1 — guaranteed-invalid value (empty initial custom property)
    // ---------------------------------------------------------------

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#guaranteed-invalid", section: "3.1")]
    [PendingFact(
        "Engine substitutes the empty token list from --x: ; into color: var(--x, green) and then " +
        "applies IACVT, but falls back to `unset` (initial black) instead of the var() fallback " +
        "(green). §3.1 requires that when the substituted value is empty the var() fallback wins.",
        trackingWp: "wp:spec-css-variables-1")]
    public void Var_referencing_empty_custom_property_uses_fallback()
    {
        // §3.1: an empty custom property value ( --x: ; ) is not the
        // guaranteed-invalid value — it is a valid empty list. But when a
        // non-custom property is set via var(--x) and the substitution yields
        // an empty token list, the value is invalid at computed-value time.
        // With a fallback: the var() fallback should be used.
        var style = Compute("--x: ; color: var(--x, green);");
        // The empty substitution makes the declaration invalid; spec says fallback green wins.
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0));
    }

    // ---------------------------------------------------------------
    // §3.2 — invalid at computed-value time (IACVT)
    // ---------------------------------------------------------------

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#invalid-at-computed-value-time", section: "3.2")]
    [SpecFact]
    public void Iacvt_on_inherited_property_inherits_from_parent()
    {
        // §3.2: if a var() in an inherited property cannot be substituted (no
        // fallback, undefined variable), the declaration is IACVT → behaves as
        // `inherit`. The child therefore inherits color from the parent (purple).
        var doc = new Document();
        var parent = doc.CreateElement("section");
        var child = doc.CreateElement("div");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        var engine = new StyleEngine();
        engine.AddStyleSheet(CssParser.ParseStyleSheet(
            "section { color: purple; } div { color: var(--undefined); }"));

        var parentStyle = engine.Compute(parent);
        var childStyle = engine.Compute(child);

        // parent gets purple
        parentStyle.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(128, 0, 128));
        // child IACVT → inherits purple
        childStyle.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(128, 0, 128));
    }

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#invalid-at-computed-value-time", section: "3.2")]
    [SpecFact]
    public void Iacvt_on_non_inherited_property_uses_initial_value()
    {
        // §3.2: if a var() in a non-inherited property cannot be substituted,
        // IACVT → behaves as `initial`. margin-top initial value is 0.
        var style = Compute("margin-top: var(--undefined);");
        style.GetLength(PropertyId.MarginTop).Should().Be(CssLength.Zero);
    }

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#invalid-at-computed-value-time", section: "3.2")]
    [SpecFact]
    public void Var_with_non_custom_property_name_is_invalid()
    {
        // §3.2 / §3: var() with a name that does not start with "--" is not a
        // valid custom property reference. The function is invalid → IACVT.
        // color: var(color) should not resolve; color falls through to initial (black).
        var style = Compute("color: var(color);");
        // Should be black (initial), not some unexpected value.
        style.GetColor(PropertyId.Color).Should().Be(Starling.Css.Values.CssColor.Black);
    }

    // ---------------------------------------------------------------
    // §3 — var() referencing an undefined property (no fallback) makes
    //       the declaration IACVT even when the property name is valid.
    // ---------------------------------------------------------------

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#using-variables", section: "3")]
    [SpecFact]
    public void Var_referencing_undefined_property_without_fallback_is_iacvt()
    {
        // var(--totally-missing) with no fallback → IACVT.
        // color is inherited; with no parent the initial value (black) applies.
        var style = Compute("color: var(--totally-missing);");
        style.GetColor(PropertyId.Color).Should().Be(Starling.Css.Values.CssColor.Black);
    }

    // ---------------------------------------------------------------
    // GetPropertyValue round-trip for a defined var chain
    // ---------------------------------------------------------------

    [Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/#using-variables", section: "3")]
    [SpecFact]
    public void GetPropertyValue_returns_resolved_token_stream_for_custom_property()
    {
        // CSSOM §6.7.4 + §3: GetPropertyValue("--x") on a custom property whose
        // value is itself a var() reference should return the substituted text.
        var style = Compute("--base: 10px; --size: var(--base);");
        style.GetPropertyValue("--size").Should().Be("10px");
    }
}

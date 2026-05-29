using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssSizing3;

/// <summary>
/// Property conformance for <see href="https://www.w3.org/TR/css-sizing-3/">CSS Box Sizing Module Level 3</see>
/// (width, height, min-width, min-height, max-width, max-height, box-sizing, and intrinsic-size keywords).
/// <para>
/// Tracking work package: <c>wp:spec-css-sizing-3</c>.
/// </para>
/// </summary>
[TestClass]
[Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/")]
public sealed class PropertyTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    // =========================================================================
    // §5 — width / height
    // https://www.w3.org/TR/css-sizing-3/#propdef-width
    // =========================================================================

    /// <summary>
    /// CSS Sizing 3 §5 — <c>width: auto</c> parses to the keyword <c>auto</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-width", section: "5")]
    [SpecFact]
    public void Width_parses_auto()
        => ValueOf("width: auto", PropertyId.Width).Should().Be(new CssKeyword("auto"));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>width: 0</c> parses to a zero length.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-width", section: "5")]
    [SpecFact]
    public void Width_parses_zero()
        => ValueOf("width: 0", PropertyId.Width).Should().Be(new CssNumber(0));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>width</c> parses pixel lengths.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-width", section: "5")]
    [SpecFact]
    public void Width_parses_px_length()
        => ValueOf("width: 200px", PropertyId.Width).Should().Be(new CssLength(200, CssLengthUnit.Px));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>width</c> parses em lengths.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-width", section: "5")]
    [SpecFact]
    public void Width_parses_em_length()
        => ValueOf("width: 10em", PropertyId.Width).Should().Be(new CssLength(10, CssLengthUnit.Em));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>width</c> parses percentage values.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-width", section: "5")]
    [SpecFact]
    public void Width_parses_percentage()
        => ValueOf("width: 50%", PropertyId.Width).Should().Be(new CssPercentage(50));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>width</c> initial value is <c>auto</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-width", section: "5")]
    [SpecFact]
    public void Width_initial_is_auto()
        => PropertyRegistry.InitialValue(PropertyId.Width).Should().Be(new CssKeyword("auto"));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>width</c> is NOT inherited.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-width", section: "5")]
    [SpecFact]
    public void Width_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.Width).Should().BeFalse();

    /// <summary>
    /// CSS Sizing 3 §5 — <c>height: auto</c> parses to the keyword <c>auto</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-height", section: "5")]
    [SpecFact]
    public void Height_parses_auto()
        => ValueOf("height: auto", PropertyId.Height).Should().Be(new CssKeyword("auto"));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>height: 0</c> parses to a zero number.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-height", section: "5")]
    [SpecFact]
    public void Height_parses_zero()
        => ValueOf("height: 0", PropertyId.Height).Should().Be(new CssNumber(0));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>height</c> parses pixel lengths.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-height", section: "5")]
    [SpecFact]
    public void Height_parses_px_length()
        => ValueOf("height: 100px", PropertyId.Height).Should().Be(new CssLength(100, CssLengthUnit.Px));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>height</c> parses em lengths.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-height", section: "5")]
    [SpecFact]
    public void Height_parses_em_length()
        => ValueOf("height: 5em", PropertyId.Height).Should().Be(new CssLength(5, CssLengthUnit.Em));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>height</c> parses percentage values.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-height", section: "5")]
    [SpecFact]
    public void Height_parses_percentage()
        => ValueOf("height: 75%", PropertyId.Height).Should().Be(new CssPercentage(75));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>height</c> initial value is <c>auto</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-height", section: "5")]
    [SpecFact]
    public void Height_initial_is_auto()
        => PropertyRegistry.InitialValue(PropertyId.Height).Should().Be(new CssKeyword("auto"));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>height</c> is NOT inherited.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-height", section: "5")]
    [SpecFact]
    public void Height_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.Height).Should().BeFalse();

    // =========================================================================
    // §5 — min-width / min-height
    // https://www.w3.org/TR/css-sizing-3/#propdef-min-width
    // =========================================================================

    /// <summary>
    /// CSS Sizing 3 §5 — <c>min-width: auto</c> parses to the keyword <c>auto</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-min-width", section: "5")]
    [SpecFact]
    public void MinWidth_parses_auto()
        => ValueOf("min-width: auto", PropertyId.MinWidth).Should().Be(new CssKeyword("auto"));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>min-width</c> parses pixel lengths.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-min-width", section: "5")]
    [SpecFact]
    public void MinWidth_parses_px_length()
        => ValueOf("min-width: 120px", PropertyId.MinWidth).Should().Be(new CssLength(120, CssLengthUnit.Px));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>min-width</c> parses percentage values.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-min-width", section: "5")]
    [SpecFact]
    public void MinWidth_parses_percentage()
        => ValueOf("min-width: 25%", PropertyId.MinWidth).Should().Be(new CssPercentage(25));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>min-width</c> initial value is <c>0</c> (as a zero-length).
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-min-width", section: "5")]
    [SpecFact]
    public void MinWidth_initial_is_zero()
        => PropertyRegistry.InitialValue(PropertyId.MinWidth).Should().Be(CssLength.Zero);

    /// <summary>
    /// CSS Sizing 3 §5 — <c>min-width</c> is NOT inherited.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-min-width", section: "5")]
    [SpecFact]
    public void MinWidth_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.MinWidth).Should().BeFalse();

    /// <summary>
    /// CSS Sizing 3 §5 — <c>min-height: auto</c> parses to the keyword <c>auto</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-min-height", section: "5")]
    [SpecFact]
    public void MinHeight_parses_auto()
        => ValueOf("min-height: auto", PropertyId.MinHeight).Should().Be(new CssKeyword("auto"));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>min-height</c> parses pixel lengths.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-min-height", section: "5")]
    [SpecFact]
    public void MinHeight_parses_px_length()
        => ValueOf("min-height: 40px", PropertyId.MinHeight).Should().Be(new CssLength(40, CssLengthUnit.Px));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>min-height</c> parses percentage values.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-min-height", section: "5")]
    [SpecFact]
    public void MinHeight_parses_percentage()
        => ValueOf("min-height: 10%", PropertyId.MinHeight).Should().Be(new CssPercentage(10));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>min-height</c> initial value is <c>0</c> (as a zero-length).
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-min-height", section: "5")]
    [SpecFact]
    public void MinHeight_initial_is_zero()
        => PropertyRegistry.InitialValue(PropertyId.MinHeight).Should().Be(CssLength.Zero);

    /// <summary>
    /// CSS Sizing 3 §5 — <c>min-height</c> is NOT inherited.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-min-height", section: "5")]
    [SpecFact]
    public void MinHeight_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.MinHeight).Should().BeFalse();

    // =========================================================================
    // §5 — max-width / max-height
    // https://www.w3.org/TR/css-sizing-3/#propdef-max-width
    // =========================================================================

    /// <summary>
    /// CSS Sizing 3 §5 — <c>max-width: none</c> parses to the keyword <c>none</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-max-width", section: "5")]
    [SpecFact]
    public void MaxWidth_parses_none()
        => ValueOf("max-width: none", PropertyId.MaxWidth).Should().Be(new CssKeyword("none"));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>max-width</c> parses pixel lengths.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-max-width", section: "5")]
    [SpecFact]
    public void MaxWidth_parses_px_length()
        => ValueOf("max-width: 800px", PropertyId.MaxWidth).Should().Be(new CssLength(800, CssLengthUnit.Px));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>max-width</c> parses percentage values.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-max-width", section: "5")]
    [SpecFact]
    public void MaxWidth_parses_percentage()
        => ValueOf("max-width: 100%", PropertyId.MaxWidth).Should().Be(new CssPercentage(100));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>max-width</c> initial value is <c>none</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-max-width", section: "5")]
    [SpecFact]
    public void MaxWidth_initial_is_none()
        => PropertyRegistry.InitialValue(PropertyId.MaxWidth).Should().Be(new CssKeyword("none"));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>max-width</c> is NOT inherited.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-max-width", section: "5")]
    [SpecFact]
    public void MaxWidth_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.MaxWidth).Should().BeFalse();

    /// <summary>
    /// CSS Sizing 3 §5 — <c>max-height: none</c> parses to the keyword <c>none</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-max-height", section: "5")]
    [SpecFact]
    public void MaxHeight_parses_none()
        => ValueOf("max-height: none", PropertyId.MaxHeight).Should().Be(new CssKeyword("none"));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>max-height</c> parses pixel lengths.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-max-height", section: "5")]
    [SpecFact]
    public void MaxHeight_parses_px_length()
        => ValueOf("max-height: 600px", PropertyId.MaxHeight).Should().Be(new CssLength(600, CssLengthUnit.Px));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>max-height</c> parses percentage values.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-max-height", section: "5")]
    [SpecFact]
    public void MaxHeight_parses_percentage()
        => ValueOf("max-height: 80%", PropertyId.MaxHeight).Should().Be(new CssPercentage(80));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>max-height</c> initial value is <c>none</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-max-height", section: "5")]
    [SpecFact]
    public void MaxHeight_initial_is_none()
        => PropertyRegistry.InitialValue(PropertyId.MaxHeight).Should().Be(new CssKeyword("none"));

    /// <summary>
    /// CSS Sizing 3 §5 — <c>max-height</c> is NOT inherited.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#propdef-max-height", section: "5")]
    [SpecFact]
    public void MaxHeight_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.MaxHeight).Should().BeFalse();

    // =========================================================================
    // §5.1 — box-sizing (shared with CSS Box Model 3; assert parse only)
    // https://www.w3.org/TR/css-sizing-3/#box-sizing
    // =========================================================================

    /// <summary>
    /// CSS Sizing 3 §5.1 — <c>box-sizing: content-box</c> parses to the keyword <c>content-box</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#box-sizing", section: "5.1")]
    [SpecFact]
    public void BoxSizing_parses_content_box()
        => ValueOf("box-sizing: content-box", PropertyId.BoxSizing).Should().Be(new CssKeyword("content-box"));

    /// <summary>
    /// CSS Sizing 3 §5.1 — <c>box-sizing: border-box</c> parses to the keyword <c>border-box</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#box-sizing", section: "5.1")]
    [SpecFact]
    public void BoxSizing_parses_border_box()
        => ValueOf("box-sizing: border-box", PropertyId.BoxSizing).Should().Be(new CssKeyword("border-box"));

    /// <summary>
    /// CSS Sizing 3 §5.1 — <c>box-sizing</c> initial value is <c>content-box</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#box-sizing", section: "5.1")]
    [SpecFact]
    public void BoxSizing_initial_is_content_box()
        => PropertyRegistry.InitialValue(PropertyId.BoxSizing).Should().Be(new CssKeyword("content-box"));

    // =========================================================================
    // §4 — Intrinsic sizing keywords (min-content, max-content, fit-content)
    // https://www.w3.org/TR/css-sizing-3/#sizing-values
    // =========================================================================

    /// <summary>
    /// CSS Sizing 3 §4 — <c>width: min-content</c> must parse to the keyword <c>min-content</c>.
    /// The Starling CSS engine passes identifiers through the value parser as-is, so
    /// <c>min-content</c> should round-trip as a <see cref="CssKeyword"/>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-min-content", section: "4")]
    [SpecFact]
    public void Width_min_content_parses_as_keyword()
        => ValueOf("width: min-content", PropertyId.Width).Should().Be(new CssKeyword("min-content"));

    /// <summary>
    /// CSS Sizing 3 §4 — <c>width: max-content</c> must parse to the keyword <c>max-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-max-content", section: "4")]
    [SpecFact]
    public void Width_max_content_parses_as_keyword()
        => ValueOf("width: max-content", PropertyId.Width).Should().Be(new CssKeyword("max-content"));

    /// <summary>
    /// CSS Sizing 3 §4 — <c>width: fit-content</c> (bare keyword, no argument) must parse to the keyword
    /// <c>fit-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-fit-content", section: "4")]
    [SpecFact]
    public void Width_fit_content_keyword_parses()
        => ValueOf("width: fit-content", PropertyId.Width).Should().Be(new CssKeyword("fit-content"));

    /// <summary>
    /// CSS Sizing 3 §4 — <c>height: min-content</c> must parse to the keyword <c>min-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-min-content", section: "4")]
    [SpecFact]
    public void Height_min_content_parses_as_keyword()
        => ValueOf("height: min-content", PropertyId.Height).Should().Be(new CssKeyword("min-content"));

    /// <summary>
    /// CSS Sizing 3 §4 — <c>height: max-content</c> must parse to the keyword <c>max-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-max-content", section: "4")]
    [SpecFact]
    public void Height_max_content_parses_as_keyword()
        => ValueOf("height: max-content", PropertyId.Height).Should().Be(new CssKeyword("max-content"));

    /// <summary>
    /// CSS Sizing 3 §4 — <c>height: fit-content</c> (bare keyword) must parse to the keyword <c>fit-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-fit-content", section: "4")]
    [SpecFact]
    public void Height_fit_content_keyword_parses()
        => ValueOf("height: fit-content", PropertyId.Height).Should().Be(new CssKeyword("fit-content"));

    /// <summary>
    /// CSS Sizing 3 §4 — <c>width: fit-content(200px)</c> (functional notation) must parse to a
    /// <see cref="CssFunctionValue"/> named <c>fit-content</c> with a single <c>200px</c> argument.
    /// The engine currently passes CSS functions through as <see cref="CssFunctionValue"/> records, so
    /// this should already work at the parse layer even though layout does not yet consume the value.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-fit-content-length-percentage", section: "4")]
    [SpecFact]
    public void Width_fit_content_function_parses()
    {
        var value = ValueOf("width: fit-content(200px)", PropertyId.Width);
        var fn = value.Should().BeOfType<CssFunctionValue>().Subject;
        fn.Name.Should().Be("fit-content");
        fn.Arguments.Should().HaveCount(1);
        fn.Arguments[0].Should().Be(new CssLength(200, CssLengthUnit.Px));
    }

    /// <summary>
    /// CSS Sizing 3 §4 — <c>height: fit-content(50%)</c> (functional notation with percentage argument)
    /// must parse to a <see cref="CssFunctionValue"/> named <c>fit-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-fit-content-length-percentage", section: "4")]
    [SpecFact]
    public void Height_fit_content_function_with_percent_parses()
    {
        var value = ValueOf("height: fit-content(50%)", PropertyId.Height);
        var fn = value.Should().BeOfType<CssFunctionValue>().Subject;
        fn.Name.Should().Be("fit-content");
        fn.Arguments.Should().HaveCount(1);
        fn.Arguments[0].Should().Be(new CssPercentage(50));
    }

    // =========================================================================
    // §4 — stretch / -webkit-fill-available
    // https://www.w3.org/TR/css-sizing-3/#valdef-width-stretch
    // =========================================================================

    /// <summary>
    /// CSS Sizing 3 §4 — <c>width: stretch</c> is the standard keyword for fill-available sizing.
    /// The Starling value parser accepts any ident as a keyword, so this should round-trip as
    /// <c>CssKeyword("stretch")</c> even if the layout engine does not yet act on it.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-stretch", section: "4")]
    [SpecFact]
    public void Width_stretch_parses_as_keyword()
        => ValueOf("width: stretch", PropertyId.Width).Should().Be(new CssKeyword("stretch"));

    /// <summary>
    /// CSS Sizing 3 §4 — <c>width: -webkit-fill-available</c> is the legacy vendor-prefixed form of
    /// <c>stretch</c>, widely used in existing stylesheets.  The value parser lowercases idents and
    /// preserves them verbatim, so the value should round-trip as the exact keyword string.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-stretch", section: "4")]
    [SpecFact]
    public void Width_webkit_fill_available_parses_as_keyword()
        => ValueOf("width: -webkit-fill-available", PropertyId.Width)
            .Should().Be(new CssKeyword("-webkit-fill-available"));

    // =========================================================================
    // §4 — intrinsic keywords on min-width / min-height
    // =========================================================================

    /// <summary>
    /// CSS Sizing 3 §4 — <c>min-width: min-content</c> must parse to the keyword <c>min-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-min-content", section: "4")]
    [SpecFact]
    public void MinWidth_min_content_parses_as_keyword()
        => ValueOf("min-width: min-content", PropertyId.MinWidth).Should().Be(new CssKeyword("min-content"));

    /// <summary>
    /// CSS Sizing 3 §4 — <c>min-width: max-content</c> must parse to the keyword <c>max-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-max-content", section: "4")]
    [SpecFact]
    public void MinWidth_max_content_parses_as_keyword()
        => ValueOf("min-width: max-content", PropertyId.MinWidth).Should().Be(new CssKeyword("max-content"));

    /// <summary>
    /// CSS Sizing 3 §4 — <c>min-height: min-content</c> must parse to the keyword <c>min-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-min-content", section: "4")]
    [SpecFact]
    public void MinHeight_min_content_parses_as_keyword()
        => ValueOf("min-height: min-content", PropertyId.MinHeight).Should().Be(new CssKeyword("min-content"));

    /// <summary>
    /// CSS Sizing 3 §4 — <c>min-height: max-content</c> must parse to the keyword <c>max-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-max-content", section: "4")]
    [SpecFact]
    public void MinHeight_max_content_parses_as_keyword()
        => ValueOf("min-height: max-content", PropertyId.MinHeight).Should().Be(new CssKeyword("max-content"));

    // =========================================================================
    // §4 — intrinsic keywords on max-width / max-height
    // =========================================================================

    /// <summary>
    /// CSS Sizing 3 §4 — <c>max-width: min-content</c> must parse to the keyword <c>min-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-min-content", section: "4")]
    [SpecFact]
    public void MaxWidth_min_content_parses_as_keyword()
        => ValueOf("max-width: min-content", PropertyId.MaxWidth).Should().Be(new CssKeyword("min-content"));

    /// <summary>
    /// CSS Sizing 3 §4 — <c>max-width: max-content</c> must parse to the keyword <c>max-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-max-content", section: "4")]
    [SpecFact]
    public void MaxWidth_max_content_parses_as_keyword()
        => ValueOf("max-width: max-content", PropertyId.MaxWidth).Should().Be(new CssKeyword("max-content"));

    /// <summary>
    /// CSS Sizing 3 §4 — <c>max-height: min-content</c> must parse to the keyword <c>min-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-min-content", section: "4")]
    [SpecFact]
    public void MaxHeight_min_content_parses_as_keyword()
        => ValueOf("max-height: min-content", PropertyId.MaxHeight).Should().Be(new CssKeyword("min-content"));

    /// <summary>
    /// CSS Sizing 3 §4 — <c>max-height: max-content</c> must parse to the keyword <c>max-content</c>.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-max-content", section: "4")]
    [SpecFact]
    public void MaxHeight_max_content_parses_as_keyword()
        => ValueOf("max-height: max-content", PropertyId.MaxHeight).Should().Be(new CssKeyword("max-content"));

    // =========================================================================
    // §4 — Layout conformance: intrinsic-size keywords are not yet resolved
    //      by the Starling layout engine (tracked: wp:spec-css-sizing-3).
    //      These PendingFacts document the gap and will be promoted to
    //      SpecFacts once layout support lands.
    // =========================================================================

    /// <summary>
    /// CSS Sizing 3 §4 — The layout engine must resolve <c>width: min-content</c> to the element's
    /// minimum content size.  This requires the Starling layout engine to query inline min-content
    /// sizes during block layout, which is not yet implemented.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-min-content", section: "4")]
    [PendingFact("Layout engine does not yet resolve min-content sizing keyword", trackingWp: "wp:spec-css-sizing-3")]
    public void Layout_resolves_width_min_content()
    {
        // When this is promoted, add a layout-level assertion via the render pipeline.
        // For now, confirm the parsed value is the correct keyword (parse-layer is already
        // covered by Width_min_content_parses_as_keyword above).
        var value = ValueOf("width: min-content", PropertyId.Width);
        value.Should().Be(new CssKeyword("min-content"));
    }

    /// <summary>
    /// CSS Sizing 3 §4 — The layout engine must resolve <c>width: max-content</c> to the element's
    /// maximum content size.  Not yet implemented in the Starling layout engine.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-max-content", section: "4")]
    [PendingFact("Layout engine does not yet resolve max-content sizing keyword", trackingWp: "wp:spec-css-sizing-3")]
    public void Layout_resolves_width_max_content()
    {
        var value = ValueOf("width: max-content", PropertyId.Width);
        value.Should().Be(new CssKeyword("max-content"));
    }

    /// <summary>
    /// CSS Sizing 3 §4 — The layout engine must resolve <c>width: fit-content</c> to
    /// <c>min(max-content, max(min-content, stretch))</c>.  Not yet implemented.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-fit-content", section: "4")]
    [PendingFact("Layout engine does not yet resolve fit-content sizing keyword", trackingWp: "wp:spec-css-sizing-3")]
    public void Layout_resolves_width_fit_content_keyword()
    {
        var value = ValueOf("width: fit-content", PropertyId.Width);
        value.Should().Be(new CssKeyword("fit-content"));
    }

    /// <summary>
    /// CSS Sizing 3 §4 — The layout engine must resolve <c>width: fit-content(200px)</c> as
    /// <c>min(200px, max(min-content, stretch))</c>.  Not yet implemented.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-fit-content-length-percentage", section: "4")]
    [PendingFact("Layout engine does not yet resolve fit-content(<length>) functional form", trackingWp: "wp:spec-css-sizing-3")]
    public void Layout_resolves_width_fit_content_function()
    {
        var value = ValueOf("width: fit-content(200px)", PropertyId.Width);
        value.Should().BeOfType<CssFunctionValue>();
    }

    /// <summary>
    /// CSS Sizing 3 §4 — The layout engine must resolve <c>width: stretch</c> so that the element
    /// fills its available inline space.  Not yet implemented.
    /// </summary>
    [Spec("css-sizing-3", "https://www.w3.org/TR/css-sizing-3/#valdef-width-stretch", section: "4")]
    [PendingFact("Layout engine does not yet resolve the stretch sizing keyword", trackingWp: "wp:spec-css-sizing-3")]
    public void Layout_resolves_width_stretch()
    {
        var value = ValueOf("width: stretch", PropertyId.Width);
        value.Should().Be(new CssKeyword("stretch"));
    }
}

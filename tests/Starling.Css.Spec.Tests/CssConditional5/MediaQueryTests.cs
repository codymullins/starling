using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Media;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Dom;
using CssColorValue = Starling.Css.Values.CssColor;

namespace Starling.Css.Spec.Tests.CssConditional5;

/// <summary>
/// Conformance suite for <see href="https://www.w3.org/TR/css-conditional-5/">CSS Conditional Rules Level 5</see>
/// — <c>@media</c> evaluation.
/// </summary>
[TestClass]
[Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/")]
public sealed class MediaQueryTests
{
    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private static MediaQueryList Parse(string query)
    {
        var sheet = CssParser.ParseStyleSheet($"@media {query} {{ }}");
        var at = sheet.Rules.OfType<AtRule>().Single();
        return MediaQueryParser.ParseList(at.Prelude);
    }

    private static bool Eval(string query, MediaContext ctx)
        => MediaQueryEvaluator.Evaluate(Parse(query), ctx);

    // =========================================================================
    // §2  Media query structure — media types
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#media-types"/>
    /// <para>Media type <c>all</c> always matches regardless of medium.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#media-types")]
    [SpecFact]
    public void MediaType_all_matches_screen()
    {
        Eval("all", new MediaContext(MediaType: "screen")).Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#media-types"/>
    /// <para>Media type <c>all</c> matches every medium, including <c>print</c>.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#media-types")]
    [SpecFact]
    public void MediaType_all_matches_print()
    {
        Eval("all", new MediaContext(MediaType: "print")).Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#media-types"/>
    /// <para>Media type <c>screen</c> matches a screen context but not print.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#media-types")]
    [SpecFact]
    public void MediaType_screen_matches_screen_not_print()
    {
        Eval("screen", new MediaContext(MediaType: "screen")).Should().BeTrue();
        Eval("screen", new MediaContext(MediaType: "print")).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#media-types"/>
    /// <para>Media type <c>print</c> matches a print context but not screen.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#media-types")]
    [SpecFact]
    public void MediaType_print_matches_print_not_screen()
    {
        Eval("print", new MediaContext(MediaType: "print")).Should().BeTrue();
        Eval("print", new MediaContext(MediaType: "screen")).Should().BeFalse();
    }

    // =========================================================================
    // §2  Modifiers — not / only
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#mq-not"/>
    /// <para><c>not</c> modifier negates a media type match.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-not")]
    [SpecFact]
    public void Not_modifier_negates_media_type()
    {
        Eval("not print", new MediaContext(MediaType: "screen")).Should().BeTrue();
        Eval("not print", new MediaContext(MediaType: "print")).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#mq-not"/>
    /// <para><c>not</c> in condition form (no media type) negates the feature.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-not")]
    [SpecFact]
    public void Not_condition_form_negates_feature()
    {
        Eval("not (min-width: 400px)", new MediaContext(ViewportWidthPx: 300)).Should().BeTrue();
        Eval("not (min-width: 400px)", new MediaContext(ViewportWidthPx: 500)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#mq-only"/>
    /// <para><c>only</c> passes when the media type matches; legacy agents ignore it.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-only")]
    [SpecFact]
    public void Only_requires_media_type_match()
    {
        Eval("only screen and (min-width: 200px)", new MediaContext(MediaType: "screen", ViewportWidthPx: 500))
            .Should().BeTrue();
        Eval("only screen and (min-width: 200px)", new MediaContext(MediaType: "print", ViewportWidthPx: 500))
            .Should().BeFalse();
    }

    // =========================================================================
    // §3  Comma-separated query lists (OR semantics)
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#mq-list"/>
    /// <para>A comma-separated list is satisfied when any individual query matches.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-list")]
    [SpecFact]
    public void Comma_list_is_or_first_arm_true()
    {
        Eval("(min-width: 1000px), (max-width: 400px)", new MediaContext(ViewportWidthPx: 1200))
            .Should().BeTrue();
    }

    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-list")]
    [SpecFact]
    public void Comma_list_is_or_second_arm_true()
    {
        Eval("(min-width: 1000px), (max-width: 400px)", new MediaContext(ViewportWidthPx: 300))
            .Should().BeTrue();
    }

    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-list")]
    [SpecFact]
    public void Comma_list_is_or_both_arms_false()
    {
        Eval("(min-width: 1000px), (max-width: 400px)", new MediaContext(ViewportWidthPx: 600))
            .Should().BeFalse();
    }

    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-list")]
    [SpecFact]
    public void Comma_list_with_media_types()
    {
        // screen arm matches, print arm does not.
        Eval("screen, print", new MediaContext(MediaType: "screen")).Should().BeTrue();
        Eval("screen, print", new MediaContext(MediaType: "print")).Should().BeTrue();
        // neither arm matches an unknown type (we can test with an explicit query).
        Eval("screen, print", new MediaContext(MediaType: "speech")).Should().BeFalse();
    }

    // =========================================================================
    // §4  Width / min-width / max-width feature queries
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-width"/>
    /// <para><c>min-width</c> is satisfied when the viewport is at least the given value.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-width")]
    [SpecFact]
    public void MinWidth_matches_wider_viewport()
    {
        Eval("(min-width: 400px)", new MediaContext(ViewportWidthPx: 500)).Should().BeTrue();
        Eval("(min-width: 400px)", new MediaContext(ViewportWidthPx: 400)).Should().BeTrue();
        Eval("(min-width: 400px)", new MediaContext(ViewportWidthPx: 399)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-width"/>
    /// <para><c>max-width</c> is satisfied when the viewport is no wider than the given value.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-width")]
    [SpecFact]
    public void MaxWidth_matches_narrower_viewport()
    {
        Eval("(max-width: 600px)", new MediaContext(ViewportWidthPx: 500)).Should().BeTrue();
        Eval("(max-width: 600px)", new MediaContext(ViewportWidthPx: 600)).Should().BeTrue();
        Eval("(max-width: 600px)", new MediaContext(ViewportWidthPx: 601)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-width"/>
    /// <para>Exact <c>(width: Npx)</c> matches only when viewport width equals that value.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-width")]
    [SpecFact]
    public void Width_exact_matches_only_equal()
    {
        Eval("(width: 768px)", new MediaContext(ViewportWidthPx: 768)).Should().BeTrue();
        Eval("(width: 768px)", new MediaContext(ViewportWidthPx: 769)).Should().BeFalse();
    }

    // =========================================================================
    // §4  Height feature queries
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-height"/>
    /// <para><c>min-height</c> and <c>max-height</c> work symmetrically with width.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-height")]
    [SpecFact]
    public void MinHeight_and_MaxHeight()
    {
        Eval("(min-height: 600px)", new MediaContext(ViewportHeightPx: 768)).Should().BeTrue();
        Eval("(min-height: 600px)", new MediaContext(ViewportHeightPx: 400)).Should().BeFalse();
        Eval("(max-height: 600px)", new MediaContext(ViewportHeightPx: 500)).Should().BeTrue();
        Eval("(max-height: 600px)", new MediaContext(ViewportHeightPx: 700)).Should().BeFalse();
    }

    // =========================================================================
    // §5  Range syntax
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#mq-range-context"/>
    /// <para>Range form <c>(width &gt;= 400px)</c> is satisfied when the viewport is at least 400 px.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-range-context")]
    [SpecFact]
    public void Range_greater_or_equal()
    {
        Eval("(width >= 400px)", new MediaContext(ViewportWidthPx: 400)).Should().BeTrue();
        Eval("(width >= 400px)", new MediaContext(ViewportWidthPx: 399)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#mq-range-context"/>
    /// <para>Range form <c>(width &gt; 400px)</c> is satisfied only when the viewport is strictly wider than 400 px.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-range-context")]
    [SpecFact]
    public void Range_strict_greater()
    {
        Eval("(width > 400px)", new MediaContext(ViewportWidthPx: 401)).Should().BeTrue();
        Eval("(width > 400px)", new MediaContext(ViewportWidthPx: 400)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#mq-range-context"/>
    /// <para>Range form <c>(width &lt;= 600px)</c> matches viewports no wider than 600 px.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-range-context")]
    [SpecFact]
    public void Range_less_or_equal()
    {
        Eval("(width <= 600px)", new MediaContext(ViewportWidthPx: 600)).Should().BeTrue();
        Eval("(width <= 600px)", new MediaContext(ViewportWidthPx: 601)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#mq-range-context"/>
    /// <para>Range form <c>(width &lt; 600px)</c> matches viewports strictly narrower than 600 px.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-range-context")]
    [SpecFact]
    public void Range_strict_less()
    {
        Eval("(width < 600px)", new MediaContext(ViewportWidthPx: 599)).Should().BeTrue();
        Eval("(width < 600px)", new MediaContext(ViewportWidthPx: 600)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#mq-range-context"/>
    /// <para>Double-bounded range <c>(400px &lt;= width &lt;= 700px)</c> includes both endpoints.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-range-context")]
    [SpecFact]
    public void Range_double_bounds_inclusive()
    {
        Eval("(400px <= width <= 700px)", new MediaContext(ViewportWidthPx: 400)).Should().BeTrue();
        Eval("(400px <= width <= 700px)", new MediaContext(ViewportWidthPx: 550)).Should().BeTrue();
        Eval("(400px <= width <= 700px)", new MediaContext(ViewportWidthPx: 700)).Should().BeTrue();
        Eval("(400px <= width <= 700px)", new MediaContext(ViewportWidthPx: 399)).Should().BeFalse();
        Eval("(400px <= width <= 700px)", new MediaContext(ViewportWidthPx: 701)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#mq-range-context"/>
    /// <para>Range with <c>em</c> units converts using 1em = 16px by default.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-range-context")]
    [SpecFact]
    public void Range_em_unit_converts_to_px()
    {
        // 25em * 16 = 400px
        Eval("(width >= 25em)", new MediaContext(ViewportWidthPx: 400)).Should().BeTrue();
        Eval("(width >= 25em)", new MediaContext(ViewportWidthPx: 399)).Should().BeFalse();
    }

    // =========================================================================
    // §6  Orientation feature
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-orientation"/>
    /// <para><c>orientation: portrait</c> matches when height ≥ width.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-orientation")]
    [SpecFact]
    public void Orientation_portrait_when_height_ge_width()
    {
        Eval("(orientation: portrait)", new MediaContext(ViewportWidthPx: 400, ViewportHeightPx: 800))
            .Should().BeTrue();
        Eval("(orientation: portrait)", new MediaContext(ViewportWidthPx: 400, ViewportHeightPx: 400))
            .Should().BeTrue("equal dimensions count as portrait");
        Eval("(orientation: portrait)", new MediaContext(ViewportWidthPx: 800, ViewportHeightPx: 400))
            .Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-orientation"/>
    /// <para><c>orientation: landscape</c> matches when width &gt; height.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-orientation")]
    [SpecFact]
    public void Orientation_landscape_when_width_gt_height()
    {
        Eval("(orientation: landscape)", new MediaContext(ViewportWidthPx: 800, ViewportHeightPx: 400))
            .Should().BeTrue();
        Eval("(orientation: landscape)", new MediaContext(ViewportWidthPx: 400, ViewportHeightPx: 800))
            .Should().BeFalse();
    }

    // =========================================================================
    // §7  prefers-color-scheme
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-color-scheme"/>
    /// <para><c>prefers-color-scheme: dark</c> matches when the user prefers a dark interface.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-color-scheme")]
    [SpecFact]
    public void PrefersColorScheme_dark()
    {
        Eval("(prefers-color-scheme: dark)", new MediaContext(ColorScheme: ColorScheme.Dark)).Should().BeTrue();
        Eval("(prefers-color-scheme: dark)", new MediaContext(ColorScheme: ColorScheme.Light)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-color-scheme"/>
    /// <para><c>prefers-color-scheme: light</c> matches a light-scheme context.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-color-scheme")]
    [SpecFact]
    public void PrefersColorScheme_light()
    {
        Eval("(prefers-color-scheme: light)", new MediaContext(ColorScheme: ColorScheme.Light)).Should().BeTrue();
        Eval("(prefers-color-scheme: light)", new MediaContext(ColorScheme: ColorScheme.Dark)).Should().BeFalse();
    }

    // =========================================================================
    // §8  prefers-reduced-motion
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-reduced-motion"/>
    /// <para><c>prefers-reduced-motion: reduce</c> matches a reduce context.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-reduced-motion")]
    [SpecFact]
    public void PrefersReducedMotion_reduce()
    {
        Eval("(prefers-reduced-motion: reduce)", new MediaContext(ReducedMotion: ReducedMotion.Reduce))
            .Should().BeTrue();
        Eval("(prefers-reduced-motion: reduce)", new MediaContext(ReducedMotion: ReducedMotion.NoPreference))
            .Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-reduced-motion"/>
    /// <para><c>prefers-reduced-motion: no-preference</c> matches when the user has no motion preference.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-reduced-motion")]
    [SpecFact]
    public void PrefersReducedMotion_no_preference()
    {
        Eval("(prefers-reduced-motion: no-preference)", new MediaContext(ReducedMotion: ReducedMotion.NoPreference))
            .Should().BeTrue();
        Eval("(prefers-reduced-motion: no-preference)", new MediaContext(ReducedMotion: ReducedMotion.Reduce))
            .Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-reduced-motion"/>
    /// <para>Boolean form <c>(prefers-reduced-motion)</c> is true only when reduce is active.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-reduced-motion")]
    [SpecFact]
    public void PrefersReducedMotion_boolean_form()
    {
        Eval("(prefers-reduced-motion)", new MediaContext(ReducedMotion: ReducedMotion.Reduce))
            .Should().BeTrue();
        Eval("(prefers-reduced-motion)", new MediaContext(ReducedMotion: ReducedMotion.NoPreference))
            .Should().BeFalse();
    }

    // =========================================================================
    // §9  resolution feature
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-resolution"/>
    /// <para><c>min-resolution</c> with <c>dppx</c> units matches high-DPI contexts.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-resolution")]
    [SpecFact]
    public void Resolution_min_dppx()
    {
        Eval("(min-resolution: 2dppx)", new MediaContext(Resolution: 2.0)).Should().BeTrue();
        Eval("(min-resolution: 2dppx)", new MediaContext(Resolution: 1.0)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-resolution"/>
    /// <para><c>min-resolution</c> with <c>dpi</c> units: 192dpi = 2dppx.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-resolution")]
    [SpecFact]
    public void Resolution_min_dpi_unit()
    {
        // 192dpi / 96 = 2 dppx
        Eval("(min-resolution: 192dpi)", new MediaContext(Resolution: 2.0)).Should().BeTrue();
        Eval("(min-resolution: 192dpi)", new MediaContext(Resolution: 1.0)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-resolution"/>
    /// <para><c>x</c> is an alias for <c>dppx</c>.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-resolution")]
    [SpecFact]
    public void Resolution_x_unit_alias_for_dppx()
    {
        Eval("(min-resolution: 2x)", new MediaContext(Resolution: 2.0)).Should().BeTrue();
        Eval("(min-resolution: 2x)", new MediaContext(Resolution: 1.5)).Should().BeFalse();
    }

    // =========================================================================
    // §10  and / or combinations
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#mq-and"/>
    /// <para><c>and</c> requires all sub-conditions to be true.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-and")]
    [SpecFact]
    public void And_requires_all_conditions()
    {
        Eval("(min-width: 400px) and (orientation: landscape)",
             new MediaContext(ViewportWidthPx: 500, ViewportHeightPx: 300))
            .Should().BeTrue();
        Eval("(min-width: 400px) and (orientation: landscape)",
             new MediaContext(ViewportWidthPx: 500, ViewportHeightPx: 800))
            .Should().BeFalse("height > width → portrait, so landscape arm is false");
        Eval("(min-width: 400px) and (orientation: landscape)",
             new MediaContext(ViewportWidthPx: 300, ViewportHeightPx: 200))
            .Should().BeFalse("width below min-width");
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#mq-or"/>
    /// <para><c>or</c> is satisfied when any sub-condition is true.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-or")]
    [SpecFact]
    public void Or_satisfied_by_any_condition()
    {
        Eval("(min-width: 1000px) or (orientation: portrait)",
             new MediaContext(ViewportWidthPx: 400, ViewportHeightPx: 800))
            .Should().BeTrue("portrait arm matches");
        Eval("(min-width: 1000px) or (orientation: portrait)",
             new MediaContext(ViewportWidthPx: 1200, ViewportHeightPx: 300))
            .Should().BeTrue("min-width arm matches");
        Eval("(min-width: 1000px) or (orientation: portrait)",
             new MediaContext(ViewportWidthPx: 400, ViewportHeightPx: 300))
            .Should().BeFalse("neither arm matches");
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#mq-and"/>
    /// <para>Three-arm <c>and</c> chain all evaluated.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#mq-and")]
    [SpecFact]
    public void And_three_arms_all_must_pass()
    {
        Eval("(min-width: 300px) and (max-width: 800px) and (orientation: landscape)",
             new MediaContext(ViewportWidthPx: 500, ViewportHeightPx: 300))
            .Should().BeTrue();
        Eval("(min-width: 300px) and (max-width: 800px) and (orientation: landscape)",
             new MediaContext(ViewportWidthPx: 500, ViewportHeightPx: 600))
            .Should().BeFalse("portrait, third arm fails");
    }

    // =========================================================================
    // §11  Additional media features
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-contrast"/>
    /// <para><c>prefers-contrast: more</c> matches when user requests higher contrast.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-contrast")]
    [SpecFact]
    public void PrefersContrast_more()
    {
        Eval("(prefers-contrast: more)", new MediaContext(Contrast: Contrast.More)).Should().BeTrue();
        Eval("(prefers-contrast: more)", new MediaContext(Contrast: Contrast.NoPreference)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-contrast"/>
    /// <para><c>prefers-contrast: less</c> matches when user requests lower contrast.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-contrast")]
    [SpecFact]
    public void PrefersContrast_less()
    {
        Eval("(prefers-contrast: less)", new MediaContext(Contrast: Contrast.Less)).Should().BeTrue();
        Eval("(prefers-contrast: less)", new MediaContext(Contrast: Contrast.More)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-contrast"/>
    /// <para><c>prefers-contrast: no-preference</c> matches when user has no contrast preference.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-contrast")]
    [SpecFact]
    public void PrefersContrast_no_preference()
    {
        Eval("(prefers-contrast: no-preference)", new MediaContext(Contrast: Contrast.NoPreference)).Should().BeTrue();
        Eval("(prefers-contrast: no-preference)", new MediaContext(Contrast: Contrast.More)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-reduced-transparency"/>
    /// <para><c>prefers-reduced-transparency: reduce</c> matches a reduce context.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-reduced-transparency")]
    [SpecFact]
    public void PrefersReducedTransparency_reduce()
    {
        Eval("(prefers-reduced-transparency: reduce)",
             new MediaContext(ReducedTransparency: ReducedTransparency.Reduce))
            .Should().BeTrue();
        Eval("(prefers-reduced-transparency: reduce)",
             new MediaContext(ReducedTransparency: ReducedTransparency.NoPreference))
            .Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-color-gamut"/>
    /// <para><c>color-gamut: srgb</c> matches sRGB and wider gamuts (superset semantics).</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-color-gamut")]
    [SpecFact]
    public void ColorGamut_srgb_superset_semantics()
    {
        Eval("(color-gamut: srgb)", new MediaContext(ColorGamut: ColorGamut.Srgb)).Should().BeTrue();
        Eval("(color-gamut: srgb)", new MediaContext(ColorGamut: ColorGamut.P3)).Should().BeTrue();
        Eval("(color-gamut: srgb)", new MediaContext(ColorGamut: ColorGamut.Rec2020)).Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-color-gamut"/>
    /// <para><c>color-gamut: p3</c> matches P3 and Rec2020 but not plain sRGB.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-color-gamut")]
    [SpecFact]
    public void ColorGamut_p3_superset_semantics()
    {
        Eval("(color-gamut: p3)", new MediaContext(ColorGamut: ColorGamut.Srgb)).Should().BeFalse();
        Eval("(color-gamut: p3)", new MediaContext(ColorGamut: ColorGamut.P3)).Should().BeTrue();
        Eval("(color-gamut: p3)", new MediaContext(ColorGamut: ColorGamut.Rec2020)).Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-color-gamut"/>
    /// <para><c>color-gamut: rec2020</c> matches only Rec2020.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-color-gamut")]
    [SpecFact]
    public void ColorGamut_rec2020_only()
    {
        Eval("(color-gamut: rec2020)", new MediaContext(ColorGamut: ColorGamut.Rec2020)).Should().BeTrue();
        Eval("(color-gamut: rec2020)", new MediaContext(ColorGamut: ColorGamut.P3)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-hover"/>
    /// <para><c>hover: hover</c> matches fine-pointer devices that support hover.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-hover")]
    [SpecFact]
    public void Hover_feature()
    {
        Eval("(hover: hover)", new MediaContext(Hover: Hover.Hover)).Should().BeTrue();
        Eval("(hover: hover)", new MediaContext(Hover: Hover.None)).Should().BeFalse();
        Eval("(hover: none)", new MediaContext(Hover: Hover.None)).Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-pointer"/>
    /// <para><c>pointer</c> distinguishes fine, coarse, and no pointer devices.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-pointer")]
    [SpecFact]
    public void Pointer_feature()
    {
        Eval("(pointer: fine)", new MediaContext(Pointer: Pointer.Fine)).Should().BeTrue();
        Eval("(pointer: coarse)", new MediaContext(Pointer: Pointer.Coarse)).Should().BeTrue();
        Eval("(pointer: none)", new MediaContext(Pointer: Pointer.None)).Should().BeTrue();
        Eval("(pointer: fine)", new MediaContext(Pointer: Pointer.Coarse)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-any-hover"/>
    /// <para><c>any-hover</c> and <c>any-pointer</c> reflect secondary input capabilities.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-any-hover")]
    [SpecFact]
    public void AnyHover_and_AnyPointer()
    {
        Eval("(any-hover: hover)", new MediaContext(AnyHover: Hover.Hover)).Should().BeTrue();
        Eval("(any-hover: none)", new MediaContext(AnyHover: Hover.None)).Should().BeTrue();
        Eval("(any-pointer: coarse)", new MediaContext(AnyPointer: Pointer.Coarse)).Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-color"/>
    /// <para>Boolean <c>(color)</c> is true when color depth &gt; 0.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-color")]
    [SpecFact]
    public void Color_boolean_feature()
    {
        Eval("(color)", new MediaContext(Color: 8)).Should().BeTrue();
        Eval("(color)", new MediaContext(Color: 0)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-color"/>
    /// <para><c>min-color</c> numeric comparison against color depth.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-color")]
    [SpecFact]
    public void MinColor_numeric()
    {
        Eval("(min-color: 8)", new MediaContext(Color: 8)).Should().BeTrue();
        Eval("(min-color: 8)", new MediaContext(Color: 4)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-scripting"/>
    /// <para><c>scripting: enabled</c> matches contexts where scripting is enabled.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-scripting")]
    [SpecFact]
    public void Scripting_enabled()
    {
        Eval("(scripting: enabled)", new MediaContext(Scripting: Scripting.Enabled)).Should().BeTrue();
        Eval("(scripting: enabled)", new MediaContext(Scripting: Scripting.None)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-scripting"/>
    /// <para><c>scripting: none</c> matches contexts with no scripting support.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-scripting")]
    [SpecFact]
    public void Scripting_none()
    {
        Eval("(scripting: none)", new MediaContext(Scripting: Scripting.None)).Should().BeTrue();
        Eval("(scripting: none)", new MediaContext(Scripting: Scripting.Enabled)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-forced-colors"/>
    /// <para><c>forced-colors: active</c> matches high-contrast / forced-colors mode.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-forced-colors")]
    [SpecFact]
    public void ForcedColors_active()
    {
        Eval("(forced-colors: active)", new MediaContext(ForcedColors: ForcedColors.Active)).Should().BeTrue();
        Eval("(forced-colors: active)", new MediaContext(ForcedColors: ForcedColors.None)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-inverted-colors"/>
    /// <para><c>inverted-colors: inverted</c> matches when colors are inverted.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-inverted-colors")]
    [SpecFact]
    public void InvertedColors_inverted()
    {
        Eval("(inverted-colors: inverted)", new MediaContext(InvertedColors: InvertedColors.Inverted)).Should().BeTrue();
        Eval("(inverted-colors: inverted)", new MediaContext(InvertedColors: InvertedColors.None)).Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-aspect-ratio"/>
    /// <para><c>min-aspect-ratio</c> compares the viewport ratio numerically.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-aspect-ratio")]
    [SpecFact]
    public void AspectRatio_min()
    {
        // 1920/1080 ≈ 1.778; 16/9 ≈ 1.778 — should match exactly.
        Eval("(min-aspect-ratio: 16/9)", new MediaContext(ViewportWidthPx: 1920, ViewportHeightPx: 1080))
            .Should().BeTrue();
        // 4/3 ≈ 1.333 < 16/9.
        Eval("(min-aspect-ratio: 16/9)", new MediaContext(ViewportWidthPx: 800, ViewportHeightPx: 600))
            .Should().BeFalse();
    }

    // =========================================================================
    // §12  StyleEngine integration
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-ruledef-media"/>
    /// <para>Rules inside a non-matching <c>@media</c> block are not applied.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-ruledef-media")]
    [SpecFact]
    public void StyleEngine_skips_non_matching_block()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.MediaContext = new MediaContext(ViewportWidthPx: 400);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("@media (min-width: 600px) { p { color: red; } }"));
        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(CssColorValue.Black);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-ruledef-media"/>
    /// <para>Rules inside a matching <c>@media</c> block are applied.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-ruledef-media")]
    [SpecFact]
    public void StyleEngine_applies_matching_block()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.MediaContext = new MediaContext(ViewportWidthPx: 800);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("@media (min-width: 600px) { p { color: red; } }"));
        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(new CssColorValue(255, 0, 0));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#at-ruledef-media"/>
    /// <para>A print-typed block is not applied when the context is screen.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#at-ruledef-media")]
    [SpecFact]
    public void StyleEngine_skips_print_block_in_screen_context()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.MediaContext = new MediaContext(MediaType: "screen");
        engine.AddStyleSheet(CssParser.ParseStyleSheet("@media print { p { color: blue; } }"));
        engine.Compute(p).GetColor(PropertyId.Color).Should().Be(CssColorValue.Black);
    }

    // =========================================================================
    // §13  Pending: prefers-reduced-data (MQ5-only feature)
    // =========================================================================

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-reduced-data"/>
    /// <para><c>prefers-reduced-data: reduce</c> — evaluator supports this feature.</para>
    /// </summary>
    [Spec("css-conditional-5", "https://www.w3.org/TR/css-conditional-5/#descdef-media-prefers-reduced-data")]
    [SpecFact]
    public void PrefersReducedData_reduce()
    {
        Eval("(prefers-reduced-data: reduce)", new MediaContext(ReducedData: ReducedData.Reduce))
            .Should().BeTrue();
        Eval("(prefers-reduced-data: reduce)", new MediaContext(ReducedData: ReducedData.NoPreference))
            .Should().BeFalse();
    }
}

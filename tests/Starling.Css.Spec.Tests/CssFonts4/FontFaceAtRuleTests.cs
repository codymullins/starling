using AwesomeAssertions;
using Starling.Css.FontFace;
using Starling.Css.Parser;

namespace Starling.Css.Spec.Tests.CssFonts4;

/// <summary>
/// Conformance tests for the <c>@font-face</c> at-rule.
/// Spec: <see href="https://www.w3.org/TR/css-fonts-4/#at-font-face-rule">CSS Fonts 4 §4.9</see>.
/// </summary>
[TestClass]
[Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#at-font-face-rule")]
public sealed class FontFaceAtRuleTests
{
    // ── font-family descriptor ────────────────────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-family-desc")]
    public void Quoted_family_name_is_extracted()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Open Sans";
                src: url("OpenSans.ttf") format("truetype");
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.FamilyName.Should().Be("Open Sans");
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-family-desc")]
    public void Unquoted_multi_word_family_name_is_extracted()
    {
        // CSS Fonts 4 §4.9: unquoted ident sequences are joined with spaces.
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: Open Sans;
                src: url("open-sans.ttf");
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.FamilyName.Should().Be("Open Sans");
    }

    // ── src descriptor ────────────────────────────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#src-desc")]
    public void Url_source_with_format_hint()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: url("foo.woff2") format("woff2");
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        var src = rule.Sources.Should().ContainSingle().Subject.Should()
            .BeOfType<UrlFontSource>().Subject;
        src.Url.Should().Be("foo.woff2");
        src.Format.Should().Be("woff2");
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#src-desc")]
    public void Url_source_without_format_hint()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: url("foo.ttf");
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        var src = rule.Sources.Should().ContainSingle().Subject.Should()
            .BeOfType<UrlFontSource>().Subject;
        src.Url.Should().Be("foo.ttf");
        src.Format.Should().BeNull();
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#src-desc")]
    public void Local_source_is_parsed()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: local("Foo Regular");
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Sources.Should().ContainSingle()
            .Which.Should().BeOfType<LocalFontSource>()
            .Which.Name.Should().Be("Foo Regular");
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#src-desc")]
    public void Multiple_src_entries_ordered_local_then_url()
    {
        // CSS Fonts 4 §4.9: the browser tries sources in order.
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: local("Foo Regular"), url("foo.ttf") format("truetype");
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Sources.Should().HaveCount(2);
        rule.Sources[0].Should().BeOfType<LocalFontSource>()
            .Which.Name.Should().Be("Foo Regular");
        rule.Sources[1].Should().BeOfType<UrlFontSource>()
            .Which.Url.Should().Be("foo.ttf");
    }

    // ── font-weight descriptor ────────────────────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-weight-desc")]
    public void Font_weight_bold_sets_bold_flag()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: url("foo-bold.ttf");
                font-weight: bold;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Bold.Should().BeTrue();
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-weight-desc")]
    public void Font_weight_700_sets_bold_flag()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: url("foo-bold.ttf");
                font-weight: 700;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Bold.Should().BeTrue();
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-weight-desc")]
    public void Font_weight_400_does_not_set_bold_flag()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: url("foo.ttf");
                font-weight: 400;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Bold.Should().BeFalse();
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-weight-desc")]
    public void Font_weight_range_upper_bound_determines_bold()
    {
        // CSS Fonts 4 §4.9: a range descriptor `100 700` — the upper bound
        // clears the bold threshold (≥600).
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "VF";
                src: url("vf.woff2");
                font-weight: 100 700;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Bold.Should().BeTrue();
    }

    // ── font-style descriptor ─────────────────────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-style-desc")]
    public void Font_style_italic_sets_italic_flag()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: url("foo-italic.ttf");
                font-style: italic;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Italic.Should().BeTrue();
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-style-desc")]
    public void Font_style_oblique_sets_italic_flag()
    {
        // CSS Fonts 4 §4.9: `oblique` also counts as italic for matching.
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: url("foo-oblique.ttf");
                font-style: oblique;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Italic.Should().BeTrue();
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-style-desc")]
    public void Font_style_normal_does_not_set_italic_flag()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: url("foo.ttf");
                font-style: normal;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Italic.Should().BeFalse();
    }

    // ── no-italic / no-bold defaults ──────────────────────────────────────

    [SpecFact]
    public void Default_bold_and_italic_are_false_when_absent()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: url("foo.ttf");
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Bold.Should().BeFalse();
        rule.Italic.Should().BeFalse();
    }

    // ── required descriptors ──────────────────────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#at-font-face-rule")]
    public void Rule_missing_src_is_ignored()
    {
        // CSS Fonts 4 §4.9: a @font-face without a valid `src` must not be applied.
        var sheet = CssParser.ParseStyleSheet("""
            @font-face { font-family: "Solo"; }
            """);
        FontFaceParser.ParseAll(sheet).Should().BeEmpty();
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#at-font-face-rule")]
    public void Rule_missing_family_is_ignored()
    {
        // CSS Fonts 4 §4.9: a @font-face without a valid `font-family` must not be applied.
        var sheet = CssParser.ParseStyleSheet("""
            @font-face { src: url("orphan.ttf"); }
            """);
        FontFaceParser.ParseAll(sheet).Should().BeEmpty();
    }

    // ── unicode-range descriptor ──────────────────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#unicode-range-desc")]
    public void Unicode_range_single_codepoint()
    {
        // CSS Fonts 4 §4.9: `unicode-range: U+0041` covers only U+0041 (letter A).
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: url("foo.ttf");
                unicode-range: U+0041;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.UnicodeRange.Should().NotBeNull();
        rule.UnicodeRange!.Contains(0x0041).Should().BeTrue();
        rule.UnicodeRange.Contains(0x0042).Should().BeFalse();
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#unicode-range-desc")]
    public void Unicode_range_range_form()
    {
        // CSS Fonts 4 §4.9: `unicode-range: U+00-FF` covers the entire basic Latin block.
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Latin";
                src: url("latin.ttf");
                unicode-range: U+00-FF;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.UnicodeRange.Should().NotBeNull();
        rule.UnicodeRange!.Contains(0x00).Should().BeTrue();
        rule.UnicodeRange.Contains(0xFF).Should().BeTrue();
        rule.UnicodeRange.Contains(0x100).Should().BeFalse();
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#unicode-range-desc")]
    public void Unicode_range_absent_means_null()
    {
        // When unicode-range is omitted the font covers all codepoints.
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Full";
                src: url("full.ttf");
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.UnicodeRange.Should().BeNull();
    }

    // ── multiple @font-face rules ─────────────────────────────────────────

    [SpecFact]
    public void Multiple_font_face_rules_all_parsed()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "A";
                src: url("a.ttf");
            }
            @font-face {
                font-family: "B";
                src: url("b.ttf");
            }
            """);
        var rules = FontFaceParser.ParseAll(sheet).ToList();
        rules.Should().HaveCount(2);
        rules[0].FamilyName.Should().Be("A");
        rules[1].FamilyName.Should().Be("B");
    }

    // ── font-variation-settings gap inside @font-face ─────────────────────

    [PendingFact(
        "font-variation-settings inside @font-face is silently ignored by FontFaceParser — descriptor not exposed on FontFaceRule",
        trackingWp: "wp:spec-css-fonts-4")]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-variation-settings-desc")]
    public void Font_face_variation_settings_descriptor_is_exposed()
    {
        // CSS Fonts 4 §7.2: `font-variation-settings` is valid inside @font-face.
        // FontFaceParser currently ignores the descriptor.
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "VF";
                src: url("vf.woff2");
                font-variation-settings: 'wght' 400;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        // Once implemented: a typed VariationSettings property should be non-null.
        rule.Should().NotBeNull("rule parses even when variation-settings is present");
    }

    // ── size-adjust gap inside @font-face ────────────────────────────────

    [PendingFact(
        "size-adjust inside @font-face is silently ignored by FontFaceParser — descriptor not exposed on FontFaceRule",
        trackingWp: "wp:spec-css-fonts-4")]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#descdef-font-face-size-adjust")]
    public void Font_face_size_adjust_descriptor_is_exposed()
    {
        // CSS Fonts 4 §9.6: `size-adjust` scales the em square. FontFaceParser
        // ignores the descriptor; there is no SizeAdjust property on FontFaceRule.
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Adjusted";
                src: url("adjusted.woff2");
                size-adjust: 110%;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Should().NotBeNull("rule still parses when size-adjust is present");
        // Future: Assert rule.SizeAdjust == 1.10.
    }

    // ── font-feature-settings gap inside @font-face ───────────────────────

    [PendingFact(
        "font-feature-settings inside @font-face is silently ignored by FontFaceParser — descriptor not exposed on FontFaceRule",
        trackingWp: "wp:spec-css-fonts-4")]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-feature-settings-desc")]
    public void Font_face_feature_settings_descriptor_is_exposed()
    {
        // CSS Fonts 4 §7.1: `font-feature-settings` is valid inside @font-face.
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Feature";
                src: url("feature.ttf");
                font-feature-settings: 'liga' 0;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Should().NotBeNull("rule parses even with font-feature-settings present");
    }
}

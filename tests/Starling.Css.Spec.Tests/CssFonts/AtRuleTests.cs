using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.FontFace;

namespace Starling.Css.Spec.Tests.CssFonts;

/// <summary>
/// At-rule conformance for <see href="https://drafts.csswg.org/css-fonts-4/">CSS Fonts Module Level 4</see>.
/// </summary>
[TestClass]
[Spec("css-fonts", "https://drafts.csswg.org/css-fonts-4/")]
public sealed class AtRuleTests
{

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-fonts-4/#at-font-face-rule"/>
    /// <para>At-rule <c>@font-face</c>.</para>
    /// </summary>
    [Spec("css-fonts", "https://drafts.csswg.org/css-fonts-4/#at-font-face-rule")]
    [SpecFact]
    public void Parses_at_font_face()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Open Sans";
                src: url("OpenSans.ttf") format("truetype");
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.FamilyName.Should().Be("Open Sans");
        rule.Bold.Should().BeFalse();
        rule.Italic.Should().BeFalse();
        rule.Sources.Should().ContainSingle()
            .Which.Should().BeOfType<UrlFontSource>()
            .Which.Url.Should().Be("OpenSans.ttf");
    }

}

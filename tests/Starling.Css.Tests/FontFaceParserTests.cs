using FluentAssertions;
using Starling.Css.FontFace;
using Starling.Css.Parser;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/")]

[TestClass]
public sealed class FontFaceParserTests
{
    [TestMethod]
    public void Parses_quoted_family_and_url_source()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Open Sans";
                src: url("OpenSans.ttf") format("truetype");
            }
            """);

        var rules = FontFaceParser.ParseAll(sheet).ToList();
        var rule = rules.Should().ContainSingle().Subject;
        rule.FamilyName.Should().Be("Open Sans");
        rule.Bold.Should().BeFalse();
        rule.Italic.Should().BeFalse();
        rule.Sources.Should().ContainSingle()
            .Which.Should().BeOfType<UrlFontSource>()
            .Which.Url.Should().Be("OpenSans.ttf");
    }

    [TestMethod]
    public void Picks_up_weight_and_style_descriptors()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: url("foo-bold-italic.ttf");
                font-weight: 700;
                font-style: italic;
            }
            """);

        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Bold.Should().BeTrue();
        rule.Italic.Should().BeTrue();
    }

    [TestMethod]
    public void Parses_multiple_src_entries_in_order()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Foo";
                src: local("Foo Regular"), url("foo.ttf") format("truetype");
            }
            """);

        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.Sources.Should().HaveCount(2);
        rule.Sources[0].Should().BeOfType<LocalFontSource>().Which.Name.Should().Be("Foo Regular");
        rule.Sources[1].Should().BeOfType<UrlFontSource>().Which.Url.Should().Be("foo.ttf");
    }

    [TestMethod]
    public void Drops_rule_missing_family_or_src()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face { font-family: "Solo"; }
            @font-face { src: url("orphan.ttf"); }
            """);

        FontFaceParser.ParseAll(sheet).Should().BeEmpty();
    }

    [TestMethod]
    public void Accepts_unquoted_multi_word_family_name()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: Open Sans;
                src: url("open-sans.ttf");
            }
            """);

        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;
        rule.FamilyName.Should().Be("Open Sans");
    }
}

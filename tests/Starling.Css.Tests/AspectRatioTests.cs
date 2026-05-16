using FluentAssertions;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Xunit;

namespace Tessera.Css.Tests;

public sealed class AspectRatioTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    [Fact]
    public void Aspect_ratio_slash_separated_pair_parses_as_value_list()
    {
        var decls = Expand("aspect-ratio: 16 / 9;");

        var value = decls.Single(d => d.Id == PropertyId.AspectRatio).Value;
        value.Should().BeOfType<CssValueList>();
        var list = (CssValueList)value;
        list.Values.Should().HaveCount(3);
        list.Values[0].Should().Be(new CssNumber(16));
        list.Values[2].Should().Be(new CssNumber(9));
    }

    [Fact]
    public void Aspect_ratio_auto_keyword()
    {
        var decls = Expand("aspect-ratio: auto;");

        decls.Single(d => d.Id == PropertyId.AspectRatio).Value.Should().Be(new CssKeyword("auto"));
    }

    [Fact]
    public void Aspect_ratio_single_number()
    {
        var decls = Expand("aspect-ratio: 1.5;");

        decls.Single(d => d.Id == PropertyId.AspectRatio).Value.Should().Be(new CssNumber(1.5));
    }

    [Fact]
    public void Object_fit_keyword_round_trips()
    {
        var decls = Expand("object-fit: cover;");

        decls.Single().Id.Should().Be(PropertyId.ObjectFit);
        decls.Single().Value.Should().Be(new CssKeyword("cover"));
    }

    [Fact]
    public void Object_position_value_list()
    {
        var decls = Expand("object-position: 50% 25%;");

        decls.Single().Id.Should().Be(PropertyId.ObjectPosition);
        decls.Single().Value.Should().BeOfType<CssValueList>();
    }
}

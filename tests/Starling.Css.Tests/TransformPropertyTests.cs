using FluentAssertions;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Xunit;

namespace Tessera.Css.Tests;

public sealed class TransformPropertyTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    [Fact]
    public void Transform_with_single_function_parses_as_function_value()
    {
        var decls = Expand("transform: translate(10px, 20px);");

        var value = decls.Single(d => d.Id == PropertyId.Transform).Value;
        value.Should().BeOfType<CssFunctionValue>();
        var fn = (CssFunctionValue)value;
        fn.Name.Should().Be("translate");
        fn.Arguments.Should().HaveCount(2);
    }

    [Fact]
    public void Transform_with_function_list_parses_as_value_list()
    {
        var decls = Expand("transform: translate(10px, 20px) rotate(45deg);");

        var value = decls.Single(d => d.Id == PropertyId.Transform).Value;
        value.Should().BeOfType<CssValueList>();
        var list = (CssValueList)value;
        list.Values.Should().HaveCount(2);
        list.Values[0].Should().BeOfType<CssFunctionValue>();
        ((CssFunctionValue)list.Values[0]).Name.Should().Be("translate");
        list.Values[1].Should().BeOfType<CssFunctionValue>();
        ((CssFunctionValue)list.Values[1]).Name.Should().Be("rotate");
    }

    [Fact]
    public void Transform_origin_parses_as_value_list()
    {
        var decls = Expand("transform-origin: 50% 50%;");

        decls.Single().Id.Should().Be(PropertyId.TransformOrigin);
        decls.Single().Value.Should().BeOfType<CssValueList>();
    }

    [Fact]
    public void Translate_property_parses_lengths()
    {
        var decls = Expand("translate: 10px 20px;");

        var value = decls.Single(d => d.Id == PropertyId.Translate).Value;
        value.Should().BeOfType<CssValueList>();
    }

    [Fact]
    public void Rotate_property_parses_angle()
    {
        var decls = Expand("rotate: 45deg;");

        decls.Single().Id.Should().Be(PropertyId.Rotate);
        decls.Single().Value.Should().BeOfType<CssAngle>();
    }

    [Fact]
    public void Filter_with_blur_function()
    {
        var decls = Expand("filter: blur(4px);");

        var value = decls.Single(d => d.Id == PropertyId.Filter).Value;
        value.Should().BeOfType<CssFunctionValue>();
        ((CssFunctionValue)value).Name.Should().Be("blur");
    }

    [Fact]
    public void Clip_path_with_inset_function()
    {
        var decls = Expand("clip-path: inset(10px);");

        var value = decls.Single(d => d.Id == PropertyId.ClipPath).Value;
        value.Should().BeOfType<CssFunctionValue>();
    }
}

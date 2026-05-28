using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.PropertiesValues;

namespace Starling.Css.Spec.Tests.CssPropertiesValues1;

/// <summary>
/// Conformance for the <c>@property</c> at-rule of
/// <see href="https://www.w3.org/TR/css-properties-values-api-1/">CSS Properties and Values API Level 1</see> §2.
/// </summary>
[TestClass]
[Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/", section: "2")]
public sealed class AtPropertyTests
{
    private static List<RegisteredProperty> Parse(string css)
        => PropertyDefinitionParser.ParseAll(CssParser.ParseStyleSheet(css)).ToList();

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#the-syntax-descriptor", section: "2.1")]
    [SpecFact]
    public void Valid_rule_captures_name_syntax_inherits_initial_value()
    {
        var props = Parse("@property --my-color { syntax: \"<color>\"; inherits: false; initial-value: red; }");
        props.Should().HaveCount(1);
        var p = props[0];
        p.Name.Should().Be("--my-color");
        p.Syntax.Should().Be("<color>");
        p.Inherits.Should().BeFalse();
        p.InitialValue.Should().Be("red");
    }

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#inherits-descriptor", section: "2.2")]
    [SpecFact]
    public void Inherits_true_is_parsed()
    {
        var props = Parse("@property --gap { syntax: \"<length>\"; inherits: true; initial-value: 0px; }");
        props.Single().Inherits.Should().BeTrue();
        props.Single().InitialValue.Should().Be("0px");
    }

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#universal-syntax", section: "2.1")]
    [SpecFact]
    public void Universal_syntax_does_not_require_initial_value()
    {
        var props = Parse("@property --anything { syntax: \"*\"; inherits: false; }");
        props.Should().HaveCount(1);
        props[0].IsUniversal.Should().BeTrue();
        props[0].InitialValue.Should().BeNull();
    }

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#at-property-rule", section: "2")]
    [SpecFact]
    public void Rule_without_syntax_descriptor_is_dropped()
    {
        Parse("@property --x { inherits: false; initial-value: 0px; }").Should().BeEmpty();
    }

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#at-property-rule", section: "2")]
    [SpecFact]
    public void Non_universal_syntax_without_initial_value_is_dropped()
    {
        Parse("@property --x { syntax: \"<length>\"; inherits: false; }").Should().BeEmpty();
    }

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#at-property-rule", section: "2")]
    [SpecFact]
    public void Rule_whose_name_is_not_dash_dash_prefixed_is_dropped()
    {
        Parse("@property color { syntax: \"<color>\"; inherits: false; initial-value: red; }").Should().BeEmpty();
    }
}

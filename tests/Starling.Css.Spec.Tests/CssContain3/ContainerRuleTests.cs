using AwesomeAssertions;
using Starling.Css.Container;
using Starling.Css.Parser;

namespace Starling.Css.Spec.Tests.CssContain3;

/// <summary>
/// Conformance for the <c>@container</c> at-rule of
/// <see href="https://www.w3.org/TR/css-contain-3/">CSS Containment Level 3</see> §5.
/// Parse level — container-relative query evaluation is not yet implemented.
/// </summary>
[TestClass]
[Spec("css-contain-3", "https://www.w3.org/TR/css-contain-3/", section: "5")]
public sealed class ContainerRuleTests
{
    private static List<ContainerRule> Parse(string css)
        => ContainerQueryParser.ParseAll(CssParser.ParseStyleSheet(css)).ToList();

    [Spec("css-contain-3", "https://www.w3.org/TR/css-contain-3/#container-rule", section: "5.1")]
    [SpecFact]
    public void Anonymous_container_query_captures_condition_and_rules()
    {
        var rules = Parse("@container (min-width: 400px) { .card { color: red; } }");
        rules.Should().HaveCount(1);
        rules[0].Name.Should().BeNull();
        rules[0].Condition.Should().Be("(min-width: 400px)");
        rules[0].Rules.Should().ContainSingle();
    }

    [Spec("css-contain-3", "https://www.w3.org/TR/css-contain-3/#container-rule", section: "5.1")]
    [SpecFact]
    public void Named_container_query_separates_name_from_condition()
    {
        var rules = Parse("@container sidebar (min-width: 400px) { a { color: blue; } }");
        rules.Single().Name.Should().Be("sidebar");
        rules.Single().Condition.Should().Be("(min-width: 400px)");
    }

    [Spec("css-contain-3", "https://www.w3.org/TR/css-contain-3/#size-container", section: "5.2")]
    [SpecFact]
    public void Range_syntax_condition_is_captured()
    {
        var rules = Parse("@container (width > 400px) { p { color: green; } }");
        rules.Single().Name.Should().BeNull();
        rules.Single().Condition.Should().Be("(width > 400px)");
    }
}

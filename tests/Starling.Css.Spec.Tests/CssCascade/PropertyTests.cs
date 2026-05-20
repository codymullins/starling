using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssCascade;

/// <summary>
/// Property conformance for <see href="https://drafts.csswg.org/css-cascade-5/">CSS Cascading and Inheritance Module Level 5</see>.
/// </summary>
[TestClass]
[Spec("css-cascade", "https://drafts.csswg.org/css-cascade-5/")]
public sealed class PropertyTests
{

    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-cascade-5/#propdef-all"/>
    /// <para>Property <c>all</c> — value <c>initial | inherit | unset | revert | revert-layer | revert-rule</c>; initial <c>see individual properties</c>.</para>
    /// </summary>
    [Spec("css-cascade", "https://drafts.csswg.org/css-cascade-5/#propdef-all")]
    [SpecFact]
    public void Parses_all()
    {
        var sheet = CssParser.ParseStyleSheet("x { all: initial; }");
        var rule = (StyleRule)sheet.Rules.Single();
        var decl = rule.Declarations.Single();
        decl.Name.Should().Be("all");
        // CSS Cascade 5 §3.2: `all` accepts the CSS-wide keywords; `initial`
        // is one of them. Round-tripped via the tokenizer + value parser.
        var value = CssValueParser.Parse(decl.Value);
        value.Should().Be(new CssKeyword("initial"));
    }
}

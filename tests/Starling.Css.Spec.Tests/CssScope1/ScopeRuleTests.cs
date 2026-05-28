using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Scope;

namespace Starling.Css.Spec.Tests.CssScope1;

/// <summary>
/// Conformance for the <c>@scope</c> at-rule of
/// <see href="https://www.w3.org/TR/css-cascade-6/#scoped-styles">CSS Cascade and Inheritance Level 6 §3</see>.
/// Parse level — the scoping proximity cascade step is not yet implemented.
/// </summary>
[TestClass]
[Spec("css-scope-1", "https://www.w3.org/TR/css-cascade-6/#scoped-styles", section: "3")]
public sealed class ScopeRuleTests
{
    private static List<ScopeRule> Parse(string css)
        => ScopeParser.ParseAll(CssParser.ParseStyleSheet(css)).ToList();

    [Spec("css-scope-1", "https://www.w3.org/TR/css-cascade-6/#scope-atrule", section: "3.1")]
    [SpecFact]
    public void Scope_with_start_only_captures_start_and_inner_rules()
    {
        var scopes = Parse("@scope (.card) { .title { color: red; } }");
        scopes.Should().HaveCount(1);
        scopes[0].ScopeStart.Should().Be(".card");
        scopes[0].ScopeEnd.Should().BeNull();
        scopes[0].Rules.Should().ContainSingle();
    }

    [Spec("css-scope-1", "https://www.w3.org/TR/css-cascade-6/#scope-atrule", section: "3.1")]
    [SpecFact]
    public void Scope_with_start_and_end_captures_both_bounds()
    {
        var scopes = Parse("@scope (.card) to (.footer) { a { color: blue; } }");
        scopes.Should().HaveCount(1);
        scopes[0].ScopeStart.Should().Be(".card");
        scopes[0].ScopeEnd.Should().Be(".footer");
    }

    [Spec("css-scope-1", "https://www.w3.org/TR/css-cascade-6/#scope-atrule", section: "3.1")]
    [SpecFact]
    public void Scope_preserves_multiple_inner_rules()
    {
        var scopes = Parse("@scope (#main) { h1 { color: red; } p { color: green; } }");
        scopes.Single().ScopeStart.Should().Be("#main");
        scopes.Single().Rules.Should().HaveCount(2);
    }
}

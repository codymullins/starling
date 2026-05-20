using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Media;

namespace Starling.Css.Spec.Tests.Mediaqueries;

/// <summary>
/// At-rule conformance for <see href="https://drafts.csswg.org/mediaqueries-4/">Media Queries Level 4</see>.
/// </summary>
[TestClass]
[Spec("mediaqueries", "https://drafts.csswg.org/mediaqueries-4/")]
public sealed class AtRuleTests
{

    /// <summary>Spec: <see href="https://drafts.csswg.org/mediaqueries-4/"/>
    /// <para>At-rule <c>@media</c>.</para>
    /// </summary>
    [Spec("mediaqueries", "https://drafts.csswg.org/mediaqueries-4/")]
    [SpecFact]
    public void Parses_at_media()
    {
        var sheet = CssParser.ParseStyleSheet("@media (min-width: 400px) { }");
        var at = sheet.Rules.OfType<AtRule>().Single();
        at.Name.Should().Be("media");
        var list = MediaQueryParser.ParseList(at.Prelude);
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 500)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 300)).Should().BeFalse();
    }
}

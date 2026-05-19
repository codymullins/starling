using FluentAssertions;
using Starling.Css.Media;
using Starling.Css.Parser;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-cascade-5", "https://www.w3.org/TR/css-cascade-5/")]

[TestClass]
public sealed class ImportConditionsTests
{
    private static ImportRule ParseImport(string source)
    {
        var sheet = CssParser.ParseStyleSheet(source);
        var at = sheet.Rules.OfType<AtRule>().Single();
        ImportRuleParser.TryParse(at, out var imp).Should().BeTrue();
        return imp;
    }

    [TestMethod]
    public void Captures_url_layer_supports_and_media_query()
    {
        var imp = ParseImport(
            """@import "x.css" layer(reset) supports(color: red) (max-width: 600px);""");

        imp.Url.Should().Be("x.css");
        imp.LayerName.Should().Be("reset");
        imp.SupportsCondition.Should().NotBeNull();
        imp.MediaQueryList.Queries.Should().NotBeEmpty();
        MediaQueryEvaluator.Evaluate(imp.MediaQueryList, new MediaContext(ViewportWidthPx: 500))
            .Should().BeTrue();
        MediaQueryEvaluator.Evaluate(imp.MediaQueryList, new MediaContext(ViewportWidthPx: 800))
            .Should().BeFalse();
    }

    [TestMethod]
    public void Captures_url_via_url_function()
    {
        var imp = ParseImport("@import url(\"styles.css\");");

        imp.Url.Should().Be("styles.css");
        imp.LayerName.Should().BeNull();
        imp.SupportsCondition.Should().BeNull();
    }

    [TestMethod]
    public void Anonymous_layer_keyword_yields_empty_layer_name()
    {
        var imp = ParseImport("@import \"a.css\" layer;");

        imp.LayerName.Should().Be(string.Empty);
    }

    [TestMethod]
    public void Bare_import_without_conditions_parses()
    {
        var imp = ParseImport("@import url(a.css);");
        imp.Url.Should().Be("a.css");
        imp.LayerName.Should().BeNull();
        imp.SupportsCondition.Should().BeNull();
    }
}

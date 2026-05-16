using FluentAssertions;
using Tessera.Css.Media;
using Tessera.Css.Parser;
using Xunit;

namespace Tessera.Css.Tests;

public sealed class ImportConditionsTests
{
    private static ImportRule ParseImport(string source)
    {
        var sheet = CssParser.ParseStyleSheet(source);
        var at = sheet.Rules.OfType<AtRule>().Single();
        ImportRuleParser.TryParse(at, out var imp).Should().BeTrue();
        return imp;
    }

    [Fact]
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

    [Fact]
    public void Captures_url_via_url_function()
    {
        var imp = ParseImport("@import url(\"styles.css\");");

        imp.Url.Should().Be("styles.css");
        imp.LayerName.Should().BeNull();
        imp.SupportsCondition.Should().BeNull();
    }

    [Fact]
    public void Anonymous_layer_keyword_yields_empty_layer_name()
    {
        var imp = ParseImport("@import \"a.css\" layer;");

        imp.LayerName.Should().Be(string.Empty);
    }

    [Fact]
    public void Bare_import_without_conditions_parses()
    {
        var imp = ParseImport("@import url(a.css);");
        imp.Url.Should().Be("a.css");
        imp.LayerName.Should().BeNull();
        imp.SupportsCondition.Should().BeNull();
    }
}

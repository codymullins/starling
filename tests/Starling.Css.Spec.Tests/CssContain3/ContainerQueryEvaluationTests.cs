using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.CssContain3;

/// <summary>
/// Behavioral conformance for <c>@container</c> size-query evaluation
/// (<see href="https://www.w3.org/TR/css-contain-3/">CSS Containment 3</see> §5):
/// rules inside a matching container query apply to descendants; non-matching
/// ones do not. The container's size is supplied via
/// <see cref="StyleEngine.ContainerSizeLookup"/>.
/// </summary>
[TestClass]
[Spec("css-contain-3", "https://www.w3.org/TR/css-contain-3/", section: "5")]
public sealed class ContainerQueryEvaluationTests
{
    private static readonly Starling.Css.Values.CssColor Red = new(255, 0, 0);

    private static (StyleEngine Engine, Element Child) Setup(string css, double containerWidth, double containerHeight)
    {
        var doc = new Document();
        var container = doc.CreateElement("div");
        var child = doc.CreateElement("p");
        doc.AppendChild(container);
        container.AppendChild(child);
        var engine = new StyleEngine
        {
            ContainerSizeLookup = el => el == container ? (containerWidth, containerHeight) : null,
        };
        engine.AddStyleSheet(CssParser.ParseStyleSheet(css));
        return (engine, child);
    }

    [Spec("css-contain-3", "https://www.w3.org/TR/css-contain-3/#container-rule", section: "5.1")]
    [SpecFact]
    public void Container_query_applies_when_min_width_matches()
    {
        var (engine, child) = Setup("@container (min-width: 400px) { p { color: red; } }", 500, 200);
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(Red);
    }

    [Spec("css-contain-3", "https://www.w3.org/TR/css-contain-3/#container-rule", section: "5.1")]
    [SpecFact]
    public void Container_query_does_not_apply_when_min_width_below_threshold()
    {
        var (engine, child) = Setup("@container (min-width: 400px) { p { color: red; } }", 300, 200);
        engine.Compute(child).GetColor(PropertyId.Color).Should().NotBe(Red);
    }

    [Spec("css-contain-3", "https://www.w3.org/TR/css-contain-3/#size-container", section: "5.2")]
    [SpecFact]
    public void Container_query_max_width_and_range_syntax()
    {
        var (e1, c1) = Setup("@container (max-width: 400px) { p { color: red; } }", 300, 200);
        e1.Compute(c1).GetColor(PropertyId.Color).Should().Be(Red, "300px <= 400px");

        var (e2, c2) = Setup("@container (width > 400px) { p { color: red; } }", 500, 200);
        e2.Compute(c2).GetColor(PropertyId.Color).Should().Be(Red, "500px > 400px");

        var (e3, c3) = Setup("@container (width > 400px) { p { color: red; } }", 350, 200);
        e3.Compute(c3).GetColor(PropertyId.Color).Should().NotBe(Red, "350px is not > 400px");
    }

    [Spec("css-contain-3", "https://www.w3.org/TR/css-contain-3/#container-rule", section: "5.1")]
    [PendingFact("named container matching not implemented — the nearest query container is used regardless of the rule's <container-name>", trackingWp: "wp:spec-css-contain-3")]
    public void Named_container_query_only_matches_the_named_container()
    {
        // `@container wrong-name (min-width: 400px)` should NOT match when the
        // nearest container is not named `wrong-name`. The current impl ignores
        // the name and matches the nearest container.
        var (engine, child) = Setup("@container nonexistent (min-width: 400px) { p { color: red; } }", 500, 200);
        engine.Compute(child).GetColor(PropertyId.Color).Should().NotBe(Red);
    }

    [Spec("css-contain-3", "https://www.w3.org/TR/css-contain-3/#style-container", section: "6")]
    [PendingFact("style() container queries are not implemented", trackingWp: "wp:spec-css-contain-3")]
    public void Style_container_query_evaluates_custom_property()
    {
        // `@container style(--theme: dark) { ... }` — style queries are not evaluated.
        var (engine, child) = Setup("@container style(--theme: dark) { p { color: red; } }", 500, 200);
        engine.Compute(child).GetColor(PropertyId.Color).Should().Be(Red);
    }
}

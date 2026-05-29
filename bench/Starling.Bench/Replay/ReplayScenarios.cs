using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Dom;
using Starling.Html;
using Starling.Layout;

namespace Starling.Bench.Replay;

/// <summary>
/// A replayable page: its parsed document, the style engine to cascade with, the
/// viewport, and the per-frame DOM mutation a script would make. The mutation
/// delegate stands in for the page's <c>requestAnimationFrame</c> body (which the
/// harness cannot run today — the JS engine does not compile). It writes the same
/// kind of deep text node the animations demo writes every frame.
/// </summary>
public sealed class ReplayScenario
{
    public required string PageName { get; init; }
    public required Document Document { get; init; }
    public required StyleEngine Style { get; init; }
    public required Size Viewport { get; init; }

    /// <summary>Mutate the DOM as one frame's script would. Receives the 0-based frame index.</summary>
    public required Action<int> MutateForFrame { get; init; }
}

public static class ReplayScenarios
{
    public static readonly IReadOnlyList<string> Names = ["flex-status", "list", "nginx"];

    public static ReplayScenario Create(string pageName) => pageName switch
    {
        "flex-status" => FlexStatus(),
        "list" => ListPage(),
        "nginx" => Nginx(),
        _ => throw new ArgumentException(
            $"Unknown replay page '{pageName}'. Known: {string.Join(", ", Names)}.", nameof(pageName)),
    };

    // The animations-demo shape: a flex-rooted page whose deep status line is
    // rewritten every frame. This is the documented CPU spike — see
    // docs/animation-relayout-perf.md.
    private static ReplayScenario FlexStatus()
    {
        var doc = HtmlParser.Parse(Fixtures.FlexRootedHtml(200));
        doc.RecordLayoutMutations = true;
        var style = NewStyle();
        style.AddStyleSheet(CssParser.ParseStyleSheet(Fixtures.FlexRootCss));
        var status = FindFirstText(doc.GetElementById("status"));
        return new ReplayScenario
        {
            PageName = "flex-status",
            Document = doc,
            Style = style,
            Viewport = new Size(1024, 768),
            MutateForFrame = frame =>
            {
                if (status is not null)
                    status.Data = frame % 2 == 0 ? "running 16 ms" : "running 32 ms";
            },
        };
    }

    // A deep list where a single deep text node changes each frame.
    private static ReplayScenario ListPage()
    {
        var doc = HtmlParser.Parse(Fixtures.ListHtml(200));
        doc.RecordLayoutMutations = true;
        var text = FindFirstText(doc.GetElementById("row-0"));
        return new ReplayScenario
        {
            PageName = "list",
            Document = doc,
            Style = NewStyle(),
            Viewport = new Size(1024, 768),
            MutateForFrame = frame =>
            {
                if (text is not null)
                    text.Data = "Item 0 frame " + (frame % 100);
            },
        };
    }

    // A real page with no per-frame mutation: measures steady-state relayout cost.
    private static ReplayScenario Nginx()
    {
        var doc = HtmlParser.Parse(File.ReadAllText(Fixtures.NginxHtmlPath));
        doc.RecordLayoutMutations = true;
        var style = NewStyle();
        style.AddStyleSheet(CssParser.ParseStyleSheet(File.ReadAllText(Fixtures.NginxCssPath)));
        return new ReplayScenario
        {
            PageName = "nginx",
            Document = doc,
            Style = style,
            Viewport = new Size(1024, 768),
            MutateForFrame = static _ => { },
        };
    }

    private static StyleEngine NewStyle()
    {
        var style = new StyleEngine();
        // Mirror Painter.CreateStyleEngine: expose the real viewport to @media
        // and viewport-length units so the cascade matches a live frame.
        style.MediaContext = style.MediaContext with { ViewportWidthPx = 1024, ViewportHeightPx = 768 };
        return style;
    }

    private static Text? FindFirstText(Element? element)
    {
        if (element is null) return null;
        for (var child = element.FirstChild; child is not null; child = child.NextSibling)
            if (child is Text t) return t;
        return null;
    }
}

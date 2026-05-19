using BenchmarkDotNet.Attributes;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Paint.DisplayList;

namespace Starling.Bench;

// Display-list IR build cost. The raster step is excluded from BDN because
// timings vary by host (CPU/GPU), but `DisplayListBuilder.Build` is pure
// managed and is the seam every backend reads from.
[MemoryDiagnoser]
public class PaintBench
{
    private BlockBox _layoutRoot = null!;

    [GlobalSetup]
    public void Setup()
    {
        var doc = HtmlParser.Parse(File.ReadAllText(Fixtures.NginxHtmlPath));
        var style = new StyleEngine();
        style.AddStyleSheet(CssParser.ParseStyleSheet(File.ReadAllText(Fixtures.NginxCssPath)));
        _layoutRoot = new LayoutEngine(style).LayoutDocument(doc, new Size(1024, 768));
    }

    [Benchmark]
    public int BuildDisplayList_NginxOrg()
        => new DisplayListBuilder().Build(_layoutRoot).Items.Count;
}

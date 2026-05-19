using BenchmarkDotNet.Attributes;
using Tessera.Css.Cascade;
using Tessera.Css.Parser;
using Tessera.Html;
using Tessera.Layout;
using Tessera.Layout.Box;
using Tessera.Paint.DisplayList;

namespace Tessera.Bench;

// Display-list IR build cost. The Skia Graphite raster backend is excluded
// from BDN — it requires the osx-arm64 native shim and produces
// machine-dependent timings — but `DisplayListBuilder.Build` is pure managed
// and is the seam every backend reads from.
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

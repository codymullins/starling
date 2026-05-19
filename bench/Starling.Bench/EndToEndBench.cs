using BenchmarkDotNet.Attributes;
using Tessera.Css.Cascade;
using Tessera.Css.Parser;
using Tessera.Dom;
using Tessera.Html;
using Tessera.Layout;
using Tessera.Paint.DisplayList;

namespace Tessera.Bench;

// Full HTML+CSS → DOM → cascade → layout → display list pipeline on the
// vendored `nginx.org` snapshot (the same fixture the M2 golden gate uses).
// Excludes the raster backend since Skia Graphite is osx-arm64 only today
// and would make this bench machine-dependent. Use this as the holistic
// regression signal; the per-stage benches localize the cause.
[MemoryDiagnoser]
public class EndToEndBench
{
    private string _html = string.Empty;
    private string _css  = string.Empty;
    private static readonly Size Viewport = new(1024, 768);

    [GlobalSetup]
    public void Setup()
    {
        _html = File.ReadAllText(Fixtures.NginxHtmlPath);
        _css  = File.ReadAllText(Fixtures.NginxCssPath);
    }

    [Benchmark]
    public int Render_NginxOrg_HtmlToDisplayList()
    {
        Document doc = HtmlParser.Parse(_html);
        var style = new StyleEngine();
        style.AddStyleSheet(CssParser.ParseStyleSheet(_css));
        var layout = new LayoutEngine(style).LayoutDocument(doc, Viewport);
        var displayList = new DisplayListBuilder().Build(layout);
        return displayList.Items.Count;
    }
}

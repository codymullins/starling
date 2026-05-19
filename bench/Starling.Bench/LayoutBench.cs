using BenchmarkDotNet.Attributes;
using Tessera.Css.Cascade;
using Tessera.Css.Parser;
using Tessera.Dom;
using Tessera.Html;
using Tessera.Layout;

namespace Tessera.Bench;

// Layout cost on a real-world page (the offline nginx.org snapshot — the same
// fixture the M2 golden gate uses). Box-tree construction + block + inline
// formatting; M1-08 scope. Style is precomputed in Setup so this bench
// isolates the layout pass.
[MemoryDiagnoser]
public class LayoutBench
{
    private Document _doc = null!;
    private StyleEngine _style = null!;
    private static readonly Size Viewport = new(1024, 768);

    [GlobalSetup]
    public void Setup()
    {
        _doc = HtmlParser.Parse(File.ReadAllText(Fixtures.NginxHtmlPath));
        _style = new StyleEngine();
        _style.AddStyleSheet(CssParser.ParseStyleSheet(File.ReadAllText(Fixtures.NginxCssPath)));
    }

    [Benchmark]
    public double LayoutDocument_NginxOrg()
    {
        var engine = new LayoutEngine(_style);
        var root = engine.LayoutDocument(_doc, Viewport);
        return root.Frame.Height;
    }
}

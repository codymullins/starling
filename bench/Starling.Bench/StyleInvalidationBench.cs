using BenchmarkDotNet.Attributes;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Dom;
using Starling.Html;

namespace Starling.Bench;

// Style recalc cost, split two ways: a full-tree precompute (what a from-scratch
// relayout pays every frame) versus a single-element recompute (the unit of work
// a selective invalidation would do). The ratio between them is how much a
// targeted invalidation saves over recascading the whole document. Scope:
// style-only. `cache.Count` is the number of elements the cascade resolved.
[MemoryDiagnoser]
public class StyleInvalidationBench
{
    [Params(50, 500)]
    public int Rows;

    private Document _doc = null!;
    private StyleSheet _sheet = null!;
    private Element _target = null!;

    [GlobalSetup]
    public void Setup()
    {
        _doc = HtmlParser.Parse(Fixtures.FlexRootedHtml(Rows));
        _sheet = CssParser.ParseStyleSheet(Fixtures.FlexRootCss);
        _target = _doc.GetElementById("status")!;
    }

    [Benchmark(Baseline = true)]
    public int Recalc_FullTree()
    {
        var engine = new StyleEngine();
        engine.AddStyleSheet(_sheet);
        var cache = new CascadeCache();
        engine.PrecomputeTree(_doc.DocumentElement!, cache);
        return cache.Count;
    }

    [Benchmark]
    public int Recalc_SingleElement()
    {
        var engine = new StyleEngine();
        engine.AddStyleSheet(_sheet);
        return engine.Compute(_target).GetType().GetHashCode();
    }
}

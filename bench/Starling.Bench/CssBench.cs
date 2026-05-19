using BenchmarkDotNet.Attributes;
using Tessera.Css.Parser;
using Tessera.Css.Tokenizer;

namespace Tessera.Bench;

// Pairs with the M1 exit (`css/css-syntax/` ≥ 80%, selectors ≥ 50%). The
// tokenizer + parser are the cost on every page load and on every dynamic
// `<style>` insertion; separating the two stages keeps regressions localized.
[MemoryDiagnoser]
public class CssBench
{
    private string _nginxCss = string.Empty;

    [GlobalSetup]
    public void Setup() => _nginxCss = File.ReadAllText(Fixtures.NginxCssPath);

    [Benchmark]
    public int TokenizerOnly_Tiny() => CssTokenizer.Tokenize(Fixtures.TinyCss).Count;

    [Benchmark]
    public int TokenizerOnly_Small() => CssTokenizer.Tokenize(Fixtures.SmallCss).Count;

    [Benchmark]
    public int TokenizerOnly_NginxOrg() => CssTokenizer.Tokenize(_nginxCss).Count;

    [Benchmark]
    public int Parse_Tiny() => CssParser.ParseStyleSheet(Fixtures.TinyCss).Rules.Count;

    [Benchmark]
    public int Parse_Small() => CssParser.ParseStyleSheet(Fixtures.SmallCss).Rules.Count;

    [Benchmark(Baseline = true)]
    public int Parse_NginxOrg() => CssParser.ParseStyleSheet(_nginxCss).Rules.Count;
}

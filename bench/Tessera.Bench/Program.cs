using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Tessera.Html;

namespace Tessera.Bench;

/// <summary>
/// BenchmarkDotNet harness. M0 ships a single trivial benchmark so the project
/// compiles and the runner has something to discover. Real workload benchmarks
/// (tokenizer, parser, layout, paint, JS interpreter) land alongside the
/// subsystems they measure — see 12_TESTING.md.
/// </summary>
internal static class Program
{
    public static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

[MemoryDiagnoser]
public class HtmlParseBench
{
    private const string Sample = "<!doctype html><html><body><p>Hello, world.</p></body></html>";

    [Benchmark]
    public int ParseHelloWorld()
    {
        var doc = HtmlParser.Parse(Sample);
        return doc.TextContent.Length;
    }
}

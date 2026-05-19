using System.Globalization;

namespace Starling.SpecGen;

/// <summary>
/// Starling.SpecGen — CLI for the CSS spec catalog &amp; coverage reporter.
///
/// Commands:
///   catalog   Read testdata/webref/css/*.json, emit a flat summary of every
///             spec found (id, title, # properties, # at-rules, # selectors).
///   report    (stub) Will rebuild tasks/SPEC_COVERAGE.md from the catalog
///             plus discovered test traits. Not implemented yet.
///
/// Run from the repo root:
///   dotnet run --project tools/Starling.SpecGen -- catalog
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var repoRoot = FindRepoRoot();
        var webrefCss = Path.Combine(repoRoot, "testdata", "webref", "css");

        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine("usage: starling-specgen <catalog|generate-stubs|report>");
            return 0;
        }

        return args[0] switch
        {
            "catalog"        => RunCatalog(webrefCss),
            "generate-stubs" => StubGenerator.Generate(webrefCss,
                Path.Combine(repoRoot, "tests", "Starling.Css.Spec.Tests")),
            "report"         => RunReport(),
            _                => Fail($"unknown command: {args[0]}"),
        };
    }

    private static int RunCatalog(string cssDir)
    {
        if (!Directory.Exists(cssDir))
        {
            return Fail($"webref data not found at {cssDir} — see testdata/webref/README.md");
        }

        var specs = WebrefLoader.LoadAll(cssDir);
        Console.WriteLine($"# Webref CSS catalog — {specs.Count} specs");
        Console.WriteLine();
        Console.WriteLine("| Spec id | Title | Props | At-rules | Selectors | Value types |");
        Console.WriteLine("|---|---|--:|--:|--:|--:|");

        var totalProps = 0;
        var totalAtRules = 0;
        var totalSelectors = 0;
        var totalValues = 0;

        foreach (var (id, doc) in specs)
        {
            var props = doc.Properties?.Count ?? 0;
            var ats   = doc.AtRules?.Count ?? 0;
            var sels  = doc.Selectors?.Count ?? 0;
            var vals  = doc.Values?.Count ?? 0;
            totalProps += props;
            totalAtRules += ats;
            totalSelectors += sels;
            totalValues += vals;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "| `{0}` | {1} | {2} | {3} | {4} | {5} |",
                id, doc.Spec.Title, props, ats, sels, vals));
        }

        Console.WriteLine();
        Console.WriteLine($"**Totals:** {totalProps} properties, {totalAtRules} at-rules, " +
                          $"{totalSelectors} selectors, {totalValues} value types across {specs.Count} specs.");
        return 0;
    }

    private static int RunReport()
    {
        Console.Error.WriteLine("report: not implemented yet — tracked by wp:spec-tooling-bootstrap.");
        Console.Error.WriteLine("Update tasks/SPEC_COVERAGE.md by hand until then.");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Starling.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root.");
    }
}

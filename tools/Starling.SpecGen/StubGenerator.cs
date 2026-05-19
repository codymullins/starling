using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Starling.SpecGen;

/// <summary>
/// Generates <c>[PendingFact]</c> stub test classes under
/// <c>tests/Starling.Css.Spec.Tests/&lt;SpecId&gt;/</c> for every property,
/// at-rule, and selector in the webref catalog.
///
/// Idempotent: existing files are left untouched (so hand-written tests like
/// the exemplar <c>CssVariables1/</c>, <c>CssColor5/</c>, <c>CssBackgrounds3/</c>
/// folders are never overwritten). The generator only fills in missing files.
/// </summary>
public static class StubGenerator
{
    // Webref includes definitions from non-CSS hosts that we don't intend to
    // mirror as CSS conformance tests. Skip them.
    private static readonly HashSet<string> SkipSpecIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "SVG", "svg-strokes",
        "fullscreen", "html", "mathml-core", "webvtt", "webxr-dom-overlays",
        "av1-isobmff", "upgrade-insecure-requests",
        "compat", // legacy WebKit prefixes; not a CSS spec we test
    };

    public static int Generate(string webrefCssDir, string outputRoot)
    {
        if (!Directory.Exists(webrefCssDir))
        {
            Console.Error.WriteLine($"webref data not found at {webrefCssDir}");
            return 1;
        }

        Directory.CreateDirectory(outputRoot);
        var specs = WebrefLoader.LoadAll(webrefCssDir);

        var foldersCreated = 0;
        var filesCreated = 0;
        var testsCreated = 0;
        var foldersSkipped = 0;

        foreach (var (specId, doc) in specs)
        {
            if (SkipSpecIds.Contains(specId)) continue;

            var props = doc.Properties ?? Array.Empty<WebrefProperty>();
            var ats   = doc.AtRules   ?? Array.Empty<WebrefAtRule>();
            var sels  = doc.Selectors ?? Array.Empty<WebrefSelector>();

            if (props.Count == 0 && ats.Count == 0 && sels.Count == 0)
            {
                continue;
            }

            var folderName = ToPascalCase(specId);
            var folderPath = Path.Combine(outputRoot, folderName);
            var existed = Directory.Exists(folderPath);
            if (!existed)
            {
                Directory.CreateDirectory(folderPath);
                foldersCreated++;
            }
            else
            {
                foldersSkipped++;
            }

            EnsureSpecManifest(folderPath, specId, doc, props.Count, ats.Count, sels.Count);

            if (props.Count > 0)
            {
                if (TryEmit(Path.Combine(folderPath, "PropertyTests.cs"),
                            EmitPropertyTests(specId, doc, folderName, props),
                            out var count))
                {
                    filesCreated++;
                    testsCreated += count;
                }
            }
            if (ats.Count > 0)
            {
                if (TryEmit(Path.Combine(folderPath, "AtRuleTests.cs"),
                            EmitAtRuleTests(specId, doc, folderName, ats),
                            out var count))
                {
                    filesCreated++;
                    testsCreated += count;
                }
            }
            if (sels.Count > 0)
            {
                if (TryEmit(Path.Combine(folderPath, "SelectorTests.cs"),
                            EmitSelectorTests(specId, doc, folderName, sels),
                            out var count))
                {
                    filesCreated++;
                    testsCreated += count;
                }
            }
        }

        Console.WriteLine($"generate-stubs: {foldersCreated} new folders, " +
                          $"{foldersSkipped} pre-existing folders kept, " +
                          $"{filesCreated} files written, " +
                          $"{testsCreated} new [PendingFact] tests.");
        return 0;
    }

    private static bool TryEmit(string path, (string body, int testCount) gen, out int testCount)
    {
        testCount = gen.testCount;
        if (File.Exists(path))
        {
            testCount = 0;
            return false;
        }
        File.WriteAllText(path, gen.body);
        return true;
    }

    private static void EnsureSpecManifest(string folderPath, string specId, WebrefCss doc,
                                            int propCount, int atCount, int selCount)
    {
        var manifestPath = Path.Combine(folderPath, "_spec.md");
        if (File.Exists(manifestPath)) return;

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"id: {specId}");
        sb.AppendLine($"url: {doc.Spec.Url}");
        sb.AppendLine($"title: {doc.Spec.Title}");
        sb.AppendLine("status: ED (snapshot via w3c/webref)");
        sb.AppendLine("fetched: 2026-05-19");
        sb.AppendLine("source: testdata/webref/css/" + specId + ".json");
        sb.AppendLine("generated: true");
        sb.AppendLine("definition_counts:");
        sb.AppendLine($"  properties: {propCount}");
        sb.AppendLine($"  at_rules: {atCount}");
        sb.AppendLine($"  selectors: {selCount}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {doc.Spec.Title}");
        sb.AppendLine();
        sb.AppendLine($"Spec: <{doc.Spec.Url}>");
        sb.AppendLine();
        sb.AppendLine("Stub conformance tests in this folder were generated by");
        sb.AppendLine("`tools/Starling.SpecGen generate-stubs` from the pinned webref snapshot.");
        sb.AppendLine("Every test is marked `[PendingFact]` — promote to `[SpecFact]` and");
        sb.AppendLine("provide a real assertion as the feature is implemented.");
        File.WriteAllText(manifestPath, sb.ToString());
    }

    private static (string body, int testCount) EmitPropertyTests(
        string specId, WebrefCss doc, string folderName, IReadOnlyList<WebrefProperty> properties)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Source: testdata/webref/css/" + specId + ".json");
        sb.AppendLine("// Regenerate via: dotnet run --project tools/Starling.SpecGen -- generate-stubs");
        sb.AppendLine();
        sb.AppendLine($"namespace Starling.Css.Spec.Tests.{folderName};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Property conformance for <see href=\"{doc.Spec.Url}\">{Escape(doc.Spec.Title)}</see>.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"[Spec(\"{specId}\", \"{doc.Spec.Url}\")]");
        sb.AppendLine("public sealed class PropertyTests");
        sb.AppendLine("{");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var emitted = 0;
        foreach (var p in properties)
        {
            var method = "Parses_" + SafeIdentifier(p.Name);
            if (!seen.Add(method)) continue;

            var href = string.IsNullOrEmpty(p.Href) ? doc.Spec.Url : p.Href;
            sb.AppendLine();
            sb.AppendLine($"    /// <summary>Spec: <see href=\"{href}\"/>");
            sb.AppendLine($"    /// <para>Property <c>{p.Name}</c>" +
                          (string.IsNullOrEmpty(p.Value) ? "." : $" — value <c>{Escape(p.Value!)}</c>; ") +
                          (string.IsNullOrEmpty(p.Initial) ? "" : $"initial <c>{Escape(p.Initial!)}</c>.") +
                          "</para>");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    [Spec(\"{specId}\", \"{href}\")]");
            sb.AppendLine($"    [PendingFact(\"property '{p.Name}' not asserted yet\", trackingWp: \"wp:spec-{specId}\")]");
            sb.AppendLine($"    public void {method}() => throw new NotImplementedException();");
            emitted++;
        }
        sb.AppendLine("}");
        return (sb.ToString(), emitted);
    }

    private static (string body, int testCount) EmitAtRuleTests(
        string specId, WebrefCss doc, string folderName, IReadOnlyList<WebrefAtRule> atRules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Source: testdata/webref/css/" + specId + ".json");
        sb.AppendLine("// Regenerate via: dotnet run --project tools/Starling.SpecGen -- generate-stubs");
        sb.AppendLine();
        sb.AppendLine($"namespace Starling.Css.Spec.Tests.{folderName};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// At-rule conformance for <see href=\"{doc.Spec.Url}\">{Escape(doc.Spec.Title)}</see>.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"[Spec(\"{specId}\", \"{doc.Spec.Url}\")]");
        sb.AppendLine("public sealed class AtRuleTests");
        sb.AppendLine("{");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var emitted = 0;
        foreach (var a in atRules)
        {
            var bare = a.Name.TrimStart('@');
            var method = "Parses_at_" + SafeIdentifier(bare);
            if (!seen.Add(method)) continue;

            var href = string.IsNullOrEmpty(a.Href) ? doc.Spec.Url : a.Href;
            sb.AppendLine();
            sb.AppendLine($"    /// <summary>Spec: <see href=\"{href}\"/>");
            sb.AppendLine($"    /// <para>At-rule <c>{a.Name}</c>.</para>");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    [Spec(\"{specId}\", \"{href}\")]");
            sb.AppendLine($"    [PendingFact(\"at-rule '{a.Name}' not asserted yet\", trackingWp: \"wp:spec-{specId}\")]");
            sb.AppendLine($"    public void {method}() => throw new NotImplementedException();");
            emitted++;
        }
        sb.AppendLine("}");
        return (sb.ToString(), emitted);
    }

    private static (string body, int testCount) EmitSelectorTests(
        string specId, WebrefCss doc, string folderName, IReadOnlyList<WebrefSelector> selectors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Source: testdata/webref/css/" + specId + ".json");
        sb.AppendLine("// Regenerate via: dotnet run --project tools/Starling.SpecGen -- generate-stubs");
        sb.AppendLine();
        sb.AppendLine($"namespace Starling.Css.Spec.Tests.{folderName};");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Selector conformance for <see href=\"{doc.Spec.Url}\">{Escape(doc.Spec.Title)}</see>.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"[Spec(\"{specId}\", \"{doc.Spec.Url}\")]");
        sb.AppendLine("public sealed class SelectorTests");
        sb.AppendLine("{");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var emitted = 0;
        foreach (var s in selectors)
        {
            var method = "Matches_" + SafeIdentifier(s.Name);
            if (!seen.Add(method)) continue;

            var href = string.IsNullOrEmpty(s.Href) ? doc.Spec.Url : s.Href;
            sb.AppendLine();
            sb.AppendLine($"    /// <summary>Spec: <see href=\"{href}\"/>");
            sb.AppendLine($"    /// <para>Selector <c>{Escape(s.Name)}</c>.</para>");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    [Spec(\"{specId}\", \"{href}\")]");
            sb.AppendLine($"    [PendingFact(\"selector '{Escape(s.Name)}' not asserted yet\", trackingWp: \"wp:spec-{specId}\")]");
            sb.AppendLine($"    public void {method}() => throw new NotImplementedException();");
            emitted++;
        }
        sb.AppendLine("}");
        return (sb.ToString(), emitted);
    }

    // ----- identifier sanitisation -----

    /// <summary>Converts a CSS identifier (kebab-case, possibly with weird characters) to
    /// a safe C# identifier suffix: alphanumerics preserved, everything else collapses to '_'.</summary>
    private static string SafeIdentifier(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is '-' or '_' or ' ' or '/' or '.') sb.Append('_');
            // drop anything else (parentheses, colons, etc.)
        }
        var result = sb.ToString().Trim('_');
        if (result.Length == 0) result = "anon";
        if (char.IsDigit(result[0])) result = "_" + result;
        return result;
    }

    private static string ToPascalCase(string id)
    {
        // css-color-5 → CssColor5; mediaqueries-5 → Mediaqueries5
        var ti = CultureInfo.InvariantCulture.TextInfo;
        var parts = Regex.Split(id, "[^A-Za-z0-9]+");
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (p.Length == 0) continue;
            sb.Append(ti.ToTitleCase(p.ToLowerInvariant()));
        }
        var name = sb.ToString();
        if (name.Length == 0) name = "Spec";
        if (char.IsDigit(name[0])) name = "_" + name;
        return name;
    }

    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

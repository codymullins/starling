using System.Collections.Concurrent;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Test262.Tests;

public enum ScenarioMode { NonStrict, Strict, Raw }

public enum Outcome { Pass, Fail, Timeout, Skip }

public sealed record ScenarioResult(string File, ScenarioMode Mode, Outcome Outcome, string? Detail);

/// <summary>
/// Parsed subset of a Test262 file's YAML frontmatter (<c>/*--- … ---*/</c>)
/// that the runner acts on. See https://github.com/tc39/test262/blob/main/INTERPRETING.md.
/// </summary>
public sealed class Test262Metadata
{
    public List<string> Flags { get; } = new();
    public List<string> Includes { get; } = new();
    public List<string> Features { get; } = new();
    public string? NegativePhase { get; set; }
    public string? NegativeType { get; set; }

    public bool IsRaw => Flags.Contains("raw");
    public bool IsModule => Flags.Contains("module");
    public bool IsAsync => Flags.Contains("async");
    public bool OnlyStrict => Flags.Contains("onlyStrict");
    public bool NoStrict => Flags.Contains("noStrict");
    public bool IsNegative => NegativePhase is not null;
}

/// <summary>
/// A self-contained Test262 harness runner: parses frontmatter, assembles the
/// required harness includes, runs each applicable scenario (non-strict / strict
/// / raw) in a fresh realm, and classifies the outcome (incl. negative-test and
/// async-<c>$DONE</c> handling). Reused across files; harness chunks are parsed
/// once and re-run per realm.
/// </summary>
public sealed class Test262Runner
{
    private readonly string _root;                 // testdata/test262
    private readonly string _harnessDir;
    private readonly ConcurrentDictionary<string, Chunk> _harnessCache = new(StringComparer.Ordinal);
    private readonly int _timeoutMs;

    public Test262Runner(string test262Root, int timeoutMs = 10_000)
    {
        _root = test262Root;
        _harnessDir = Path.Combine(test262Root, "harness");
        _timeoutMs = timeoutMs;
    }

    /// <summary>Test262 <c>features</c> tags that fall outside our targeted
    /// ECMAScript level (ES2024 / ES15): post-ES2024 proposals (ES2025+ and
    /// Stage-3 proposals) and explicitly out-of-scope subsystems (worker-only
    /// shared memory, tail calls, ECMA-402 Intl). An ES2024-conformant engine
    /// is not expected to implement these, so they are skipped rather than
    /// counted as failures. Kept deliberately conservative — only features that
    /// are unambiguously beyond ES2024.</summary>
    private static readonly HashSet<string> OutOfScopeFeatures = new(StringComparer.Ordinal)
    {
        // Stage-3 proposals (not in any published edition):
        "decorators", "explicit-resource-management", "Temporal",
        "import-attributes", "import-assertions",
        // Source-phase imports (`import source x from …`, `import.source(…)`):
        // a Stage-3 proposal beyond ES2024. Test262 tags it `source-phase-imports`
        // (plural); the earlier singular spelling never matched, so these were
        // run and counted as failures.
        "source-phase-imports", "source-phase-import",
        "import-defer", "tail-call-optimization",
        // Worker-only shared memory (browser-plan: M8+, out of v1 scope):
        "Atomics", "SharedArrayBuffer",
        // ES2025+ library proposals:
        "iterator-helpers", "set-methods", "Float16Array", "uint8array-base64",
        "regexp-duplicate-named-groups", "promise-try", "regexp-escape",
        "Array.fromAsync", "json-parse-with-source", "iterator-sequencing",
        // ECMA-402 (Intl) — out of scope per 09_JS_ENGINE.md:
        "Intl.DurationFormat", "Intl-enumeration", "Intl.Locale-info",
    };

    /// <summary>Filesystem module host: resolves a (relative) specifier against
    /// the referrer's directory and reads the file. Lets dynamic <c>import()</c>
    /// in a Test262 script load that test's fixture modules from disk.</summary>
    private sealed class FsModuleHost : Starling.Js.Modules.IModuleHost
    {
        public string? Resolve(string specifier, string? referrer)
        {
            try
            {
                var baseDir = referrer is not null
                    ? Path.GetDirectoryName(referrer)
                    : Directory.GetCurrentDirectory();
                if (baseDir is null) return null;
                // Only relative/absolute path specifiers are resolvable here;
                // bare specifiers (no leading . or /) have no mapping.
                if (!specifier.StartsWith('.') && !specifier.StartsWith('/')) return null;
                return Path.GetFullPath(Path.Combine(baseDir, specifier));
            }
            catch { return null; }
        }

        public string? FetchSource(string resolvedUrl)
        {
            try { return File.Exists(resolvedUrl) ? File.ReadAllText(resolvedUrl) : null; }
            catch { return null; }
        }
    }

    public IReadOnlyList<ScenarioResult> RunFile(string path)
    {
        string source;
        try { source = File.ReadAllText(path); }
        catch (Exception ex) { return new[] { new ScenarioResult(path, ScenarioMode.NonStrict, Outcome.Skip, "read: " + ex.Message) }; }

        var meta = ParseMetadata(source);
        var rel = Path.GetRelativePath(_root, path);

        // Module tests need the full loader + relative resolution; deferred.
        if (meta.IsModule)
            return new[] { new ScenarioResult(rel, ScenarioMode.NonStrict, Outcome.Skip, "module") };

        // Skip tests that require a feature outside our targeted spec level
        // (ES2024 / ES15). These are post-ES2024 proposals or explicitly
        // out-of-scope subsystems (see browser-plan/09_JS_ENGINE.md "Out"),
        // analogous to Jint's Test262Harness exclusion list — an ES2024 engine
        // is not expected to pass them, so counting them would understate
        // conformance against the targeted surface.
        foreach (var f in meta.Features)
            if (OutOfScopeFeatures.Contains(f))
                return new[] { new ScenarioResult(rel, ScenarioMode.NonStrict, Outcome.Skip, "out-of-scope:" + f) };

        var results = new List<ScenarioResult>();
        foreach (var mode in Modes(meta))
            results.Add(RunScenario(rel, source, meta, mode, path));
        return results;
    }

    private static IEnumerable<ScenarioMode> Modes(Test262Metadata meta)
    {
        if (meta.IsRaw) { yield return ScenarioMode.Raw; yield break; }
        if (!meta.OnlyStrict) yield return ScenarioMode.NonStrict;
        if (!meta.NoStrict) yield return ScenarioMode.Strict;
    }

    private ScenarioResult RunScenario(string rel, string source, Test262Metadata meta, ScenarioMode mode, string absPath)
    {
        Outcome outcome = Outcome.Fail;
        string? detail = null;

        var worker = new Thread(() =>
        {
            try { (outcome, detail) = Execute(source, meta, mode, absPath); }
            catch (Exception ex) { outcome = Outcome.Fail; detail = "host:" + ex.GetType().Name + ":" + Truncate(ex.Message); }
        }, maxStackSize: 64 * 1024 * 1024)  // headroom for the depth-1000 guard (~16 KB/frame)
        { IsBackground = true };

        worker.Start();
        if (!worker.Join(_timeoutMs))
            return new ScenarioResult(rel, mode, Outcome.Timeout, "timeout");
        return new ScenarioResult(rel, mode, outcome, detail);
    }

    private (Outcome, string?) Execute(string source, Test262Metadata meta, ScenarioMode mode, string absPath)
    {
        // ---- Negative parse: the test body itself must fail to parse. ----
        if (meta.IsNegative && meta.NegativePhase == "parse")
        {
            try
            {
                var prog = new JsParser(WithStrict(source, mode)).ParseProgram();
                _ = JsCompiler.Compile(prog, "<test262>");
                return (Outcome.Fail, "expected parse error, parsed OK");
            }
            catch (JsParseException) { return (Outcome.Pass, null); }
            catch (Exception ex) when (IsParseLevel(ex)) { return (Outcome.Pass, null); }
            // A non-parse error means it parsed then failed — wrong phase.
            catch (Exception ex) { return (Outcome.Fail, "expected parse error, got " + ex.GetType().Name); }
        }

        // ---- Build a fresh realm + harness for an executable scenario. ----
        var runtime = new JsRuntime();
        var printed = new List<string>();
        runtime.RegisterGlobal("print", args =>
        {
            printed.Add(args.Length > 0 && args[0].IsString ? args[0].AsString : Stringify(args.Length > 0 ? args[0] : JsValue.Undefined));
            return JsValue.Undefined;
        });
        var vm = new JsVm(runtime);

        // Wire a filesystem module loader so dynamic import() in a script
        // resolves the test's fixture modules relative to the test file. The
        // referrer is the test body's chunk name (its absolute path), set below.
        _ = new Starling.Js.Modules.ModuleLoader(runtime, new FsModuleHost());

        try
        {
            // Minimal $262 host object (INTERPRETING.md §"Host-Defined Functions").
            RunSource(vm, "var $262 = { global: globalThis, detachArrayBuffer: function(){}, gc: function(){} };", "<host>");

            if (!meta.IsRaw)
            {
                RunHarness(vm, "assert.js");
                RunHarness(vm, "sta.js");
                if (meta.IsAsync) RunHarness(vm, "doneprintHandle.js");
                foreach (var inc in meta.Includes) RunHarness(vm, inc);
            }
        }
        catch (Exception ex)
        {
            return (Outcome.Fail, "harness:" + ex.GetType().Name + ":" + Truncate(ExtractMessage(ex)));
        }

        // ---- Run the test body. ----
        // Name the chunk with the test's absolute path so dynamic import()
        // resolves its specifier relative to the test file.
        try
        {
            RunSource(vm, WithStrict(source, mode), absPath);
        }
        catch (JsThrow jt)
        {
            if (meta.IsNegative)
            {
                var name = ErrorName(jt.Value);
                return name == meta.NegativeType
                    ? (Outcome.Pass, null)
                    : (Outcome.Fail, $"expected {meta.NegativeType}, threw {FormatThrow(jt.Value)}");
            }
            return (Outcome.Fail, "threw " + FormatThrow(jt.Value));
        }
        catch (Exception ex) when (IsParseLevel(ex))
        {
            // Unexpected parse failure on a runnable test.
            return (Outcome.Fail, "parse:" + Truncate(ex.Message));
        }
        catch (Exception ex)
        {
            return (Outcome.Fail, "host:" + ex.GetType().Name + ":" + Truncate(ex.Message));
        }

        // Reached end without throwing.
        if (meta.IsNegative)
            return (Outcome.Fail, $"expected {meta.NegativeType}, no throw");

        if (meta.IsAsync)
        {
            return printed.Contains("Test262:AsyncTestComplete")
                ? (Outcome.Pass, null)
                : (Outcome.Fail, "async incomplete: " + Truncate(string.Join("|", printed)));
        }

        return (Outcome.Pass, null);
    }

    private void RunHarness(JsVm vm, string name)
    {
        var chunk = _harnessCache.GetOrAdd(name, n =>
        {
            var src = File.ReadAllText(Path.Combine(_harnessDir, n));
            return JsCompiler.Compile(new JsParser(src).ParseProgram(), "<harness:" + n + ">");
        });
        vm.Run(chunk);
    }

    private static void RunSource(JsVm vm, string source, string name)
    {
        var chunk = JsCompiler.Compile(new JsParser(source).ParseProgram(), name);
        vm.Run(chunk);
    }

    private static string WithStrict(string source, ScenarioMode mode) =>
        mode == ScenarioMode.Strict ? "\"use strict\";\n" + source : source;

    private static bool IsParseLevel(Exception ex) =>
        ex is JsParseException || ex.GetType().Name is "JsParseException" or "JsLexException";

    /// <summary>Read the <c>name</c> property of a thrown error value (e.g.
    /// "TypeError"); null when the throw isn't an error-shaped object.</summary>
    private static string? ErrorName(JsValue value)
    {
        if (!value.IsObject) return null;
        var n = value.AsObject.Get("name");
        return n.IsString ? n.AsString : null;
    }

    private static string ExtractMessage(Exception ex) =>
        ex is JsThrow jt ? FormatThrow(jt.Value) : ex.Message;

    /// <summary>Format a thrown JS value as "Name: message" for triage.</summary>
    private static string FormatThrow(JsValue value)
    {
        if (!value.IsObject) return Stringify(value);
        var obj = value.AsObject;
        var name = obj.Get("name");
        var msg = obj.Get("message");
        var n = name.IsString ? name.AsString : "Error";
        var mraw = msg.IsString ? msg.AsString : "";
        return mraw.Length > 0 ? $"{n}: {Truncate(mraw)}" : n;
    }

    private static string Stringify(JsValue v) =>
        v.IsString ? v.AsString : v.IsUndefined ? "undefined" : v.ToString() ?? "";

    private static string Truncate(string s) => s.Length <= 120 ? s : s[..120];

    // ---------------------------------------------------------------------
    // Frontmatter parsing (lightweight; not a full YAML parser).
    // ---------------------------------------------------------------------
    public static Test262Metadata ParseMetadata(string source)
    {
        var meta = new Test262Metadata();
        var start = source.IndexOf("/*---", StringComparison.Ordinal);
        if (start < 0) return meta;
        var end = source.IndexOf("---*/", start, StringComparison.Ordinal);
        if (end < 0) return meta;
        var block = source.Substring(start + 5, end - start - 5);
        var lines = block.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.StartsWith("flags:", StringComparison.Ordinal))
                AddInlineList(trimmed["flags:".Length..], meta.Flags);
            else if (trimmed.StartsWith("features:", StringComparison.Ordinal))
                CollectList(lines, ref i, trimmed["features:".Length..], meta.Features);
            else if (trimmed.StartsWith("includes:", StringComparison.Ordinal))
                CollectList(lines, ref i, trimmed["includes:".Length..], meta.Includes);
            else if (trimmed.StartsWith("negative:", StringComparison.Ordinal))
                ParseNegative(lines, ref i, meta);
        }
        return meta;
    }

    private static void ParseNegative(string[] lines, ref int i, Test262Metadata meta)
    {
        // negative:
        //   phase: parse
        //   type: SyntaxError
        for (var j = i + 1; j < lines.Length; j++)
        {
            var t = lines[j].Trim();
            if (t.Length == 0) continue;
            // Stop at the next top-level key (no leading indent on original line).
            if (!char.IsWhiteSpace(lines[j][0]) && t.Contains(':')) { i = j - 1; return; }
            if (t.StartsWith("phase:", StringComparison.Ordinal)) meta.NegativePhase = t["phase:".Length..].Trim();
            else if (t.StartsWith("type:", StringComparison.Ordinal)) meta.NegativeType = t["type:".Length..].Trim();
            else { i = j - 1; return; }
        }
    }

    /// <summary>Handle both inline (<c>key: [a, b]</c>) and block list
    /// (<c>key:</c> then <c>  - a</c>) forms.</summary>
    private static void CollectList(string[] lines, ref int i, string rest, List<string> into)
    {
        rest = rest.Trim();
        if (rest.StartsWith('['))
        {
            AddInlineList(rest, into);
            return;
        }
        // Block list on following indented "- item" lines.
        for (var j = i + 1; j < lines.Length; j++)
        {
            var raw = lines[j];
            var t = raw.Trim();
            if (t.Length == 0) { continue; }
            if (t.StartsWith("- ", StringComparison.Ordinal) || t == "-")
            {
                var item = t.Length > 2 ? t[2..].Trim() : "";
                if (item.Length > 0) into.Add(item);
                i = j;
            }
            else { return; }
        }
    }

    private static void AddInlineList(string rest, List<string> into)
    {
        rest = rest.Trim();
        var lb = rest.IndexOf('[');
        var rb = rest.LastIndexOf(']');
        if (lb < 0 || rb < lb) return;
        foreach (var part in rest.Substring(lb + 1, rb - lb - 1).Split(','))
        {
            var item = part.Trim().Trim('"', '\'');
            if (item.Length > 0) into.Add(item);
        }
    }
}

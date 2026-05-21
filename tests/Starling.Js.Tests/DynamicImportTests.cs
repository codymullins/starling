using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Modules;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// wp:M3-03c — dynamic <c>import()</c> + <c>import.meta</c>.
/// <para>
/// <c>import(specifier)</c> is a call-like expression (valid in modules AND
/// classic scripts) that returns a Promise of the imported module's namespace
/// object. A dynamically-imported module with top-level await resolves only
/// after it settles. <c>import.meta</c> exposes the running module's host meta
/// object (at least <c>import.meta.url</c>).
/// </para>
/// </summary>
/// <remarks>
/// Uses the same in-memory <see cref="IModuleHost"/> + <c>report()</c> harness as
/// <c>JsModuleTests</c> / <c>TopLevelAwaitTests</c>. Tests are deterministic:
/// <c>WithActiveVm</c> / <c>LoadAndEvaluate</c> drain the microtask queue, so the
/// chained <c>.then</c> the import promise rides settles before assertions — no
/// sleeps.
/// </remarks>
[TestClass]
public class DynamicImportTests
{
    /// <summary>In-memory module host: identity resolution over a flat map of
    /// specifiers to source text (mirrors <c>JsModuleTests.MapHost</c>).</summary>
    private sealed class MapHost(Dictionary<string, string> modules) : IModuleHost
    {
        public string? Resolve(string specifier, string? referrer) =>
            modules.ContainsKey(specifier) ? specifier : null;

        public string? FetchSource(string resolvedUrl) =>
            modules.TryGetValue(resolvedUrl, out var src) ? src : null;
    }

    /// <summary>Evaluate the entry module and return the single value the entry
    /// passed to <c>report(...)</c>.</summary>
    private static JsValue RunGraph(Dictionary<string, string> modules, string entry)
    {
        JsValue captured = JsValue.Undefined;
        var runtime = new JsRuntime();
        runtime.RegisterGlobal("report", args =>
        {
            captured = args.Length > 0 ? args[0] : JsValue.Undefined;
            return JsValue.Undefined;
        });
        var loader = new ModuleLoader(runtime, new MapHost(modules));
        runtime.WithActiveVm(() => loader.LoadAndEvaluate(entry));
        return captured;
    }

    /// <summary>Evaluate the entry module, collecting every <c>report(...)</c> call
    /// in order so a test can assert ordering (e.g. that a TLA module's body runs
    /// before the import resolution observes its exports).</summary>
    private static List<string> RunGraphLog(Dictionary<string, string> modules, string entry)
    {
        var log = new List<string>();
        var runtime = new JsRuntime();
        runtime.RegisterGlobal("report", args =>
        {
            log.Add(args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined");
            return JsValue.Undefined;
        });
        var loader = new ModuleLoader(runtime, new MapHost(modules));
        runtime.WithActiveVm(() => loader.LoadAndEvaluate(entry));
        return log;
    }

    /// <summary>Run a classic (non-module) script that uses <c>import()</c>. A
    /// loader is registered on the realm (so <c>import()</c> can reach it) but the
    /// top-level code itself is compiled and run as an ordinary script.</summary>
    private static JsValue RunClassicScript(string source, Dictionary<string, string> modules)
    {
        JsValue captured = JsValue.Undefined;
        var runtime = new JsRuntime();
        runtime.RegisterGlobal("report", args =>
        {
            captured = args.Length > 0 ? args[0] : JsValue.Undefined;
            return JsValue.Undefined;
        });
        // Construction registers the loader on the realm; the script never calls
        // LoadAndEvaluate — it's a classic script that triggers import() at runtime.
        _ = new ModuleLoader(runtime, new MapHost(modules));
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        runtime.WithActiveVm(() => new JsVm(runtime).Run(chunk));
        return captured;
    }

    // -----------------------------------------------------------------------
    // import() — namespace resolution
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Dynamic_import_resolves_named_export()
    {
        var modules = new Dictionary<string, string>
        {
            ["m"] = "export const answer = 42;",
            ["main"] = @"
                import('m').then(function(ns) { report(ns.answer); });",
        };
        RunGraph(modules, "main").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Dynamic_import_resolves_default_export()
    {
        var modules = new Dictionary<string, string>
        {
            ["m"] = "export default 7; export const tag = 'x';",
            ["main"] = @"
                import('m').then(function(ns) { report(ns.default + ':' + ns.tag); });",
        };
        RunGraph(modules, "main").AsString.Should().Be("7:x");
    }

    [TestMethod]
    public void Dynamic_import_namespace_is_live()
    {
        // A dynamic import shares the same record/cells as static imports, so a
        // value mutated by the module body is visible through the namespace.
        var modules = new Dictionary<string, string>
        {
            ["m"] = "export let n = 1; export function bump(){ n++; }",
            ["main"] = @"
                import('m').then(function(ns) { ns.bump(); ns.bump(); report(ns.n); });",
        };
        RunGraph(modules, "main").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Dynamic_import_caches_record_returns_same_namespace()
    {
        // Two imports of the same specifier yield the identical namespace object.
        var modules = new Dictionary<string, string>
        {
            ["m"] = "export const v = 5;",
            ["main"] = @"
                Promise.all([import('m'), import('m')]).then(function(both) {
                    report(both[0] === both[1]);
                });",
        };
        RunGraph(modules, "main").AsBool.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // import() of a top-level-await module — settles only after it finishes
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Dynamic_import_of_tla_module_resolves_after_it_settles()
    {
        // The imported module awaits before publishing its export and logs as it
        // runs; the import's .then must observe the settled value AFTER the body.
        var modules = new Dictionary<string, string>
        {
            ["m"] = @"
                report('m:start');
                export const v = await Promise.resolve('ready');
                report('m:done');",
            ["main"] = @"
                report('main:before');
                import('m').then(function(ns) { report('main:got:' + ns.v); });
                report('main:after');",
        };

        var log = RunGraphLog(modules, "main");

        // import('m') synchronously begins loading + linking the graph and runs
        // the imported module body up to its first top-level await — so 'm:start'
        // logs before the entry's next statement ('main:after'). The body then
        // suspends; 'm:done' runs after the awaited promise settles. Crucially the
        // import's .then ('main:got:ready') fires only AFTER the module fully
        // settles ('m:done') — i.e. a dynamic import of a TLA module resolves
        // only once it has settled. The value it observes is the awaited export.
        log.Should().Equal(
            "main:before",
            "m:start",
            "main:after",
            "m:done",
            "main:got:ready");

        // The settled resolution must come strictly after the module finished.
        log.IndexOf("main:got:ready").Should().BeGreaterThan(log.IndexOf("m:done"));
    }

    // -----------------------------------------------------------------------
    // import.meta
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Import_meta_url_returns_module_url()
    {
        var modules = new Dictionary<string, string>
        {
            ["https://example.test/mod.js"] = "report(import.meta.url);",
        };
        RunGraph(modules, "https://example.test/mod.js")
            .AsString.Should().Be("https://example.test/mod.js");
    }

    [TestMethod]
    public void Import_meta_is_object_and_stable_within_a_module()
    {
        var modules = new Dictionary<string, string>
        {
            ["m"] = "report(typeof import.meta === 'object' && import.meta === import.meta);",
        };
        RunGraph(modules, "m").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Import_meta_url_visible_through_dynamic_import()
    {
        var modules = new Dictionary<string, string>
        {
            ["dep"] = "export const myUrl = import.meta.url;",
            ["main"] = @"
                import('dep').then(function(ns) { report(ns.myUrl); });",
        };
        RunGraph(modules, "main").AsString.Should().Be("dep");
    }

    // -----------------------------------------------------------------------
    // failure paths — a bad specifier rejects the promise (no synchronous throw)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Dynamic_import_of_missing_module_rejects()
    {
        var modules = new Dictionary<string, string>
        {
            ["main"] = @"
                import('does-not-exist').then(
                    function() { report('resolved'); },
                    function(err) { report('rejected'); });",
        };
        RunGraph(modules, "main").AsString.Should().Be("rejected");
    }

    [TestMethod]
    public void Dynamic_import_of_module_that_throws_rejects()
    {
        // Evaluation error in the imported module surfaces as a rejection.
        var modules = new Dictionary<string, string>
        {
            ["boom"] = "throw new Error('kaboom');",
            ["main"] = @"
                import('boom').catch(function(e) { report('caught:' + e.message); });",
        };
        RunGraph(modules, "main").AsString.Should().Be("caught:kaboom");
    }

    [TestMethod]
    public void Dynamic_import_failed_tla_module_rejects()
    {
        // A module that rejects its top-level await rejects the import promise.
        var modules = new Dictionary<string, string>
        {
            ["m"] = "await Promise.reject(new Error('async-fail'));",
            ["main"] = @"
                import('m').then(
                    function() { report('resolved'); },
                    function(e) { report('rejected:' + e.message); });",
        };
        RunGraph(modules, "main").AsString.Should().Be("rejected:async-fail");
    }

    // -----------------------------------------------------------------------
    // import() from a classic (non-module) script
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Dynamic_import_works_from_classic_script()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const greeting = 'hello-from-module';",
        };
        var source = @"
            import('lib').then(function(ns) { report(ns.greeting); });";
        RunClassicScript(source, modules).AsString.Should().Be("hello-from-module");
    }

    [TestMethod]
    public void Dynamic_import_from_classic_script_rejects_on_missing()
    {
        var source = @"
            import('nope').catch(function() { report('rejected'); });";
        RunClassicScript(source, new Dictionary<string, string>())
            .AsString.Should().Be("rejected");
    }

    // -----------------------------------------------------------------------
    // regression — static import/export declarations still parse + run
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Static_import_export_still_works_alongside_dynamic_import()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const a = 10;",
            ["lazy"] = "export const b = 20;",
            ["main"] = @"
                import { a } from 'lib';
                import('lazy').then(function(ns) { report(a + ns.b); });",
        };
        RunGraph(modules, "main").AsNumber.Should().Be(30);
    }
}

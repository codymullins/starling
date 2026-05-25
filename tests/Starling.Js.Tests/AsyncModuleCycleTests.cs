using AwesomeAssertions;
using Starling.Js.Modules;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// wp:M3-03d — async-module cycle (SCC) ordering. The top-level-await slice
/// (wp:M3-03b) made acyclic async graphs correct but did not model cyclic async
/// ordering: a back-edge into a module that later turned out async observed its
/// partial bindings instead of waiting for it to settle. This suite covers the
/// SCC-aware fix: a cycle that contains a top-level-await module settles jointly
/// — every member finishes (async members first, so synchronous members read
/// settled exports rather than in-flight bindings) before any module that
/// depends on the cycle observes its exports. Acyclic async graphs, fully
/// synchronous graphs, and purely synchronous cycles keep their existing
/// behavior.
/// </summary>
/// <remarks>
/// Uses the same in-memory <see cref="IModuleHost"/> + <c>report()</c> harness as
/// <c>JsModuleTests</c> / <c>TopLevelAwaitTests</c>. Deterministic:
/// <c>LoadAndEvaluate</c> drives <see cref="MicrotaskQueue.DrainAll"/> internally
/// to quiescence, so the microtask queue is fully pumped with no sleeps.
/// </remarks>
[TestClass]
public class AsyncModuleCycleTests
{
    /// <summary>In-memory module host: identity resolution over a flat map of
    /// specifiers to source text (mirrors <c>TopLevelAwaitTests.MapHost</c>).</summary>
    private sealed class MapHost(Dictionary<string, string> modules) : IModuleHost
    {
        public string? Resolve(string specifier, string? referrer) =>
            modules.ContainsKey(specifier) ? specifier : null;

        public string? FetchSource(string resolvedUrl) =>
            modules.TryGetValue(resolvedUrl, out var src) ? src : null;
    }

    /// <summary>Evaluate the module graph, collecting every <c>report(...)</c> call
    /// in order so a test can assert cross-module evaluation ordering.</summary>
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

    /// <summary>Evaluate the module graph and return the single value the entry
    /// module passed to <c>report(...)</c>.</summary>
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

    // -----------------------------------------------------------------------
    // Async cycles
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Async_cycle_both_members_settle_before_importer_observes_exports()
    {
        // a <-> b cycle; a is async (top-level await). 'main' (outside the cycle)
        // imports both. The whole component must settle — a's await completes and
        // b's body runs — before main's body observes either export. The async
        // member settles first so the synchronous member reads its settled export.
        var modules = new Dictionary<string, string>
        {
            ["a"] = @"
                import { bVal } from 'b';
                report('a:start');
                export const aVal = await Promise.resolve('A');
                report('a:done');",
            ["b"] = @"
                import { aVal } from 'a';
                report('b:start aVal=' + aVal);
                export const bVal = 'B';
                report('b:done');",
            ["main"] = @"
                import { aVal } from 'a';
                import { bVal } from 'b';
                report('main:' + aVal + ',' + bVal);",
        };
        var log = RunGraphLog(modules, "main");

        // a settles first, b reads a's *settled* export (not undefined), then main.
        log.Should().Equal("a:start", "a:done", "b:start aVal=A", "b:done", "main:A,B");
    }

    [TestMethod]
    public void Importer_of_either_cycle_member_waits_for_whole_cycle()
    {
        // Same a<->b cycle, but the importer pulls only from the *synchronous*
        // member b. It must still wait for the entire component (including a's
        // top-level await) to settle before its body runs.
        var modules = new Dictionary<string, string>
        {
            ["a"] = @"
                import { bVal } from 'b';
                report('a:start');
                export const aVal = await Promise.resolve('A');
                report('a:done');",
            ["b"] = @"
                import { aVal } from 'a';
                report('b:run');
                export const bVal = 'B';",
            ["main"] = @"
                import { bVal } from 'b';
                report('main:' + bVal);",
        };
        var log = RunGraphLog(modules, "main");

        log[^1].Should().Be("main:B");
        log.IndexOf("a:done").Should().BeLessThan(log.IndexOf("main:B"));
        log.IndexOf("b:run").Should().BeLessThan(log.IndexOf("main:B"));
        // a's async work settles before the synchronous member b runs.
        log.IndexOf("a:done").Should().BeLessThan(log.IndexOf("b:run"));
    }

    [TestMethod]
    public void Async_members_tla_result_read_by_other_member_after_settlement()
    {
        // The cycle's synchronous member reads the async member's top-level-await
        // RESULT. Because the async member settles before the synchronous member
        // runs, the read sees the post-await value — never a partial binding.
        var modules = new Dictionary<string, string>
        {
            ["a"] = @"
                import { tag } from 'b';
                export const ready = await Promise.resolve('READY');",
            ["b"] = @"
                import { ready } from 'a';
                export const tag = 'b-tag';
                report('observed:' + ready);",
            ["main"] = "import { tag } from 'b'; report('main:' + tag);",
        };
        var log = RunGraphLog(modules, "main");

        log.Should().Contain("observed:READY");
        log.Should().NotContain("observed:undefined");
        log[^1].Should().Be("main:b-tag");
    }

    [TestMethod]
    public void Self_importing_async_module_terminates_and_settles()
    {
        // Trivial cycle: a module imports itself and uses top-level await. It must
        // terminate (no infinite recursion) and settle, binding the awaited value.
        // The self-import aliases the export to a DIFFERENT local name (`selfX`):
        // importing under the same name as a local declaration would be a
        // duplicate-LexicallyDeclaredNames SyntaxError (§16.2.1.6.2).
        var modules = new Dictionary<string, string>
        {
            ["self"] = @"
                import { x as selfX } from 'self';
                report('self:start');
                export const x = await Promise.resolve('X');
                report('self:done x=' + x + ' selfX=' + selfX);",
        };
        var log = RunGraphLog(modules, "self");
        log.Should().Equal("self:start", "self:done x=X selfX=X");
    }

    [TestMethod]
    public void Async_self_import_value_observable_by_outside_importer()
    {
        var modules = new Dictionary<string, string>
        {
            // Self-import aliased to a distinct local name (see note above): the
            // same name would be a duplicate-lexical-name SyntaxError.
            ["self"] = @"
                import { x as selfX } from 'self';
                export const x = await Promise.resolve(99);",
            ["main"] = "import { x } from 'self'; report(x);",
        };
        RunGraph(modules, "main").AsNumber.Should().Be(99);
    }

    // -----------------------------------------------------------------------
    // Error propagation in async cycles
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Tla_rejection_in_cycle_propagates_to_dependents()
    {
        // The async member of a cycle rejects in its top-level await. The whole
        // component errors; the dependent importer's body must NOT run and the
        // graph evaluation surfaces the rejection reason.
        var modules = new Dictionary<string, string>
        {
            ["a"] = @"
                import { bVal } from 'b';
                export const aVal = await Promise.reject('cycle-boom');
                report('a:should-not-run');",
            ["b"] = @"
                import { aVal } from 'a';
                report('b:body');
                export const bVal = 'B';",
            ["main"] = "import { aVal } from 'a'; report('main:should-not-run ' + aVal);",
        };
        var act = () => RunGraph(modules, "main");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsString.Should().Be("cycle-boom");
    }

    [TestMethod]
    public void Tla_rejection_in_cycle_does_not_run_dependent_body()
    {
        var modules = new Dictionary<string, string>
        {
            ["a"] = @"
                import { bVal } from 'b';
                export const aVal = await Promise.reject('cycle-boom');",
            ["b"] = @"
                import { aVal } from 'a';
                export const bVal = 'B';",
            ["main"] = "import { aVal } from 'a'; report('main:ran ' + aVal);",
        };
        var log = new List<string>();
        var runtime = new JsRuntime();
        runtime.RegisterGlobal("report", args =>
        {
            log.Add(args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined");
            return JsValue.Undefined;
        });
        var loader = new ModuleLoader(runtime, new MapHost(modules));
        var act = () => runtime.WithActiveVm(() => loader.LoadAndEvaluate("main"));

        act.Should().Throw<JsThrow>();
        log.Should().NotContain(s => s.StartsWith("main:ran"));
    }

    // -----------------------------------------------------------------------
    // Regressions — preserved behavior
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Synchronous_cycle_const_back_edge_read_is_a_tdz_reference_error()
    {
        // A pure synchronous cycle runs bodies in DFS post-order (deepest member
        // first), so b runs before a. b reads `aVal`, a `const` exported by a that
        // a has not yet initialized — i.e. a read of an imported binding still in
        // its Temporal Dead Zone. Per §16.2.1.6.2 (imported bindings share the
        // source's lexical binding) this is a ReferenceError, NOT a partial
        // `undefined` read. (Previously the engine seeded module cells with
        // `undefined` and observed the partial binding; TDZ seeding now matches
        // the spec — Test262 module-code/instn-* cover the same rule.)
        var modules = new Dictionary<string, string>
        {
            ["a"] = @"
                import { bVal } from 'b';
                report('a:start bVal=' + bVal);
                export const aVal = 'A';
                report('a:done');",
            ["b"] = @"
                import { aVal } from 'a';
                report('b:start aVal=' + aVal);
                export const bVal = 'B';
                report('b:done');",
            ["main"] = @"
                import { aVal } from 'a';
                import { bVal } from 'b';
                report('main:' + aVal + ',' + bVal);",
        };
        var act = () => RunGraphLog(modules, "main");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("ReferenceError");
    }

    [TestMethod]
    public void Regression_fully_synchronous_graph_evaluates_without_promise_round_trip()
    {
        // No top-level await anywhere: synchronous fast path, no async settling.
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const answer = 42;",
            ["main"] = "import { answer } from 'lib'; report(answer);",
        };
        RunGraph(modules, "main").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Regression_acyclic_async_chain_settles_top_down()
    {
        // a (TLA) <- b (no TLA, imports a) <- main (no TLA, imports b). Acyclic;
        // the async settling threads up so main runs only after a's await
        // completes — exactly the wp:M3-03b contract, unchanged.
        var modules = new Dictionary<string, string>
        {
            ["a"] = @"
                report('a:start');
                export const val = await Promise.resolve('v');
                report('a:done');",
            ["b"] = @"
                import { val } from 'a';
                report('b:' + val);
                export const fromB = val + '!';",
            ["main"] = @"
                import { fromB } from 'b';
                report('main:' + fromB);",
        };
        var log = RunGraphLog(modules, "main");
        log.Should().Equal("a:start", "a:done", "b:v", "main:v!");
    }

    [TestMethod]
    public void Regression_two_async_dependencies_still_join_before_dependent()
    {
        // Acyclic diamond-ish: two independent TLA deps, dependent waits for both.
        var modules = new Dictionary<string, string>
        {
            ["a"] = "export const a = await Promise.resolve(1); report('a:done');",
            ["b"] = "export const b = await Promise.resolve(2); report('b:done');",
            ["main"] = @"
                import { a } from 'a';
                import { b } from 'b';
                report('main:' + (a + b));",
        };
        var log = RunGraphLog(modules, "main");
        log.Should().Contain("a:done");
        log.Should().Contain("b:done");
        log[^1].Should().Be("main:3");
        log.IndexOf("a:done").Should().BeLessThan(log.IndexOf("main:3"));
        log.IndexOf("b:done").Should().BeLessThan(log.IndexOf("main:3"));
    }
}

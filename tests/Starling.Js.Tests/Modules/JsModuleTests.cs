using AwesomeAssertions;
using Starling.Js.Modules;
using Starling.Js.Runtime;

namespace Starling.Js.Tests.Modules;

/// <summary>
/// ES2024 §16.2 module compile + load + run coverage: named / default /
/// namespace imports, live bindings, re-exports, and cyclic imports. Modules
/// are supplied through an in-memory <see cref="IModuleHost"/> so the graph is
/// self-contained.
/// </summary>
[TestClass]
public class JsModuleTests
{
    /// <summary>In-memory module host: a flat map of bare specifiers to source.
    /// Resolution is identity (relative resolution is exercised by the engine
    /// file:// tests); fetching reads the map.</summary>
    private sealed class MapHost(Dictionary<string, string> modules) : IModuleHost
    {
        public string? Resolve(string specifier, string? referrer) =>
            modules.ContainsKey(specifier) ? specifier : null;

        public string? FetchSource(string resolvedUrl) =>
            modules.TryGetValue(resolvedUrl, out var src) ? src : null;
    }

    /// <summary>Run the entry module and return the value of a global the entry
    /// module wrote, so tests can observe cross-module results.</summary>
    private static JsValue RunGraph(Dictionary<string, string> modules, string entry, string resultGlobal)
    {
        var runtime = new JsRuntime();
        JsValue captured = JsValue.Undefined;
        runtime.RegisterGlobal("report", args =>
        {
            captured = args.Length > 0 ? args[0] : JsValue.Undefined;
            return JsValue.Undefined;
        });
        var loader = new ModuleLoader(runtime, new MapHost(modules));
        runtime.WithActiveVm(() => loader.LoadAndEvaluate(entry));
        // resultGlobal lets a test alternatively read a global the entry set.
        return resultGlobal.Length == 0 ? captured : runtime.GetGlobal(resultGlobal);
    }

    [TestMethod]
    public void Named_export_and_import_across_modules()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const answer = 42; export function greet(){ return 'hi'; }",
            ["main"] = "import { answer, greet } from 'lib'; report(answer + ':' + greet());",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("42:hi");
    }

    [TestMethod]
    public void Default_export_and_import()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export default function(x){ return x * 2; }",
            ["main"] = "import dbl from 'lib'; report(dbl(21));",
        };
        RunGraph(modules, "main", "").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Default_and_named_combined()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const name = 'lib'; export default 7;",
            ["main"] = "import val, { name } from 'lib'; report(name + '=' + val);",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("lib=7");
    }

    [TestMethod]
    public void Namespace_import_exposes_all_exports()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const a = 1; export const b = 2;",
            ["main"] = "import * as ns from 'lib'; report(ns.a + ns.b);",
        };
        RunGraph(modules, "main", "").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Live_binding_reflects_exporter_mutation()
    {
        // count starts at 0; tick() increments the module-local binding. The
        // importer must observe the post-mutation value (live binding), not a
        // snapshot taken at import time.
        var modules = new Dictionary<string, string>
        {
            ["counter"] = "export let count = 0; export function tick(){ count = count + 1; }",
            ["main"] = "import { count, tick } from 'counter'; tick(); tick(); report(count);",
        };
        RunGraph(modules, "main", "").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Indirect_reexport_forwards_named_binding()
    {
        var modules = new Dictionary<string, string>
        {
            ["base"] = "export const value = 99;",
            ["mid"] = "export { value } from 'base';",
            ["main"] = "import { value } from 'mid'; report(value);",
        };
        RunGraph(modules, "main", "").AsNumber.Should().Be(99);
    }

    [TestMethod]
    public void Star_reexport_forwards_all_names()
    {
        var modules = new Dictionary<string, string>
        {
            ["base"] = "export const x = 5; export const y = 6;",
            ["mid"] = "export * from 'base';",
            ["main"] = "import { x, y } from 'mid'; report(x + y);",
        };
        RunGraph(modules, "main", "").AsNumber.Should().Be(11);
    }

    [TestMethod]
    public void Cyclic_import_evaluates_without_deadlock()
    {
        // a imports from b and b imports from a. Per spec, the function
        // declarations are hoisted/instantiated so the cross-references resolve
        // even though evaluation order is a→b (a is entry). a calls b.fromB()
        // which calls a.fromA(); the live bindings make both visible.
        var modules = new Dictionary<string, string>
        {
            ["a"] = "import { fromB } from 'b'; export function fromA(){ return 'A'; } report(fromB());",
            ["b"] = "import { fromA } from 'a'; export function fromB(){ return 'B+' + fromA(); }",
        };
        RunGraph(modules, "a", "").AsString.Should().Be("B+A");
    }

    [TestMethod]
    public void Side_effect_import_runs_dependency_once()
    {
        var modules = new Dictionary<string, string>
        {
            ["dep"] = "globalThis.sideEffect = (globalThis.sideEffect || 0) + 1;",
            ["main"] = "import 'dep'; import 'dep'; report(globalThis.sideEffect);",
        };
        RunGraph(modules, "main", "").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Imported_class_default_export()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export default class { hello(){ return 'world'; } }",
            ["main"] = "import C from 'lib'; const c = new C(); report(c.hello());",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("world");
    }
}

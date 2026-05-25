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

    // =====================================================================
    // §10.4.6 Module Namespace Exotic Object behaviour.
    // =====================================================================

    [TestMethod]
    public void Namespace_toStringTag_is_Module()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const a = 1;",
            ["main"] = "import * as ns from 'lib'; " +
                       "report(ns[Symbol.toStringTag] + '/' + Object.prototype.toString.call(ns));",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("Module/[object Module]");
    }

    [TestMethod]
    public void Namespace_is_not_extensible()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const a = 1;",
            ["main"] = "import * as ns from 'lib'; report(Object.isExtensible(ns));",
        };
        RunGraph(modules, "main", "").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Namespace_prototype_is_null()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const a = 1;",
            ["main"] = "import * as ns from 'lib'; report(Object.getPrototypeOf(ns) === null);",
        };
        RunGraph(modules, "main", "").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Namespace_keys_are_sorted_exports_without_toStringTag()
    {
        // Declared out of code-unit order; [[OwnPropertyKeys]] must sort them.
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const c = 3; export const a = 1; export const b = 2;",
            ["main"] = "import * as ns from 'lib'; report(Object.keys(ns).join(','));",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("a,b,c");
    }

    [TestMethod]
    public void Namespace_ownPropertyNames_sorted_then_default_excludes_symbol()
    {
        // getOwnPropertyNames returns only string keys (sorted); the
        // @@toStringTag symbol is not among them.
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const z = 1; export const a = 2;",
            ["main"] = "import * as ns from 'lib'; report(Object.getOwnPropertyNames(ns).join(','));",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("a,z");
    }

    [TestMethod]
    public void Namespace_reflect_own_keys_is_sorted_strings_then_toStringTag()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const b = 1; export const a = 2;",
            ["main"] = "import * as ns from 'lib'; const ks = Reflect.ownKeys(ns); " +
                       "report(ks.length + '|' + ks[0] + '|' + ks[1] + '|' + (ks[2] === Symbol.toStringTag));",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("3|a|b|true");
    }

    [TestMethod]
    public void Namespace_getOwnPropertySymbols_is_toStringTag_only()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const a = 1;",
            ["main"] = "import * as ns from 'lib'; const syms = Object.getOwnPropertySymbols(ns); " +
                       "report(syms.length + '|' + (syms[0] === Symbol.toStringTag));",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("1|true");
    }

    [TestMethod]
    public void Namespace_live_binding_value_updates_through_namespace()
    {
        // The namespace must reflect a later mutation of the exporting binding.
        var modules = new Dictionary<string, string>
        {
            ["counter"] = "export let count = 0; export function tick(){ count = count + 1; }",
            ["main"] = "import * as ns from 'counter'; const before = ns.count; " +
                       "ns.tick(); ns.tick(); report(before + '->' + ns.count);",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("0->2");
    }

    [TestMethod]
    public void Namespace_get_own_property_descriptor_is_live_data_descriptor()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const a = 42;",
            ["main"] = "import * as ns from 'lib'; " +
                       "const d = Object.getOwnPropertyDescriptor(ns, 'a'); " +
                       "report(d.value + '/' + d.writable + '/' + d.enumerable + '/' + d.configurable);",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("42/true/true/false");
    }

    [TestMethod]
    public void Namespace_get_missing_export_is_undefined()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const a = 1;",
            ["main"] = "import * as ns from 'lib'; report(typeof ns.nope);",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("undefined");
    }

    [TestMethod]
    public void Namespace_has_reports_exports_and_toStringTag_only()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const a = 1;",
            ["main"] = "import * as ns from 'lib'; " +
                       "report(('a' in ns) + ',' + ('b' in ns) + ',' + (Symbol.toStringTag in ns) + ',' + (Symbol.iterator in ns));",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("true,false,true,false");
    }

    [TestMethod]
    public void Namespace_strict_assignment_throws()
    {
        // Module code is always strict, so [[Set]] returning false throws.
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const a = 1;",
            ["main"] = "import * as ns from 'lib'; " +
                       "let threw = false; try { ns.a = 5; } catch (e) { threw = e instanceof TypeError; } " +
                       "report(threw + '/' + ns.a);",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("true/1");
    }

    [TestMethod]
    public void Namespace_defineProperty_throws_on_change()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const a = 1;",
            ["main"] = "import * as ns from 'lib'; " +
                       "let threw = false; " +
                       "try { Object.defineProperty(ns, 'a', { value: 9 }); } catch (e) { threw = e instanceof TypeError; } " +
                       "report(threw + '/' + ns.a);",
        };
        RunGraph(modules, "main", "").AsString.Should().Be("true/1");
    }

    [TestMethod]
    public void Namespace_defineProperty_succeeds_on_exact_match()
    {
        // Redefining with the exact current descriptor is allowed (returns true).
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const a = 1;",
            ["main"] = "import * as ns from 'lib'; " +
                       "let ok = true; " +
                       "try { Object.defineProperty(ns, 'a', " +
                       "  { value: 1, writable: true, enumerable: true, configurable: false }); } " +
                       "catch (e) { ok = false; } report(ok);",
        };
        RunGraph(modules, "main", "").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Namespace_delete_existing_export_fails_unknown_succeeds()
    {
        // delete returns false for an existing export (and @@toStringTag) and
        // true for any other key. In sloppy-eval'd report we just observe the
        // results; the delete on the export is a strict-mode TypeError, so we
        // probe via the boolean result inside a try.
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const a = 1;",
            ["main"] = "import * as ns from 'lib'; " +
                       "let delA = false; try { delA = delete ns.a; } catch (e) { delA = 'threw'; } " +
                       "const delB = delete ns.nope; " +
                       "report(delA + '/' + delB);",
        };
        // `delete ns.a` in strict module code: the binding exists and is
        // non-configurable, so [[Delete]] returns false → strict delete throws.
        RunGraph(modules, "main", "").AsString.Should().Be("threw/true");
    }
}

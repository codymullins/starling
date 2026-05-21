using AwesomeAssertions;
using Starling.Js.Modules;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// wp:M3-03a — destructuring binding patterns (<c>const { a, b } = obj</c>,
/// <c>let [x, ...rest] = arr</c>, nested + default + rest) in const/let/var
/// declarations at ES2024 §16.2 module top level. The extracted names must bind
/// as live module bindings (upvalue cells), exactly like a single-identifier
/// <c>const name = …</c>, so they read correctly in the declaring module and
/// resolve from importers when re-exported.
/// </summary>
[TestClass]
public class ModuleDestructuringTests
{
    /// <summary>In-memory module host: a flat map of bare specifiers to source.
    /// Resolution is identity; fetching reads the map.</summary>
    private sealed class MapHost(Dictionary<string, string> modules) : IModuleHost
    {
        public string? Resolve(string specifier, string? referrer) =>
            modules.ContainsKey(specifier) ? specifier : null;

        public string? FetchSource(string resolvedUrl) =>
            modules.TryGetValue(resolvedUrl, out var src) ? src : null;
    }

    /// <summary>Run the entry module and return the value passed to
    /// <c>report(...)</c> by any module in the graph.</summary>
    private static JsValue RunGraph(Dictionary<string, string> modules, string entry)
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
        return captured;
    }

    /// <summary>Convenience for a single-module graph that calls report().</summary>
    private static JsValue Run(string source) =>
        RunGraph(new Dictionary<string, string> { ["main"] = source }, "main");

    [TestMethod]
    public void Object_pattern_binds_each_name()
    {
        Run("const obj = { a: 1, b: 2 }; const { a, b } = obj; report(a + ':' + b);")
            .AsString.Should().Be("1:2");
    }

    [TestMethod]
    public void Object_pattern_with_rename()
    {
        Run("const { a: first, b: second } = { a: 10, b: 20 }; report(first + second);")
            .AsNumber.Should().Be(30);
    }

    [TestMethod]
    public void Array_pattern_with_elision()
    {
        Run("const arr = [1, 2, 3]; const [x, , z] = arr; report(x + ':' + z);")
            .AsString.Should().Be("1:3");
    }

    [TestMethod]
    public void Array_pattern_with_rest()
    {
        Run("const [head, ...tail] = [1, 2, 3, 4]; report(head + ':' + tail.join(','));")
            .AsString.Should().Be("1:2,3,4");
    }

    [TestMethod]
    public void Object_pattern_with_rest()
    {
        Run("const { x, ...rest } = { x: 1, y: 2, z: 3 }; report(x + ':' + rest.y + ':' + rest.z);")
            .AsString.Should().Be("1:2:3");
    }

    [TestMethod]
    public void Nested_pattern_with_default_present()
    {
        Run("const obj = { a: { b: 7 } }; const { a: { b = 5 } = {} } = obj; report(b);")
            .AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Nested_pattern_with_default_applied()
    {
        // a is absent → the `= {}` default for the inner pattern fires, then
        // b is absent inside it → the `= 5` default fires.
        Run("const obj = {}; const { a: { b = 5 } = {} } = obj; report(b);")
            .AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Top_level_default_applies_when_missing()
    {
        Run("const { missing = 42 } = {}; report(missing);")
            .AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Let_destructuring_binding_is_mutable()
    {
        Run("let { a } = { a: 1 }; a = a + 9; report(a);")
            .AsNumber.Should().Be(10);
    }

    [TestMethod]
    public void Var_destructuring_binding()
    {
        Run("var [p, q] = [3, 4]; report(p * q);")
            .AsNumber.Should().Be(12);
    }

    [TestMethod]
    public void Mixed_nested_array_in_object()
    {
        Run("const { items: [first, second] } = { items: [8, 9] }; report(first + second);")
            .AsNumber.Should().Be(17);
    }

    [TestMethod]
    public void Exported_object_pattern_resolves_from_importer()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const { a, b } = { a: 1, b: 2 };",
            ["main"] = "import { a, b } from 'lib'; report(a + ':' + b);",
        };
        RunGraph(modules, "main").AsString.Should().Be("1:2");
    }

    [TestMethod]
    public void Exported_array_pattern_with_rest_resolves_from_importer()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const [head, ...tail] = [1, 2, 3];",
            ["main"] = "import { head, tail } from 'lib'; report(head + ':' + tail.join(','));",
        };
        RunGraph(modules, "main").AsString.Should().Be("1:2,3");
    }

    [TestMethod]
    public void Exported_destructured_names_via_namespace()
    {
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const { a, b } = { a: 5, b: 6 };",
            ["main"] = "import * as ns from 'lib'; report(ns.a + ns.b);",
        };
        RunGraph(modules, "main").AsNumber.Should().Be(11);
    }

    [TestMethod]
    public void Exported_destructured_binding_is_live()
    {
        // The destructured `count` is a live binding: tick() mutates it and the
        // importer observes the post-mutation value.
        var modules = new Dictionary<string, string>
        {
            ["counter"] =
                "export let { count } = { count: 0 };" +
                " export function tick(){ count = count + 1; }",
            ["main"] = "import { count, tick } from 'counter'; tick(); tick(); report(count);",
        };
        RunGraph(modules, "main").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Destructuring_does_not_clobber_simple_module_bindings()
    {
        // Regression: a simple `const name = …` next to a destructuring decl
        // both bind correctly through their own upvalue cells.
        Run("const simple = 100; const { a } = { a: 1 }; report(simple + a);")
            .AsNumber.Should().Be(101);
    }

    [TestMethod]
    public void Simple_const_module_binding_still_works()
    {
        // Regression for the single-identifier path the destructuring change
        // sits beside.
        Run("const x = 7; report(x);").AsNumber.Should().Be(7);
    }
}

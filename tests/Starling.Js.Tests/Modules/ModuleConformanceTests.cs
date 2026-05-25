using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Modules;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests.Modules;

/// <summary>
/// wp:M3-70 — module-code conformance: top-level await on the module-goal parse
/// path, §16.2.1.6.2 module early errors, and import-binding semantics
/// (immutability + TDZ). These exercise the same paths the Test262
/// <c>language/module-code</c> cluster does, but as focused in-memory unit tests.
/// </summary>
[TestClass]
public class ModuleConformanceTests
{
    /// <summary>In-memory module host (identity resolution over a flat map),
    /// mirroring <see cref="JsModuleTests"/>'s harness.</summary>
    private sealed class MapHost(Dictionary<string, string> modules) : IModuleHost
    {
        public string? Resolve(string specifier, string? referrer) =>
            modules.ContainsKey(specifier) ? specifier : null;

        public string? FetchSource(string resolvedUrl) =>
            modules.TryGetValue(resolvedUrl, out var src) ? src : null;
    }

    /// <summary>Evaluate the entry module and return the single value passed to
    /// the last <c>report(...)</c> call.</summary>
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

    private static JsValue RunSingle(string source) =>
        RunGraph(new Dictionary<string, string> { ["main"] = source }, "main");

    /// <summary>Parse + compile a module the way the loader does — under the
    /// Module goal (<see cref="JsParser.ParseModule"/>) then
    /// <see cref="JsCompiler.CompileModule"/> — so an early SyntaxError surfaces.</summary>
    private static void ParseAndCompileModule(string source) =>
        JsCompiler.CompileModule(new JsParser(source).ParseModule(), "<test>");

    // -----------------------------------------------------------------------
    // 1) Top-level await on the module-goal parse path.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Module_top_level_await_parses_and_runs()
    {
        // A bare top-level `await` (no enclosing async function) is valid module
        // syntax and evaluates: the awaited value binds for a later statement.
        RunSingle("const v = await Promise.resolve(7); report(v);")
            .AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Top_level_await_in_class_heritage_marks_module_async()
    {
        // The `extends` heritage clause is evaluated in the module's top-level
        // [+Await] context, so a top-level await there is valid and the class
        // builds against the awaited base.
        var modules = new Dictionary<string, string>
        {
            ["main"] = @"
                const Base = await Promise.resolve(class { tag() { return 'base'; } });
                class C extends Base {}
                report(new C().tag());",
        };
        RunGraph(modules, "main").AsString.Should().Be("base");
    }

    [TestMethod]
    public void Await_in_a_nested_non_async_function_in_a_module_is_a_syntax_error()
    {
        // `await` does not propagate into a nested non-async function body: it is
        // reserved in module code, so `await 0` there is an early SyntaxError.
        var act = () => ParseAndCompileModule("function f() { await 0; }");
        act.Should().Throw<JsParseException>();
    }

    [TestMethod]
    public void Top_level_await_is_rejected_in_a_classic_script()
    {
        // The classic-script goal (ParseProgram) does not enable top-level await
        // as an async context, so the body runs as ordinary (non-async) code and
        // the VM rejects the suspend with a SyntaxError at runtime.
        var runtime = new JsRuntime();
        var vm = new JsVm(runtime);
        var act = () =>
        {
            var chunk = JsCompiler.Compile(
                new JsParser("await Promise.resolve(1);").ParseProgram(), "<script>");
            vm.Run(chunk);
        };
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("SyntaxError");
    }

    // -----------------------------------------------------------------------
    // 2) Module early errors (§16.2.1.6.2).
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Duplicate_export_name_is_a_syntax_error()
    {
        var act = () => ParseAndCompileModule("var x; export { x }; export { x };");
        act.Should().Throw<JsParseException>();
    }

    [TestMethod]
    public void Exporting_an_undeclared_binding_is_a_syntax_error()
    {
        var act = () => ParseAndCompileModule("export { undeclared };");
        act.Should().Throw<JsParseException>();
    }

    [TestMethod]
    public void Duplicate_top_level_function_is_a_syntax_error_in_a_module()
    {
        // At the Module top level a FunctionDeclaration is a LexicallyDeclaredName,
        // so two same-named top-level functions collide (unlike a classic script).
        var act = () => ParseAndCompileModule("function x() {} function x() {}");
        act.Should().Throw<JsParseException>();
    }

    [TestMethod]
    public void Escaped_from_keyword_in_import_is_a_syntax_error()
    {
        var act = () => ParseAndCompileModule("import a fr\\u006fm 'x';");
        act.Should().Throw<JsParseException>();
    }

    [TestMethod]
    public void Importing_eval_as_a_binding_is_a_syntax_error()
    {
        // Module code is strict: `eval`/`arguments` may not be a binding name.
        var act = () => ParseAndCompileModule("import { x as eval } from 'm';");
        act.Should().Throw<JsParseException>();
    }

    [TestMethod]
    public void Top_level_return_is_a_syntax_error_in_a_module()
    {
        var act = () => ParseAndCompileModule("return;");
        act.Should().Throw<JsParseException>();
    }

    // -----------------------------------------------------------------------
    // 3) Import-binding semantics: immutability + TDZ.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Assigning_to_an_imported_binding_throws_TypeError()
    {
        // §16.2.1.6.2 — imported bindings are immutable; the assignment runs
        // inside a function so it is a runtime TypeError (not a static error).
        var modules = new Dictionary<string, string>
        {
            ["lib"] = "export const value = 1;",
            ["main"] = @"
                import { value } from 'lib';
                let threw = '';
                try { (function(){ value = 2; })(); }
                catch (e) { threw = e.name; }
                report(threw);",
        };
        RunGraph(modules, "main").AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Assigning_to_a_module_const_throws_TypeError()
    {
        var source = @"
            const c = 1;
            let threw = '';
            try { (function(){ c = 2; })(); } catch (e) { threw = e.name; }
            report(threw);";
        RunSingle(source).AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Importing_a_binding_in_its_TDZ_throws_ReferenceError()
    {
        // 'main' reads the imported binding before the exporting module has
        // initialized it (the dependency reads back into the importer during its
        // own evaluation, observing the not-yet-initialized lexical binding).
        var modules = new Dictionary<string, string>
        {
            // dep, evaluated first, reaches back into main for `late` before main's
            // body has initialized it — a read of an imported binding in its TDZ.
            ["dep"] = @"
                import { late } from 'main';
                export function probe() { return late; }
                export let threw = '';
                try { probe(); } catch (e) { threw = e.name; }",
            ["main"] = @"
                import { threw } from 'dep';
                export let late = 'ready';
                report(threw);",
        };
        RunGraph(modules, "main").AsString.Should().Be("ReferenceError");
    }

    [TestMethod]
    public void Local_lexical_binding_is_in_TDZ_before_its_declaration()
    {
        // A module-top `const`/`let` is in the Temporal Dead Zone until its
        // initializer runs: a read before the declaration throws ReferenceError.
        var source = @"
            let threw = '';
            try { x; } catch (e) { threw = e.name; }
            const x = 1;
            report(threw + ':' + x);";
        RunSingle(source).AsString.Should().Be("ReferenceError:1");
    }

    [TestMethod]
    public void Live_binding_update_is_observed_through_an_import()
    {
        // Regression: after the immutability/TDZ changes a mutable (`let`) export
        // is still a live binding — an importer sees the exporter's later update.
        var modules = new Dictionary<string, string>
        {
            ["lib"] = @"
                export let n = 1;
                export function bump() { n = n + 1; }",
            ["main"] = @"
                import { n, bump } from 'lib';
                bump();
                bump();
                report(n);",
        };
        RunGraph(modules, "main").AsNumber.Should().Be(3);
    }
}

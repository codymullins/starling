using FluentAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Bindings.Tests;

/// <summary>
/// B5-4 — MutationObserver JS surface tests. The DOM-side mutation hook is
/// not yet wired (see MutationObserverBinding's file-level TODO), so these
/// tests only assert the constructable / observable / disconnectable shape.
/// They do NOT assert that mutation records actually fire.
/// </summary>
[TestClass]
public sealed class MutationObserverTests
{
    [TestMethod]
    public void Constructor_is_installed_globally_with_correct_name()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = typeof MutationObserver;")
            .AsString.Should().Be("function");
        Eval(runtime, "result = MutationObserver.name;")
            .AsString.Should().Be("MutationObserver");
    }

    [TestMethod]
    public void Constructor_requires_callable_argument()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            try { new MutationObserver(); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");

        Eval(runtime, """
            try { new MutationObserver(123); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Constructor_accepts_callback_and_instance_has_correct_constructor_name()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            result = o.constructor.name;
        """).AsString.Should().Be("MutationObserver");
    }

    [TestMethod]
    public void Observe_with_empty_options_throws_type_error()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            try { o.observe(document.body, {}); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Observe_with_child_list_true_succeeds()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            o.observe(document.body, { childList: true });
            result = 'ok';
        """).AsString.Should().Be("ok");
    }

    [TestMethod]
    public void Observe_with_attribute_filter_validates_array()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            o.observe(document.body, { attributes: true, attributeFilter: ['class', 'id'] });
            result = 'ok';
        """).AsString.Should().Be("ok");
    }

    [TestMethod]
    public void Observe_requires_node_target()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            try { o.observe({}, { childList: true }); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Disconnect_is_callable_and_idempotent()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            o.observe(document.body, { childList: true });
            o.disconnect();
            o.disconnect();
            result = 'ok';
        """).AsString.Should().Be("ok");
    }

    [TestMethod]
    public void Take_records_returns_array_and_is_empty_on_fresh_observer()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var o = new MutationObserver(function () {});
            var r = o.takeRecords();
            result = Array.isArray(r) && r.length === 0 ? 'ok' : 'fail';
        """).AsString.Should().Be("ok");
    }

    private static (JsRuntime, Document) BuildEnv()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc);
        return (runtime, doc);
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}

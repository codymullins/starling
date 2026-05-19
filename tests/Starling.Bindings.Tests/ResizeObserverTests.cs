using FluentAssertions;
using Tessera.Dom;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Bindings.Tests;

/// <summary>
/// B5-4 — ResizeObserver JS surface tests. Layout integration is not yet
/// wired (see ResizeObserverBinding's file-level TODO); these tests assert
/// only the constructable / observable / disconnectable shape.
/// </summary>
public sealed class ResizeObserverTests
{
    [Fact]
    public void Constructor_is_installed_globally_with_correct_name()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = typeof ResizeObserver;")
            .AsString.Should().Be("function");
        Eval(runtime, "result = ResizeObserver.name;")
            .AsString.Should().Be("ResizeObserver");
    }

    [Fact]
    public void Constructor_requires_callable_argument()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            try { new ResizeObserver(); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");

        Eval(runtime, """
            try { new ResizeObserver(123); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");
    }

    [Fact]
    public void Constructor_accepts_callback_and_instance_has_correct_constructor_name()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var ro = new ResizeObserver(function () {});
            result = ro.constructor.name;
        """).AsString.Should().Be("ResizeObserver");
    }

    [Fact]
    public void Observe_requires_element_target()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var ro = new ResizeObserver(function () {});
            try { ro.observe({}); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");
    }

    [Fact]
    public void Observe_with_valid_box_option_succeeds()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var ro = new ResizeObserver(function () {});
            var el = document.createElement('div');
            document.body.appendChild(el);
            ro.observe(el, { box: 'border-box' });
            ro.observe(el, { box: 'content-box' });
            ro.observe(el, { box: 'device-pixel-content-box' });
            result = 'ok';
        """).AsString.Should().Be("ok");
    }

    [Fact]
    public void Observe_with_invalid_box_option_throws()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var ro = new ResizeObserver(function () {});
            var el = document.createElement('div');
            document.body.appendChild(el);
            try { ro.observe(el, { box: 'bogus' }); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");
    }

    [Fact]
    public void Unobserve_and_disconnect_are_idempotent()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var ro = new ResizeObserver(function () {});
            var el = document.createElement('div');
            document.body.appendChild(el);
            ro.observe(el);
            ro.unobserve(el);
            ro.unobserve(el);
            ro.disconnect();
            ro.disconnect();
            result = 'ok';
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

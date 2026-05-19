using FluentAssertions;
using Tessera.Dom;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Bindings.Tests;

/// <summary>
/// B5-4 — IntersectionObserver JS surface tests. Layout integration is not
/// wired (see IntersectionObserverBinding's file-level TODO); these tests
/// assert only the constructable / observable / disconnectable shape.
/// </summary>
public sealed class IntersectionObserverTests
{
    [Fact]
    public void Constructor_is_installed_globally_with_correct_name()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = typeof IntersectionObserver;")
            .AsString.Should().Be("function");
        Eval(runtime, "result = IntersectionObserver.name;")
            .AsString.Should().Be("IntersectionObserver");
    }

    [Fact]
    public void Constructor_requires_callable_argument()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            try { new IntersectionObserver(); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");

        Eval(runtime, """
            try { new IntersectionObserver(123); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");
    }

    [Fact]
    public void Constructor_accepts_callback_and_default_options()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var io = new IntersectionObserver(function () {});
            result = io.constructor.name + '/' + io.rootMargin + '/' + io.thresholds.length;
        """).AsString.Should().Be("IntersectionObserver/0px 0px 0px 0px/1");
    }

    [Fact]
    public void Threshold_out_of_range_throws_range_error()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            try { new IntersectionObserver(function () {}, { threshold: 1.5 }); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("RangeError");
    }

    [Fact]
    public void Observe_requires_element_target()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var io = new IntersectionObserver(function () {});
            try { io.observe({}); result = 'no-throw'; }
            catch (e) { result = (e && e.constructor && e.constructor.name) || 'other'; }
        """).AsString.Should().Be("TypeError");
    }

    [Fact]
    public void Observe_and_unobserve_succeed()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var io = new IntersectionObserver(function () {});
            var el = document.createElement('div');
            document.body.appendChild(el);
            io.observe(el);
            io.unobserve(el);
            io.disconnect();
            io.disconnect();
            result = 'ok';
        """).AsString.Should().Be("ok");
    }

    [Fact]
    public void Take_records_returns_empty_array()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var io = new IntersectionObserver(function () {});
            var r = io.takeRecords();
            result = Array.isArray(r) && r.length === 0 ? 'ok' : 'fail';
        """).AsString.Should().Be("ok");
    }

    [Fact]
    public void Threshold_array_is_parsed_and_exposed()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var io = new IntersectionObserver(function () {}, { threshold: [0, 0.5, 1] });
            result = io.thresholds.length + ':' + io.thresholds[1];
        """).AsString.Should().Be("3:0.5");
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

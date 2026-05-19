using FluentAssertions;
using Tessera.Dom;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Bindings.Tests;

public sealed class HistoryTests
{
    [Fact]
    public void History_starts_with_one_entry_and_null_state()
    {
        var (runtime, _) = BuildEnv("https://example.com/start");
        Eval(runtime, "result = history.length;").AsNumber.Should().Be(1);
        Eval(runtime, "result = history.state;").IsNull.Should().BeTrue();
    }

    [Fact]
    public void PushState_appends_entry_and_updates_state()
    {
        var (runtime, _) = BuildEnv("https://example.com/start");
        Eval(runtime, "history.pushState({ a: 1 }, '', '/next'); result = history.length;")
            .AsNumber.Should().Be(2);
        Eval(runtime, "result = history.state.a;").AsNumber.Should().Be(1);
    }

    [Fact]
    public void PushState_updates_location_href()
    {
        var (runtime, _) = BuildEnv("https://example.com/start");
        Eval(runtime, "history.pushState(null, '', '/foo?q=1'); result = window.location.href;")
            .AsString.Should().Be("https://example.com/foo?q=1");
        Eval(runtime, "result = window.location.pathname;").AsString.Should().Be("/foo");
        Eval(runtime, "result = window.location.search;").AsString.Should().Be("?q=1");
    }

    [Fact]
    public void ReplaceState_does_not_change_length()
    {
        var (runtime, _) = BuildEnv("https://example.com/start");
        Eval(runtime, "history.replaceState({ x: 9 }, '', '/replaced'); result = history.length;")
            .AsNumber.Should().Be(1);
        Eval(runtime, "result = history.state.x;").AsNumber.Should().Be(9);
        Eval(runtime, "result = window.location.pathname;").AsString.Should().Be("/replaced");
    }

    [Fact]
    public void Back_returns_to_previous_state_and_fires_popstate()
    {
        var (runtime, _) = BuildEnv("https://example.com/a");
        Eval(runtime, """
            var captured = null;
            window.addEventListener('popstate', function (e) { captured = e.state; });
            history.pushState({ tag: 'second' }, '', '/b');
            history.back();
            result = captured && captured === null ? 'null' : (captured ? 'has' : 'missing');
        """);
        // After back(): currentState is the initial null entry, popstate sees null.
        runtime.GetGlobal("captured").IsNull.Should().BeTrue();
        Eval(runtime, "result = window.location.pathname;").AsString.Should().Be("/a");
        Eval(runtime, "result = history.length;").AsNumber.Should().Be(2);
    }

    [Fact]
    public void Forward_after_back_restores_pushed_state()
    {
        var (runtime, _) = BuildEnv("https://example.com/a");
        Eval(runtime, """
            history.pushState({ n: 7 }, '', '/b');
            history.back();
            history.forward();
            result = history.state.n;
        """).AsNumber.Should().Be(7);
        Eval(runtime, "result = window.location.pathname;").AsString.Should().Be("/b");
    }

    [Fact]
    public void PushState_after_back_truncates_forward_entries()
    {
        var (runtime, _) = BuildEnv("https://example.com/a");
        Eval(runtime, """
            history.pushState(null, '', '/b');
            history.pushState(null, '', '/c');
            history.back();
            history.pushState(null, '', '/d');
            result = history.length;
        """).AsNumber.Should().Be(3);
        Eval(runtime, "result = window.location.pathname;").AsString.Should().Be("/d");
    }

    [Fact]
    public void Go_with_negative_delta_traverses_multiple_entries()
    {
        var (runtime, _) = BuildEnv("https://example.com/a");
        Eval(runtime, """
            history.pushState(null, '', '/b');
            history.pushState(null, '', '/c');
            history.go(-2);
            result = window.location.pathname;
        """).AsString.Should().Be("/a");
    }

    [Fact]
    public void Back_at_first_entry_is_noop()
    {
        var (runtime, _) = BuildEnv("https://example.com/a");
        Eval(runtime, """
            var fired = 0;
            window.addEventListener('popstate', function () { fired = fired + 1; });
            history.back();
            result = fired;
        """).AsNumber.Should().Be(0);
    }

    [Fact]
    public void Fragment_only_url_resolves_against_current()
    {
        var (runtime, _) = BuildEnv("https://example.com/page");
        Eval(runtime, "history.pushState(null, '', '#section'); result = window.location.href;")
            .AsString.Should().Be("https://example.com/page#section");
        Eval(runtime, "result = window.location.hash;").AsString.Should().Be("#section");
    }

    [Fact]
    public void ScrollRestoration_round_trips_only_known_values()
    {
        var (runtime, _) = BuildEnv("https://example.com/");
        Eval(runtime, "result = history.scrollRestoration;").AsString.Should().Be("auto");
        Eval(runtime, "history.scrollRestoration = 'manual'; result = history.scrollRestoration;")
            .AsString.Should().Be("manual");
        Eval(runtime, "history.scrollRestoration = 'bogus'; result = history.scrollRestoration;")
            .AsString.Should().Be("manual");
    }

    private static (JsRuntime, Document) BuildEnv(string url)
    {
        var doc = BuildDocument();
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions(DocumentUrl: url));
        return (runtime, doc);
    }

    private static Document BuildDocument()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        return doc;
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}

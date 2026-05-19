using FluentAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Net.Http.Cookies;
namespace Starling.Bindings.Tests;

[TestClass]
public sealed class CookieTests
{
    [TestMethod]
    public void Document_cookie_is_empty_with_no_jar()
    {
        var runtime = BuildEnvWithoutJar("https://example.com/");
        Eval(runtime, "result = document.cookie;").AsString.Should().Be("");
        // Setter is a silent no-op when no jar is wired.
        Eval(runtime, "document.cookie = 'x=1'; result = document.cookie;")
            .AsString.Should().Be("");
    }

    [TestMethod]
    public void Setter_stores_then_getter_serializes()
    {
        var (runtime, _, _) = BuildEnv("https://example.com/");
        Eval(runtime, "document.cookie = 'session=abc; Path=/'; result = document.cookie;")
            .AsString.Should().Be("session=abc");
    }

    [TestMethod]
    public void Multiple_cookies_join_with_semicolon()
    {
        var (runtime, _, _) = BuildEnv("https://example.com/");
        Eval(runtime, """
            document.cookie = 'a=1; Path=/';
            document.cookie = 'b=2; Path=/';
            result = document.cookie;
        """).AsString.Should().Be("a=1; b=2");
    }

    [TestMethod]
    public void Cookies_set_via_http_are_visible_to_script()
    {
        var (runtime, _, jar) = BuildEnv("https://example.com/");
        jar.StoreFromHeaders(Starling.Url.UrlParser.Parse("https://example.com/").Value,
            new[] { "from_http=yes; Path=/" });
        Eval(runtime, "result = document.cookie;").AsString.Should().Be("from_http=yes");
    }

    [TestMethod]
    public void Cookies_set_via_script_round_trip_to_jar()
    {
        var (runtime, _, jar) = BuildEnv("https://example.com/");
        Eval(runtime, "document.cookie = 'k=v; Path=/';");
        jar.BuildCookieHeader(Starling.Url.UrlParser.Parse("https://example.com/").Value)
            .Should().Be("k=v");
    }

    [TestMethod]
    public void Cookie_setter_silently_drops_malformed_input()
    {
        var (runtime, _, _) = BuildEnv("https://example.com/");
        Eval(runtime, "document.cookie = 'no-equals-here'; result = document.cookie;")
            .AsString.Should().Be("");
    }

    [TestMethod]
    public void Cookies_from_other_origin_not_visible()
    {
        var (runtimeA, _, jarShared) = BuildEnv("https://a.example.com/");
        Eval(runtimeA, "document.cookie = 'k=a; Path=/';");

        var (runtimeB, _, _) = BuildEnv("https://b.example.com/", jar: jarShared);
        Eval(runtimeB, "result = document.cookie;").AsString.Should().Be("");
    }

    private static (JsRuntime Runtime, Document Doc, CookieJar Jar) BuildEnv(string url, CookieJar? jar = null)
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        var runtime = new JsRuntime();
        var resolvedJar = jar ?? new CookieJar();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions(
            DocumentUrl: url,
            CookieJar: resolvedJar));
        return (runtime, doc, resolvedJar);
    }

    private static JsRuntime BuildEnvWithoutJar(string url)
    {
        var doc = new Document();
        doc.AppendChild(doc.CreateElement("html"));
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions(DocumentUrl: url));
        return runtime;
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}

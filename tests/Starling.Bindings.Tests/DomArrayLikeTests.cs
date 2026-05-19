using FluentAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Net;
namespace Starling.Bindings.Tests;

/// <summary>
/// B5-1-followup: the DOM result lists (<c>children</c>, <c>childNodes</c>,
/// <c>querySelectorAll</c>, <c>getElementsByTagName</c>,
/// <c>getElementsByClassName</c>) are now real <c>JsArray</c> instances, so
/// <c>Array.isArray</c> + the full <c>Array.prototype</c> surface works on them.
/// Headers <c>entries()</c> / <c>keys()</c> / <c>values()</c> now return real
/// iterators (Array Iterator over an insertion-order snapshot) so the iterator
/// protocol (for-of, spread, destructuring) works.
/// </summary>
[TestClass]
public sealed class DomArrayLikeTests
{
    // -------------------- DOM array-like surfaces --------------------------

    [TestMethod]
    public void QuerySelectorAll_returns_real_array()
    {
        var (runtime, _) = BuildEnv();
        SeedThreeDivs(runtime);
        Eval(runtime, "result = Array.isArray(document.querySelectorAll('*'));")
            .AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void QuerySelectorAll_map_returns_ids()
    {
        var (runtime, _) = BuildEnv();
        SeedThreeDivs(runtime);
        Eval(runtime, "result = document.querySelectorAll('div').map(function (d) { return d.id; }).join(',');")
            .AsString.Should().Be("a,b,c");
    }

    [TestMethod]
    public void QuerySelectorAll_filter_narrows_result()
    {
        var (runtime, _) = BuildEnv();
        SeedThreeDivs(runtime);
        Eval(runtime, """
            var hits = document.querySelectorAll('div').filter(function (d) { return d.id === 'b'; });
            result = hits.length === 1 && hits[0].id === 'b';
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Children_is_real_array()
    {
        var (runtime, _) = BuildEnv();
        SeedThreeDivs(runtime);
        Eval(runtime, "result = Array.isArray(document.body.children);")
            .AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Children_spread_produces_array()
    {
        var (runtime, _) = BuildEnv();
        SeedThreeDivs(runtime);
        Eval(runtime, """
            var copy = [...document.body.children];
            result = Array.isArray(copy) && copy.length === 3 && copy[2].id === 'c';
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void ChildNodes_walkable_with_for_of()
    {
        var (runtime, _) = BuildEnv();
        SeedThreeDivs(runtime);
        Eval(runtime, """
            var ids = '';
            for (var n of document.body.childNodes) ids = ids + (n.id || '');
            result = ids;
        """).AsString.Should().Be("abc");
    }

    [TestMethod]
    public void GetElementsByTagName_returns_real_array()
    {
        var (runtime, _) = BuildEnv();
        SeedThreeDivs(runtime);
        Eval(runtime, """
            var list = document.getElementsByTagName('div');
            result = Array.isArray(list) && list.length === 3 && list.map(function (e) { return e.id; }).join(',');
        """).AsString.Should().Be("a,b,c");
    }

    [TestMethod]
    public void GetElementsByClassName_returns_real_array()
    {
        var (runtime, _) = BuildEnv();
        SeedThreeDivs(runtime);
        Eval(runtime, """
            var b = document.getElementById('b');
            b.setAttribute('class', 'pick me');
            var list = document.getElementsByClassName('pick');
            result = Array.isArray(list) && list.length === 1 && list[0].id === 'b';
        """).AsBool.Should().BeTrue();
    }

    // ----------------------- Headers iterators -----------------------------

    [TestMethod]
    public void Headers_entries_walkable_with_for_of()
    {
        var runtime = BuildFetchEnv();
        Eval(runtime, """
            var h = new Headers({ a: '1', b: '2' });
            var pairs = '';
            for (var kv of h.entries()) pairs = pairs + kv[0] + '=' + kv[1] + ';';
            result = pairs;
        """).AsString.Should().Be("a=1;b=2;");
    }

    [TestMethod]
    public void Headers_keys_spread_into_array()
    {
        var runtime = BuildFetchEnv();
        Eval(runtime, """
            var h = new Headers({ a: '1', b: '2' });
            var k = [...h.keys()];
            result = Array.isArray(k) && k.length === 2 && k[0] === 'a' && k[1] === 'b';
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Headers_values_is_iterable()
    {
        var runtime = BuildFetchEnv();
        Eval(runtime, """
            var h = new Headers({ a: '1', b: '2' });
            var vs = '';
            for (var v of h.values()) vs = vs + v + ',';
            result = vs;
        """).AsString.Should().Be("1,2,");
    }

    // -------------------------- helpers ------------------------------------

    private static void SeedThreeDivs(JsRuntime runtime)
    {
        Eval(runtime, """
            ['a','b','c'].forEach(function (id) {
                var d = document.createElement('div');
                d.id = id;
                document.body.appendChild(d);
            });
        """);
    }

    private static (JsRuntime, Document) BuildEnv()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions());
        return (runtime, doc);
    }

    private static JsRuntime BuildFetchEnv()
    {
        var doc = new Document();
        doc.AppendChild(doc.CreateElement("html"));
        var runtime = new JsRuntime();
        var client = new StarlingHttpClient();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions(
            DocumentUrl: "http://localhost/", HttpClient: client));
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

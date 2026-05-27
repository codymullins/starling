using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Bindings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class StorageTests
{
    public StorageTests() => StorageBinding.ResetForTests();

    [TestMethod]
    public void SetItem_then_getItem_round_trips()
    {
        var runtime = BuildEnv("https://a.example.com/");
        Eval(runtime, "localStorage.setItem('k', 'v'); result = localStorage.getItem('k');")
            .AsString.Should().Be("v");
    }

    [TestMethod]
    public void Bracket_access_is_named_setter()
    {
        var runtime = BuildEnv("https://a.example.com/");
        Eval(runtime, "localStorage.foo = 'bar'; result = localStorage.getItem('foo');")
            .AsString.Should().Be("bar");
        Eval(runtime, "result = localStorage.foo;").AsString.Should().Be("bar");
    }

    [TestMethod]
    public void Missing_key_returns_null_from_getItem_but_undefined_from_bracket()
    {
        var runtime = BuildEnv("https://a.example.com/");
        Eval(runtime, "result = localStorage.getItem('absent');").IsNull.Should().BeTrue();
        Eval(runtime, "result = typeof localStorage.absent;").AsString.Should().Be("undefined");
    }

    [TestMethod]
    public void Length_reflects_count()
    {
        var runtime = BuildEnv("https://a.example.com/");
        Eval(runtime, "localStorage.setItem('a', '1'); localStorage.setItem('b', '2'); result = localStorage.length;")
            .AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Key_returns_insertion_ordered_entry()
    {
        var runtime = BuildEnv("https://a.example.com/");
        Eval(runtime, """
            localStorage.setItem('first', '1');
            localStorage.setItem('second', '2');
            result = localStorage.key(0) + '/' + localStorage.key(1);
        """).AsString.Should().Be("first/second");
    }

    [TestMethod]
    public void RemoveItem_and_clear_drop_entries()
    {
        var runtime = BuildEnv("https://a.example.com/");
        Eval(runtime, """
            localStorage.setItem('a', '1');
            localStorage.setItem('b', '2');
            localStorage.removeItem('a');
            var afterRemove = localStorage.length;
            localStorage.clear();
            result = afterRemove + ':' + localStorage.length;
        """).AsString.Should().Be("1:0");
    }

    [TestMethod]
    public void Values_are_coerced_to_strings()
    {
        var runtime = BuildEnv("https://a.example.com/");
        Eval(runtime, "localStorage.setItem('n', 42); result = localStorage.getItem('n');")
            .AsString.Should().Be("42");
        Eval(runtime, "localStorage.b = true; result = localStorage.getItem('b');")
            .AsString.Should().Be("true");
    }

    [TestMethod]
    public void LocalStorage_is_shared_across_same_origin_realms()
    {
        var first = BuildEnv("https://a.example.com/page1");
        Eval(first, "localStorage.setItem('shared', 'yes');");

        var second = BuildEnv("https://a.example.com/page2");
        Eval(second, "result = localStorage.getItem('shared');")
            .AsString.Should().Be("yes");
    }

    [TestMethod]
    public void LocalStorage_is_isolated_across_origins()
    {
        var first = BuildEnv("https://a.example.com/");
        Eval(first, "localStorage.setItem('only-a', '1');");

        var second = BuildEnv("https://b.example.com/");
        Eval(second, "result = localStorage.getItem('only-a');")
            .IsNull.Should().BeTrue();
    }

    [TestMethod]
    public void SessionStorage_is_isolated_across_realms()
    {
        var first = BuildEnv("https://a.example.com/");
        Eval(first, "sessionStorage.setItem('s', 'one');");

        var second = BuildEnv("https://a.example.com/");
        Eval(second, "result = sessionStorage.getItem('s');")
            .IsNull.Should().BeTrue();
    }

    // (Direct `delete localStorage.foo` is covered by removeItem above; the JS
    // compiler does not yet emit the `delete` unary opcode — pin a test when
    // that compiler gap closes.)

    [TestMethod]
    public void Object_keys_returns_insertion_ordered_entries()
    {
        var runtime = BuildEnv("https://a.example.com/");
        Eval(runtime, """
            localStorage.setItem('one', '1');
            localStorage.setItem('two', '2');
            result = Object.keys(localStorage).join(',');
        """).AsString.Should().Be("one,two");
    }

    private static JsRuntime BuildEnv(string url)
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
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

using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for <c>WeakSet</c> (B3-3).</summary>
public class WeakSetTests
{
    [Fact]
    public void WeakSet_constructor_wired()
    {
        var rt = new JsRuntime();
        rt.GetGlobal("WeakSet").IsObject.Should().BeTrue();
        rt.Realm.WeakSetConstructor.Should().NotBeNull();
    }

    [Fact]
    public void Add_and_has_for_object_key()
    {
        Eval(@"
            var s = new WeakSet();
            var k = {};
            s.add(k);
            s.has(k);").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Primitive_throws_TypeError_on_add()
    {
        // Test rewritten to use the C# boundary because the compiler does
        // not yet support `try`/`catch` or `instanceof` (wp:M3-05). The
        // thrown JsThrow's value is the spec-mandated TypeError instance.
        Action act = () => Eval("new WeakSet().add(1);");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    [Fact]
    public void Delete_removes_membership()
    {
        var r = Eval(@"
            var s = new WeakSet();
            var k = {};
            s.add(k);
            var before = s.has(k);
            s.delete(k);
            before + ',' + s.has(k);");
        r.AsString.Should().Be("true,false");
    }

    [Fact]
    public void No_size_and_no_iteration()
    {
        Eval("typeof (new WeakSet()).size;").AsString.Should().Be("undefined");
        Eval("typeof (new WeakSet())[Symbol.iterator];").AsString.Should().Be("undefined");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

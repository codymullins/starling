using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for <c>WeakSet</c> (B3-3).</summary>
[TestClass]
public class WeakSetTests
{
    [TestMethod]
    public void WeakSet_constructor_wired()
    {
        var rt = new JsRuntime();
        rt.GetGlobal("WeakSet").IsObject.Should().BeTrue();
        rt.Realm.WeakSetConstructor.Should().NotBeNull();
    }

    [TestMethod]
    public void Add_and_has_for_object_key()
    {
        Eval(@"
            var s = new WeakSet();
            var k = {};
            s.add(k);
            s.has(k);").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Primitive_throws_TypeError_on_add()
    {
        Eval(@"
            var ok = false;
            try { new WeakSet().add(1); }
            catch (e) { ok = e instanceof TypeError; }
            ok;").AsBool.Should().BeTrue();
    }

    [TestMethod]
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

    [TestMethod]
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

using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for <c>WeakMap</c> (B3-3).</summary>
[TestClass]
public class WeakMapTests
{
    [TestMethod]
    public void WeakMap_constructor_wired()
    {
        var rt = new JsRuntime();
        rt.GetGlobal("WeakMap").IsObject.Should().BeTrue();
        rt.Realm.WeakMapConstructor.Should().NotBeNull();
    }

    [TestMethod]
    public void Set_get_with_object_key()
    {
        Eval(@"
            var m = new WeakMap();
            var k = {};
            m.set(k, 7);
            m.get(k);").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Primitive_key_on_set_throws_TypeError()
    {
        Eval(@"
            var ok = false;
            try { new WeakMap().set(1, 'x'); }
            catch (e) { ok = e instanceof TypeError; }
            ok;").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Has_and_delete()
    {
        var r = Eval(@"
            var m = new WeakMap();
            var k = {};
            m.set(k, 1);
            var out = m.has(k);
            m.delete(k);
            out + ',' + m.has(k);");
        r.AsString.Should().Be("true,false");
    }

    [TestMethod]
    public void No_size_and_no_symbol_iterator()
    {
        // Spec: WeakMap.prototype has no `size` and no @@iterator. Plain
        // member access returns undefined.
        Eval("typeof (new WeakMap()).size;").AsString.Should().Be("undefined");
        Eval("typeof (new WeakMap())[Symbol.iterator];").AsString.Should().Be("undefined");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

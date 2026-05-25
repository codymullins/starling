using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// wp:M3-76 — subclassing built-in constructors. A derived class's
/// <c>super(...)</c> must invoke the native constructor as a real
/// [[Construct]] threading the derived class as new.target, so the instance
/// is created via OrdinaryCreateFromConstructor (subclass prototype + the
/// builtin's internal slots/state).
/// </summary>
[TestClass]
public class SubclassBuiltinTests
{
    [TestMethod]
    public void Subclass_Array_instanceof_both_and_is_real_array()
    {
        Eval(@"
            class A extends Array {}
            var a = new A();
            a.push(1, 2, 3);
            (a instanceof A) && (a instanceof Array) && a.length === 3 && Array.isArray(a);
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_Map_has_working_internal_slots()
    {
        Eval(@"
            class M extends Map {}
            var m = new M();
            m.set(1, 2);
            (m instanceof M) && (m instanceof Map) && m.get(1) === 2 && m.size === 1;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_Map_with_iterable_argument()
    {
        Eval(@"
            class M extends Map {}
            var m = new M([['a', 1]]);
            m.size;
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Subclass_Set_instanceof_both()
    {
        Eval(@"
            class S extends Set {}
            var s = new S();
            s.add(5);
            (s instanceof S) && (s instanceof Set) && s.has(5) && s.size === 1;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_WeakMap_instanceof_both()
    {
        Eval(@"
            class W extends WeakMap {}
            var w = new W();
            var k = {};
            w.set(k, 9);
            (w instanceof W) && (w instanceof WeakMap) && w.get(k) === 9;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_WeakSet_instanceof_both()
    {
        Eval(@"
            class W extends WeakSet {}
            var w = new W();
            (w instanceof W) && (w instanceof WeakSet);
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_WeakRef_instanceof_both()
    {
        Eval(@"
            class W extends WeakRef {}
            var w = new W({});
            (w instanceof W) && (w instanceof WeakRef);
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_TypeError_instanceof_chain_and_message()
    {
        Eval(@"
            class E extends TypeError {}
            var e = new E('boom');
            (e instanceof E) && (e instanceof TypeError) && (e instanceof Error) && e.message === 'boom';
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_Error_instanceof_both()
    {
        Eval(@"
            class E extends Error {}
            var e = new E('x');
            (e instanceof E) && (e instanceof Error) && e.message === 'x';
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_Uint8Array_constructs_and_instanceof_both()
    {
        Eval(@"
            class U extends Uint8Array {}
            var u = new U(3);
            (u instanceof U) && (u instanceof Uint8Array) && u.length === 3;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_Float64Array_instanceof_both()
    {
        Eval(@"
            class F extends Float64Array {}
            var f = new F([1.5, 2.5]);
            (f instanceof F) && (f instanceof Float64Array) && f.length === 2 && f[0] === 1.5;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_Date_instanceof_both()
    {
        Eval(@"
            class D extends Date {}
            var d = new D(0);
            (d instanceof D) && (d instanceof Date) && d.getTime() === 0;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_RegExp_instanceof_both()
    {
        Eval(@"
            class R extends RegExp {}
            var r = new R('ab', 'g');
            (r instanceof R) && (r instanceof RegExp) && r.source === 'ab' && r.global === true;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_Promise_instanceof_both()
    {
        Eval(@"
            class P extends Promise {}
            var p = new P(function(res) { res(1); });
            (p instanceof P) && (p instanceof Promise);
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_Boolean_wrapper_instanceof_both()
    {
        Eval(@"
            class B extends Boolean {}
            var b = new B(true);
            (b instanceof B) && (b instanceof Boolean) && b.valueOf() === true;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_Number_wrapper_instanceof_both()
    {
        Eval(@"
            class N extends Number {}
            var n = new N(42);
            (n instanceof N) && (n instanceof Number) && n.valueOf() === 42;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_String_wrapper_instanceof_both()
    {
        Eval(@"
            class S extends String {}
            var s = new S('hi');
            (s instanceof S) && (s instanceof String) && s.length === 2 && s.valueOf() === 'hi';
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_ArrayBuffer_instanceof_both()
    {
        Eval(@"
            class B extends ArrayBuffer {}
            var b = new B(8);
            (b instanceof B) && (b instanceof ArrayBuffer) && b.byteLength === 8;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_DataView_instanceof_both()
    {
        Eval(@"
            class V extends DataView {}
            var v = new V(new ArrayBuffer(8));
            (v instanceof V) && (v instanceof DataView) && v.byteLength === 8;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_Object_instanceof_both()
    {
        Eval(@"
            class S extends Object {}
            var s = new S();
            (s instanceof S) && (s instanceof Object);
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_Function_instanceof_both()
    {
        Eval(@"
            class S extends Function {}
            var s = new S();
            (s instanceof S) && (s instanceof Function);
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_AggregateError_instanceof_both()
    {
        Eval(@"
            class S extends AggregateError {}
            var s = new S([]);
            (s instanceof S) && (s instanceof AggregateError) && (s instanceof Error);
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_URIError_instanceof_chain()
    {
        Eval(@"
            class S extends URIError {}
            var s = new S();
            (s instanceof S) && (s instanceof URIError) && (s instanceof Error);
        ").AsBool.Should().BeTrue();
    }

    // ----- Negative / non-regression -----

    [TestMethod]
    public void Plain_new_Map_still_works()
    {
        Eval(@"
            var m = new Map();
            m.set('a', 1);
            (m instanceof Map) && m.get('a') === 1 && m.size === 1;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Plain_new_Array_still_works()
    {
        Eval(@"
            var a = new Array(1, 2, 3);
            (a instanceof Array) && a.length === 3 && a[1] === 2;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Plain_new_Uint8Array_still_works()
    {
        Eval(@"
            var u = new Uint8Array(4);
            (u instanceof Uint8Array) && u.length === 4;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Map_called_without_new_throws()
    {
        var act = () => Eval("Map();");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void Uint8Array_called_without_new_throws()
    {
        var act = () => Eval("Uint8Array(3);");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void Derived_without_super_throws()
    {
        var act = () => Eval(@"
            class M extends Map { constructor() {} }
            new M();
        ");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void Boolean_called_without_new_returns_primitive()
    {
        Eval("typeof Boolean(1);").AsString.Should().Be("boolean");
    }

    [TestMethod]
    public void Number_called_without_new_returns_primitive()
    {
        Eval("typeof Number('3');").AsString.Should().Be("number");
    }

    [TestMethod]
    public void String_called_without_new_returns_primitive()
    {
        Eval("typeof String(5);").AsString.Should().Be("string");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

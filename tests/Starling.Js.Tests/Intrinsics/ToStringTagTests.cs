using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>
/// §20.1.3.6 <c>Object.prototype.toString</c> follow-up. Verifies the host
/// classifier picks the spec-mandated default tag for each built-in class, and
/// that <c>@@toStringTag</c> on the receiver (or its prototype chain) overrides
/// the default. Each intrinsic's prototype was updated to install the symbol-
/// keyed tag per spec (non-writable, non-enumerable, configurable).
/// </summary>
[TestClass]
public class ToStringTagTests
{
    [TestMethod]
    public void PlainObject_yields_object_Object()
        => Eval("Object.prototype.toString.call({})").AsString.Should().Be("[object Object]");

    [TestMethod]
    public void Array_yields_object_Array()
        => Eval("Object.prototype.toString.call([])").AsString.Should().Be("[object Array]");

    [TestMethod]
    public void Function_yields_object_Function()
        => Eval("Object.prototype.toString.call(function(){})").AsString.Should().Be("[object Function]");

    [TestMethod]
    public void Error_yields_object_Error()
        => Eval("Object.prototype.toString.call(new Error())").AsString.Should().Be("[object Error]");

    [TestMethod]
    public void TypeError_yields_object_Error_via_prototype_chain()
        => Eval("Object.prototype.toString.call(new TypeError())").AsString.Should().Be("[object Error]");

    [TestMethod]
    public void Date_yields_object_Date()
        => Eval("Object.prototype.toString.call(new Date())").AsString.Should().Be("[object Date]");

    [TestMethod]
    public void RegExp_yields_object_RegExp()
        => Eval("Object.prototype.toString.call(/x/)").AsString.Should().Be("[object RegExp]");

    [TestMethod]
    public void Map_yields_object_Map()
        => Eval("Object.prototype.toString.call(new Map())").AsString.Should().Be("[object Map]");

    [TestMethod]
    public void Set_yields_object_Set()
        => Eval("Object.prototype.toString.call(new Set())").AsString.Should().Be("[object Set]");

    [TestMethod]
    public void WeakMap_yields_object_WeakMap()
        => Eval("Object.prototype.toString.call(new WeakMap())").AsString.Should().Be("[object WeakMap]");

    [TestMethod]
    public void WeakSet_yields_object_WeakSet()
        => Eval("Object.prototype.toString.call(new WeakSet())").AsString.Should().Be("[object WeakSet]");

    [TestMethod]
    public void Promise_yields_object_Promise()
        => Eval("Object.prototype.toString.call(Promise.resolve(1))").AsString.Should().Be("[object Promise]");

    [TestMethod]
    public void WeakRef_yields_object_WeakRef()
        => Eval("Object.prototype.toString.call(new WeakRef({}))").AsString.Should().Be("[object WeakRef]");

    [TestMethod]
    public void FinalizationRegistry_yields_object_FinalizationRegistry()
        => Eval("Object.prototype.toString.call(new FinalizationRegistry(function(){}))").AsString
            .Should().Be("[object FinalizationRegistry]");

    [TestMethod]
    public void Math_yields_object_Math()
        => Eval("Object.prototype.toString.call(Math)").AsString.Should().Be("[object Math]");

    [TestMethod]
    public void Json_yields_object_JSON()
        => Eval("Object.prototype.toString.call(JSON)").AsString.Should().Be("[object JSON]");

    [TestMethod]
    public void Reflect_yields_object_Reflect()
        => Eval("Object.prototype.toString.call(Reflect)").AsString.Should().Be("[object Reflect]");

    [TestMethod]
    public void ArrayBuffer_yields_object_ArrayBuffer()
        => Eval("Object.prototype.toString.call(new ArrayBuffer(0))").AsString.Should().Be("[object ArrayBuffer]");

    [TestMethod]
    public void DataView_yields_object_DataView()
        => Eval("Object.prototype.toString.call(new DataView(new ArrayBuffer(8)))").AsString
            .Should().Be("[object DataView]");

    [TestMethod]
    public void TypedArray_yields_typed_specific_tag()
    {
        Eval("Object.prototype.toString.call(new Uint8Array(0))").AsString.Should().Be("[object Uint8Array]");
        Eval("Object.prototype.toString.call(new Int32Array(0))").AsString.Should().Be("[object Int32Array]");
        Eval("Object.prototype.toString.call(new Float32Array(0))").AsString.Should().Be("[object Float32Array]");
    }

    /// <summary>§23.2.3.34 — @@toStringTag lives on <c>%TypedArray%.prototype</c>
    /// as an accessor, not as own data property on each concrete prototype.
    /// Concrete prototypes (Uint8Array.prototype, …) have no own descriptor.</summary>
    [TestMethod]
    public void TypedArray_concrete_prototype_has_no_own_toStringTag()
        => Eval("Object.getOwnPropertyDescriptor(Uint8Array.prototype, Symbol.toStringTag) === undefined")
            .AsBool.Should().BeTrue();

    [TestMethod]
    public void Shared_TypedArray_prototype_exposes_toStringTag_accessor()
    {
        var r = Eval(@"
            var p = Object.getPrototypeOf(Uint8Array.prototype);
            var d = Object.getOwnPropertyDescriptor(p, Symbol.toStringTag);
            (typeof d.get) + '|' + (d.set === undefined) + '|' + d.enumerable + '|' + d.configurable
        ");
        r.AsString.Should().Be("function|true|false|true");
    }

    [TestMethod]
    public void TypedArray_toStringTag_getter_returns_undefined_on_non_TypedArray()
    {
        var r = Eval(@"
            var p = Object.getPrototypeOf(Uint8Array.prototype);
            var d = Object.getOwnPropertyDescriptor(p, Symbol.toStringTag);
            d.get.call({})
        ");
        r.IsUndefined.Should().BeTrue();
    }

    [TestMethod]
    public void Undefined_yields_object_Undefined()
        => Eval("Object.prototype.toString.call(undefined)").AsString.Should().Be("[object Undefined]");

    [TestMethod]
    public void Null_yields_object_Null()
        => Eval("Object.prototype.toString.call(null)").AsString.Should().Be("[object Null]");

    [TestMethod]
    public void CustomToStringTag_overrides_default_for_plain_object()
        => Eval("var o = {}; o[Symbol.toStringTag] = 'Foo'; Object.prototype.toString.call(o)")
            .AsString.Should().Be("[object Foo]");

    [TestMethod]
    public void CustomToStringTag_on_class_overrides_default()
        => Eval(@"
            function C(){};
            C.prototype[Symbol.toStringTag] = 'CustomThing';
            Object.prototype.toString.call(new C())
        ").AsString.Should().Be("[object CustomThing]");

    [TestMethod]
    public void NonStringToStringTag_falls_back_to_default()
        => Eval("var o = {}; o[Symbol.toStringTag] = 42; Object.prototype.toString.call(o)")
            .AsString.Should().Be("[object Object]");

    [TestMethod]
    public void Symbol_prototype_has_toStringTag()
        => Eval("Symbol.prototype[Symbol.toStringTag]").AsString.Should().Be("Symbol");

    [TestMethod]
    public void ToStringTag_descriptor_is_nonenumerable_nonwritable_configurable()
    {
        var r = Eval(@"
            var d = Object.getOwnPropertyDescriptor(Map.prototype, Symbol.toStringTag);
            d.value + '|' + d.writable + '|' + d.enumerable + '|' + d.configurable
        ");
        r.AsString.Should().Be("Map|false|false|true");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

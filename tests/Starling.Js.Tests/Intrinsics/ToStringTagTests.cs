using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Intrinsics;

/// <summary>
/// §20.1.3.6 <c>Object.prototype.toString</c> follow-up. Verifies the host
/// classifier picks the spec-mandated default tag for each built-in class, and
/// that <c>@@toStringTag</c> on the receiver (or its prototype chain) overrides
/// the default. Each intrinsic's prototype was updated to install the symbol-
/// keyed tag per spec (non-writable, non-enumerable, configurable).
/// </summary>
public class ToStringTagTests
{
    [Fact]
    public void PlainObject_yields_object_Object()
        => Eval("Object.prototype.toString.call({})").AsString.Should().Be("[object Object]");

    [Fact]
    public void Array_yields_object_Array()
        => Eval("Object.prototype.toString.call([])").AsString.Should().Be("[object Array]");

    [Fact]
    public void Function_yields_object_Function()
        => Eval("Object.prototype.toString.call(function(){})").AsString.Should().Be("[object Function]");

    [Fact]
    public void Error_yields_object_Error()
        => Eval("Object.prototype.toString.call(new Error())").AsString.Should().Be("[object Error]");

    [Fact]
    public void TypeError_yields_object_Error_via_prototype_chain()
        => Eval("Object.prototype.toString.call(new TypeError())").AsString.Should().Be("[object Error]");

    [Fact]
    public void Date_yields_object_Date()
        => Eval("Object.prototype.toString.call(new Date())").AsString.Should().Be("[object Date]");

    [Fact]
    public void RegExp_yields_object_RegExp()
        => Eval("Object.prototype.toString.call(/x/)").AsString.Should().Be("[object RegExp]");

    [Fact]
    public void Map_yields_object_Map()
        => Eval("Object.prototype.toString.call(new Map())").AsString.Should().Be("[object Map]");

    [Fact]
    public void Set_yields_object_Set()
        => Eval("Object.prototype.toString.call(new Set())").AsString.Should().Be("[object Set]");

    [Fact]
    public void WeakMap_yields_object_WeakMap()
        => Eval("Object.prototype.toString.call(new WeakMap())").AsString.Should().Be("[object WeakMap]");

    [Fact]
    public void WeakSet_yields_object_WeakSet()
        => Eval("Object.prototype.toString.call(new WeakSet())").AsString.Should().Be("[object WeakSet]");

    [Fact]
    public void Promise_yields_object_Promise()
        => Eval("Object.prototype.toString.call(Promise.resolve(1))").AsString.Should().Be("[object Promise]");

    [Fact]
    public void WeakRef_yields_object_WeakRef()
        => Eval("Object.prototype.toString.call(new WeakRef({}))").AsString.Should().Be("[object WeakRef]");

    [Fact]
    public void FinalizationRegistry_yields_object_FinalizationRegistry()
        => Eval("Object.prototype.toString.call(new FinalizationRegistry(function(){}))").AsString
            .Should().Be("[object FinalizationRegistry]");

    [Fact]
    public void Math_yields_object_Math()
        => Eval("Object.prototype.toString.call(Math)").AsString.Should().Be("[object Math]");

    [Fact]
    public void Json_yields_object_JSON()
        => Eval("Object.prototype.toString.call(JSON)").AsString.Should().Be("[object JSON]");

    [Fact]
    public void Reflect_yields_object_Reflect()
        => Eval("Object.prototype.toString.call(Reflect)").AsString.Should().Be("[object Reflect]");

    [Fact]
    public void ArrayBuffer_yields_object_ArrayBuffer()
        => Eval("Object.prototype.toString.call(new ArrayBuffer(0))").AsString.Should().Be("[object ArrayBuffer]");

    [Fact]
    public void DataView_yields_object_DataView()
        => Eval("Object.prototype.toString.call(new DataView(new ArrayBuffer(8)))").AsString
            .Should().Be("[object DataView]");

    [Fact]
    public void TypedArray_yields_typed_specific_tag()
    {
        Eval("Object.prototype.toString.call(new Uint8Array(0))").AsString.Should().Be("[object Uint8Array]");
        Eval("Object.prototype.toString.call(new Int32Array(0))").AsString.Should().Be("[object Int32Array]");
    }

    [Fact]
    public void Undefined_yields_object_Undefined()
        => Eval("Object.prototype.toString.call(undefined)").AsString.Should().Be("[object Undefined]");

    [Fact]
    public void Null_yields_object_Null()
        => Eval("Object.prototype.toString.call(null)").AsString.Should().Be("[object Null]");

    [Fact]
    public void CustomToStringTag_overrides_default_for_plain_object()
        => Eval("var o = {}; o[Symbol.toStringTag] = 'Foo'; Object.prototype.toString.call(o)")
            .AsString.Should().Be("[object Foo]");

    [Fact]
    public void CustomToStringTag_on_class_overrides_default()
        => Eval(@"
            function C(){};
            C.prototype[Symbol.toStringTag] = 'CustomThing';
            Object.prototype.toString.call(new C())
        ").AsString.Should().Be("[object CustomThing]");

    [Fact]
    public void NonStringToStringTag_falls_back_to_default()
        => Eval("var o = {}; o[Symbol.toStringTag] = 42; Object.prototype.toString.call(o)")
            .AsString.Should().Be("[object Object]");

    [Fact]
    public void Symbol_prototype_has_toStringTag()
        => Eval("Symbol.prototype[Symbol.toStringTag]").AsString.Should().Be("Symbol");

    [Fact]
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

using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Intrinsics;

/// <summary>
/// End-to-end coverage for the <c>Object</c> intrinsic (B2-1). Spin up a fresh
/// <see cref="JsRuntime"/>, run a script that uses <c>Object.*</c> + prototype
/// methods, then assert on the resulting globals.
/// </summary>
/// <remarks>
/// Array-returning statics (<c>keys</c>/<c>values</c>/<c>entries</c>/
/// <c>getOwnPropertyNames</c>) currently return ordinary objects with
/// integer-string keys and a <c>length</c> property — the dedicated
/// <c>JsArray</c> exotic lands in B2-4. Tests probe both <c>length</c> and
/// bracketed indices so the assertions keep working once <c>JsArray</c>
/// arrives.
/// </remarks>
public class ObjectTests
{
    [Fact]
    public void Object_is_registered_on_global_with_prototype_slot()
    {
        var rt = new JsRuntime();
        var Object = rt.GetGlobal("Object");

        Object.IsObject.Should().BeTrue();
        var proto = Object.AsObject.Get("prototype");
        proto.AsObject.Should().BeSameAs(rt.Realm.ObjectPrototype);
        rt.Realm.ObjectConstructor.Should().BeSameAs(Object.AsObject);
    }

    [Fact]
    public void Object_keys_returns_own_enumerable_string_keys_in_insertion_order()
    {
        var r = Eval(@"
            var o = { a: 1, b: 2, c: 3 };
            var ks = Object.keys(o);
            ks.length + ':' + ks[0] + ',' + ks[1] + ',' + ks[2];
        ");
        r.AsString.Should().Be("3:a,b,c");
    }

    [Fact]
    public void Object_values_returns_own_enumerable_values_in_insertion_order()
    {
        var r = Eval(@"
            var o = { a: 10, b: 20 };
            var vs = Object.values(o);
            vs.length + ':' + vs[0] + ',' + vs[1];
        ");
        r.AsString.Should().Be("2:10,20");
    }

    [Fact]
    public void Object_entries_pairs_keys_and_values()
    {
        var r = Eval(@"
            var o = { a: 1, b: 2 };
            var es = Object.entries(o);
            es.length + ':' + es[0][0] + '=' + es[0][1] + ',' + es[1][0] + '=' + es[1][1];
        ");
        r.AsString.Should().Be("2:a=1,b=2");
    }

    [Fact]
    public void Object_assign_copies_in_source_order_with_later_overriding()
    {
        var r = Eval(@"
            var t = Object.assign({}, { a: 1 }, { b: 2 }, { a: 3 });
            t.a + ',' + t.b;
        ");
        r.AsString.Should().Be("3,2");
    }

    [Fact]
    public void Object_create_with_null_prototype_returns_object_with_no_proto()
    {
        var rt = new JsRuntime();
        var program = new JsParser("var o = Object.create(null); o;").ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        var result = new JsVm(rt).Run(chunk);

        result.IsObject.Should().BeTrue();
        result.AsObject.Prototype.Should().BeNull();
    }

    [Fact]
    public void Object_create_with_prototype_links_chain()
    {
        var rt = new JsRuntime();
        var program = new JsParser(@"
            var p = { greet: 1 };
            var c = Object.create(p);
            c;
        ").ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        var result = new JsVm(rt).Run(chunk);

        result.IsObject.Should().BeTrue();
        // Inherits via the prototype chain.
        result.AsObject.Get("greet").AsNumber.Should().Be(1);
        result.AsObject.HasOwn("greet").Should().BeFalse();
    }

    [Fact]
    public void Object_defineProperty_with_writable_false_blocks_assignment()
    {
        // Note: assignment to a non-writable property currently silently no-ops
        // (sloppy-mode semantics). Strict-throw lands when the compiler emits
        // strict bytecode. Pinning the "value did not change" invariant here.
        var r = Eval(@"
            var o = {};
            Object.defineProperty(o, 'x', { value: 5, writable: false, enumerable: true, configurable: true });
            o.x = 99;
            o.x;
        ");
        r.AsNumber.Should().Be(5);
    }

    [Fact]
    public void Object_freeze_then_isFrozen_is_true_and_writes_rejected()
    {
        var r = Eval(@"
            var o = { x: 1 };
            Object.freeze(o);
            o.x = 99;
            Object.isFrozen(o) + ',' + o.x;
        ");
        r.AsString.Should().Be("true,1");
    }

    [Fact]
    public void Object_seal_prevents_adds_but_allows_writes_to_existing_slots()
    {
        var r = Eval(@"
            var o = { x: 1 };
            Object.seal(o);
            o.x = 99;
            o.y = 7;
            Object.isSealed(o) + ',' + o.x + ',' + (typeof o.y);
        ");
        r.AsString.Should().Be("true,99,undefined");
    }

    [Fact]
    public void Object_preventExtensions_makes_isExtensible_false()
    {
        var r = Eval(@"
            var o = {};
            Object.preventExtensions(o);
            Object.isExtensible(o);
        ");
        r.AsBool.Should().BeFalse();
    }

    [Fact]
    public void Object_is_treats_NaN_as_equal_and_signed_zero_as_distinct()
    {
        Eval("Object.is(NaN, NaN);").AsBool.Should().BeTrue();
        Eval("Object.is(+0, -0);").AsBool.Should().BeFalse();
        Eval("Object.is('a', 'a');").AsBool.Should().BeTrue();
        Eval("Object.is(1, 2);").AsBool.Should().BeFalse();
    }

    [Fact]
    public void Object_hasOwn_is_true_for_own_props_and_false_for_inherited()
    {
        var r = Eval(@"
            var p = { inh: 1 };
            var c = Object.create(p);
            c.own = 2;
            Object.hasOwn(c, 'own') + ',' + Object.hasOwn(c, 'inh') + ',' + Object.hasOwn(c, 'missing');
        ");
        r.AsString.Should().Be("true,false,false");
    }

    [Fact]
    public void hasOwnProperty_inherited_via_Object_prototype_chain()
    {
        var r = Eval(@"
            var o = { x: 1 };
            o.hasOwnProperty('x') + ',' + o.hasOwnProperty('missing');
        ");
        r.AsString.Should().Be("true,false");
    }

    [Fact]
    public void getOwnPropertyDescriptor_returns_data_descriptor_with_flags()
    {
        var r = Eval(@"
            var o = { x: 42 };
            var d = Object.getOwnPropertyDescriptor(o, 'x');
            d.value + ',' + d.writable + ',' + d.enumerable + ',' + d.configurable;
        ");
        r.AsString.Should().Be("42,true,true,true");
    }

    [Fact]
    public void getOwnPropertyDescriptor_returns_undefined_for_missing_key()
    {
        var r = Eval(@"
            var o = {};
            var d = Object.getOwnPropertyDescriptor(o, 'absent');
            typeof d;
        ");
        r.AsString.Should().Be("undefined");
    }

    [Fact]
    public void getPrototypeOf_returns_Object_prototype_for_literal()
    {
        var rt = new JsRuntime();
        var program = new JsParser("var o = {}; Object.getPrototypeOf(o);").ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        var result = new JsVm(rt).Run(chunk);

        result.IsObject.Should().BeTrue();
        result.AsObject.Should().BeSameAs(rt.Realm.ObjectPrototype);
    }

    [Fact]
    public void setPrototypeOf_re_links_chain()
    {
        var r = Eval(@"
            var p = { inh: 99 };
            var o = {};
            Object.setPrototypeOf(o, p);
            o.inh;
        ");
        r.AsNumber.Should().Be(99);
    }

    [Fact]
    public void getOwnPropertyNames_includes_non_enumerable_keys()
    {
        // for/while loops aren't compiled yet (wp:M3-03). Probe length and
        // indexed access manually — the array-like contract guarantees
        // names[0]/names[1] resolve in insertion order.
        var r = Eval(@"
            var o = {};
            Object.defineProperty(o, 'hidden', { value: 1, writable: true, enumerable: false, configurable: true });
            o.visible = 2;
            var names = Object.getOwnPropertyNames(o);
            names.length + ':' + names[0] + ',' + names[1];
        ");
        r.AsString.Should().Be("2:hidden,visible");
    }

    [Fact]
    public void getOwnPropertySymbols_returns_empty_array_like()
    {
        var r = Eval(@"
            var o = { x: 1 };
            Object.getOwnPropertySymbols(o).length;
        ");
        r.AsNumber.Should().Be(0);
    }

    [Fact]
    public void Object_toString_returns_object_Object_for_plain_object()
    {
        var r = Eval("({}).toString();");
        r.AsString.Should().Be("[object Object]");
    }

    [Fact]
    public void propertyIsEnumerable_respects_descriptor_flag()
    {
        var r = Eval(@"
            var o = {};
            o.visible = 1;
            Object.defineProperty(o, 'hidden', { value: 2, writable: true, enumerable: false, configurable: true });
            o.propertyIsEnumerable('visible') + ',' + o.propertyIsEnumerable('hidden') + ',' + o.propertyIsEnumerable('missing');
        ");
        r.AsString.Should().Be("true,false,false");
    }

    [Fact]
    public void isPrototypeOf_walks_chain()
    {
        var r = Eval(@"
            var p = {};
            var c = Object.create(p);
            p.isPrototypeOf(c) + ',' + c.isPrototypeOf(p);
        ");
        r.AsString.Should().Be("true,false");
    }

    // ---------------------------------------------------------------- Helpers

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

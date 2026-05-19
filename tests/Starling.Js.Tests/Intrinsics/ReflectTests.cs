using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for ECMA-262 §28.1 Reflect namespace.</summary>
public class ReflectTests
{
    [Fact]
    public void Reflect_get_reads_property()
    {
        Eval("Reflect.get({a: 1}, 'a');").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Reflect_set_writes_property_and_returns_true()
    {
        Eval(@"
            var o = {};
            var ok = Reflect.set(o, 'x', 5);
            ok && o.x === 5;
        ").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Reflect_has_mirrors_in_operator()
    {
        Eval("Reflect.has({a: 1}, 'a');").AsBool.Should().BeTrue();
        Eval("Reflect.has({}, 'a');").AsBool.Should().BeFalse();
    }

    [Fact]
    public void Reflect_deleteProperty_removes_property()
    {
        // `in` operator pending (wp:M3-05); use Reflect.has to verify the property is gone.
        Eval(@"
            var o = {a: 1, b: 2};
            var ok = Reflect.deleteProperty(o, 'a');
            ok && !Reflect.has(o, 'a');
        ").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Reflect_getOwnPropertyDescriptor_returns_descriptor_or_undefined()
    {
        Eval("Reflect.getOwnPropertyDescriptor({a: 1}, 'a').value;").AsNumber.Should().Be(1);
        Eval("Reflect.getOwnPropertyDescriptor({}, 'missing');").IsUndefined.Should().BeTrue();
    }

    [Fact]
    public void Reflect_defineProperty_returns_boolean()
    {
        Eval(@"
            var o = {};
            Reflect.defineProperty(o, 'x', { value: 7, writable: true, enumerable: true, configurable: true });
        ").AsBool.Should().BeTrue();
        Eval(@"
            var o = {};
            Reflect.defineProperty(o, 'x', { value: 7, writable: true, enumerable: true, configurable: true });
            o.x;
        ").AsNumber.Should().Be(7);
    }

    [Fact]
    public void Reflect_getPrototypeOf_and_setPrototypeOf_work()
    {
        Eval("Reflect.getPrototypeOf({}) === Object.prototype;").AsBool.Should().BeTrue();
        Eval(@"
            var o = {};
            Reflect.setPrototypeOf(o, null);
            Reflect.getPrototypeOf(o) === null;
        ").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Reflect_isExtensible_and_preventExtensions()
    {
        Eval("Reflect.isExtensible({});").AsBool.Should().BeTrue();
        Eval(@"
            var o = {};
            Reflect.preventExtensions(o);
            Reflect.isExtensible(o);
        ").AsBool.Should().BeFalse();
    }

    [Fact]
    public void Reflect_ownKeys_returns_a_real_array()
    {
        Eval("Array.isArray(Reflect.ownKeys({a: 1, b: 2}));").AsBool.Should().BeTrue();
        Eval(@"
            var keys = Reflect.ownKeys({a: 1, b: 2});
            keys.length + ':' + keys[0] + ',' + keys[1];
        ").AsString.Should().Be("2:a,b");
    }

    [Fact]
    public void Reflect_apply_uses_explicit_thisArg_and_args()
    {
        Eval(@"
            function fn(a, b) { return this.x + a + b; }
            Reflect.apply(fn, { x: 10 }, [2, 3]);
        ").AsNumber.Should().Be(15);
    }

    [Fact]
    public void Reflect_construct_invokes_constructor_with_arg_list()
    {
        Eval(@"
            function Box(x) { this.x = x; }
            Reflect.construct(Box, [5]).x;
        ").AsNumber.Should().Be(5);
    }

    [Fact]
    public void Reflect_apply_throws_on_non_callable_target()
    {
        Action act = () => Eval("Reflect.apply({}, null, []);");
        act.Should().Throw<JsThrow>();
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

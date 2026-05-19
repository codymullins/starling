using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for ECMA-262 §28.2 Proxy + §10.5 trap semantics.</summary>
public class ProxyTests
{
    [Fact]
    public void Empty_handler_is_transparent_to_target_properties()
    {
        Eval("var p = new Proxy({a:1}, {}); p.a;").AsNumber.Should().Be(1);
        // `in` operator isn't lowered yet (wp:M3-05); use Reflect.has for the same semantic.
        Eval("var p = new Proxy({a:1}, {}); Reflect.has(p, 'a');").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Get_trap_intercepts_property_reads()
    {
        Eval(@"
            var p = new Proxy({}, { get: function(t, k) { return k + '!'; } });
            p.foo;
        ").AsString.Should().Be("foo!");
    }

    [Fact]
    public void Set_trap_observes_side_effects_and_signals_success()
    {
        // Trap captures side effects on a host object (free-variable assignment
        // from inside a nested function would write to a different binding given
        // the script-local/global split in the current compiler).
        Eval(@"
            var box = { observed: null };
            var p = new Proxy({}, {
                set: function(t, k, v) { box.observed = k + '=' + v; return true; }
            });
            p.x = 7;
            box.observed;
        ").AsString.Should().Be("x=7");
    }

    [Fact]
    public void Has_trap_drives_in_operator()
    {
        // `in` operator pending (wp:M3-05); Reflect.has hits the same trap.
        Eval("var p = new Proxy({}, { has: function() { return true; } }); Reflect.has(p, 'foo');").AsBool.Should().BeTrue();
        Eval("var p = new Proxy({a:1}, { has: function() { return false; } }); Reflect.has(p, 'a');").AsBool.Should().BeFalse();
    }

    [Fact]
    public void DeleteProperty_trap_drives_delete_operator()
    {
        // `delete` operator + global `++` aren't lowered yet (wp:M3-05);
        // Reflect.deleteProperty + box-counted hits exercise the same trap path.
        Eval(@"
            var box = { hits: 0 };
            var p = new Proxy({a:1}, {
                deleteProperty: function(t, k) { box.hits = box.hits + 1; Reflect.deleteProperty(t, k); return true; }
            });
            var ok = Reflect.deleteProperty(p, 'a');
            ok ? box.hits : -1;
        ").AsNumber.Should().Be(1);
    }

    [Fact]
    public void GetOwnPropertyDescriptor_trap_drives_object_descriptor_access()
    {
        Eval(@"
            var p = new Proxy({}, {
                getOwnPropertyDescriptor: function(t, k) {
                    return { value: 42, writable: true, enumerable: true, configurable: true };
                }
            });
            Object.getOwnPropertyDescriptor(p, 'x').value;
        ").AsNumber.Should().Be(42);
    }

    [Fact]
    public void DefineProperty_trap_intercepts_object_defineProperty()
    {
        Eval(@"
            var box = { seenKey: null };
            var p = new Proxy({}, {
                defineProperty: function(t, k, d) { box.seenKey = k; return true; }
            });
            Object.defineProperty(p, 'foo', { value: 1, writable: true, enumerable: true, configurable: true });
            box.seenKey;
        ").AsString.Should().Be("foo");
    }

    [Fact]
    public void GetPrototypeOf_and_setPrototypeOf_traps_route_correctly()
    {
        Eval(@"
            var fake = { tag: 'fake-proto' };
            var p = new Proxy({}, { getPrototypeOf: function(t) { return fake; } });
            Object.getPrototypeOf(p).tag;
        ").AsString.Should().Be("fake-proto");
        Eval(@"
            var box = { hits: 0 };
            var p = new Proxy({}, { setPrototypeOf: function(t, v) { box.hits = box.hits + 1; return true; } });
            Object.setPrototypeOf(p, {});
            box.hits;
        ").AsNumber.Should().Be(1);
    }

    [Fact]
    public void IsExtensible_and_preventExtensions_traps_route_correctly()
    {
        Eval(@"
            var p = new Proxy({}, { isExtensible: function(t) { return Object.isExtensible(t); } });
            Object.isExtensible(p);
        ").AsBool.Should().BeTrue();
        Eval(@"
            var box = { hits: 0 };
            var t = {};
            var p = new Proxy(t, {
                preventExtensions: function(target) { box.hits = box.hits + 1; Object.preventExtensions(target); return true; }
            });
            Object.preventExtensions(p);
            box.hits + (Object.isExtensible(p) ? 100 : 0);
        ").AsNumber.Should().Be(1);
    }

    [Fact]
    public void OwnKeys_trap_drives_object_keys()
    {
        var r = Eval(@"
            var p = new Proxy({}, { ownKeys: function() { return ['a', 'b', 'c']; }, getOwnPropertyDescriptor: function(t,k) {
                return { value: 1, writable: true, enumerable: true, configurable: true };
            } });
            var keys = Object.keys(p);
            keys.length + ':' + keys[0] + ',' + keys[1] + ',' + keys[2];
        ");
        r.AsString.Should().Be("3:a,b,c");
    }

    [Fact]
    public void Apply_trap_intercepts_function_call()
    {
        Eval(@"
            var p = new Proxy(function(){}, {
                apply: function(t, this_, args) { return args[0] * 2; }
            });
            p(5);
        ").AsNumber.Should().Be(10);
    }

    [Fact]
    public void Construct_trap_intercepts_new_call()
    {
        Eval(@"
            function Base() {}
            var P = new Proxy(Base, {
                construct: function(t, args, nt) { return { x: 7 }; }
            });
            new P().x;
        ").AsNumber.Should().Be(7);
    }

    [Fact]
    public void OwnKeys_invariant_throws_when_non_extensible_target_omits_keys()
    {
        Action act = () => Eval(@"
            var t = { a: 1 };
            Object.preventExtensions(t);
            var p = new Proxy(t, { ownKeys: function() { return []; } });
            Object.keys(p);
        ");
        act.Should().Throw<JsThrow>();
    }

    [Fact]
    public void Revocable_returns_proxy_and_revoke_that_invalidates_access()
    {
        Eval(@"
            var pair = Proxy.revocable({a: 1}, {});
            pair.proxy.a;
        ").AsNumber.Should().Be(1);

        Action act = () => Eval(@"
            var pair = Proxy.revocable({a: 1}, {});
            pair.revoke();
            pair.proxy.a;
        ");
        act.Should().Throw<JsThrow>();
    }

    [Fact]
    public void Proxy_is_callable_only_if_target_is_callable()
    {
        // Calling a proxy wrapping a non-callable target should throw.
        Action act = () => Eval(@"
            var p = new Proxy({}, {});
            p();
        ");
        act.Should().Throw<JsThrow>();
    }

    [Fact]
    public void Get_trap_invariant_throws_when_returning_different_value_for_non_configurable_non_writable()
    {
        Action act = () => Eval(@"
            var t = {};
            Object.defineProperty(t, 'x', { value: 42, writable: false, configurable: false });
            var p = new Proxy(t, { get: function() { return 99; } });
            p.x;
        ");
        act.Should().Throw<JsThrow>();
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

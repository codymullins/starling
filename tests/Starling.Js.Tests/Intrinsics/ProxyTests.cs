using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for ECMA-262 §28.2 Proxy + §10.5 trap semantics.</summary>
[TestClass]
public class ProxyTests
{
    [TestMethod]
    public void Empty_handler_is_transparent_to_target_properties()
    {
        Eval("var p = new Proxy({a:1}, {}); p.a;").AsNumber.Should().Be(1);
        Eval("var p = new Proxy({a:1}, {}); 'a' in p;").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Get_trap_intercepts_property_reads()
    {
        Eval(@"
            var p = new Proxy({}, { get: function(t, k) { return k + '!'; } });
            p.foo;
        ").AsString.Should().Be("foo!");
    }

    [TestMethod]
    public void Set_trap_observes_side_effects_and_signals_success()
    {
        Eval(@"
            var observed = null;
            var p = new Proxy({}, {
                set: function(t, k, v) { observed = k + '=' + v; return true; }
            });
            p.x = 7;
            observed;
        ").AsString.Should().Be("x=7");
    }

    [TestMethod]
    public void Has_trap_drives_in_operator()
    {
        Eval("var p = new Proxy({}, { has: function() { return true; } }); 'foo' in p;").AsBool.Should().BeTrue();
        Eval("var p = new Proxy({a:1}, { has: function() { return false; } }); 'a' in p;").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void DeleteProperty_trap_drives_delete_operator()
    {
        Eval(@"
            var hits = 0;
            var p = new Proxy({a:1}, {
                deleteProperty: function(t, k) { hits++; delete t[k]; return true; }
            });
            var ok = delete p.a;
            ok ? hits : -1;
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
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

    [TestMethod]
    public void DefineProperty_trap_intercepts_object_defineProperty()
    {
        Eval(@"
            var seenKey = null;
            var p = new Proxy({}, {
                defineProperty: function(t, k, d) { seenKey = k; return true; }
            });
            Object.defineProperty(p, 'foo', { value: 1, writable: true, enumerable: true, configurable: true });
            seenKey;
        ").AsString.Should().Be("foo");
    }

    [TestMethod]
    public void GetPrototypeOf_and_setPrototypeOf_traps_route_correctly()
    {
        Eval(@"
            var fake = { tag: 'fake-proto' };
            var p = new Proxy({}, { getPrototypeOf: function(t) { return fake; } });
            Object.getPrototypeOf(p).tag;
        ").AsString.Should().Be("fake-proto");
        Eval(@"
            var hits = 0;
            var p = new Proxy({}, { setPrototypeOf: function(t, v) { hits++; return true; } });
            Object.setPrototypeOf(p, {});
            hits;
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void IsExtensible_and_preventExtensions_traps_route_correctly()
    {
        Eval(@"
            var p = new Proxy({}, { isExtensible: function(t) { return Object.isExtensible(t); } });
            Object.isExtensible(p);
        ").AsBool.Should().BeTrue();
        Eval(@"
            var hits = 0;
            var t = {};
            var p = new Proxy(t, {
                preventExtensions: function(target) { hits++; Object.preventExtensions(target); return true; }
            });
            Object.preventExtensions(p);
            hits + (Object.isExtensible(p) ? 100 : 0);
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
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

    [TestMethod]
    public void Apply_trap_intercepts_function_call()
    {
        Eval(@"
            var p = new Proxy(function(){}, {
                apply: function(t, this_, args) { return args[0] * 2; }
            });
            p(5);
        ").AsNumber.Should().Be(10);
    }

    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
    public void Proxy_is_callable_only_if_target_is_callable()
    {
        // Calling a proxy wrapping a non-callable target should throw.
        Action act = () => Eval(@"
            var p = new Proxy({}, {});
            p();
        ");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
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

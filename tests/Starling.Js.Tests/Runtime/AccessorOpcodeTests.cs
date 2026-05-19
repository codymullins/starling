using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// gap:opcode-fast-path-bypasses-accessors — the VM's <c>LoadGlobal</c>,
/// <c>StoreGlobal</c>, and object-spread opcodes used to consult
/// <see cref="JsObject.Get(string)"/>'s data-only path and silently return
/// <c>undefined</c> (or skip) for accessor descriptors. These end-to-end tests
/// pin the fix that routes every property read/write through
/// <c>AbstractOperations.Get</c> / <c>AbstractOperations.Set</c> so getters
/// and setters are invoked.
/// </summary>
[TestClass]
public class AccessorOpcodeTests
{
    [TestMethod]
    public void Global_accessor_read_invokes_getter()
    {
        Eval(@"
            Object.defineProperty(globalThis, 'foo', { get: () => 42 });
            foo;
        ").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Global_accessor_write_invokes_setter()
    {
        Eval(@"
            var hit = [];
            Object.defineProperty(globalThis, 'bar', { set: function(v) { hit[0] = v; } });
            bar = 99;
            hit[0];
        ").AsNumber.Should().Be(99);
    }

    [TestMethod]
    public void Global_accessor_no_setter_silently_ignored_in_sloppy_mode()
    {
        // No setter — write should be a no-op (sloppy mode); subsequent read
        // still returns the getter's value.
        Eval(@"
            Object.defineProperty(globalThis, 'baz', { get: () => 7 });
            try { baz = 999; } catch (e) {}
            baz;
        ").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Property_accessor_read_invokes_getter()
    {
        Eval(@"
            var o = {};
            Object.defineProperty(o, 'x', { get: () => 7 });
            o.x;
        ").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Property_accessor_write_invokes_setter()
    {
        Eval(@"
            var o = {}, captured = [];
            Object.defineProperty(o, 'y', { set: function(v) { captured[0] = v; } });
            o.y = 5;
            captured[0];
        ").AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Object_defineProperty_chained_getter()
    {
        Eval(@"
            var o = {};
            Object.defineProperty(o, 'z', { get: function() { return 11; } });
            o.z;
        ").AsNumber.Should().Be(11);
    }

    [TestMethod]
    public void Inherited_accessor_read_via_prototype_chain()
    {
        Eval(@"
            var proto = {};
            Object.defineProperty(proto, 'a', { get: function() { return 'p'; } });
            var o = Object.create(proto);
            o.a;
        ").AsString.Should().Be("p");
    }

    [TestMethod]
    public void Accessor_getter_receives_this_as_receiver()
    {
        Eval(@"
            var o = { x: 9 };
            Object.defineProperty(o, 'y', { get: function() { return this.x; } });
            o.y;
        ").AsNumber.Should().Be(9);
    }

    [TestMethod]
    public void Object_spread_invokes_source_getters()
    {
        // §7.3.27 CopyDataProperties — spread of an object with an accessor
        // must call the getter and copy the value into the new object as data.
        Eval(@"
            var src = {};
            Object.defineProperty(src, 'k', {
                enumerable: true,
                get: function() { return 'spread-' + 41; }
            });
            var dst = { ...src };
            dst.k;
        ").AsString.Should().Be("spread-41");
    }

    [TestMethod]
    public void Rest_object_invokes_source_getters()
    {
        // Destructuring rest also goes through CopyDataProperties — accessors
        // on the source must be invoked.
        Eval(@"
            var src = { skipped: 1 };
            Object.defineProperty(src, 'k', {
                enumerable: true,
                get: function() { return 21 * 2; }
            });
            var { skipped, ...rest } = src;
            rest.k;
        ").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Global_data_property_round_trips_unchanged()
    {
        // Regression guard: the AO-routed fast path must still write/read plain
        // data globals correctly (the most common case).
        Eval(@"
            var n = 0;
            n = n + 5;
            n;
        ").AsNumber.Should().Be(5);
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

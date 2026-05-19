using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for <c>Map</c> (B3-3).</summary>
public class MapTests
{
    [Fact]
    public void Empty_map_has_size_zero_and_constructor_wired()
    {
        var rt = new JsRuntime();
        var Map_ = rt.GetGlobal("Map");
        Map_.IsObject.Should().BeTrue();
        rt.Realm.MapConstructor.Should().BeSameAs(Map_.AsObject);

        Eval("new Map().size;").AsNumber.Should().Be(0);
    }

    [Fact]
    public void Map_from_iterable_of_entries()
    {
        Eval("new Map([[1,'a'],[2,'b']]).get(1);").AsString.Should().Be("a");
        Eval("new Map([[1,'a'],[2,'b']]).get(2);").AsString.Should().Be("b");
        Eval("new Map([[1,'a'],[2,'b']]).size;").AsNumber.Should().Be(2);
    }

    [Fact]
    public void Set_returns_the_map_and_chains()
    {
        Eval("new Map().set('a', 1).set('b', 2).size;").AsNumber.Should().Be(2);
    }

    [Fact]
    public void Has_delete_clear_work()
    {
        var src = @"
            var m = new Map();
            m.set('a', 1);
            m.set('b', 2);
            var r = m.has('a') + ',' + m.has('z');
            m.delete('a');
            r = r + ',' + m.has('a') + ',' + m.size;
            m.clear();
            r = r + ',' + m.size;
            r;";
        Eval(src).AsString.Should().Be("true,false,false,1,0");
    }

    [Fact]
    public void Insertion_order_preserved_for_keys()
    {
        var r = Eval(@"
            var m = new Map();
            m.set(2,'b');
            m.set(1,'a');
            var out = '';
            var it = m.keys();
            var n = it.next(); out += n.value;
            n = it.next(); out += ',' + n.value;
            n = it.next(); out += ',' + (n.done ? 'done' : n.value);
            out;");
        r.AsString.Should().Be("2,1,done");
    }

    [Fact]
    public void NaN_keys_collapse_to_one_entry()
    {
        var r = Eval(@"
            var m = new Map();
            m.set(NaN, 1);
            m.set(NaN, 2);
            m.size + ':' + m.get(NaN);");
        r.AsString.Should().Be("1:2");
    }

    [Fact]
    public void Plus_zero_and_minus_zero_are_the_same_key()
    {
        var r = Eval(@"
            var m = new Map();
            m.set(+0, 'p');
            m.set(-0, 'm');
            m.size + ':' + m.get(0);");
        r.AsString.Should().Be("1:m");
    }

    [Fact]
    public void ForEach_invokes_callback_with_value_key_map()
    {
        // Callback writes through a captured object reference because the
        // compiler does not yet support write-back through upvalue snapshots
        // (wp:M3-04c2). The compound `+=` operator on a property also reads
        // the snapshotted base value, so we use explicit `obj.x = obj.x + …`
        // to force fresh reads each iteration. Both points are tracked
        // separately; the forEach intrinsic itself is correct.
        var r = Eval(@"
            var m = new Map();
            m.set('a', 1); m.set('b', 2);
            var ctx = { out: '' };
            m.forEach(function(v, k) { ctx.out = ctx.out + k + '=' + v + ';'; });
            ctx.out;");
        r.AsString.Should().Be("a=1;b=2;");
    }

    [Fact]
    public void For_of_over_entries_yields_pairs()
    {
        var r = Eval(@"
            var m = new Map();
            m.set('a', 1); m.set('b', 2);
            var out = '';
            for (var pair of m.entries()) { out += pair[0] + '=' + pair[1] + ';'; }
            out;");
        r.AsString.Should().Be("a=1;b=2;");
    }

    [Fact]
    public void For_of_over_map_uses_entries_via_symbol_iterator()
    {
        var r = Eval(@"
            var m = new Map();
            m.set('a', 1); m.set('b', 2);
            var out = '';
            for (var pair of m) { out += pair[0] + '=' + pair[1] + ';'; }
            out;");
        r.AsString.Should().Be("a=1;b=2;");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

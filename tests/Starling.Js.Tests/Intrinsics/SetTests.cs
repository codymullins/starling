using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for <c>Set</c> + ES2025 set-theoretic
/// operations (B3-3).</summary>
[TestClass]
public class SetTests
{
    [TestMethod]
    public void Set_constructor_wired_and_iterable_init()
    {
        var rt = new JsRuntime();
        rt.GetGlobal("Set").IsObject.Should().BeTrue();
        rt.Realm.SetConstructor.Should().NotBeNull();

        Eval("new Set([1,2,3]).size;").AsNumber.Should().Be(3);
        Eval("new Set([1,1,2,2,3]).size;").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Add_returns_set_and_chains()
    {
        Eval("new Set().add(1).add(2).add(3).size;").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Has_delete_clear()
    {
        var r = Eval(@"
            var s = new Set([1,2,3]);
            var out = s.has(2) + ',' + s.has(99);
            s.delete(2);
            out += ',' + s.has(2) + ',' + s.size;
            s.clear();
            out += ',' + s.size;
            out;");
        r.AsString.Should().Be("true,false,false,2,0");
    }

    [TestMethod]
    public void For_of_yields_values_in_insertion_order()
    {
        var r = Eval(@"
            var s = new Set();
            s.add('a'); s.add('b'); s.add('c');
            var out = '';
            for (var v of s) out += v;
            out;");
        r.AsString.Should().Be("abc");
    }

    [TestMethod]
    public void Entries_returns_pair_of_value_value()
    {
        var r = Eval(@"
            var s = new Set([1, 2]);
            var it = s.entries();
            var n = it.next();
            n.value[0] + ':' + n.value[1];");
        r.AsString.Should().Be("1:1");
    }

    [TestMethod]
    public void Union_combines_unique_values()
    {
        var r = Eval(@"
            var a = new Set([1,2,3]);
            var b = new Set([3,4,5]);
            var u = a.union(b);
            u.size + ':' + u.has(1) + ',' + u.has(5);");
        r.AsString.Should().Be("5:true,true");
    }

    [TestMethod]
    public void Intersection_keeps_only_shared()
    {
        var r = Eval(@"
            var a = new Set([1,2,3]);
            var b = new Set([2,3,4]);
            var i = a.intersection(b);
            i.size + ':' + i.has(2) + ',' + i.has(3) + ',' + i.has(1);");
        r.AsString.Should().Be("2:true,true,false");
    }

    [TestMethod]
    public void Difference_excludes_other_members()
    {
        var r = Eval(@"
            var a = new Set([1,2,3]);
            var b = new Set([2,3]);
            var d = a.difference(b);
            d.size + ':' + d.has(1) + ',' + d.has(2);");
        r.AsString.Should().Be("1:true,false");
    }

    [TestMethod]
    public void SymmetricDifference_excludes_shared_keeps_unique()
    {
        var r = Eval(@"
            var a = new Set([1,2,3]);
            var b = new Set([3,4,5]);
            var d = a.symmetricDifference(b);
            d.size + ':' + d.has(1) + ',' + d.has(3) + ',' + d.has(5);");
        r.AsString.Should().Be("4:true,false,true");
    }

    [TestMethod]
    public void IsSubsetOf_returns_correct_boolean()
    {
        Eval("new Set([1,2]).isSubsetOf(new Set([1,2,3]));").AsBool.Should().BeTrue();
        Eval("new Set([1,4]).isSubsetOf(new Set([1,2,3]));").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void IsSupersetOf_returns_correct_boolean()
    {
        Eval("new Set([1,2,3]).isSupersetOf(new Set([1,2]));").AsBool.Should().BeTrue();
        Eval("new Set([1,2]).isSupersetOf(new Set([1,2,3]));").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void IsDisjointFrom_returns_correct_boolean()
    {
        Eval("new Set([1,2]).isDisjointFrom(new Set([3,4]));").AsBool.Should().BeTrue();
        Eval("new Set([1,2]).isDisjointFrom(new Set([2,3]));").AsBool.Should().BeFalse();
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

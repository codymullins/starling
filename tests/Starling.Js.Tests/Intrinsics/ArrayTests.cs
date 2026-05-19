using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>
/// End-to-end coverage for the <c>Array</c> intrinsic + dense
/// <see cref="JsArray"/> exotic (B2-4). Iterator-protocol returns
/// (<c>entries</c>/<c>keys</c>/<c>values</c>) are array snapshots until
/// B3-2 wires the real protocol; tests probe via index access.
/// </summary>
[TestClass]
public class ArrayTests
{
    [TestMethod]
    public void Array_is_registered_on_global_and_isArray_works()
    {
        var rt = new JsRuntime();
        var Array_ = rt.GetGlobal("Array");
        Array_.IsObject.Should().BeTrue();
        rt.Realm.ArrayConstructor.Should().BeSameAs(Array_.AsObject);

        Eval("Array.isArray([]);").AsBool.Should().BeTrue();
        Eval("Array.isArray({});").AsBool.Should().BeFalse();
        Eval("Array.isArray('');").AsBool.Should().BeFalse();
        Eval("Array.isArray(null);").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void New_Array_with_no_args_is_empty_array()
    {
        var r = Eval("var a = new Array(); Array.isArray(a) + ',' + a.length;");
        r.AsString.Should().Be("true,0");
    }

    [TestMethod]
    public void New_Array_with_single_number_creates_length_holes()
    {
        var r = Eval("var a = new Array(3); Array.isArray(a) + ',' + a.length + ',' + (typeof a[0]);");
        r.AsString.Should().Be("true,3,undefined");
    }

    [TestMethod]
    public void New_Array_with_multiple_args_creates_dense_array()
    {
        var r = Eval("var a = new Array(1, 2, 3); a.length + ':' + a[0] + ',' + a[1] + ',' + a[2];");
        r.AsString.Should().Be("3:1,2,3");
    }

    [TestMethod]
    public void Array_of_treats_single_number_as_element_not_length()
    {
        var r = Eval("var a = Array.of(7); Array.isArray(a) + ',' + a.length + ',' + a[0];");
        r.AsString.Should().Be("true,1,7");
    }

    [TestMethod]
    public void Array_from_array_like_with_length_and_indices()
    {
        var r = Eval("var a = Array.from({length: 3, 0: 'a', 1: 'b', 2: 'c'}); a.length + ':' + a[0] + ',' + a[1] + ',' + a[2];");
        r.AsString.Should().Be("3:a,b,c");
    }

    [TestMethod]
    public void Array_from_with_map_fn_transforms_elements()
    {
        var r = Eval("var a = Array.from([1,2,3], function(x){return x*2;}); a[0] + ',' + a[1] + ',' + a[2];");
        r.AsString.Should().Be("2,4,6");
    }

    [TestMethod]
    public void Array_from_string_yields_per_char_array()
    {
        var r = Eval("var a = Array.from('abc'); a.length + ':' + a[0] + a[1] + a[2];");
        r.AsString.Should().Be("3:abc");
    }

    [TestMethod]
    public void Length_grows_when_index_assigned_past_end()
    {
        var r = Eval("var a = []; a[5] = 'x'; a.length + ',' + a[5];");
        r.AsString.Should().Be("6,x");
    }

    [TestMethod]
    public void Setting_length_smaller_truncates_indexed_slots()
    {
        var r = Eval("var a = [1,2,3,4,5,6]; a.length = 2; a.length + ',' + a[0] + ',' + (typeof a[5]);");
        r.AsString.Should().Be("2,1,undefined");
    }

    [TestMethod]
    public void Push_returns_new_length_and_appends()
    {
        var r = Eval("var a = [1,2,3]; var n = a.push(4, 5); n + ':' + a.length + ':' + a[3] + ',' + a[4];");
        r.AsString.Should().Be("5:5:4,5");
    }

    [TestMethod]
    public void Pop_returns_last_and_shrinks()
    {
        var r = Eval("var a = [1,2,3]; var v = a.pop(); v + '|' + a.length + ':' + a[0] + ',' + a[1];");
        r.AsString.Should().Be("3|2:1,2");
    }

    [TestMethod]
    public void Pop_on_empty_returns_undefined()
    {
        var r = Eval("var a = []; var v = a.pop(); (typeof v) + ':' + a.length;");
        r.AsString.Should().Be("undefined:0");
    }

    [TestMethod]
    public void Shift_removes_first_element()
    {
        var r = Eval("var a = [1,2,3]; var v = a.shift(); v + '|' + a.length + ':' + a[0] + ',' + a[1];");
        r.AsString.Should().Be("1|2:2,3");
    }

    [TestMethod]
    public void Unshift_inserts_at_front()
    {
        var r = Eval("var a = [2,3]; var n = a.unshift(0, 1); n + ':' + a[0] + ',' + a[1] + ',' + a[2] + ',' + a[3];");
        r.AsString.Should().Be("4:0,1,2,3");
    }

    [TestMethod]
    public void Splice_returns_removed_and_inserts_replacements()
    {
        var r = Eval(@"
            var a = [1,2,3,4,5];
            var removed = a.splice(1, 2, 'a', 'b');
            removed.length + ':' + removed[0] + ',' + removed[1] + ';' + a.length + ':' + a[0] + ',' + a[1] + ',' + a[2] + ',' + a[3] + ',' + a[4];
        ");
        r.AsString.Should().Be("2:2,3;5:1,a,b,4,5");
    }

    [TestMethod]
    public void Sort_default_uses_string_compare()
    {
        var r = Eval("var a = [10, 9, 1]; a.sort(); a[0] + ',' + a[1] + ',' + a[2];");
        r.AsString.Should().Be("1,10,9");
    }

    [TestMethod]
    public void Sort_with_comparator_works()
    {
        var r = Eval("var a = [3,1,2]; a.sort(function(a,b){return a-b;}); a[0] + ',' + a[1] + ',' + a[2];");
        r.AsString.Should().Be("1,2,3");
    }

    [TestMethod]
    public void Reverse_mutates_in_place()
    {
        var r = Eval("var a = [1,2,3]; var b = a.reverse(); (a === b) + ':' + a[0] + ',' + a[1] + ',' + a[2];");
        r.AsString.Should().Be("true:3,2,1");
    }

    [TestMethod]
    public void ToReversed_returns_new_array_without_mutating()
    {
        var r = Eval("var a = [1,2,3]; var b = a.toReversed(); (a === b) + ':' + a[0] + a[1] + a[2] + ':' + b[0] + b[1] + b[2];");
        r.AsString.Should().Be("false:123:321");
    }

    [TestMethod]
    public void ToSorted_returns_new_sorted_array()
    {
        var r = Eval("var a = [3,1,2]; var b = a.toSorted(function(a,b){return a-b;}); a[0] + ',' + a[1] + ',' + a[2] + ';' + b[0] + ',' + b[1] + ',' + b[2];");
        r.AsString.Should().Be("3,1,2;1,2,3");
    }

    [TestMethod]
    public void With_replaces_one_index_in_a_copy()
    {
        // `with` is a reserved word but is a valid IdentifierName after `.`
        // per ES §13.3.2; B3-4-followup-a wired this in the parser.
        var r = Eval("var a = [1,2,3]; var b = a.with(1, 'x'); a[1] + ':' + b[0] + ',' + b[1] + ',' + b[2];");
        r.AsString.Should().Be("2:1,x,3");
    }

    [TestMethod]
    public void ToSpliced_returns_new_array_with_replacement()
    {
        var r = Eval("var a = [1,2,3,4]; var b = a.toSpliced(1, 2, 'a'); a.length + ':' + b.length + ':' + b[0] + ',' + b[1] + ',' + b[2];");
        r.AsString.Should().Be("4:3:1,a,4");
    }

    [TestMethod]
    public void Concat_flattens_array_args_one_level()
    {
        var r = Eval("var a = [1,2,3].concat([4,5]); var b = [1].concat(2, [3]); a.length + ',' + a[3] + ';' + b.length + ',' + b[2];");
        r.AsString.Should().Be("5,4;3,3");
    }

    [TestMethod]
    public void Slice_negative_start_resolves_from_end()
    {
        var r = Eval("var a = [1,2,3,4,5]; var x = a.slice(1, 4); var y = a.slice(-2); x[0] + ',' + x[1] + ',' + x[2] + ';' + y[0] + ',' + y[1];");
        r.AsString.Should().Be("2,3,4;4,5");
    }

    [TestMethod]
    public void Join_default_separator_is_comma()
    {
        var r = Eval("[1,2,3].join('-') + ';' + [null, undefined, 1].join(',');");
        r.AsString.Should().Be("1-2-3;,,1");
    }

    [TestMethod]
    public void ToString_uses_default_join()
    {
        var r = Eval("[1,2,3].toString();");
        r.AsString.Should().Be("1,2,3");
    }

    [TestMethod]
    public void IndexOf_uses_strict_equality_NaN_returns_minus_one()
    {
        var r = Eval("[1,2,3].indexOf(2) + ',' + [NaN].indexOf(NaN);");
        r.AsString.Should().Be("1,-1");
    }

    [TestMethod]
    public void Includes_uses_SameValueZero_so_NaN_matches()
    {
        var r = Eval("[NaN].includes(NaN) + ',' + [1,2,3].includes(2) + ',' + [1,2,3].includes(99);");
        r.AsString.Should().Be("true,true,false");
    }

    [TestMethod]
    public void Map_returns_new_transformed_array()
    {
        var r = Eval("var b = [1,2,3].map(function(x){return x*2;}); b[0] + ',' + b[1] + ',' + b[2];");
        r.AsString.Should().Be("2,4,6");
    }

    [TestMethod]
    public void Filter_keeps_truthy_callback_results()
    {
        var r = Eval("var b = [1,2,3,4].filter(function(x){return x%2 === 0;}); b.length + ':' + b[0] + ',' + b[1];");
        r.AsString.Should().Be("2:2,4");
    }

    [TestMethod]
    public void Reduce_with_and_without_initial_value()
    {
        var r = Eval("[1,2,3].reduce(function(a,b){return a+b;}) + ',' + [1,2,3].reduce(function(a,b){return a+b;}, 10);");
        r.AsString.Should().Be("6,16");
    }

    [TestMethod]
    public void Every_and_some_short_circuit()
    {
        var r = Eval(@"
            [1,2,3].every(function(x){return x>0;}) + ',' +
            [1,-1,3].every(function(x){return x>0;}) + ',' +
            [1,2,3].some(function(x){return x>2;}) + ',' +
            [1,2,3].some(function(x){return x>99;});
        ");
        r.AsString.Should().Be("true,false,true,false");
    }

    [TestMethod]
    public void Find_and_findIndex_return_first_match()
    {
        var r = Eval("[1,2,3].find(function(x){return x>1;}) + ',' + [1,2,3].findIndex(function(x){return x>1;});");
        r.AsString.Should().Be("2,1");
    }

    [TestMethod]
    public void FindLast_and_findLastIndex_walk_from_end()
    {
        var r = Eval("[1,2,3,4].findLast(function(x){return x<4;}) + ',' + [1,2,3,4].findLastIndex(function(x){return x<4;});");
        r.AsString.Should().Be("3,2");
    }

    [TestMethod]
    public void Flat_default_depth_one_flattens_one_level()
    {
        var r = Eval("var b = [[1,2],[3,[4,5]]].flat(); b.length + ':' + b[0] + ',' + b[1] + ',' + b[2] + ',' + Array.isArray(b[3]);");
        r.AsString.Should().Be("4:1,2,3,true");
    }

    [TestMethod]
    public void Flat_Infinity_flattens_fully()
    {
        var r = Eval("var b = [[1,2],[3,[4,5]]].flat(Infinity); b.length + ':' + b[0] + ',' + b[1] + ',' + b[2] + ',' + b[3] + ',' + b[4];");
        r.AsString.Should().Be("5:1,2,3,4,5");
    }

    [TestMethod]
    public void FlatMap_maps_then_flattens_one_level()
    {
        var r = Eval("var b = [1,2,3].flatMap(function(x){return [x, x*10];}); b.length + ':' + b[0] + ',' + b[1] + ',' + b[2] + ',' + b[3] + ',' + b[4] + ',' + b[5];");
        r.AsString.Should().Be("6:1,10,2,20,3,30");
    }

    [TestMethod]
    public void Fill_with_default_range_fills_all()
    {
        var r = Eval("var a = [1,2,3,4]; a.fill('x'); a[0] + a[1] + a[2] + a[3];");
        r.AsString.Should().Be("xxxx");
    }

    [TestMethod]
    public void Fill_with_explicit_range_fills_only_slice()
    {
        var r = Eval("var a = [1,2,3,4,5]; a.fill('x', 1, 3); a[0] + ',' + a[1] + ',' + a[2] + ',' + a[3] + ',' + a[4];");
        r.AsString.Should().Be("1,x,x,4,5");
    }

    [TestMethod]
    public void CopyWithin_copies_in_place()
    {
        var r = Eval("var a = [1,2,3,4,5]; a.copyWithin(0, 3); a[0] + ',' + a[1] + ',' + a[2] + ',' + a[3] + ',' + a[4];");
        r.AsString.Should().Be("4,5,3,4,5");
    }

    [TestMethod]
    public void New_Array_RangeError_for_invalid_length()
    {
        // try/catch isn't compiled yet; assert via the host JsThrow surface.
        var rt = new JsRuntime();
        var program = new JsParser("new Array(-1);").ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        var act = () => new JsVm(rt).Run(chunk);
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void Object_keys_returns_real_array_after_B2_4()
    {
        // The footgun fix: Object.keys etc. now return a real JsArray.
        var r = Eval("Array.isArray(Object.keys({a:1, b:2}));");
        r.AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Object_values_and_entries_return_real_arrays()
    {
        var r = Eval("Array.isArray(Object.values({a:1})) + ',' + Array.isArray(Object.entries({a:1}));");
        r.AsString.Should().Be("true,true");
    }

    [TestMethod]
    public void Array_length_property_descriptor_is_non_configurable()
    {
        // Probing through getOwnPropertyDescriptor.
        var r = Eval(@"
            var a = [1,2,3];
            var d = Object.getOwnPropertyDescriptor(a, 'length');
            d.writable + ',' + d.enumerable + ',' + d.configurable + ',' + d.value;
        ");
        r.AsString.Should().Be("true,false,false,3");
    }

    [TestMethod]
    public void JsArray_host_side_construction_is_an_array()
    {
        var rt = new JsRuntime();
        var arr = new JsArray(rt.Realm);
        arr.Push(JsValue.Number(1));
        arr.Push(JsValue.Number(2));
        arr.Length.Should().Be(2);
        arr[0].AsNumber.Should().Be(1);
        arr[1].AsNumber.Should().Be(2);
        // The instance must round-trip through Array.isArray.
        rt.SetGlobal("h", JsValue.Object(arr));
        var program = new JsParser("Array.isArray(h);").ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        new JsVm(rt).Run(chunk).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void JsArray_IsArrayIndex_canonical_uint32_validation()
    {
        JsArray.IsArrayIndex("0", out var i0).Should().BeTrue();
        i0.Should().Be(0u);
        JsArray.IsArrayIndex("42", out var i1).Should().BeTrue();
        i1.Should().Be(42u);
        JsArray.IsArrayIndex("00", out _).Should().BeFalse();
        JsArray.IsArrayIndex("01", out _).Should().BeFalse();
        JsArray.IsArrayIndex("-1", out _).Should().BeFalse();
        JsArray.IsArrayIndex("1.5", out _).Should().BeFalse();
        JsArray.IsArrayIndex("foo", out _).Should().BeFalse();
        JsArray.IsArrayIndex("", out _).Should().BeFalse();
        JsArray.IsArrayIndex("4294967295", out _).Should().BeFalse(); // 2^32-1 reserved
    }

    // ------------------------------------------------------------- Helpers

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

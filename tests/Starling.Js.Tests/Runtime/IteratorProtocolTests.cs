using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// B3-2 — iterator protocol coverage: built-in array iterator semantics,
/// <c>for…of</c> retargeted to <c>@@iterator</c>, spread in array literals
/// and call expressions, and <c>Array.from</c> consuming iterables.
/// </summary>
[TestClass]
public class IteratorProtocolTests
{
    // ============================================================
    //                     Built-in iterators
    // ============================================================

    [TestMethod]
    public void Array_at_symbol_iterator_returns_iterator_with_next()
    {
        var n = Eval(@"
            var it = [10, 20, 30][Symbol.iterator]();
            var r1 = it.next();
            var r2 = it.next();
            var r3 = it.next();
            var r4 = it.next();
            r1.value + ',' + r1.done + '|' +
            r2.value + ',' + r2.done + '|' +
            r3.value + ',' + r3.done + '|' +
            r4.value + ',' + r4.done;
        ");
        n.AsString.Should().Be("10,false|20,false|30,false|undefined,true");
    }

    [TestMethod]
    public void Array_values_is_iterable_via_iterator_returns_self()
    {
        var v = Eval(@"
            var it = [1,2,3].values();
            it[Symbol.iterator]() === it;
        ");
        v.AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Array_keys_walks_indexes()
    {
        var v = Eval(@"
            var out = [];
            var it = ['a','b','c'].keys();
            var r;
            while (!(r = it.next()).done) out[out.length] = r.value;
            out[0] + ',' + out[1] + ',' + out[2] + '|' + out.length;
        ");
        v.AsString.Should().Be("0,1,2|3");
    }

    [TestMethod]
    public void Array_entries_walks_key_value_pairs()
    {
        var v = Eval(@"
            var it = ['x','y'].entries();
            var a = it.next().value;
            var b = it.next().value;
            a[0] + ':' + a[1] + ',' + b[0] + ':' + b[1];
        ");
        v.AsString.Should().Be("0:x,1:y");
    }

    [TestMethod]
    public void Iterator_prototype_chain_carries_symbol_iterator()
    {
        // Walk one step up — ArrayIterator inherits from %IteratorPrototype%
        // which has Symbol.iterator returning this.
        var v = Eval(@"
            var it = [1].values();
            typeof it[Symbol.iterator];
        ");
        v.AsString.Should().Be("function");
    }

    // ============================================================
    //                          for…of
    // ============================================================

    [TestMethod]
    public void ForOf_sums_array_values()
    {
        Eval("var s = 0; for (var x of [1,2,3]) s = s + x; s;").AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void ForOf_walks_string_by_code_point()
    {
        // "ab😀" is 4 UTF-16 code units but 3 code points.
        var v = Eval(@"
            var out = [];
            for (var c of 'ab😀') out[out.length] = c;
            out.length + '|' + out[0] + '|' + out[1] + '|' + out[2];
        ");
        v.AsString.Should().Be("3|a|b|😀");
    }

    [TestMethod]
    public void ForOf_iterates_user_defined_iterable()
    {
        // Use a state object so the closure mutates a property (supported)
        // rather than incrementing a captured local (UpdateExpression on
        // upvalues is M3-05's job, not B3-2's).
        var v = Eval(@"
            var obj = {};
            obj[Symbol.iterator] = function() {
                var state = { i: 0 };
                return {
                    next: function() {
                        if (state.i < 3) {
                            var r = { value: state.i, done: false };
                            state.i = state.i + 1;
                            return r;
                        }
                        return { value: undefined, done: true };
                    }
                };
            };
            var s = 0;
            for (var x of obj) s = s + x;
            s;
        ");
        v.AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void ForOf_uses_array_iterator()
    {
        var v = Eval(@"
            var pairs = [];
            for (var p of [10, 20, 30].entries()) pairs[pairs.length] = p[0] + ':' + p[1];
            pairs.join('/');
        ");
        v.AsString.Should().Be("0:10/1:20/2:30");
    }

    // ============================================================
    //                          Spread
    // ============================================================

    [TestMethod]
    public void Spread_in_array_literal_with_trailing()
    {
        var v = Eval(@"
            var arr = [...[1,2,3], 4];
            arr.length + ':' + arr[0] + ',' + arr[1] + ',' + arr[2] + ',' + arr[3];
        ");
        v.AsString.Should().Be("4:1,2,3,4");
    }

    [TestMethod]
    public void Spread_in_call_args()
    {
        Eval("Math.max(...[1, 9, 3]);").AsNumber.Should().Be(9);
    }

    [TestMethod]
    public void Spread_string_into_array_yields_code_points()
    {
        var v = Eval("var a = [...'ab']; a.length + ':' + a[0] + ',' + a[1];");
        v.AsString.Should().Be("2:a,b");
    }

    [TestMethod]
    public void Spread_user_iterable_into_array()
    {
        var v = Eval(@"
            var obj = {};
            obj[Symbol.iterator] = function() {
                var state = { i: 1 };
                return { next: function() {
                    if (state.i <= 3) {
                        var r = { value: state.i, done: false };
                        state.i = state.i + 1;
                        return r;
                    }
                    return { value: undefined, done: true };
                }};
            };
            var a = [...obj];
            a.length + ':' + a[0] + ',' + a[1] + ',' + a[2];
        ");
        v.AsString.Should().Be("3:1,2,3");
    }

    [TestMethod]
    public void Spread_before_and_after_plain_args()
    {
        var v = Eval(@"
            var a = [0, ...[1,2], 3, ...[4,5]];
            a.length + ':' + a[0] + a[1] + a[2] + a[3] + a[4] + a[5];
        ");
        v.AsString.Should().Be("6:012345");
    }

    // ============================================================
    //                       Array.from
    // ============================================================

    [TestMethod]
    public void ArrayFrom_string_yields_array_of_characters()
    {
        var v = Eval(@"
            var a = Array.from('abc');
            a.length + ':' + a[0] + a[1] + a[2];
        ");
        v.AsString.Should().Be("3:abc");
    }

    [TestMethod]
    public void ArrayFrom_user_iterable()
    {
        var v = Eval(@"
            var obj = {};
            obj[Symbol.iterator] = function() {
                var state = { i: 0, seq: ['x', 'y'] };
                return { next: function() {
                    if (state.i < state.seq.length) {
                        var r = { value: state.seq[state.i], done: false };
                        state.i = state.i + 1;
                        return r;
                    }
                    return { value: undefined, done: true };
                }};
            };
            var a = Array.from(obj);
            a.length + ':' + a[0] + ',' + a[1];
        ");
        v.AsString.Should().Be("2:x,y");
    }

    [TestMethod]
    public void ArrayFrom_array_like_without_iterator()
    {
        var v = Eval(@"
            var a = Array.from({ length: 3, 0: 'a', 1: 'b', 2: 'c' });
            a.length + ':' + a[0] + a[1] + a[2];
        ");
        v.AsString.Should().Be("3:abc");
    }

    [TestMethod]
    public void ArrayFrom_with_map_fn()
    {
        var v = Eval(@"
            var a = Array.from([1,2,3], function(x) { return x * 10; });
            a.length + ':' + a[0] + ',' + a[1] + ',' + a[2];
        ");
        v.AsString.Should().Be("3:10,20,30");
    }

    // ============================================================
    //          TypedArrays are iterable (§23.2.3.36)
    // ============================================================

    [TestMethod]
    public void ForOf_iterates_typed_array()
    {
        // %TypedArray%.prototype[@@iterator] === %TypedArray%.prototype.values.
        // Before the fix this threw "value is not iterable".
        var v = Eval(@"
            var ta = new Int32Array([5, 10, 15]);
            var s = 0;
            for (var x of ta) s = s + x;
            s;
        ");
        v.AsNumber.Should().Be(30);
    }

    [TestMethod]
    public void TypedArray_symbol_iterator_is_values()
    {
        var v = Eval("Uint8Array.prototype[Symbol.iterator] === Uint8Array.prototype.values;");
        v.AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Spread_typed_array_into_array_literal()
    {
        var v = Eval(@"
            var a = [...new Float64Array([1, 2, 3])];
            a.length + ':' + a[0] + ',' + a[1] + ',' + a[2];
        ");
        v.AsString.Should().Be("3:1,2,3");
    }

    [TestMethod]
    public void Array_destructuring_a_typed_array()
    {
        var v = Eval(@"
            var [a, b, c] = new Uint16Array([7, 8, 9]);
            a + ',' + b + ',' + c;
        ");
        v.AsString.Should().Be("7,8,9");
    }

    // ============================================================
    //          Inherited / getter-provided @@iterator
    // ============================================================

    [TestMethod]
    public void ForOf_uses_inherited_symbol_iterator_from_prototype()
    {
        // @@iterator lives on the prototype, not the own object — GetIterator
        // must walk the prototype chain (§7.4.1 → GetMethod → [[Get]]).
        var v = Eval(@"
            var proto = {};
            proto[Symbol.iterator] = function() {
                var i = 0;
                return { next: function() {
                    return i < 3
                        ? { value: i++, done: false }
                        : { value: undefined, done: true };
                }};
            };
            var obj = Object.create(proto);
            var s = 0;
            for (var x of obj) s += x;
            s;
        ");
        v.AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Spread_object_whose_iterator_comes_from_a_getter()
    {
        // @@iterator resolved through an accessor (getter) must still work.
        var v = Eval(@"
            var obj = {
                get [Symbol.iterator]() {
                    return function() {
                        var i = 1;
                        return { next: function() {
                            return i <= 2
                                ? { value: i++, done: false }
                                : { value: undefined, done: true };
                        }};
                    };
                }
            };
            var a = [...obj];
            a.length + ':' + a[0] + ',' + a[1];
        ");
        v.AsString.Should().Be("2:1,2");
    }

    // ============================================================
    //                         Helpers
    // ============================================================

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// B7-followup-b + B3-2-followup-a — pins the bytecode lowering for
/// <c>for</c>, <c>do…while</c>, <c>for…in</c>, and the <c>break</c> /
/// <c>continue</c> jumps that thread through them. <c>for…of</c>'s
/// IteratorClose-on-break path lives in <see cref="IteratorProtocolTests"/>
/// but a duplicate pin lives here so a regression in the loop frame stack
/// surfaces immediately.
/// </summary>
[TestClass]
public class JsForLoopTests
{
    // -----------------------------------------------------------------------
    // ForStatement (B7-followup-b)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void For_basic_sum()
    {
        Eval("var s = 0; for (var i = 0; i < 5; i++) s += i; s")
            .AsNumber.Should().Be(10);
    }

    [TestMethod]
    public void For_with_empty_init_test_update_runs_until_break()
    {
        Eval("var n = 0; for (;;) { n++; if (n >= 3) break } n")
            .AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void For_let_binding_collects_values()
    {
        Eval(@"
            var out = [];
            for (let i = 0; i < 3; i++) out.push(i);
            out.join(',')
        ").AsString.Should().Be("0,1,2");
    }

    [TestMethod]
    public void For_init_only_assignment_to_existing_binding()
    {
        Eval("var i; for (i = 0; i < 3; i++); i")
            .AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void For_no_update_clause()
    {
        Eval("var i; for (i = 0; i < 3;) { i++ } i")
            .AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void For_no_test_clause_loops_until_break()
    {
        Eval("var s = 0; for (var i = 0;; i++) { if (i >= 4) break; s += i } s")
            .AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void For_inside_function_returns_accumulated_sum()
    {
        Eval(@"
            function f() {
              var s = 0;
              for (var i = 0; i < 4; i++) s += i;
              return s;
            }
            f()
        ").AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void For_with_expression_init_evaluates_side_effects()
    {
        Eval(@"
            var n = 0;
            for (n = 7; n < 10; n++);
            n
        ").AsNumber.Should().Be(10);
    }

    // -----------------------------------------------------------------------
    // break / continue (B3-2-followup-a)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void For_break_exits_early()
    {
        Eval("var s = 0; for (var i = 0; i < 10; i++) { if (i === 3) break; s += i } s")
            .AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void For_continue_skips_iteration()
    {
        Eval("var s = 0; for (var i = 0; i < 5; i++) { if (i === 2) continue; s += i } s")
            .AsNumber.Should().Be(8);
    }

    [TestMethod]
    public void While_break_exits()
    {
        Eval("var i = 0; while (true) { if (i === 5) break; i++ } i")
            .AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void While_continue_jumps_to_test()
    {
        Eval("var s = 0, i = 0; while (i < 5) { i++; if (i === 2) continue; s += i } s")
            .AsNumber.Should().Be(13);
    }

    [TestMethod]
    public void Break_exits_only_inner_loop()
    {
        Eval(@"
            var s = 0;
            for (var i = 0; i < 3; i++)
              for (var j = 0; j < 3; j++) {
                if (j === 1) break;
                s++;
              }
            s
        ").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Continue_in_nested_for_skips_inner_only()
    {
        Eval(@"
            var s = 0;
            for (var i = 0; i < 3; i++)
              for (var j = 0; j < 3; j++) {
                if (j === 1) continue;
                s++;
              }
            s
        ").AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void Break_inside_for_of_invokes_iterator_close()
    {
        // Drives the for…of cleanup path: a user iterable with a `return()`
        // method must see it called when `break` fires inside the body.
        Eval(@"
            var closed = false;
            var iterable = {
              [Symbol.iterator]() {
                var n = 0;
                return {
                  next() { return n < 5 ? { value: n++, done: false } : { value: undefined, done: true } },
                  return() { closed = true; return { value: undefined, done: true } }
                };
              }
            };
            var found;
            for (const x of iterable) {
              if (x === 2) { found = x; break }
            }
            found === 2 && closed === true
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Continue_inside_for_of_skips_one_iteration()
    {
        Eval(@"
            var s = 0;
            for (const x of [1, 2, 3, 4]) {
              if (x === 2) continue;
              s += x;
            }
            s
        ").AsNumber.Should().Be(8);
    }

    [TestMethod]
    public void Break_outside_any_loop_is_compile_error()
    {
        // The parser permits `break;` anywhere; the compiler raises the
        // §13.2 "Illegal break" syntactic check. This is an early SyntaxError,
        // so it surfaces as a JsParseException (the engine's SyntaxError type).
        var src = "break;";
        var program = new JsParser(src).ParseProgram();
        var act = () => JsCompiler.CompileForEval(program);
        act.Should().Throw<JsParseException>()
            .WithMessage("*Illegal break*");
    }

    // -----------------------------------------------------------------------
    // do…while
    // -----------------------------------------------------------------------

    [TestMethod]
    public void DoWhile_increments_until_test_false()
    {
        Eval("var i = 0; do { i++ } while (i < 3); i")
            .AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void DoWhile_runs_at_least_once_even_when_test_false()
    {
        Eval("var ran = false; do { ran = true } while (false); ran")
            .AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void DoWhile_break_inside_body()
    {
        Eval("var i = 0; do { i++; if (i === 2) break } while (true); i")
            .AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void DoWhile_continue_jumps_to_test()
    {
        // The continue must land at the test (not the loop top), so the
        // post-test increment / decrement decides whether to loop again.
        // Pin: continue skips the rest of the body but does not bypass the
        // test, matching §14.7.2.
        Eval(@"
            var s = 0, i = 0;
            do {
              i++;
              if (i === 2) continue;
              s += i;
            } while (i < 3);
            s
        ").AsNumber.Should().Be(4); // 1 + 3
    }

    // -----------------------------------------------------------------------
    // for…in
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ForIn_iterates_own_enumerable_string_keys()
    {
        Eval(@"
            var keys = [];
            for (var k in {a: 1, b: 2}) keys.push(k);
            keys.join(',')
        ").AsString.Should().Be("a,b");
    }

    [TestMethod]
    public void ForIn_iterates_inherited_enumerable_keys()
    {
        Eval(@"
            var p = {x: 1};
            var o = Object.create(p);
            o.y = 2;
            var keys = [];
            for (var k in o) keys.push(k);
            // Sort to make the assertion robust against own-vs-proto ordering
            // choices; the spec doesn't pin a total order across the chain.
            keys.sort().join(',')
        ").AsString.Should().Be("x,y");
    }

    [TestMethod]
    public void ForIn_skips_null_and_undefined()
    {
        Eval(@"
            var hit = 0;
            for (var k in null) hit++;
            for (var k in undefined) hit++;
            hit
        ").AsNumber.Should().Be(0);
    }

    [TestMethod]
    public void ForIn_snapshot_does_not_observe_mutation()
    {
        // §14.7.5.10: keys snapshotted at loop entry. Adding a new key
        // mid-iteration must not surface.
        Eval(@"
            var o = {a: 1, b: 2};
            var seen = [];
            for (var k in o) {
              seen.push(k);
              o.c = 99;
            }
            seen.join(',')
        ").AsString.Should().Be("a,b");
    }

    [TestMethod]
    public void ForIn_break_exits()
    {
        Eval(@"
            var seen = [];
            for (var k in {a: 1, b: 2, c: 3}) {
              if (k === 'b') break;
              seen.push(k);
            }
            seen.join(',')
        ").AsString.Should().Be("a");
    }

    [TestMethod]
    public void ForIn_array_iterates_indices_as_strings()
    {
        Eval(@"
            var keys = [];
            for (var k in ['x', 'y', 'z']) keys.push(k + ':' + typeof k);
            keys.join(',')
        ").AsString.Should().Be("0:string,1:string,2:string");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

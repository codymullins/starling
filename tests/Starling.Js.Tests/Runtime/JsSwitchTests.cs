using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// ECMA-262 §14.12 SwitchStatement — end-to-end coverage for the bytecode
/// compiler's switch lowering. Tests are ordered from simplest to most
/// intricate so a regression is easy to bisect.
/// </summary>
[TestClass]
[Spec("ecma262", "https://tc39.es/ecma262/#sec-switch-statement", "14.12 The switch Statement")]
public class JsSwitchTests
{
    // -----------------------------------------------------------------------
    // Basic numeric switch + break
    // -----------------------------------------------------------------------

    [SpecFact]
    public void Switch_basic_numeric_matching_case_with_break()
    {
        // §14.12.4 Runtime Semantics: CaseClauseIsSelected
        // Discriminant evaluated once; matching case body runs; break exits.
        Eval(@"
            var r = 0;
            switch (2) {
              case 1: r = 10; break;
              case 2: r = 20; break;
              case 3: r = 30; break;
            }
            r
        ").AsNumber.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // switch(true) { case cond: } — the mcmaster.com pattern
    // -----------------------------------------------------------------------

    [SpecFact]
    public void Switch_true_idiom_selects_first_matching_condition()
    {
        // §14.12 §14.12.4: strict equality; switch(true) case-guards act as
        // if-elseif chain. This is the pattern that caused the mcmaster boot failure.
        Eval(@"
            var x = 7;
            var r = '';
            switch (true) {
              case x < 5:  r = 'lo'; break;
              case x < 10: r = 'mid'; break;
              default:     r = 'hi';
            }
            r
        ").AsString.Should().Be("mid");
    }

    // -----------------------------------------------------------------------
    // Fall-through (no break between cases)
    // -----------------------------------------------------------------------

    [SpecFact]
    public void Switch_fallthrough_without_break_runs_multiple_bodies()
    {
        // §14.12.4: when no break is present control falls to the next clause body.
        Eval(@"
            var r = 0;
            switch (1) {
              case 1: r += 1;   // fall through
              case 2: r += 2;   // fall through
              case 3: r += 4; break;
              case 4: r += 8;
            }
            r
        ").AsNumber.Should().Be(7); // 1 + 2 + 4
    }

    // -----------------------------------------------------------------------
    // default clause — no match, and default placed in MIDDLE
    // -----------------------------------------------------------------------

    [SpecFact]
    public void Switch_default_taken_when_nothing_matches()
    {
        Eval(@"
            var r = '';
            switch (99) {
              case 1: r = 'one'; break;
              case 2: r = 'two'; break;
              default: r = 'other';
            }
            r
        ").AsString.Should().Be("other");
    }

    [SpecFact]
    public void Switch_default_in_middle_is_only_selected_after_all_tests_fail()
    {
        // §14.12.4 step 8: default clause is only selected once all case tests
        // fail, regardless of where default appears in source. After selection
        // it falls through normally.
        Eval(@"
            var r = 0;
            switch (5) {
              case 1: r = 1; break;
              default: r = 99; break;
              case 5: r = 5; break;
            }
            r
        ").AsNumber.Should().Be(5); // case 5 matches; default skipped
    }

    [SpecFact]
    public void Switch_default_in_middle_runs_when_no_case_matches_with_fallthrough()
    {
        // No test matches → default body runs, then falls through into the
        // subsequent clause (case 5 body) if there's no break.
        Eval(@"
            var r = 0;
            switch (99) {
              case 1: r += 1; break;
              default: r += 10;
              case 2: r += 2; break;
            }
            r
        ").AsNumber.Should().Be(12); // default(10) falls into case 2(+2)
    }

    // -----------------------------------------------------------------------
    // Strict equality — no coercion
    // -----------------------------------------------------------------------

    [SpecFact]
    public void Switch_strict_equality_does_not_coerce_types()
    {
        // §14.12.4 step 6.a.ii: IsStrictlyEqual (===) — "1" must NOT match 1.
        Eval(@"
            var r = 'none';
            switch (1) {
              case '1': r = 'string'; break;
              case 1:   r = 'number'; break;
            }
            r
        ").AsString.Should().Be("number");
    }

    // -----------------------------------------------------------------------
    // break vs continue inside switch inside for loop
    // -----------------------------------------------------------------------

    [SpecFact]
    public void Switch_continue_inside_switch_inside_for_continues_the_for_loop()
    {
        // §14.12.4 note: `continue` is NOT consumed by the switch; it propagates
        // to the nearest enclosing iteration statement.
        Eval(@"
            var sum = 0;
            for (var i = 0; i < 5; i++) {
              switch (i) {
                case 2: continue;   // skip i==2
                default: sum += i;
              }
            }
            sum
        ").AsNumber.Should().Be(8); // 0+1+3+4 = 8 (2 skipped)
    }

    [SpecFact]
    public void Switch_break_inside_switch_does_not_break_enclosing_loop()
    {
        Eval(@"
            var count = 0;
            for (var i = 0; i < 4; i++) {
              switch (i) {
                case 2: break;   // breaks switch, not the for
              }
              count++;
            }
            count
        ").AsNumber.Should().Be(4); // all 4 iterations run
    }

    // -----------------------------------------------------------------------
    // Labeled break out of a switch
    // -----------------------------------------------------------------------

    [SpecFact]
    public void Switch_labeled_break_exits_outer_labeled_switch()
    {
        // §14.12.4 step 4.b: a labeled break targeting the switch's own label
        // exits the switch.
        Eval(@"
            var r = 0;
            outer: switch (1) {
              case 1:
                r = 1;
                break outer;
              case 2:
                r = 2;
            }
            r
        ").AsNumber.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // let/const scope + TDZ inside switch
    // -----------------------------------------------------------------------

    [SpecFact]
    public void Switch_let_declared_in_case_body_accessible_in_subsequent_clause()
    {
        // §14.12.2: switch body has one shared lexical scope — let/const
        // declared in any clause are in scope for the whole switch (with TDZ
        // before their textual declaration).
        Eval(@"
            var r = 0;
            switch (1) {
              case 1:
                let x = 42;
                r = x;
                break;
              case 2:
                r = x + 1;   // x in scope (TDZ if never initialized, but we break)
            }
            r
        ").AsNumber.Should().Be(42);
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

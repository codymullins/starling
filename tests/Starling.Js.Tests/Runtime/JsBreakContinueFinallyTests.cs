using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// wp:M3-15 — §14.15 abrupt-completion routing through <c>finally</c> for
/// <c>break</c> / <c>continue</c> that exits a loop or switch across an
/// enclosing <c>try…finally</c>. Each intervening finalizer must run on the
/// way out (innermost first), and a finalizer that itself performs an abrupt
/// completion overrides the pending one (§14.15.3 / §14.7 / §14.13).
///
/// jQuery (mcmaster.com) relies on this; before the fix the compiler threw
/// "'break' across an enclosing try/finally is not yet supported".
/// </summary>
[TestClass]
[Spec("ecma262", "https://tc39.es/ecma262/#sec-try-statement", "14.15 / 14.7")]
public class JsBreakContinueFinallyTests
{
    // Repro 1 — break out of a loop across finally: finally runs, then exit.
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-try-statement", "14.15 / 14.7")]
    public void Break_out_of_loop_runs_intervening_finally()
    {
        Eval(@"
            var log='';
            for (var i=0;i<3;i++){ try{ if(i===1) break; log+='t'+i; } finally { log+='f'; } }
            log;
        ").AsString.Should().Be("t0ff");
    }

    // Repro 2 — continue across finally: finally runs each iteration, loop continues.
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-try-statement", "14.15 / 14.7")]
    public void Continue_across_finally_runs_finally_each_iteration()
    {
        Eval(@"
            var s='';
            for (var i=0;i<3;i++){ try{ if(i===1) continue; s+='b'+i; } finally { s+='f'; } }
            s;
        ").AsString.Should().Be("b0ffb2f");
    }

    // Repro 3 — labeled break across finally jumps to the OUTER loop's exit.
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-labelled-statements", "14.13 / 14.15")]
    public void Labeled_break_outer_across_finally()
    {
        Eval(@"
            var log='';
            outer:
            for (var i=0;i<2;i++){
              for (var j=0;j<2;j++){
                try { if (i===0 && j===1) break outer; log+='x'+i+j; }
                finally { log+='f'; }
              }
            }
            log;
        ").AsString.Should().Be("x00ff");
    }

    // Repro 3b — labeled continue of the OUTER loop across finally.
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-labelled-statements", "14.13 / 14.15")]
    public void Labeled_continue_outer_across_finally()
    {
        Eval(@"
            var log='';
            outer:
            for (var i=0;i<2;i++){
              for (var j=0;j<3;j++){
                try { if (j===1) continue outer; log+='x'+i+j; }
                finally { log+='f'; }
              }
            }
            log;
        ").AsString.Should().Be("x00ffx10ff");
    }

    // Repro 4 — break across NESTED finallies runs BOTH, innermost first.
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-try-statement", "14.15")]
    public void Break_across_nested_finallies_runs_both_innermost_first()
    {
        Eval(@"
            var log='';
            for (var i=0;i<3;i++){
              try {
                try { if (i===1) break; log+='t'+i; }
                finally { log+='inner'; }
              } finally { log+='outer'; }
            }
            log;
        ").AsString.Should().Be("t0innerouterinnerouter");
    }

    // Repro 5 — return across finally still works.
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-try-statement", "14.15")]
    public void Return_across_finally_still_works()
    {
        Eval(@"
            (function(){
              var log='';
              for (var i=0;i<3;i++){ try{ if(i===1) return log; log+='t'+i; } finally { log+='f'; } }
              return log;
            })();
        ").AsString.Should().Be("t0f");
    }

    // Completion-value override: a finally that breaks overrides a pending break.
    // The inner break tries to leave the inner loop; the finally's own `break`
    // (of the outer-labeled loop) overrides it and exits further out.
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-try-statement", "14.15.3 override")]
    public void Finally_break_overrides_pending_continue()
    {
        // Pending completion is `continue` (skip to next inner iteration);
        // the finally performs `break outer`, which overrides it.
        Eval(@"
            var log='';
            outer:
            for (var i=0;i<2;i++){
              for (var j=0;j<3;j++){
                try { if (j===0) continue; log+='x'+i+j; }
                finally { log+='f'; if (i===1 && j===2) break outer; }
              }
            }
            log;
        ").AsString.Should().Be("fx01fx02ffx11fx12f");
    }

    // break across finally inside a switch (switch break crosses finally).
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-switch-statement", "14.12 / 14.15")]
    public void Switch_break_across_finally()
    {
        Eval(@"
            var log='';
            switch (1) {
              case 1:
                try { log+='a'; break; } finally { log+='f'; }
                log+='unreached';
            }
            log;
        ").AsString.Should().Be("af");
    }

    // continue across finally where the finally itself returns (override).
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-try-statement", "14.15.3 override")]
    public void Finally_return_overrides_pending_break()
    {
        Eval(@"
            (function(){
              var log='';
              for (var i=0;i<3;i++){
                try { if (i===0) break; log+='t'+i; }
                finally { log+='f'; return 'fin'+log; }
              }
              return 'end'+log;
            })();
        ").AsString.Should().Be("finf");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

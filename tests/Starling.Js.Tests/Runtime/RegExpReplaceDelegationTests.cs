using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// Regression coverage for the RegExp/String replace spec gaps that made
/// core-js 3.0.1 feature-detect Starling's native methods as "broken" and
/// install its own recursing <c>RegExp.prototype[@@replace]</c> polyfill
/// (infinite recursion → RangeError on mcmaster.com's first bundle).
///
/// Each test mirrors one of core-js's feature-detects in
/// internals/fix-regexp-well-known-symbol-logic.js and internals/regexp-exec.js.
/// </summary>
[TestClass]
public class RegExpReplaceDelegationTests
{
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    // --- NPCG: non-participating capture groups are undefined, not "" ---------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexpbuiltinexec", "22.2.7.2 RegExpBuiltinExec")]
    [SpecFact]
    public void Nonparticipating_capture_group_is_undefined()
    {
        // core-js NPCG_INCLUDED detect: /()??/.exec('')[1] must be undefined.
        Eval("typeof /()??/.exec('')[1]").AsString.Should().Be("undefined");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexpbuiltinexec", "22.2.7.2 RegExpBuiltinExec")]
    [SpecFact]
    public void Optional_group_that_did_not_match_is_undefined()
    {
        Eval("var r=/(a)?/.exec('b'); typeof r[1]").AsString.Should().Be("undefined");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexpbuiltinexec", "22.2.7.2 RegExpBuiltinExec")]
    [SpecFact]
    public void Untaken_alternation_branch_capture_is_undefined()
    {
        // /(a)|(b)/ on "b": group 1 untaken -> undefined; group 2 -> "b".
        Eval("var r=/(a)|(b)/.exec('b'); typeof r[1] + ',' + r[2]").AsString
            .Should().Be("undefined,b");
    }

    // --- DELEGATES_TO_SYMBOL: String#replace delegates to any @@replace --------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-string.prototype.replace", "22.1.3.19 String.prototype.replace")]
    [SpecFact]
    public void String_replace_delegates_to_searchValue_Symbol_replace()
    {
        // core-js DELEGATES_TO_SYMBOL detect: a plain object exposing
        // [Symbol.replace] must be honored by String.prototype.replace.
        Eval(@"
            var O = {};
            O[Symbol.replace] = function () { return 7; };
            ''.replace(O);
        ").AsNumber.Should().Be(7);
    }

    // --- DELEGATES_TO_EXEC: @@replace drives RegExpExec -> this.exec -----------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexpexec", "22.2.7.1 RegExpExec")]
    [SpecFact]
    public void Symbol_replace_calls_the_regexps_own_exec()
    {
        // core-js DELEGATES_TO_EXEC detect: RegExp.prototype[@@replace] must
        // call the (possibly overridden) exec method on the receiver.
        Eval(@"
            var calls = 0;
            var re = /a/;
            re.exec = function () { calls++; return null; };
            re[Symbol.replace]('aaa', 'X');
            calls;
        ").AsNumber.Should().Be(1);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexpexec", "22.2.7.1 RegExpExec")]
    [SpecFact]
    public void Symbol_replace_uses_overridden_exec_result_array()
    {
        // The match text and index come from the array exec returns, not from
        // an internal re-match.
        Eval(@"
            var re = /zzz/;
            re.exec = function (s) {
                if (re.exec.done) return null;
                re.exec.done = true;
                var r = ['b']; r.index = 1; r.input = s; return r;
            };
            'abc'.replace(re, 'X');
        ").AsString.Should().Be("aXc");
    }

    // --- REPLACE_SUPPORTS_NAMED_GROUPS: $<name> via exec result's groups ------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-getsubstitution", "22.2.6.11.1 GetSubstitution")]
    [SpecFact]
    public void Named_group_substitution_reads_exec_result_groups()
    {
        // core-js REPLACE_SUPPORTS_NAMED_GROUPS detect: $<a> must resolve from
        // the result.groups object produced by exec.
        Eval(@"
            var re = /./;
            re.exec = function () { var r = []; r.groups = { a: '7' }; return r; };
            ''.replace(re, '$<a>');
        ").AsString.Should().Be("7");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-getsubstitution", "22.2.6.11.1 GetSubstitution")]
    [SpecFact]
    public void Native_named_group_substitution_still_works()
    {
        Eval(@"'2024-01'.replace(/(?<y>\d{4})-(?<m>\d{2})/, '$<m>/$<y>')")
            .AsString.Should().Be("01/2024");
    }

    // --- end-to-end: native @@replace semantics unchanged ---------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexp.prototype-@@replace", "22.2.6.11 RegExp.prototype [ @@replace ]")]
    [SpecFact]
    public void Global_replace_with_numbered_captures()
    {
        Eval(@"'a1b2c3'.replace(/([a-z])(\d)/g, '$2$1')").AsString.Should().Be("1a2b3c");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexp.prototype-@@replace", "22.2.6.11 RegExp.prototype [ @@replace ]")]
    [SpecFact]
    public void Functional_replace_receives_undefined_for_unmatched_group()
    {
        Eval(@"
            'b'.replace(/(a)|(b)/, function (m, g1, g2) {
                return (g1 === undefined ? 'U' : g1) + (g2 === undefined ? 'U' : g2);
            });
        ").AsString.Should().Be("Ub");
    }
}

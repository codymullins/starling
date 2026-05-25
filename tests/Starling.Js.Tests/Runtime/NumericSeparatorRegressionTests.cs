using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// wp:M3-51 follow-up — numeric separators (§12.9.3) for the two paths the
/// initial lexer work missed: BigInt value conversion and leading-dot fractions
/// with an exponent. The lexer already stripped <c>_</c> into the token Value,
/// but (1) <c>ParseBigIntLexeme</c> re-read the raw lexeme so radix/decimal
/// <c>BigInteger.Parse</c> threw "invalid digit '_'", and (2)
/// <c>ScanLeadingDotNumber</c> used raw digit loops + the raw lexeme, so
/// <c>.0_1e2</c> left <c>_1e2</c> as a stray identifier.
/// </summary>
[TestClass]
public class NumericSeparatorRegressionTests
{
    [TestMethod]
    public void BigInt_separators_evaluate_to_correct_value()
    {
        var (rt, _) = Eval(@"
            globalThis.ok =
                1_000n === 1000n &&
                0xA_Bn === 171n &&
                0b1_0n === 2n &&
                0o7_7n === 63n &&
                0xFF_FFn === 65535n;
        ");
        rt.GetGlobal("ok").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Leading_dot_fraction_separator_with_exponent()
    {
        var (rt, _) = Eval(@"
            globalThis.ok =
                (.0_1e2 === 1) &&
                (.0_1e2 === .01e2) &&
                (1.0_1e1 === 10.1);
        ");
        rt.GetGlobal("ok").AsBool.Should().BeTrue();
    }

    private static (JsRuntime runtime, JsValue result) Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        var rt = new JsRuntime();
        var r = new JsVm(rt).Run(chunk);
        return (rt, r);
    }
}

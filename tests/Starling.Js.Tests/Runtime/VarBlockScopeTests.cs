using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// Regression tests for `var` being function-scoped (§14.3.2) rather than
/// block-local. A `var` declared inside a block previously got a block-local
/// slot, which shadowed the function-top cell that capture analysis reserves for
/// a captured var: the initializer wrote the block-local while a closure read
/// the never-initialized cell, yielding undefined/NaN.
/// </summary>
[TestClass]
public class VarBlockScopeTests
{
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    [TestMethod]
    public void Captured_var_in_block_mutated_through_closure()
    {
        var r = Eval(@"
            function f(){
              { var total = 0; }
              let add = (n) => { total += n; };
              add(3); add(4);
              return total;
            }
            f();
        ");
        r.AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Var_in_block_is_visible_after_the_block()
    {
        // var hoists to function scope — readable outside the declaring block.
        var r = Eval(@"
            function f(){
              { var x = 5; }
              return x;
            }
            f();
        ");
        r.AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Captured_var_in_block_read_only_from_closure()
    {
        var r = Eval(@"
            function f(){
              { var v = 11; }
              let get = () => v;
              return get();
            }
            f();
        ");
        r.AsNumber.Should().Be(11);
    }

    [TestMethod]
    public void Var_in_nested_blocks_shares_one_binding()
    {
        var r = Eval(@"
            function f(){
              { { var c = 1; } }
              let inc = () => { c += 10; };
              inc(); inc();
              return c;
            }
            f();
        ");
        r.AsNumber.Should().Be(21);
    }

    [TestMethod]
    public void Var_in_block_inside_class_method_mutated_through_closure()
    {
        var r = Eval(@"
            class C {
              m(){
                { var i = 0; }
                let bump = () => ++i;
                bump(); bump(); bump();
                return i;
              }
            }
            new C().m();
        ");
        r.AsNumber.Should().Be(3);
    }
}

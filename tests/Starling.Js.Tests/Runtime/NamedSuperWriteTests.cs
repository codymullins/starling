using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// Regression tests for named super-property assignment (`super.name = v` and
/// compound `super.name op= v`). The compiler previously threw
/// NotSupportedException for these even though the VM's StoreSuperProperty /
/// LoadSuperProperty handlers existed; only the computed `super[k] = v` form was
/// wired. Per §13.3.4 PutValue on a super reference, the write targets the
/// receiver `this`, while the compound read resolves through the home object's
/// prototype.
/// </summary>
[TestClass]
public class NamedSuperWriteTests
{
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    [TestMethod]
    public void Named_super_write_invokes_base_setter()
    {
        Eval(@"
            class A { set x(v){ this._x = v; } get x(){ return this._x; } }
            class B extends A { m(){ super.x = 5; return this._x; } }
            globalThis.r = new B().m();
        ");
        // setter writes through to this._x
        Eval(@"
            class A { set x(v){ this._x = v; } get x(){ return this._x; } }
            class B extends A { m(){ super.x = 5; return this._x; } }
            new B().m();
        ").AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Named_super_write_targets_this_not_prototype()
    {
        // With no base setter, super.x = v defines an own property on `this`.
        var r = Eval(@"
            class A {}
            class B extends A { m(){ super.x = 9; return this.x; } }
            new B().m();
        ");
        r.AsNumber.Should().Be(9);
    }

    [TestMethod]
    public void Named_super_compound_assignment_reads_base_then_writes_this()
    {
        var r = Eval(@"
            class A { _x = 10; get x(){ return this._x; } set x(v){ this._x = v; } }
            class B extends A {
              m(){ super.x += 5; return this.x; }
            }
            new B().m();
        ");
        // compound read sees the inherited getter (10), +5 → 15 written via the
        // inherited setter onto this._x.
        r.AsNumber.Should().Be(15);
    }

    [TestMethod]
    public void Named_super_method_read_still_works()
    {
        // regression: super.name (read/call) unchanged.
        var r = Eval(@"
            class A { greet(){ return 7; } }
            class B extends A { greet(){ return super.greet() + 1; } }
            new B().greet();
        ");
        r.AsNumber.Should().Be(8);
    }
}

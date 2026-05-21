using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// Regression tests for §15.7.14: the class name binding must be initialized to
/// the constructor BEFORE static elements (static blocks + static field
/// initializers) run, so they can reference the class by name. Previously the
/// name was bound only after BuildClass (which ran the static elements), so
/// `static { C.x = ... }` saw C undefined.
/// </summary>
[TestClass]
public class StaticBlockClassNameTests
{
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    [TestMethod]
    public void Static_block_can_reference_class_by_name()
    {
        var r = Eval(@"
            class C { static foo(){ return 42; } static { C.cached = C.foo(); } }
            C.cached;
        ");
        r.AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Static_field_initializer_can_reference_class_by_name()
    {
        var r = Eval(@"
            class C { static a = 1; static b = C.a + 41; }
            C.b;
        ");
        r.AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Static_block_sees_earlier_static_block_via_class_name()
    {
        var r = Eval(@"
            class C {
              static { C.x = 2; }
              static { C.y = C.x * 5; }
            }
            C.y;
        ");
        r.AsNumber.Should().Be(10);
    }

    [TestMethod]
    public void Named_class_expression_name_does_not_leak_to_global()
    {
        // BindNameToGlobal must stay false for class expressions: the inner name
        // is scoped to the class body, not the global.
        var r = Eval(@"
            let X = class Foo { static v = 1; };
            (typeof Foo === 'undefined') ? 1 : 0;
        ");
        r.AsNumber.Should().Be(1);
    }
}

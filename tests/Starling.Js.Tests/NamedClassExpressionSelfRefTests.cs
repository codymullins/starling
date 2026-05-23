using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// §15.7.14 ClassDefinitionEvaluation — a class <em>expression</em> with a name
/// binds that name, inside the class body, to the constructor (an inner
/// immutable binding scoped to the class). Method/constructor bodies capture it,
/// so the class can reference itself. This was missing: <c>C</c> inside the body
/// resolved to an undefined free global, so e.g.
/// <c>Object.setPrototypeOf(this, C.prototype)</c> threw — which broke
/// mcmaster.com's bundle (a <c>class yu extends Array</c> doing exactly that).
/// </summary>
[TestClass]
public class NamedClassExpressionSelfRefTests
{
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    [TestMethod]
    public void Constructor_sees_own_class_name_top_level()
    {
        Eval(@"
            var C = class Inner { constructor(){ this.proto = Inner.prototype; } };
            new C().proto === C.prototype;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Constructor_sees_own_class_name_inside_function()
    {
        Eval(@"
            function make(){
              return class Inner { constructor(){ this.proto = Inner.prototype; } };
            }
            var C = make();
            new C().proto === C.prototype;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Method_sees_own_class_name()
    {
        Eval(@"
            var C = class Inner { self(){ return Inner; } };
            new C().self() === C;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Subclass_of_array_setprototypeof_with_own_name()
    {
        // The exact mcmaster pattern (a named class expression extending Array
        // that re-pins its own prototype in the constructor).
        Eval(@"
            var L = class yu extends Array {
              constructor(...t){ super(...t); Object.setPrototypeOf(this, yu.prototype); }
              static get [Symbol.species](){ return yu; }
            };
            var x = new L(1,2,3);
            x.length;
        ").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Inner_name_does_not_leak_to_enclosing_scope()
    {
        // The class-name binding is scoped to the class body only.
        Eval(@"
            var C = class Inner {};
            typeof Inner;
        ").AsString.Should().Be("undefined");
    }
}

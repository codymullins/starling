using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

[TestClass]
public class JsMethodBindingTests
{
    [TestMethod]
    public void Dot_method_call_binds_this_to_receiver()
    {
        var r = Eval(@"
            function C() {
                this.n = 5;
                this.get = function() { return this.n; };
            }
            new C().get();
        ");
        r.AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Bracket_method_call_binds_this()
    {
        var r = Eval(@"
            function C() {
                this.n = 7;
                this.get = function() { return this.n; };
            }
            var c = new C();
            c['get']();
        ");
        r.AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Plain_function_call_still_has_undefined_this()
    {
        // Regression: M3-04e only affects member-call sites. Top-level
        // function calls still see this=Undefined per §10.2.1.
        var r = Eval(@"
            function f() { return typeof this; }
            f();
        ");
        r.AsString.Should().Be("undefined");
    }

    [TestMethod]
    public void Method_can_mutate_via_this()
    {
        var r = Eval(@"
            function Counter() {
                this.n = 0;
                this.inc = function() { this.n = this.n + 1; return this.n; };
            }
            var c = new Counter();
            c.inc(); c.inc(); c.inc();
            c.n;
        ");
        r.AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Chained_method_calls_each_bind_their_own_this()
    {
        var r = Eval(@"
            function Box(v) {
                this.v = v;
                this.scale = function(k) { return new Box(this.v * k); };
                this.value = function() { return this.v; };
            }
            new Box(3).scale(2).scale(5).value();
        ");
        r.AsNumber.Should().Be(30); // 3*2*5
    }

    [TestMethod]
    public void Receiver_evaluated_only_once_for_dot_call()
    {
        // The receiver expression in a method call should be evaluated
        // exactly once, even though the runtime needs it twice (for
        // property lookup AND for this-binding).
        var runtime = new JsRuntime();
        var callCount = 0;
        runtime.RegisterGlobal("recv", args => {
            callCount++;
            var obj = new JsObject();
            obj.Set("m", JsValue.Object(new JsNativeFunction("m",
                a => JsValue.Number(42))));
            return JsValue.Object(obj);
        });

        var program = new JsParser("recv().m();").ParseProgram();
        new JsVm(runtime).Run(JsCompiler.Compile(program));

        callCount.Should().Be(1, "receiver evaluation should not double-execute");
    }

    [TestMethod]
    public void Method_returning_this_enables_fluent_chain()
    {
        var r = Eval(@"
            function Builder() {
                this.parts = '';
                this.add = function(s) { this.parts = this.parts + s; return this; };
                this.done = function() { return this.parts; };
            }
            new Builder().add('a').add('b').add('c').done();
        ");
        r.AsString.Should().Be("abc");
    }

    [TestMethod]
    public void Stored_method_called_as_bare_function_loses_this()
    {
        // `var fn = obj.method; fn();` is NOT a method call — fn has no
        // bound receiver. Spec: this=Undefined.
        var r = Eval(@"
            function C() { this.n = 99; this.get = function() { return typeof this; }; }
            var c = new C();
            var fn = c.get;
            fn();
        ");
        r.AsString.Should().Be("undefined");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

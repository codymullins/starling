using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

[TestClass]
public class JsNewThisTests
{
    [TestMethod]
    public void This_at_script_top_level_is_undefined()
        => Eval("this;").IsUndefined.Should().BeTrue();

    [TestMethod]
    public void This_inside_plain_function_call_is_undefined()
    {
        Eval(@"
            function f() { return typeof this; }
            f();
        ").AsString.Should().Be("undefined");
    }

    [TestMethod]
    public void Constructor_binds_this_to_a_fresh_object()
    {
        // Without an explicit return, new returns the freshly-allocated `this`.
        var r = Eval(@"
            function Point(x, y) { this.x = x; this.y = y; }
            var p = new Point(3, 4);
            p.x + p.y;
        ");
        r.AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Constructor_returns_this_when_body_has_no_return()
    {
        var r = Eval(@"
            function C() { this.tag = 'created'; }
            new C();
        ");
        r.IsObject.Should().BeTrue();
        r.AsObject.Get("tag").AsString.Should().Be("created");
    }

    [TestMethod]
    public void Constructor_with_explicit_object_return_uses_that_object()
    {
        var r = Eval(@"
            function Factory() {
                this.shouldBeIgnored = 1;
                return { custom: 42 };
            }
            var x = new Factory();
            x.custom;
        ");
        r.AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Constructor_with_explicit_non_object_return_uses_this()
    {
        // §13.3.5.1: if the return value isn't an object, ignore it and
        // use `this`. So `return 42` from a constructor falls back to `this`.
        var r = Eval(@"
            function C() { this.kept = 'yes'; return 42; }
            var x = new C();
            x.kept;
        ");
        r.AsString.Should().Be("yes");
    }

    [TestMethod]
    public void Multi_arg_constructor()
    {
        var r = Eval(@"
            function Pixel(r, g, b, a) {
                this.r = r; this.g = g; this.b = b; this.a = a;
            }
            var p = new Pixel(255, 128, 64, 200);
            p.r + p.g + p.b + p.a;
        ");
        r.AsNumber.Should().Be(255 + 128 + 64 + 200);
    }

    [TestMethod]
    public void This_field_read_via_dot_works_after_assignment()
    {
        Eval(@"
            function C() {
                this.x = 1;
                this.y = this.x + 10;
            }
            var c = new C();
            c.y;
        ").AsNumber.Should().Be(11);
    }

    [TestMethod]
    public void Calling_non_constructor_with_new_throws()
    {
        var act = () => Eval("new 5();");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void Computed_property_via_this_bracket_syntax()
    {
        Eval(@"
            function C() {
                var key = 'dynamic';
                this[key] = 99;
            }
            var c = new C();
            c.dynamic;
        ").AsNumber.Should().Be(99);
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

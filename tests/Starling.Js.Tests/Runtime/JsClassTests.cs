using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Runtime;

/// <summary>
/// End-to-end (parse → compile → run) tests for B1b-2a class declarations,
/// expressions, <c>extends</c>/<c>super</c>, accessors, static members,
/// private fields, and instance/static field initializers.
/// </summary>
public class JsClassTests
{
    [Fact]
    public void Bare_class_constructs_instance_whose_prototype_is_class_prototype()
    {
        Eval(@"
            class Foo {}
            var f = new Foo();
            Object.getPrototypeOf(f) === Foo.prototype;
        ").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Method_callable_on_instance()
    {
        Eval(@"
            class Foo { greet() { return 'hi'; } }
            new Foo().greet();
        ").AsString.Should().Be("hi");
    }

    [Fact]
    public void Constructor_assigns_to_this()
    {
        Eval(@"
            class Foo { constructor(x) { this.x = x; } }
            new Foo(5).x;
        ").AsNumber.Should().Be(5);
    }

    [Fact]
    public void Getter_returns_value()
    {
        Eval(@"
            class Foo { get x() { return 1; } }
            new Foo().x;
        ").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Setter_writes_via_accessor()
    {
        Eval(@"
            class Foo { set x(v) { this._v = v; } }
            var f = new Foo(); f.x = 7; f._v;
        ").AsNumber.Should().Be(7);
    }

    [Fact]
    public void Static_method_lives_on_constructor()
    {
        Eval(@"
            class Foo { static bar() { return 42; } }
            Foo.bar();
        ").AsNumber.Should().Be(42);
    }

    [Fact]
    public void Extends_inherits_method_through_prototype_chain()
    {
        Eval(@"
            class A { hello() { return 'a'; } }
            class B extends A {}
            new B().hello();
        ").AsString.Should().Be("a");
    }

    [Fact]
    public void Super_method_calls_parent_method()
    {
        Eval(@"
            class A { greet() { return 'a'; } }
            class B extends A { greet() { return super.greet() + 'b'; } }
            new B().greet();
        ").AsString.Should().Be("ab");
    }

    [Fact]
    public void Super_call_in_constructor_passes_args()
    {
        Eval(@"
            class A { constructor(x) { this.x = x; } }
            class B extends A { constructor() { super(7); } }
            new B().x;
        ").AsNumber.Should().Be(7);
    }

    [Fact]
    public void This_before_super_throws_reference_error()
    {
        var act = () => Eval(@"
            class A {}
            class B extends A { constructor() { this.x = 1; super(); } }
            new B();
        ");
        act.Should().Throw<JsThrow>();
    }

    [Fact]
    public void Default_derived_constructor_forwards_args()
    {
        Eval(@"
            class A { constructor(x, y) { this.s = x + y; } }
            class B extends A {}
            new B(2, 3).s;
        ").AsNumber.Should().Be(5);
    }

    [Fact]
    public void Instance_field_with_literal()
    {
        Eval(@"
            class Foo { x = 5; }
            new Foo().x;
        ").AsNumber.Should().Be(5);
    }

    [Fact]
    public void Field_initializer_can_reference_this()
    {
        Eval(@"
            class Foo { name = 'foo'; greeting = 'hi ' + this.name; }
            new Foo().greeting;
        ").AsString.Should().Be("hi foo");
    }

    [Fact]
    public void Static_field_lives_on_constructor()
    {
        Eval(@"
            class Foo { static count = 0; }
            Foo.count;
        ").AsNumber.Should().Be(0);
    }

    [Fact]
    public void Static_block_runs_at_class_evaluation_with_this_constructor()
    {
        Eval(@"
            class Foo { static x = 1; static { this.y = 2; } }
            Foo.x + ',' + Foo.y;
        ").AsString.Should().Be("1,2");
    }

    [Fact]
    public void Private_field_readable_via_accessor_inside_class()
    {
        Eval(@"
            class Foo { #x = 7; get x() { return this.#x; } }
            new Foo().x;
        ").AsNumber.Should().Be(7);
    }

    [Fact]
    public void Private_method_callable_only_inside_class()
    {
        Eval(@"
            class Foo { #greet() { return 'hi'; } greet() { return this.#greet(); } }
            new Foo().greet();
        ").AsString.Should().Be("hi");
    }

    [Fact]
    public void Static_private_field_accessed_via_class()
    {
        Eval(@"
            class Foo {
                static #count = 0;
                static inc() { Foo.#count = Foo.#count + 1; return Foo.#count; }
            }
            Foo.inc();
        ").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Class_expression_anonymous()
    {
        Eval(@"
            var F = class { x() { return 1; } };
            new F().x();
        ").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Static_method_inheritance_through_constructor_prototype_chain()
    {
        Eval(@"
            class A { static bar() { return 42; } }
            class B extends A {}
            B.bar();
        ").AsNumber.Should().Be(42);
    }

    [Fact]
    public void Class_expression_with_constructor_returns_instance()
    {
        Eval(@"
            var F = class { constructor(n) { this.n = n; } };
            new F(11).n;
        ").AsNumber.Should().Be(11);
    }

    // ----------------------------------------------------- Helpers

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

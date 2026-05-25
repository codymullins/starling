using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// End-to-end (parse → compile → run) tests for B1b-2a class declarations,
/// expressions, <c>extends</c>/<c>super</c>, accessors, static members,
/// private fields, and instance/static field initializers.
/// </summary>
[TestClass]
public class JsClassTests
{
    [TestMethod]
    public void Bare_class_constructs_instance_whose_prototype_is_class_prototype()
    {
        Eval(@"
            class Foo {}
            var f = new Foo();
            Object.getPrototypeOf(f) === Foo.prototype;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Method_callable_on_instance()
    {
        Eval(@"
            class Foo { greet() { return 'hi'; } }
            new Foo().greet();
        ").AsString.Should().Be("hi");
    }

    [TestMethod]
    public void Constructor_assigns_to_this()
    {
        Eval(@"
            class Foo { constructor(x) { this.x = x; } }
            new Foo(5).x;
        ").AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Getter_returns_value()
    {
        Eval(@"
            class Foo { get x() { return 1; } }
            new Foo().x;
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Setter_writes_via_accessor()
    {
        Eval(@"
            class Foo { set x(v) { this._v = v; } }
            var f = new Foo(); f.x = 7; f._v;
        ").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Static_method_lives_on_constructor()
    {
        Eval(@"
            class Foo { static bar() { return 42; } }
            Foo.bar();
        ").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Extends_inherits_method_through_prototype_chain()
    {
        Eval(@"
            class A { hello() { return 'a'; } }
            class B extends A {}
            new B().hello();
        ").AsString.Should().Be("a");
    }

    [TestMethod]
    public void Super_method_calls_parent_method()
    {
        Eval(@"
            class A { greet() { return 'a'; } }
            class B extends A { greet() { return super.greet() + 'b'; } }
            new B().greet();
        ").AsString.Should().Be("ab");
    }

    [TestMethod]
    public void Super_call_in_constructor_passes_args()
    {
        Eval(@"
            class A { constructor(x) { this.x = x; } }
            class B extends A { constructor() { super(7); } }
            new B().x;
        ").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void This_before_super_throws_reference_error()
    {
        var act = () => Eval(@"
            class A {}
            class B extends A { constructor() { this.x = 1; super(); } }
            new B();
        ");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void Default_derived_constructor_forwards_args()
    {
        Eval(@"
            class A { constructor(x, y) { this.s = x + y; } }
            class B extends A {}
            new B(2, 3).s;
        ").AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Instance_field_with_literal()
    {
        Eval(@"
            class Foo { x = 5; }
            new Foo().x;
        ").AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Field_initializer_can_reference_this()
    {
        Eval(@"
            class Foo { name = 'foo'; greeting = 'hi ' + this.name; }
            new Foo().greeting;
        ").AsString.Should().Be("hi foo");
    }

    [TestMethod]
    public void Static_field_lives_on_constructor()
    {
        Eval(@"
            class Foo { static count = 0; }
            Foo.count;
        ").AsNumber.Should().Be(0);
    }

    [TestMethod]
    public void Static_block_runs_at_class_evaluation_with_this_constructor()
    {
        Eval(@"
            class Foo { static x = 1; static { this.y = 2; } }
            Foo.x + ',' + Foo.y;
        ").AsString.Should().Be("1,2");
    }

    [TestMethod]
    public void Private_field_readable_via_accessor_inside_class()
    {
        Eval(@"
            class Foo { #x = 7; get x() { return this.#x; } }
            new Foo().x;
        ").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Private_method_callable_only_inside_class()
    {
        Eval(@"
            class Foo { #greet() { return 'hi'; } greet() { return this.#greet(); } }
            new Foo().greet();
        ").AsString.Should().Be("hi");
    }

    [TestMethod]
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

    [TestMethod]
    public void Class_expression_anonymous()
    {
        Eval(@"
            var F = class { x() { return 1; } };
            new F().x();
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Static_method_inheritance_through_constructor_prototype_chain()
    {
        Eval(@"
            class A { static bar() { return 42; } }
            class B extends A {}
            B.bar();
        ").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Class_expression_with_constructor_returns_instance()
    {
        Eval(@"
            var F = class { constructor(n) { this.n = n; } };
            new F(11).n;
        ").AsNumber.Should().Be(11);
    }

    // -------------------------------------------- Private-member brand checks
    // wp:M3-66 — accessing a private member the receiver does not carry is a
    // TypeError (§13.3.4 PrivateGet/PrivateSet/PrivateElementFind); the brand is
    // installed when instance elements initialize (post-super() for a derived
    // class), so access before that throws too. The success cases confirm valid
    // private read/write/call is not regressed.

    [TestMethod]
    public void Valid_private_field_read_write_and_method_call_work()
    {
        Eval(@"
            class C {
                #x = 1;
                #m() { return this.#x; }
                read() { return this.#x; }
                bump() { this.#x = this.#x + 1; return this.#x; }
                callM() { return this.#m(); }
            }
            var c = new C();
            '' + c.read() + c.bump() + c.callM();
        ").AsString.Should().Be("122");
    }

    [TestMethod]
    public void Private_field_read_on_wrong_receiver_throws_type_error()
    {
        ExpectTypeError(@"
            class C { #x = 1; read(o) { return o.#x; } }
            new C().read({});
        ");
    }

    [TestMethod]
    public void Private_field_write_on_wrong_receiver_throws_type_error()
    {
        ExpectTypeError(@"
            class C { #x = 1; write(o) { o.#x = 5; } }
            new C().write({});
        ");
    }

    [TestMethod]
    public void Private_method_call_on_wrong_receiver_throws_type_error()
    {
        ExpectTypeError(@"
            class C { #m() { return 1; } call(o) { return o.#m(); } }
            new C().call({});
        ");
    }

    [TestMethod]
    public void Private_in_on_wrong_receiver_is_false_not_true()
    {
        Eval(@"
            class C { #x = 1; has(o) { return #x in o; } }
            var c = new C();
            (c.has(c)) && !(c.has({}));
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Static_private_method_on_subclass_receiver_throws_type_error()
    {
        // §15.7.10 — only the class constructor itself carries a static private
        // member's brand; a subclass constructor receiver must throw.
        ExpectTypeError(@"
            class C { static f() { return this.#g(); } static #g() { return 42; } }
            class D extends C {}
            D.f();
        ");
    }

    [TestMethod]
    public void Static_private_method_on_own_constructor_works()
    {
        Eval(@"
            class C { static f() { return this.#g(); } static #g() { return 42; } }
            C.f();
        ").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Static_private_field_on_subclass_receiver_throws_type_error()
    {
        ExpectTypeError(@"
            class C { static #v = 7; static read() { return this.#v; } }
            class D extends C {}
            D.read();
        ");
    }

    [TestMethod]
    public void Private_method_call_before_super_returns_throws_type_error()
    {
        // The base constructor calls this.f(), which reaches D.prototype.f and
        // calls this.#m() — but D's private method brand is not installed until
        // super() returns, so this throws (prod-private-method-before-super).
        ExpectTypeError(@"
            var C = class { constructor() { this.f(); } };
            class D extends C { f() { this.#m(); } #m() { return 42; } }
            new D();
        ");
    }

    [TestMethod]
    public void Private_field_access_before_super_returns_throws_type_error()
    {
        ExpectTypeError(@"
            var C = class { constructor() { this.f(); } };
            class D extends C { #x = 1; f() { return this.#x; } }
            new D();
        ");
    }

    [TestMethod]
    public void Private_field_access_through_proxy_throws_type_error()
    {
        // Private access is never trapped; the proxy object is not a brand
        // carrier, so reading #x with the proxy as receiver throws.
        ExpectTypeError(@"
            class C { #x = 1; x() { return this.#x; } }
            var c = new C();
            var p = new Proxy(c, {});
            p.x();
        ");
    }

    [TestMethod]
    public void Static_private_method_through_proxy_throws_type_error()
    {
        ExpectTypeError(@"
            class C { static #m() { return 1; } static x() { return this.#m(); } }
            var P = new Proxy(C, {});
            P.x();
        ");
    }

    [TestMethod]
    public void Private_method_call_through_proxy_throws_type_error_but_direct_works()
    {
        Eval(@"
            class C { #m() { return 1; } x() { return this.#m(); } }
            var c = new C();
            c.x();
        ").AsNumber.Should().Be(1);

        ExpectTypeError(@"
            class C { #m() { return 1; } x() { return this.#m(); } }
            var c = new C();
            new Proxy(c, {}).x();
        ");
    }

    // ------------------------------------------ wp:M3-77 private element fixes

    [TestMethod]
    public void Private_in_returns_true_for_instance_false_otherwise_no_crash()
    {
        // §13.10 ergonomic brand check — `#x in obj` evaluates to whether obj
        // carries the #x brand; it never crashes and yields false for a plain
        // object that lacks the brand.
        Eval(@"
            class C { #x = 1; static has(o) { return #x in o; } }
            var c = new C();
            (C.has(c) === true) && (C.has({}) === false);
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Private_in_for_method_brand_is_true_on_instance()
    {
        Eval(@"
            class C { #m() { return 1; } static has(o) { return #m in o; } }
            C.has(new C());
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Private_method_getter_setter_callable_on_valid_instance()
    {
        // A private method, getter, and setter all register their brand on the
        // instance, so valid access from inside the class succeeds.
        Eval(@"
            class C {
                #v = 0;
                #m() { return 7; }
                get #g() { return this.#v; }
                set #s(x) { this.#v = x; }
                run() { this.#s = 5; return this.#m() + this.#g; }
            }
            new C().run();
        ").AsNumber.Should().Be(12);
    }

    [TestMethod]
    public void Private_method_access_through_inner_arrow_succeeds()
    {
        // §10.2.1.1 — an inner arrow inherits the method's `this`, so the brand
        // check on `this.#m()` finds the instance's brand.
        Eval(@"
            class C { #m() { return 'ok'; } method() { var f = () => this.#m(); return f(); } }
            new C().method();
        ").AsString.Should().Be("ok");
    }

    [TestMethod]
    public void Private_method_call_on_wrong_receiver_via_arrow_still_throws()
    {
        ExpectTypeError(@"
            class C { #m() { return 1; } method() { var f = () => this.#m(); return f(); } }
            var c = new C(); var o = {};
            c.method.call(o);
        ");
    }

    [TestMethod]
    public void Forward_referenced_private_name_resolves()
    {
        // A method body references #later before its declaration appears in the
        // class body; CollectPrivateNames makes all private names visible first.
        Eval(@"
            class C { early() { return this.#later(); } #later() { return 99; } }
            new C().early();
        ").AsNumber.Should().Be(99);
    }

    [TestMethod]
    public void Nested_class_references_outer_private_name()
    {
        // An inner class's method references the OUTER class's private name; the
        // private-name scope of every enclosing class body is visible.
        Eval(@"
            class Outer {
                #secret = 42;
                make() {
                    var self = this;
                    class Inner { read() { return self.#secret; } }
                    return new Inner().read();
                }
            }
            new Outer().make();
        ").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Destructuring_into_private_member_target_works_no_crash()
    {
        // §13.15 — `({a: this.#x} = obj)` and `[this.#y] = arr`: a private member
        // as a destructuring-assignment target stores through the brand-checked
        // PrivateSet (previously crashed casting PrivateNameExpression to Identifier).
        Eval(@"
            class C {
                #x = 0; #y = 0;
                load() {
                    ({ a: this.#x } = { a: 11 });
                    [this.#y] = [22];
                    return this.#x + this.#y;
                }
            }
            new C().load();
        ").AsNumber.Should().Be(33);
    }

    [TestMethod]
    public void Private_name_visible_to_direct_eval()
    {
        // §19.2.1.1 — a direct eval inherits the enclosing class's private
        // environment, so a private-member access resolves inside eval'd code.
        Eval("class C { #m = 44; getWithEval() { return eval('this.#m'); } } new C().getWithEval();")
            .AsNumber.Should().Be(44);
    }

    [TestMethod]
    public void Private_name_in_direct_eval_wrong_receiver_throws()
    {
        ExpectTypeError("class C { #m = 44; getWithEval() { return eval('this.#m'); } } class D { #m = 44; } var c = new C(); var d = new D(); c.getWithEval.call(d);");
    }

    // ----------------------------------------------------- Helpers

    private static void ExpectTypeError(string src)
    {
        JsThrow? caught = null;
        try { Eval(src); }
        catch (JsThrow t) { caught = t; }
        caught.Should().NotBeNull("the snippet should throw a TypeError");
        caught!.Value.IsObject.Should().BeTrue();
        // The thrown error's constructor name should be "TypeError".
        caught.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

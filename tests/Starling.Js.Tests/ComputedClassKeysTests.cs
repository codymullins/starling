using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// wp:M3-04f — end-to-end (parse → compile → run) coverage for computed class
/// member keys: <c>[expr]() {}</c>, <c>static [expr] = v</c>,
/// <c>get/set [expr]()</c>, and <c>[Symbol.iterator]</c>. Computed keys must
/// evaluate exactly once, in source order, at class-definition time.
/// </summary>
[TestClass]
public class ComputedClassKeysTests
{
    [TestMethod]
    public void Computed_method_key_from_string_expression()
    {
        Eval(@"
            const k = 'gr' + 'eet';
            class Foo { [k]() { return 'hi'; } }
            new Foo().greet();
        ").AsString.Should().Be("hi");
    }

    [TestMethod]
    public void Computed_static_method_key()
    {
        Eval(@"
            const k = 'b' + 'ar';
            class Foo { static [k]() { return 42; } }
            Foo.bar();
        ").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Computed_getter_key()
    {
        Eval(@"
            const k = 'x';
            class Foo { get [k]() { return 7; } }
            new Foo().x;
        ").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Computed_setter_key()
    {
        Eval(@"
            const k = 'x';
            class Foo { set [k](v) { this._v = v; } }
            var f = new Foo(); f.x = 9; f._v;
        ").AsNumber.Should().Be(9);
    }

    [TestMethod]
    public void Computed_get_and_set_share_one_accessor_descriptor()
    {
        Eval(@"
            const k = 'x';
            class Foo {
                get [k]() { return this._v; }
                set [k](v) { this._v = v * 2; }
            }
            var f = new Foo(); f.x = 5; f.x;
        ").AsNumber.Should().Be(10);
    }

    [TestMethod]
    public void Computed_instance_field_with_initializer()
    {
        Eval(@"
            const k = 'count';
            class Foo { [k] = 3; }
            new Foo().count;
        ").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Computed_instance_field_without_initializer_is_undefined()
    {
        Eval(@"
            const k = 'count';
            class Foo { [k]; }
            var f = new Foo();
            ('count' in f) && (f.count === undefined);
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Computed_static_field_with_initializer()
    {
        Eval(@"
            const k = 'total';
            class Foo { static [k] = 1 + 2; }
            Foo.total;
        ").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Computed_static_field_without_initializer_is_undefined()
    {
        Eval(@"
            const k = 'total';
            class Foo { static [k]; }
            ('total' in Foo) && (Foo.total === undefined);
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Computed_field_initializer_sees_this()
    {
        Eval(@"
            const k = 'doubled';
            class Foo {
                base = 4;
                [k] = this.base * 2;
            }
            new Foo().doubled;
        ").AsNumber.Should().Be(8);
    }

    [TestMethod]
    public void Numeric_computed_key_is_stringified()
    {
        Eval(@"
            class Foo { [1 + 1]() { return 'two'; } }
            new Foo()['2']();
        ").AsString.Should().Be("two");
    }

    [TestMethod]
    public void Symbol_iterator_computed_method_drives_for_of()
    {
        // Delegate to a backing array's own @@iterator so the test exercises
        // the computed [Symbol.iterator] method install + for-of dispatch
        // without depending on the (separately tracked) class-method/let
        // closure-capture path.
        Eval(@"
            class Range {
                constructor(arr) { this.arr = arr; }
                [Symbol.iterator]() { return this.arr[Symbol.iterator](); }
            }
            let sum = 0;
            for (const v of new Range([1, 2, 3, 4])) sum += v;
            sum;
        ").AsNumber.Should().Be(10);
    }

    [TestMethod]
    public void Computed_symbol_key_installs_under_symbol_not_string()
    {
        // The member must live under the Symbol, with no spurious string key.
        Eval(@"
            class Foo { [Symbol.iterator]() { return 1; } }
            const p = Foo.prototype;
            (typeof p[Symbol.iterator] === 'function') &&
            !Object.getOwnPropertyNames(p).includes('Symbol(Symbol.iterator)');
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Computed_key_expression_evaluated_exactly_once()
    {
        // The key expression has a side effect; it must run exactly once even
        // though the member is installed once.
        Eval(@"
            let calls = 0;
            function key() { calls++; return 'm'; }
            class Foo { [key()]() {} }
            calls;
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Computed_method_keys_evaluate_in_declaration_order()
    {
        Eval(@"
            const order = [];
            function k(name) { order.push(name); return name; }
            class Foo {
                [k('a')]() {}
                static [k('b')]() {}
                [k('c')]() {}
            }
            order.join(',');
        ").AsString.Should().Be("a,b,c");
    }

    [TestMethod]
    public void Computed_field_keys_evaluate_in_declaration_order()
    {
        Eval(@"
            const order = [];
            function k(name) { order.push(name); return name; }
            class Foo {
                [k('a')] = 1;
                static [k('b')] = 2;
                [k('c')] = 3;
            }
            order.join(',');
        ").AsString.Should().Be("a,b,c");
    }

    [TestMethod]
    public void Computed_method_name_property_matches_key()
    {
        Eval(@"
            const k = 'greet';
            class Foo { [k]() {} }
            Foo.prototype.greet.name;
        ").AsString.Should().Be("greet");
    }

    [TestMethod]
    public void Computed_getter_name_property_has_get_prefix()
    {
        Eval(@"
            const k = 'x';
            class Foo { get [k]() { return 1; } }
            Object.getOwnPropertyDescriptor(Foo.prototype, 'x').get.name;
        ").AsString.Should().Be("get x");
    }

    [TestMethod]
    public void Mixed_static_named_and_computed_members_coexist()
    {
        // Regression: existing static *named* members must keep working
        // alongside computed ones.
        Eval(@"
            const k = 'dynamic';
            class Foo {
                static named() { return 1; }
                static [k]() { return 2; }
            }
            Foo.named() + Foo.dynamic();
        ").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Computed_key_uses_ToPrimitive_on_object()
    {
        // §7.1.19 ToPropertyKey → §7.1.1 ToPrimitive("string"); an object key
        // with Symbol.toPrimitive is coerced via that trap.
        Eval(@"
            const k = { [Symbol.toPrimitive]() { return 'fromObj'; } };
            class Foo { [k]() { return 'ok'; } }
            new Foo().fromObj();
        ").AsString.Should().Be("ok");
    }

    // ----------------------------------------------------- Helpers

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

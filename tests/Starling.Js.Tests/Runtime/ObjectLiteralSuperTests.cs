using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// wp:M3-64 — end-to-end (parse → compile → run) coverage for <c>super</c>
/// property access in object-literal method definitions. Per §13.2.5 /
/// §13.3.4 a SuperProperty (<c>super.x</c> / <c>super[x]</c>) is valid in ANY
/// concise method, getter, or setter — including object-literal ones — and in
/// arrow functions that lexically inherit the enclosing method's
/// [[HomeObject]]. An object method's home object is the object being
/// constructed, so <c>super.x</c> resolves against
/// <c>Object.getPrototypeOf(theObject)</c>. <c>super(...)</c> (a SuperCall)
/// stays restricted to derived-class constructors.
/// </summary>
[TestClass]
public class ObjectLiteralSuperTests
{
    [TestMethod]
    public void Object_method_super_call_resolves_via_prototype()
    {
        // §13.2.5 — the concise method's home object is `object`; super.m()
        // resolves to Object.getPrototypeOf(object).m.
        Eval(@"
            var proto = { m() { return ' proto m'; } };
            var object = { call() { return 'a' + super.m(); } };
            Object.setPrototypeOf(object, proto);
            object.call();
        ").AsString.Should().Be("a proto m");
    }

    [TestMethod]
    public void Object_method_super_property_read_resolves_via_prototype()
    {
        Eval(@"
            var proto = { x: 'base-x' };
            var object = { read() { return super.x; } };
            Object.setPrototypeOf(object, proto);
            object.read();
        ").AsString.Should().Be("base-x");
    }

    [TestMethod]
    public void Object_method_home_object_is_object_prototype_by_default()
    {
        // name-super-prop-body.js — a method's home object is the object, whose
        // prototype is Object.prototype; super.toString === Object.prototype.toString.
        Eval(@"
            var obj = { method() { return super.toString === Object.prototype.toString; } };
            obj.toString = null;
            obj.method();
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Object_getter_super_read_resolves_via_prototype()
    {
        // getter-super-prop.js — the base getter runs with `this` = object.
        Eval(@"
            var proto = { _x: 42, get x() { return 'proto' + this._x; } };
            var object = { _x: 42, get x() { return super.x; } };
            Object.setPrototypeOf(object, proto);
            object.x;
        ").AsString.Should().Be("proto42");
    }

    [TestMethod]
    public void Object_setter_super_write_invokes_base_setter()
    {
        // setter-super-prop.js — super.x = v runs the base setter with the
        // object as receiver, so the base setter's `this._x = v` lands on the
        // object (not the prototype).
        Eval(@"
            var proto = { _x: 0, set x(v) { this._x = v; } };
            var object = { set x(v) { super.x = v; } };
            Object.setPrototypeOf(object, proto);
            object.x = 1;
            object._x === 1 && Object.getPrototypeOf(object)._x === 0;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Computed_key_object_method_super_call()
    {
        // computed-property-names/object/method/super.js — computed-key methods
        // can call super methods.
        Eval(@"
            function ID(x) { return x; }
            var proto = { m() { return ' proto m'; } };
            var object = {
                ['a']() { return 'a' + super.m(); },
                [ID('b')]() { return 'b' + super.m(); },
                [0]() { return '0' + super.m(); },
            };
            Object.setPrototypeOf(object, proto);
            object.a() + '|' + object.b() + '|' + object[0]();
        ").AsString.Should().Be("a proto m|b proto m|0 proto m");
    }

    [TestMethod]
    public void Computed_key_object_getter_super_read()
    {
        Eval(@"
            var proto = { get x() { return 'proto-x'; } };
            var key = 'x';
            var object = { get [key]() { return super.x; } };
            Object.setPrototypeOf(object, proto);
            object.x;
        ").AsString.Should().Be("proto-x");
    }

    [TestMethod]
    public void Arrow_in_object_method_inherits_super_lexically()
    {
        // §14.2 — an arrow nested in a concise method inherits the method's
        // [[HomeObject]] so super.m() inside the arrow resolves correctly.
        Eval(@"
            var proto = { m() { return 'pm'; } };
            var object = { call() { return (() => super.m())(); } };
            Object.setPrototypeOf(object, proto);
            object.call();
        ").AsString.Should().Be("pm");
    }

    [TestMethod]
    public void Arrow_in_class_method_inherits_super_lexically()
    {
        // lexical-super-property.js — an arrow inside a derived-class method
        // inherits super; super.increment() bumps the shared counter once.
        Eval(@"
            var count = 0;
            class A { increment() { count++; } }
            class B extends A {
                incrementer() { (_ => super.increment())(); }
            }
            new B().incrementer();
            count;
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Nested_arrows_in_object_method_inherit_super()
    {
        // An arrow inside an arrow inside a method still inherits super
        // transitively through the closure chain.
        Eval(@"
            var proto = { m() { return 'deep'; } };
            var object = { call() { return (() => (() => super.m())())(); } };
            Object.setPrototypeOf(object, proto);
            object.call();
        ").AsString.Should().Be("deep");
    }

    // ---- super inside a plain data-property function is NOT a method ----

    [TestMethod]
    public void Data_property_function_value_is_not_a_method_super_is_a_syntax_error()
    {
        // wp:M3-71 — `{ x: function(){} }` is a data property whose value is a
        // function, NOT a MethodDefinition — it gets no [[HomeObject]], so a
        // SuperProperty in its body is an early SyntaxError (§13.3.7.1), now
        // surfaced at parse time rather than via the runtime home-object guard.
        var act = () => new JsParser(@"
            var object = { call: function() { return super.m(); } };
        ").ParseProgram();
        act.Should().Throw<JsParseException>();
    }

    // ---- SuperCall (super(...)) stays restricted to derived constructors ----

    [TestMethod]
    public void Super_call_in_object_method_is_a_syntax_error()
    {
        var act = () => new JsParser(@"
            var o = { m() { super(); } };
        ").ParseProgram();
        act.Should().Throw<JsParseException>();
    }

    [TestMethod]
    public void Super_call_in_arrow_in_object_method_is_a_syntax_error()
    {
        var act = () => new JsParser(@"
            var o = { m() { (() => super())(); } };
        ").ParseProgram();
        act.Should().Throw<JsParseException>();
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

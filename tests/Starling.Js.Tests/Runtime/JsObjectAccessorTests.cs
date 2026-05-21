using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// wp:M3-26 — object-literal accessor (getter/setter) shorthand:
/// <c>{ get x(){…}, set x(v){…} }</c> (ECMA-262 §13.2.5 Object Initializer).
/// Includes the disambiguation cases that must NOT regress: a data property
/// named "get"/"set", and a method named "get"/"set".
/// </summary>
[TestClass]
public class JsObjectAccessorTests
{
    // -----------------------------------------------------------------
    //  Getter
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Getter_returns_value()
    {
        Eval("var o = { get x(){ return 42; } }; o.x").AsNumber.Should().Be(42);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Getter_this_is_the_object()
    {
        Eval("var o = { _v: 7, get x(){ return this._v; } }; o.x").AsNumber.Should().Be(7);
    }

    // -----------------------------------------------------------------
    //  Setter
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Setter_runs_and_receives_value()
    {
        Eval("var o = { _x: 0, set x(v){ this._x = v; } }; o.x = 9; o._x")
            .AsNumber.Should().Be(9);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Setter_only_property_reads_undefined()
    {
        // A property with a setter but no getter reads as undefined.
        Eval("var o = { set x(v){ this._x = v; } }; typeof o.x")
            .AsString.Should().Be("undefined");
    }

    // -----------------------------------------------------------------
    //  Get/set pair on the same key — one descriptor
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Getter_setter_pair_round_trips()
    {
        Eval("var o = { _x: 1, get x(){ return this._x; }, set x(v){ this._x = v * 2; } };"
           + " o.x = 5; o.x").AsNumber.Should().Be(10);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Setter_then_getter_pair_also_shares_descriptor()
    {
        // Declaration order reversed (set before get): still one accessor.
        Eval("var o = { _x: 0, set x(v){ this._x = v + 1; }, get x(){ return this._x; } };"
           + " o.x = 4; o.x").AsNumber.Should().Be(5);
    }

    // -----------------------------------------------------------------
    //  Redux-shape mixed object: data + accessor + data
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Mixed_data_and_accessor_reads_all_members()
    {
        // Mirrors RTK createSlice's { reducerPath:x, get selectors(){…}, selectSlice:w }.
        Eval("var o = { a:1, get b(){ return 2; }, c:3 }; o.a + o.b + o.c")
            .AsNumber.Should().Be(6);
    }

    // -----------------------------------------------------------------
    //  Accessor keys: numeric, string, computed
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Numeric_accessor_key()
    {
        Eval("var o = { get 0(){ return 11; } }; o[0]").AsNumber.Should().Be(11);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void String_accessor_key()
    {
        Eval("var o = { get \"s\"(){ return 12; } }; o.s").AsNumber.Should().Be(12);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Computed_accessor_key()
    {
        Eval("var k = 'dyn'; var o = { get [k](){ return 13; } }; o.dyn")
            .AsNumber.Should().Be(13);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Computed_accessor_setter()
    {
        Eval("var k = 'dyn'; var o = { _x:0, set [k](v){ this._x = v; } }; o.dyn = 8; o._x")
            .AsNumber.Should().Be(8);
    }

    // -----------------------------------------------------------------
    //  Function "name" is "get x" / "set x" (§13.2.5.5)
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5.5")]
    [SpecFact]
    public void Getter_function_name_is_prefixed()
    {
        Eval("var d = Object.getOwnPropertyDescriptor({ get x(){} }, 'x'); d.get.name")
            .AsString.Should().Be("get x");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5.5")]
    [SpecFact]
    public void Setter_function_name_is_prefixed()
    {
        Eval("var d = Object.getOwnPropertyDescriptor({ set x(v){} }, 'x'); d.set.name")
            .AsString.Should().Be("set x");
    }

    // -----------------------------------------------------------------
    //  Accessor descriptors are enumerable (object-literal — §13.2.5)
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Object_literal_accessor_is_enumerable()
    {
        Eval("var d = Object.getOwnPropertyDescriptor({ get x(){return 1;} }, 'x');"
           + " d.enumerable").AsBool.Should().BeTrue();
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Object_literal_accessor_appears_in_keys()
    {
        Eval("var o = { a:1, get b(){return 2;} }; Object.keys(o).join(',')")
            .AsString.Should().Be("a,b");
    }

    // -----------------------------------------------------------------
    //  DISAMBIGUATION — must not regress
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Data_property_named_get_still_works()
    {
        Eval("var o = { get: 1 }; o.get").AsNumber.Should().Be(1);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Data_property_named_set_still_works()
    {
        Eval("var o = { set: 2 }; o.set").AsNumber.Should().Be(2);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Method_named_get_still_works()
    {
        // { get(){} } is a METHOD named "get", not an accessor.
        Eval("var o = { get(){ return 5; } }; o.get()").AsNumber.Should().Be(5);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Method_named_set_still_works()
    {
        Eval("var o = { set(){ return 6; } }; o.set()").AsNumber.Should().Be(6);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Shorthand_get_still_works()
    {
        // { get } shorthand where `get` is a binding in scope.
        Eval("var get = 7; var o = { get }; o.get").AsNumber.Should().Be(7);
    }

    // -----------------------------------------------------------------
    //  Later definitions win (data shadows accessor and vice versa)
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Data_after_getter_wins()
    {
        Eval("var o = { get x(){ return 1; }, x: 99 }; o.x").AsNumber.Should().Be(99);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "13.2.5")]
    [SpecFact]
    public void Getter_after_data_wins()
    {
        Eval("var o = { x: 1, get x(){ return 2; } }; o.x").AsNumber.Should().Be(2);
    }

    // -----------------------------------------------------------------
    //  Well-formedness errors
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "15.4.1")]
    [SpecFact]
    public void Getter_with_param_is_a_syntax_error()
    {
        var act = () => Eval("var o = { get x(a){ return a; } };");
        act.Should().Throw<JsParseException>();
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-object-initializer", "15.4.1")]
    [SpecFact]
    public void Setter_with_zero_params_is_a_syntax_error()
    {
        var act = () => Eval("var o = { set x(){ } };");
        act.Should().Throw<JsParseException>();
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

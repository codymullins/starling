using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for ECMA-262 §20.4 Symbol primitives and intrinsics.</summary>
[TestClass]
public class SymbolTests
{
    [TestMethod]
    public void Factory_creates_unique_symbol_primitives_with_descriptions()
    {
        Eval("Symbol('x') === Symbol('x');").AsBool.Should().BeFalse();
        Eval("var s = Symbol('x'); typeof s;").AsString.Should().Be("symbol");
        Eval("Symbol('x').toString();").AsString.Should().Be("Symbol(x)");
        Eval("Symbol().toString();").AsString.Should().Be("Symbol()");
        Eval("Symbol('x').valueOf() === Symbol('x');").AsBool.Should().BeFalse();
        Eval("var s = Symbol('x'); s.valueOf() === s;").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Description_accessor_returns_description_or_undefined()
    {
        Eval("Symbol('hello').description;").AsString.Should().Be("hello");
        Eval("Symbol('').description;").AsString.Should().Be(string.Empty);
        Eval("Symbol().description;").IsUndefined.Should().BeTrue();
    }

    [TestMethod]
    public void Global_registry_round_trips_registered_symbols()
    {
        Eval("Symbol['for']('x') === Symbol['for']('x');").AsBool.Should().BeTrue();
        Eval("Symbol['for']('x') === Symbol['for']('y');").AsBool.Should().BeFalse();
        Eval("Symbol.keyFor(Symbol['for']('x'));").AsString.Should().Be("x");
        Eval("Symbol.keyFor(Symbol('x'));").IsUndefined.Should().BeTrue();
        Eval("Symbol['for'](123).description;").AsString.Should().Be("123");
    }

    [TestMethod]
    public void String_conversion_and_constructor_rules_match_symbol_spec()
    {
        Eval("var s = Symbol('x'); String(s);").AsString.Should().Be("Symbol(x)");
        Action add = () => Eval("var s = Symbol('x'); s + ''; ");
        add.Should().Throw<JsThrow>();
        Action construct = () => Eval("new Symbol('x');");
        construct.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void Symbols_work_as_non_enumerated_property_keys()
    {
        var r = Eval(@"
            var sym = Symbol('k');
            var o = {};
            o[sym] = 42;
            o.visible = 7;
            var symbols = Object.getOwnPropertySymbols(o);
            o[sym] + ',' + o.hasOwnProperty(sym) + ',' + Object.hasOwn(o, sym) + ',' +
                symbols.length + ',' + (symbols[0] === sym) + ',' + Object.keys(o).length + ',' + Object.keys(o)[0];
        ");
        r.AsString.Should().Be("42,true,true,1,true,1,visible");
    }

    [TestMethod]
    public void Symbol_keys_flow_through_object_descriptor_apis()
    {
        var r = Eval(@"
            var sym = Symbol('d');
            var o = {};
            Object.defineProperty(o, sym, { value: 99, writable: true, enumerable: false, configurable: true });
            var d = Object.getOwnPropertyDescriptor(o, sym);
            var symbols = Object.getOwnPropertySymbols(o);
            d.value + ',' + d.enumerable + ',' + o.propertyIsEnumerable(sym) + ',' + (symbols[0] === sym);
        ");
        r.AsString.Should().Be("99,false,false,true");
    }

    [TestMethod]
    public void Well_known_symbols_are_defined_distinct_and_stable()
    {
        Eval("typeof Symbol.iterator;").AsString.Should().Be("symbol");
        Eval("Symbol.iterator === Symbol.iterator;").AsBool.Should().BeTrue();
        Eval("Symbol.iterator === Symbol.toPrimitive;").AsBool.Should().BeFalse();
        Eval("Symbol.iterator = 1; typeof Symbol.iterator;").AsString.Should().Be("symbol");

        var all = Eval(@"
            typeof Symbol.asyncIterator + ',' +
            typeof Symbol.hasInstance + ',' +
            typeof Symbol.isConcatSpreadable + ',' +
            typeof Symbol.iterator + ',' +
            typeof Symbol.match + ',' +
            typeof Symbol.matchAll + ',' +
            typeof Symbol.replace + ',' +
            typeof Symbol.search + ',' +
            typeof Symbol.species + ',' +
            typeof Symbol.split + ',' +
            typeof Symbol.toPrimitive + ',' +
            typeof Symbol.toStringTag + ',' +
            typeof Symbol.unscopables;
        ");
        all.AsString.Should().Be("symbol,symbol,symbol,symbol,symbol,symbol,symbol,symbol,symbol,symbol,symbol,symbol,symbol");

        var distinct = Eval(@"
            Symbol.asyncIterator !== Symbol.hasInstance &&
            Symbol.hasInstance !== Symbol.isConcatSpreadable &&
            Symbol.isConcatSpreadable !== Symbol.iterator &&
            Symbol.iterator !== Symbol.match &&
            Symbol.match !== Symbol.matchAll &&
            Symbol.matchAll !== Symbol.replace &&
            Symbol.replace !== Symbol.search &&
            Symbol.search !== Symbol.species &&
            Symbol.species !== Symbol.split &&
            Symbol.split !== Symbol.toPrimitive &&
            Symbol.toPrimitive !== Symbol.toStringTag &&
            Symbol.toStringTag !== Symbol.unscopables;
        ");
        distinct.AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void ToPrimitive_well_known_symbol_is_consulted_before_string_or_valueOf()
    {
        var r = Eval(@"
            var o = {};
            o[Symbol.toPrimitive] = function(hint) { return 'ok-' + hint; };
            o + '!';
        ");
        r.AsString.Should().Be("ok-default!");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

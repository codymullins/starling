using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// JSON.stringify must serialize a real Array exotic object as a JSON array
/// (§25.5.2 IsArray ⇒ SerializeJSONArray), not as a plain object. Regression for
/// a bug where stringify only recognized JSON.parse's internal array type, so
/// array literals and <c>.map(...)</c> results came out as <c>{"0":…}</c>. This
/// broke any page using <c>JSON.stringify</c> on an array — including the WPT
/// runner reading testharness results.
/// </summary>
[TestClass]
public class JsonStringifyArrayTests
{
    [TestMethod] public void Stringify_array_literal()
        => Eval("JSON.stringify([1,2,3]);").AsString.Should().Be("[1,2,3]");

    [TestMethod] public void Stringify_map_result()
        => Eval("JSON.stringify([1,2,3].map(function(x){return x*2;}));").AsString.Should().Be("[2,4,6]");

    [TestMethod] public void Stringify_nested_array_of_objects()
        => Eval("JSON.stringify([{a:1},{a:2}]);").AsString.Should().Be("[{\"a\":1},{\"a\":2}]");

    [TestMethod] public void Stringify_parse_roundtrip()
        => Eval("JSON.stringify(JSON.parse('[1,[2,3],4]'));").AsString.Should().Be("[1,[2,3],4]");

    private static JsValue Eval(string src)
        => new JsVm(new JsRuntime()).Run(JsCompiler.CompileForEval(new JsParser(src).ParseProgram()));
}

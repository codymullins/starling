using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Intrinsics;

/// <summary>
/// End-to-end tests for the <c>JSON</c> intrinsic — <c>parse</c> and
/// <c>stringify</c> per §25.5.
/// </summary>
/// <remarks>
/// Arrays are produced indirectly via <c>JSON.parse</c> (the engine doesn't
/// yet compile array literals — that lands with B2-4). Each test runs a
/// fresh runtime so the JSON global is installed without contamination.
/// </remarks>
public class JsonTests
{
    // ============================================================
    // JSON.parse — primitives and shapes
    // ============================================================

    [Fact]
    public void Parse_null_primitive()
    {
        var v = Eval("JSON.parse('null');");
        v.IsNull.Should().BeTrue();
    }

    [Fact]
    public void Parse_true_primitive()
    {
        Eval("JSON.parse('true');").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Parse_number_primitive()
    {
        Eval("JSON.parse('42');").AsNumber.Should().Be(42);
    }

    [Fact]
    public void Parse_string_primitive()
    {
        Eval("JSON.parse('\"hi\"');").AsString.Should().Be("hi");
    }

    [Fact]
    public void Parse_negative_and_exponent_numbers()
    {
        Eval("JSON.parse('-3.5');").AsNumber.Should().Be(-3.5);
        Eval("JSON.parse('1e3');").AsNumber.Should().Be(1000);
    }

    [Fact]
    public void Parse_escape_sequences()
    {
        // JS source: JSON.parse('"\n\tA"')  — the JS string literal
        // contains literal backslashes, so when JSON.parse receives the text
        // it sees the six characters {", \, n, \, t, \, u, 0, 0, 4, 1, "}
        // and decodes them to {newline, tab, 'A'}.
        Eval("JSON.parse('\"\\\\n\\\\t\\\\u0041\"');").AsString.Should().Be("\n\tA");
    }

    [Fact]
    public void Parse_object_with_nested_array()
    {
        // Round-trip through stringify since we can't yet index arrays via
        // literal syntax. Verifies structure + value reproduction.
        Eval("JSON.stringify(JSON.parse('{\"a\":[1,2,{\"b\":true}]}'));")
            .AsString.Should().Be("{\"a\":[1,2,{\"b\":true}]}");
    }

    [Fact]
    public void Parse_array_indexable_and_has_length()
    {
        Eval("var a = JSON.parse('[10,20,30]'); a.length;").AsNumber.Should().Be(3);
        Eval("var a = JSON.parse('[10,20,30]'); a[1];").AsNumber.Should().Be(20);
    }

    // ============================================================
    // JSON.parse — invalid grammar throws SyntaxError
    // ============================================================

    [Theory]
    [InlineData("'{\"a\":1,}'")]      // trailing comma in object
    [InlineData("'[1,2,]'")]           // trailing comma in array
    [InlineData("'{a:1}'")]            // unquoted key
    [InlineData("\"'single'\"")]      // single-quoted strings rejected
    [InlineData("'undefined'")]
    [InlineData("'NaN'")]
    [InlineData("'.5'")]               // bare-fraction number
    public void Parse_invalid_throws_syntax_error(string src)
    {
        var action = () => Eval($"JSON.parse({src});");
        action.Should().Throw<JsThrow>();
    }

    // ============================================================
    // JSON.parse — reviver
    // ============================================================

    [Fact]
    public void Parse_reviver_multiplies_numbers()
    {
        var s = Eval(@"
            var a = JSON.parse('[1,2,3]', (k, v) => typeof v === 'number' ? v * 10 : v);
            JSON.stringify(a);
        ");
        s.AsString.Should().Be("[10,20,30]");
    }

    [Fact]
    public void Parse_reviver_returning_undefined_drops_key()
    {
        var s = Eval(@"
            var o = JSON.parse('{""a"":1,""b"":2}', (k, v) => k === 'a' ? undefined : v);
            JSON.stringify(o);
        ");
        s.AsString.Should().Be("{\"b\":2}");
    }

    // ============================================================
    // JSON.stringify — primitives, objects, arrays
    // ============================================================

    [Fact]
    public void Stringify_flat_object()
    {
        Eval("JSON.stringify({a:1, b:\"x\", c:true, d:null});")
            .AsString.Should().Be("{\"a\":1,\"b\":\"x\",\"c\":true,\"d\":null}");
    }

    [Fact]
    public void Stringify_nan_and_infinity_become_null()
    {
        Eval("JSON.stringify(NaN);").AsString.Should().Be("null");
        Eval("JSON.stringify(Infinity);").AsString.Should().Be("null");
        Eval("JSON.stringify(-Infinity);").AsString.Should().Be("null");
    }

    [Fact]
    public void Stringify_undefined_in_object_is_omitted()
    {
        Eval("JSON.stringify({a:1, b:undefined, c:3});")
            .AsString.Should().Be("{\"a\":1,\"c\":3}");
    }

    [Fact]
    public void Stringify_undefined_in_array_becomes_null()
    {
        // Build an array with an undefined slot through JSON.parse + mutation.
        var s = Eval(@"
            var a = JSON.parse('[0,0,0]');
            a[0] = 1;
            a[1] = undefined;
            a[2] = 3;
            JSON.stringify(a);
        ");
        s.AsString.Should().Be("[1,null,3]");
    }

    [Fact]
    public void Stringify_function_value_omitted_from_object()
    {
        Eval("JSON.stringify({a:1, f: function(){}, b:2});")
            .AsString.Should().Be("{\"a\":1,\"b\":2}");
    }

    [Fact]
    public void Stringify_function_in_array_becomes_null()
    {
        var s = Eval(@"
            var a = JSON.parse('[0,0]');
            a[0] = function(){};
            a[1] = 7;
            JSON.stringify(a);
        ");
        s.AsString.Should().Be("[null,7]");
    }

    [Fact]
    public void Stringify_honors_toJSON()
    {
        Eval("JSON.stringify({ toJSON: function() { return 'custom'; } });")
            .AsString.Should().Be("\"custom\"");
    }

    [Fact]
    public void Stringify_cycle_throws_type_error()
    {
        var action = () => Eval("var a = {}; a.self = a; JSON.stringify(a);");
        action.Should().Throw<JsThrow>();
    }

    // ============================================================
    // JSON.stringify — replacer + space
    // ============================================================

    [Fact]
    public void Stringify_replacer_array_keeps_allowed_keys_only()
    {
        // The replacer "array" comes through JSON.parse so it's a JsonArray.
        Eval("JSON.stringify({a:1, b:2, c:3}, JSON.parse('[\"a\",\"c\"]'));")
            .AsString.Should().Be("{\"a\":1,\"c\":3}");
    }

    [Fact]
    public void Stringify_replacer_function_drops_key()
    {
        Eval("JSON.stringify({a:1, b:2}, function(k, v) { return k === 'a' ? undefined : v; });")
            .AsString.Should().Be("{\"b\":2}");
    }

    [Fact]
    public void Stringify_indent_number_pretty_prints_object()
    {
        Eval("JSON.stringify({a:1}, null, 2);")
            .AsString.Should().Be("{\n  \"a\": 1\n}");
    }

    [Fact]
    public void Stringify_indent_string_pretty_prints_array()
    {
        Eval("JSON.stringify(JSON.parse('[1,2]'), null, '\t');")
            .AsString.Should().Be("[\n\t1,\n\t2\n]");
    }

    [Fact]
    public void Stringify_nested_indent_levels_grow()
    {
        // Nested object inside object — indent grows by one gap per level.
        Eval("JSON.stringify({a:{b:1}}, null, 2);")
            .AsString.Should().Be("{\n  \"a\": {\n    \"b\": 1\n  }\n}");
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

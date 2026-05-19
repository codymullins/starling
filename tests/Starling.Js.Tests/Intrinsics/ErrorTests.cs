using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Intrinsics;

/// <summary>
/// End-to-end coverage for the <c>Error</c> intrinsic family (B2-3). Verifies
/// that each of the eight constructors is callable + constructible, has the
/// expected name/message/toString behavior, and inherits via the right
/// prototype chain.
/// </summary>
/// <remarks>
/// <c>instanceof</c> is not yet wired in the compiler (tracked in
/// wp:M3-05), so the inheritance assertions below probe the prototype chain
/// directly via <c>Object.getPrototypeOf</c> instead. The semantics are
/// equivalent — <c>instanceof</c> walks the same chain.
/// </remarks>
public class ErrorTests
{
    // ---------------------------------------------------------- callable

    [Fact]
    public void Error_call_with_message_sets_message_own_property()
    {
        Eval("Error('boom').message;").AsString.Should().Be("boom");
    }

    [Fact]
    public void TypeError_call_with_message_sets_message_own_property()
    {
        Eval("TypeError('x').message;").AsString.Should().Be("x");
    }

    [Fact]
    public void RangeError_call_with_message_sets_message_own_property()
    {
        Eval("RangeError('r').message;").AsString.Should().Be("r");
    }

    [Fact]
    public void ReferenceError_call_with_message_sets_message_own_property()
    {
        Eval("ReferenceError('ref').message;").AsString.Should().Be("ref");
    }

    [Fact]
    public void SyntaxError_call_with_message_sets_message_own_property()
    {
        Eval("SyntaxError('s').message;").AsString.Should().Be("s");
    }

    [Fact]
    public void UriError_call_with_message_sets_message_own_property()
    {
        Eval("URIError('u').message;").AsString.Should().Be("u");
    }

    [Fact]
    public void EvalError_call_with_message_sets_message_own_property()
    {
        Eval("EvalError('e').message;").AsString.Should().Be("e");
    }

    // ---------------------------------------------------------- construct + name

    [Fact]
    public void New_TypeError_has_name_TypeError()
    {
        Eval("(new TypeError('y')).name;").AsString.Should().Be("TypeError");
    }

    [Fact]
    public void New_Error_has_name_Error()
    {
        Eval("(new Error()).name;").AsString.Should().Be("Error");
    }

    [Fact]
    public void New_RangeError_has_name_RangeError()
    {
        Eval("(new RangeError()).name;").AsString.Should().Be("RangeError");
    }

    [Fact]
    public void New_AggregateError_has_name_AggregateError()
    {
        Eval("(new AggregateError([])).name;").AsString.Should().Be("AggregateError");
    }

    // ---------------------------------------------------------- prototype chain

    [Fact]
    public void TypeError_instance_prototype_is_TypeError_prototype()
    {
        Eval(@"
            var e = new TypeError();
            Object.getPrototypeOf(e) === TypeError.prototype;
        ").Should().Be(JsValue.True);
    }

    [Fact]
    public void TypeError_prototype_chain_walks_to_Error_prototype()
    {
        Eval(@"
            Object.getPrototypeOf(TypeError.prototype) === Error.prototype;
        ").Should().Be(JsValue.True);
    }

    [Fact]
    public void TypeError_instance_chain_reaches_Error_prototype()
    {
        // Equivalent to `new TypeError() instanceof Error` — walks the chain
        // looking for Error.prototype.
        Eval(@"
            var e = new TypeError();
            var p1 = Object.getPrototypeOf(e);
            var p2 = Object.getPrototypeOf(p1);
            p2 === Error.prototype;
        ").Should().Be(JsValue.True);
    }

    [Fact]
    public void TypeError_instance_chain_reaches_TypeError_prototype()
    {
        // Equivalent to `new TypeError() instanceof TypeError`.
        Eval(@"
            var e = new TypeError();
            Object.getPrototypeOf(e) === TypeError.prototype;
        ").Should().Be(JsValue.True);
    }

    [Fact]
    public void All_error_subclasses_inherit_from_Error_prototype()
    {
        var subs = new[] { "TypeError", "RangeError", "ReferenceError", "SyntaxError", "URIError", "EvalError", "AggregateError" };
        foreach (var name in subs)
        {
            Eval($"Object.getPrototypeOf({name}.prototype) === Error.prototype;")
                .Should().Be(JsValue.True, because: $"{name}.prototype should inherit from Error.prototype");
        }
    }

    // ---------------------------------------------------------- toString

    [Fact]
    public void Error_with_no_message_toString_returns_just_name()
    {
        Eval("(new Error()).toString();").AsString.Should().Be("Error");
    }

    [Fact]
    public void Error_with_message_toString_returns_name_colon_message()
    {
        Eval("(new Error('m')).toString();").AsString.Should().Be("Error: m");
    }

    [Fact]
    public void TypeError_with_message_toString_returns_name_colon_message()
    {
        Eval("(new TypeError('z')).toString();").AsString.Should().Be("TypeError: z");
    }

    [Fact]
    public void TypeError_with_no_message_toString_returns_just_name()
    {
        Eval("(new TypeError()).toString();").AsString.Should().Be("TypeError");
    }

    // ---------------------------------------------------------- cause

    [Fact]
    public void Error_with_options_cause_records_cause_on_instance()
    {
        Eval("(new Error('m', { cause: 42 })).cause;").AsNumber.Should().Be(42);
    }

    [Fact]
    public void TypeError_with_options_cause_records_cause_on_instance()
    {
        Eval("(new TypeError('m', { cause: 'why' })).cause;").AsString.Should().Be("why");
    }

    [Fact]
    public void Error_without_options_cause_has_no_cause_own_property()
    {
        Eval(@"
            var e = new Error('m');
            Object.prototype.hasOwnProperty.call(e, 'cause');
        ").Should().Be(JsValue.False);
    }

    // ---------------------------------------------------------- AggregateError

    [Fact]
    public void AggregateError_copies_errors_array_into_errors_property()
    {
        Eval(@"
            var e = new AggregateError([new Error('a'), new Error('b')], 'agg');
            e.errors.length;
        ").AsNumber.Should().Be(2);
    }

    [Fact]
    public void AggregateError_message_is_second_argument()
    {
        Eval("(new AggregateError([], 'agg')).message;").AsString.Should().Be("agg");
    }

    [Fact]
    public void AggregateError_preserves_individual_error_messages()
    {
        Eval(@"
            var e = new AggregateError([new Error('a'), new Error('b')], 'agg');
            e.errors[0].message + '/' + e.errors[1].message;
        ").AsString.Should().Be("a/b");
    }

    [Fact]
    public void AggregateError_throws_TypeError_on_non_array_like_errors()
    {
        var rt = new JsRuntime();
        Action act = () => new JsVm(rt).Run(JsCompiler.CompileForEval(new JsParser("new AggregateError(123);").ParseProgram()));
        act.Should().Throw<JsThrow>();
    }

    // ---------------------------------------------------------- constructor back-ref

    [Fact]
    public void Error_prototype_constructor_points_back_at_Error()
    {
        Eval("Error.prototype.constructor === Error;").Should().Be(JsValue.True);
    }

    [Fact]
    public void Each_subclass_prototype_constructor_points_back_at_its_constructor()
    {
        var subs = new[] { "TypeError", "RangeError", "ReferenceError", "SyntaxError", "URIError", "EvalError", "AggregateError" };
        foreach (var name in subs)
        {
            Eval($"{name}.prototype.constructor === {name};")
                .Should().Be(JsValue.True, because: $"{name}.prototype.constructor should be {name}");
        }
    }

    // ---------------------------------------------------------- constructor metadata

    [Fact]
    public void Error_constructor_name_is_Error()
    {
        Eval("Error.name;").AsString.Should().Be("Error");
    }

    [Fact]
    public void Error_constructor_length_is_one()
    {
        Eval("Error.length;").AsNumber.Should().Be(1);
    }

    [Fact]
    public void AggregateError_constructor_length_is_two()
    {
        Eval("AggregateError.length;").AsNumber.Should().Be(2);
    }

    // ---------------------------------------------------------- helpers

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}

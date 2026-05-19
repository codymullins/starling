using AwesomeAssertions;
using Starling.Js.Ast;
using Starling.Js.Parse;

namespace Starling.Js.Tests.Parse;

[TestClass]
public class JsParserDestructuringTests
{
    [TestMethod]
    public void Variable_array_pattern_supports_holes_defaults_nested_and_rest()
    {
        var decl = ParseProgram("let [a = 1, , {b}, ...rest] = xs;").Body[0]
            .Should().BeOfType<VariableDeclaration>().Subject;

        var pattern = decl.Declarations[0].Id.Should().BeOfType<ArrayPattern>().Subject;
        pattern.Elements.Should().HaveCount(4);
        pattern.Elements[0].Should().BeOfType<ArrayPatternBindingElement>()
            .Which.Default.Should().BeOfType<NumericLiteral>();
        pattern.Elements[1].Should().BeOfType<ArrayPatternHole>();
        pattern.Elements[2].Should().BeOfType<ArrayPatternBindingElement>()
            .Which.Target.Should().BeOfType<ObjectPattern>();
        pattern.Elements[3].Should().BeOfType<ArrayPatternRestElement>()
            .Which.Target.Should().BeOfType<Identifier>()
            .Which.Name.Should().Be("rest");
    }

    [TestMethod]
    public void Variable_object_pattern_supports_shorthand_renamed_computed_defaults_and_rest()
    {
        var decl = ParseProgram("const {a, b: c = 2, [k]: v, ...rest} = obj;").Body[0]
            .Should().BeOfType<VariableDeclaration>().Subject;

        var pattern = decl.Declarations[0].Id.Should().BeOfType<ObjectPattern>().Subject;
        pattern.Properties.Should().HaveCount(3);
        pattern.Properties[0].Shorthand.Should().BeTrue();
        pattern.Properties[1].Target.Should().BeOfType<Identifier>().Which.Name.Should().Be("c");
        pattern.Properties[1].Default.Should().BeOfType<NumericLiteral>();
        pattern.Properties[2].Computed.Should().BeTrue();
        pattern.Rest.Should().NotBeNull();
        pattern.Rest!.Argument.Should().BeOfType<Identifier>().Which.Name.Should().Be("rest");
    }

    [TestMethod]
    public void Function_and_arrow_parameters_accept_patterns_and_defaults()
    {
        var fn = ParseProgram("function f({a}, [b] = xs, ...rest) {}").Body[0]
            .Should().BeOfType<FunctionDeclaration>().Subject;
        fn.Params[0].Should().BeOfType<ObjectPattern>();
        fn.Params[1].Should().BeOfType<AssignmentPattern>()
            .Which.Target.Should().BeOfType<ArrayPattern>();
        fn.Params[2].Should().BeOfType<SpreadElement>()
            .Which.Argument.Should().BeOfType<Identifier>();

        var arrow = ParseExpression("({x}, [y = 1]) => x + y")
            .Should().BeOfType<ArrowFunctionExpression>().Subject;
        arrow.Params[0].Should().BeOfType<ObjectPattern>();
        arrow.Params[1].Should().BeOfType<ArrayPattern>();
    }

    [TestMethod]
    public void For_heads_and_catch_parameters_accept_patterns()
    {
        ParseProgram("for (const {a} of xs) a;").Body[0]
            .Should().BeOfType<ForOfStatement>()
            .Which.Left.Should().BeOfType<VariableDeclaration>()
            .Which.Declarations[0].Id.Should().BeOfType<ObjectPattern>();

        ParseProgram("for ([k, v] in obj) log(k, v);").Body[0]
            .Should().BeOfType<ForInStatement>()
            .Which.Left.Should().BeOfType<ArrayPattern>();

        ParseProgram("try { throw e; } catch ({message}) { log(message); }").Body[0]
            .Should().BeOfType<TryStatement>()
            .Which.Handler!.Param.Should().BeOfType<ObjectPattern>();
    }

    [TestMethod]
    public void Assignment_targets_reinterpret_cover_literals_as_patterns()
    {
        var arrayAssign = ParseExpression("[a, {b}] = xs")
            .Should().BeOfType<AssignmentExpression>().Subject;
        arrayAssign.Target.Should().BeOfType<ArrayPattern>();

        var objectAssign = ParseExpression("({a: target = 1, ...rest} = obj)")
            .Should().BeOfType<AssignmentExpression>().Subject;
        var objectPattern = objectAssign.Target.Should().BeOfType<ObjectPattern>().Subject;
        objectPattern.Properties[0].Default.Should().BeOfType<NumericLiteral>();
        objectPattern.Rest.Should().NotBeNull();
    }

    [TestMethod]
    public void Nested_rest_binding_patterns_are_recorded()
    {
        var array = ParseProgram("let [...[a]] = xs;").Body[0]
            .Should().BeOfType<VariableDeclaration>().Subject
            .Declarations[0].Id.Should().BeOfType<ArrayPattern>().Subject;
        array.Elements[0].Should().BeOfType<ArrayPatternRestElement>()
            .Which.Target.Should().BeOfType<ArrayPattern>();

        var obj = ParseProgram("let {...{a}} = xs;").Body[0]
            .Should().BeOfType<VariableDeclaration>().Subject
            .Declarations[0].Id.Should().BeOfType<ObjectPattern>().Subject;
        obj.Rest!.Argument.Should().BeOfType<ObjectPattern>();
    }

    [TestMethod]
    public void Invalid_pattern_rest_forms_throw()
    {
        Action arrayRestNotLast = () => ParseProgram("let [a, ...rest, b] = xs;");
        arrayRestNotLast.Should().Throw<JsParseException>();

        Action objectRestNotLast = () => ParseProgram("let {...rest, a} = xs;");
        objectRestNotLast.Should().Throw<JsParseException>();

        Action objectRestDefault = () => ParseProgram("let {...rest = 1} = xs;");
        objectRestDefault.Should().Throw<JsParseException>();

        Action computedShorthand = () => ParseProgram("let {[k]} = xs;");
        computedShorthand.Should().Throw<JsParseException>();
    }

    private static Program ParseProgram(string src) => new JsParser(src).ParseProgram();
    private static Expression ParseExpression(string src) => new JsParser(src).ParseExpression();
}

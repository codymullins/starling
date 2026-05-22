using Starling.Js.Ast;
using Starling.Js.Lex;

namespace Starling.Js.Parse;

public sealed partial class JsParser
{
    private Expression ParseBindingTarget()
    {
        if (Check(JsTokenKind.LBracket)) return ParseArrayBindingPattern();
        if (Check(JsTokenKind.LBrace)) return ParseObjectBindingPattern();
        // In sloppy (non-strict) code outside a generator, `yield` is a plain
        // IdentifierReference and a legal BindingIdentifier (§12.7.1). The lexer
        // always classifies it as the `Yield` keyword token, so accept it here.
        // Strict-mode misuse is still caught by CheckBindingIdentifier callers.
        if (!_strict && Check(JsTokenKind.Yield))
        {
            var y = Advance();
            return new Identifier(y.Lexeme, y.Start, y.End);
        }
        var id = Expect(JsTokenKind.Identifier, "expected binding name or pattern");
        return new Identifier(id.Lexeme, id.Start, id.End);
    }

    private Expression ParseParameter()
    {
        var target = ParseBindingTarget();
        if (!Match(JsTokenKind.Eq)) return target;
        var fallback = ParseAssignment();
        return new AssignmentPattern(target, fallback, target.Start, fallback.End);
    }

    private ArrayPattern ParseArrayBindingPattern()
    {
        var start = _current.Start;
        Expect(JsTokenKind.LBracket, "[ expected");
        var elements = new List<ArrayPatternElement>();
        while (!Check(JsTokenKind.RBracket))
        {
            if (Check(JsTokenKind.Comma))
            {
                var hole = Advance();
                elements.Add(new ArrayPatternHole(hole.Start, hole.End));
                continue;
            }

            if (Check(JsTokenKind.Ellipsis))
            {
                var restStart = _current.Start;
                Advance();
                var target = ParseBindingTarget();
                if (Check(JsTokenKind.Eq))
                    throw new JsParseException("array rest binding cannot have a default", _current.Start);
                elements.Add(new ArrayPatternRestElement(target, restStart, target.End));
                if (!Check(JsTokenKind.RBracket))
                    throw new JsParseException("array rest binding must be last", _current.Start);
                break;
            }

            var elementStart = _current.Start;
            var inner = ParseBindingTarget();
            Expression? fallback = null;
            if (Match(JsTokenKind.Eq)) fallback = ParseAssignment();
            elements.Add(new ArrayPatternBindingElement(inner, fallback, elementStart, (fallback ?? inner).End));
            if (!Match(JsTokenKind.Comma)) break;
        }
        var end = _current.End;
        Expect(JsTokenKind.RBracket, "expected ']' to close array binding pattern");
        return new ArrayPattern(elements, start, end);
    }

    private ObjectPattern ParseObjectBindingPattern()
    {
        var start = _current.Start;
        Expect(JsTokenKind.LBrace, "{ expected");
        var props = new List<ObjectPatternProperty>();
        RestElement? rest = null;
        while (!Check(JsTokenKind.RBrace))
        {
            if (Check(JsTokenKind.Ellipsis))
            {
                var restStart = _current.Start;
                Advance();
                var target = ParseBindingTarget();
                if (Check(JsTokenKind.Eq))
                    throw new JsParseException("object rest binding cannot have a default", _current.Start);
                rest = new RestElement(target, restStart, target.End);
                if (!Check(JsTokenKind.RBrace))
                    throw new JsParseException("object rest binding must be last", _current.Start);
                break;
            }

            props.Add(ParseObjectBindingProperty());
            if (!Match(JsTokenKind.Comma)) break;
        }
        var end = _current.End;
        Expect(JsTokenKind.RBrace, "expected '}' to close object binding pattern");
        return new ObjectPattern(props, rest, start, end);
    }

    private ObjectPatternProperty ParseObjectBindingProperty()
    {
        var start = _current.Start;
        var (key, computed) = ParsePatternPropertyKey();

        if (Match(JsTokenKind.Colon))
        {
            var target = ParseBindingTarget();
            Expression? fallback = null;
            if (Match(JsTokenKind.Eq)) fallback = ParseAssignment();
            return new ObjectPatternProperty(key, target, Shorthand: false, computed,
                fallback, start, (fallback ?? target).End);
        }

        if (!computed && key is Identifier id)
        {
            Expression? fallback = null;
            if (Match(JsTokenKind.Eq)) fallback = ParseAssignment();
            return new ObjectPatternProperty(key, id, Shorthand: true, Computed: false,
                fallback, start, (fallback ?? id).End);
        }

        throw new JsParseException("expected ':' after computed object binding key", _current.Start);
    }

    private (Expression Key, bool Computed) ParsePatternPropertyKey()
    {
        if (Match(JsTokenKind.LBracket))
        {
            // A computed key is `[ AssignmentExpression[+In] ]` — `in` is always
            // allowed inside the brackets even within a `for` header [NoIn].
            var savedNoIn = _disallowInDepth;
            _disallowInDepth = 0;
            Expression expr;
            try { expr = ParseAssignment(); }
            finally { _disallowInDepth = savedNoIn; }
            Expect(JsTokenKind.RBracket, "expected ']' after computed key");
            return (expr, true);
        }
        if (_current.Kind == JsTokenKind.StringLiteral)
        {
            var t = Advance();
            return (new StringLiteral((string)t.Value!, t.Start, t.End), false);
        }
        if (_current.Kind == JsTokenKind.NumericLiteral)
        {
            var t = Advance();
            return (new NumericLiteral((double)t.Value!, t.Start, t.End), false);
        }
        if (_current.Kind == JsTokenKind.Identifier || IsReservedNameAllowedAsPropertyName(_current.Kind))
        {
            var t = Advance();
            return (new Identifier(t.Lexeme, t.Start, t.End), false);
        }
        throw new JsParseException($"expected property name, got {_current.Kind}", _current.Start);
    }

    private static Expression ReinterpretAssignmentTarget(Expression expr)
        => expr switch
        {
            ArrayExpression array => ReinterpretArrayPattern(array),
            ObjectExpression obj => ReinterpretObjectPattern(obj),
            AssignmentExpression { Op: "=" } assignment => new AssignmentPattern(
                ReinterpretAssignmentTarget(assignment.Target), assignment.Value, assignment.Start, assignment.End),
            AssignmentPattern assignment => new AssignmentPattern(
                ReinterpretAssignmentTarget(assignment.Target), assignment.Default, assignment.Start, assignment.End),
            // wp:M3-04h — `super[expr]` (and `super.name`) are valid
            // SimpleAssignmentTargets per §13.15.1; the compiler lowers the
            // write to StoreSuperComputed / StoreSuperProperty.
            Identifier or MemberExpression or SuperPropertyExpression => expr,
            BindingPattern => expr,
            _ => throw new JsParseException("invalid destructuring assignment target", expr.Start),
        };

    private static Expression ReinterpretBindingParameter(Expression expr)
        => expr switch
        {
            AssignmentExpression { Op: "=" } assignment => new AssignmentPattern(
                ReinterpretBindingParameter(assignment.Target), assignment.Value, assignment.Start, assignment.End),
            AssignmentPattern assignment => new AssignmentPattern(
                ReinterpretBindingParameter(assignment.Target), assignment.Default, assignment.Start, assignment.End),
            _ => ReinterpretBindingTarget(expr),
        };

    private static Expression ReinterpretBindingTarget(Expression expr)
        => expr switch
        {
            ArrayExpression array => ReinterpretArrayPattern(array),
            ObjectExpression obj => ReinterpretObjectPattern(obj),
            Identifier => expr,
            BindingPattern => expr,
            _ => throw new JsParseException("binding pattern must contain only binding identifiers", expr.Start),
        };

    private static ArrayPattern ReinterpretArrayPattern(ArrayExpression array)
    {
        var elements = new List<ArrayPatternElement>();
        for (var i = 0; i < array.Elements.Count; i++)
        {
            var element = array.Elements[i];
            if (element is null)
            {
                elements.Add(new ArrayPatternHole(array.Start, array.Start));
                continue;
            }
            if (element is SpreadElement spread)
            {
                if (i != array.Elements.Count - 1)
                    throw new JsParseException("array rest binding must be last", spread.Start);
                elements.Add(new ArrayPatternRestElement(ReinterpretAssignmentTarget(spread.Argument), spread.Start, spread.End));
                continue;
            }
            if (element is AssignmentExpression { Op: "=" } assignment)
            {
                var target = ReinterpretAssignmentTarget(assignment.Target);
                elements.Add(new ArrayPatternBindingElement(target, assignment.Value, assignment.Start, assignment.End));
                continue;
            }
            var targetElement = ReinterpretAssignmentTarget(element);
            elements.Add(new ArrayPatternBindingElement(targetElement, null, targetElement.Start, targetElement.End));
        }
        return new ArrayPattern(elements, array.Start, array.End);
    }

    private static ObjectPattern ReinterpretObjectPattern(ObjectExpression obj)
    {
        var props = new List<ObjectPatternProperty>();
        RestElement? rest = null;
        for (var i = 0; i < obj.Properties.Count; i++)
        {
            var prop = obj.Properties[i];
            if (prop.Value is SpreadElement spread)
            {
                if (i != obj.Properties.Count - 1)
                    throw new JsParseException("object rest binding must be last", spread.Start);
                rest = new RestElement(ReinterpretAssignmentTarget(spread.Argument), spread.Start, spread.End);
                continue;
            }

            var value = prop.Value;
            Expression? fallback = null;
            if (value is AssignmentExpression { Op: "=" } assignment)
            {
                value = assignment.Target;
                fallback = assignment.Value;
            }
            var target = ReinterpretAssignmentTarget(value);
            props.Add(new ObjectPatternProperty(prop.Key, target, prop.Shorthand, prop.Computed,
                fallback, prop.Start, (fallback ?? target).End));
        }
        return new ObjectPattern(props, rest, obj.Start, obj.End);
    }
}

using Tessera.Js.Ast;
using Tessera.Js.Lex;

namespace Tessera.Js.Parse;

/// <summary>
/// Recursive-descent ES2024 parser. This slice (wp:M3-02a) implements
/// expression-level parsing only; statements + declarations land in
/// wp:M3-02b.
/// </summary>
/// <remarks>
/// Operator precedence mirrors ES2024 §13.16. Methods are named by
/// precedence level (e.g. <c>ParseAdditive</c>, <c>ParseMultiplicative</c>)
/// so cross-referencing the spec is line-of-sight. Right-associative
/// operators (assignment, exponentiation, conditional) recurse on the
/// right side; left-associative ones loop.
/// </remarks>
public sealed partial class JsParser
{
    private readonly JsLexer _lex;
    private JsToken _current;

    public JsParser(string source)
        : this(new JsLexer(source)) { }

    public JsParser(JsLexer lex)
    {
        _lex = lex ?? throw new ArgumentNullException(nameof(lex));
        _current = _lex.Next();
    }

    // -----------------------------------------------------------------------
    // Public entries
    // -----------------------------------------------------------------------

    /// <summary>
    /// Parse a full expression and require EOF immediately after. Throws
    /// <see cref="JsParseException"/> on syntax error.
    /// </summary>
    public Expression ParseExpression()
    {
        var expr = ParseAssignment();
        // Allow sequence at the top.
        if (Match(JsTokenKind.Comma))
        {
            var parts = new List<Expression> { expr };
            do { parts.Add(ParseAssignment()); }
            while (Match(JsTokenKind.Comma));
            expr = new SequenceExpression(parts, parts[0].Start, parts[^1].End);
        }
        Expect(JsTokenKind.EndOfFile, "expected end of input");
        return expr;
    }

    // -----------------------------------------------------------------------
    // Token plumbing
    // -----------------------------------------------------------------------

    private bool Check(JsTokenKind k) => _current.Kind == k;

    private bool Match(JsTokenKind k)
    {
        if (_current.Kind != k) return false;
        Advance();
        return true;
    }

    private JsToken Advance()
    {
        var prev = _current;
        _current = _lex.Next();
        return prev;
    }

    private JsToken Expect(JsTokenKind k, string what)
    {
        if (_current.Kind != k)
            throw new JsParseException($"{what} (got {_current.Kind} '{_current.Lexeme}')", _current.Start);
        return Advance();
    }

    // -----------------------------------------------------------------------
    // ES2024 §13.16 precedence ladder (low to high).
    // -----------------------------------------------------------------------

    // AssignmentExpression
    private Expression ParseAssignment()
    {
        // Arrow function fast path: a bare identifier followed by '=>' is the
        // concise-param form `x => expr`.
        if (_current.Kind == JsTokenKind.Identifier && _lex.Peek().Kind == JsTokenKind.Arrow)
        {
            var paramTok = Advance();
            var param = new Identifier(paramTok.Lexeme, paramTok.Start, paramTok.End);
            Expect(JsTokenKind.Arrow, "expected '=>' in arrow function");
            return ParseArrowBody(new List<Expression> { param }, paramTok.Start);
        }
        // Empty-param arrow: `() =>`. Two-token lookahead.
        if (_current.Kind == JsTokenKind.LParen && _lex.Peek().Kind == JsTokenKind.RParen)
        {
            var start = _current.Start;
            Advance(); Advance(); // consume `(` and `)`
            Expect(JsTokenKind.Arrow, "expected '=>' after '()' in arrow function");
            return ParseArrowBody(Array.Empty<Expression>(), start);
        }

        var left = ParseConditional();
        // Parenthesized-params arrow form: ParseConditional() consumed the
        // `(...)` as either a grouping or a sequence. Either case maps cleanly
        // to ArrowFunctionExpression when followed by `=>`.
        if (_current.Kind == JsTokenKind.Arrow)
        {
            Advance();
            var paramList = LiftArrowParams(left);
            return ParseArrowBody(paramList, left.Start);
        }
        if (IsAssignmentOp(_current.Kind))
        {
            var op = _current.Lexeme;
            Advance();
            var right = ParseAssignment(); // right-associative
            return new AssignmentExpression(op, left, right, left.Start, right.End);
        }
        return left;
    }

    private ArrowFunctionExpression ParseArrowBody(IReadOnlyList<Expression> @params, JsPosition start)
    {
        if (Check(JsTokenKind.LBrace))
        {
            var block = ParseBlock();
            return new ArrowFunctionExpression(@params, block, IsExpression: false, Async: false, start, block.End);
        }
        var expr = ParseAssignment();
        return new ArrowFunctionExpression(@params, expr, IsExpression: true, Async: false, start, expr.End);
    }

    /// <summary>Turn a parenthesized expression list (already parsed as a
    /// grouping or <see cref="SequenceExpression"/>) back into an arrow
    /// parameter list. Today: identifiers only — destructuring patterns land
    /// in B1b-2.</summary>
    private static List<Expression> LiftArrowParams(Expression expr)
    {
        var list = new List<Expression>();
        switch (expr)
        {
            case SequenceExpression seq:
                foreach (var e in seq.Expressions) list.Add(LiftSingleParam(e));
                break;
            case Identifier:
            case ArrayExpression:
            case ObjectExpression:
            case AssignmentExpression { Op: "=" }:
                list.Add(LiftSingleParam(expr));
                break;
            default:
                throw new JsParseException(
                    "arrow parameter list must be identifiers or destructuring patterns", expr.Start);
        }
        return list;
    }

    private static Expression LiftSingleParam(Expression e) => e switch
    {
        Identifier => e,
        ArrayExpression => e,
        ObjectExpression => e,
        AssignmentExpression { Op: "=" } a when IsBindingPattern(a.Target) => e,
        _ => throw new JsParseException(
            "arrow parameter list must be identifiers or destructuring patterns", e.Start),
    };

    private static bool IsBindingPattern(Expression e) => e switch
    {
        Identifier => true,
        ArrayExpression => true,
        ObjectExpression => true,
        AssignmentExpression { Op: "=" } a => IsBindingPattern(a.Target),
        SpreadElement { Argument: var inner } => IsBindingPattern(inner),
        _ => false,
    };

    private static bool IsAssignmentOp(JsTokenKind k) => k is
        JsTokenKind.Eq or JsTokenKind.PlusEq or JsTokenKind.MinusEq
        or JsTokenKind.StarEq or JsTokenKind.SlashEq or JsTokenKind.PercentEq
        or JsTokenKind.StarStarEq or JsTokenKind.LtLtEq or JsTokenKind.GtGtEq
        or JsTokenKind.GtGtGtEq or JsTokenKind.AmpEq or JsTokenKind.PipeEq
        or JsTokenKind.CaretEq or JsTokenKind.AmpAmpEq or JsTokenKind.PipePipeEq
        or JsTokenKind.QuestionQuestionEq;

    // ConditionalExpression
    private Expression ParseConditional()
    {
        var test = ParseNullishCoalescing();
        if (Match(JsTokenKind.Question))
        {
            var cons = ParseAssignment();
            Expect(JsTokenKind.Colon, "expected ':' in conditional");
            var alt = ParseAssignment();
            return new ConditionalExpression(test, cons, alt, test.Start, alt.End);
        }
        return test;
    }

    // NullishCoalescing — left-associative.
    private Expression ParseNullishCoalescing()
    {
        var left = ParseLogicalOr();
        while (Check(JsTokenKind.QuestionQuestion))
        {
            var op = _current.Lexeme; Advance();
            var right = ParseLogicalOr();
            left = new LogicalExpression(op, left, right, left.Start, right.End);
        }
        return left;
    }

    private Expression ParseLogicalOr()
    {
        var left = ParseLogicalAnd();
        while (Check(JsTokenKind.PipePipe))
        {
            var op = _current.Lexeme; Advance();
            var right = ParseLogicalAnd();
            left = new LogicalExpression(op, left, right, left.Start, right.End);
        }
        return left;
    }

    private Expression ParseLogicalAnd()
    {
        var left = ParseBitwiseOr();
        while (Check(JsTokenKind.AmpAmp))
        {
            var op = _current.Lexeme; Advance();
            var right = ParseBitwiseOr();
            left = new LogicalExpression(op, left, right, left.Start, right.End);
        }
        return left;
    }

    private Expression ParseBitwiseOr() => ParseLeftAssoc(ParseBitwiseXor, JsTokenKind.Pipe);
    private Expression ParseBitwiseXor() => ParseLeftAssoc(ParseBitwiseAnd, JsTokenKind.Caret);
    private Expression ParseBitwiseAnd() => ParseLeftAssoc(ParseEquality, JsTokenKind.Amp);

    private Expression ParseEquality()
    {
        var left = ParseRelational();
        while (_current.Kind is JsTokenKind.EqEq or JsTokenKind.BangEq
                                or JsTokenKind.EqEqEq or JsTokenKind.BangEqEq)
        {
            var op = _current.Lexeme; Advance();
            var right = ParseRelational();
            left = new BinaryExpression(op, left, right, left.Start, right.End);
        }
        return left;
    }

    private Expression ParseRelational()
    {
        var left = ParseShift();
        while (_current.Kind is JsTokenKind.Lt or JsTokenKind.Gt
                                or JsTokenKind.LtEq or JsTokenKind.GtEq
                                or JsTokenKind.Instanceof or JsTokenKind.In)
        {
            var op = _current.Lexeme; Advance();
            var right = ParseShift();
            left = new BinaryExpression(op, left, right, left.Start, right.End);
        }
        return left;
    }

    private Expression ParseShift()
    {
        var left = ParseAdditive();
        while (_current.Kind is JsTokenKind.LtLt or JsTokenKind.GtGt or JsTokenKind.GtGtGt)
        {
            var op = _current.Lexeme; Advance();
            var right = ParseAdditive();
            left = new BinaryExpression(op, left, right, left.Start, right.End);
        }
        return left;
    }

    private Expression ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (_current.Kind is JsTokenKind.Plus or JsTokenKind.Minus)
        {
            var op = _current.Lexeme; Advance();
            var right = ParseMultiplicative();
            left = new BinaryExpression(op, left, right, left.Start, right.End);
        }
        return left;
    }

    private Expression ParseMultiplicative()
    {
        var left = ParseExponentiation();
        while (_current.Kind is JsTokenKind.Star or JsTokenKind.Slash or JsTokenKind.Percent)
        {
            var op = _current.Lexeme; Advance();
            var right = ParseExponentiation();
            left = new BinaryExpression(op, left, right, left.Start, right.End);
        }
        return left;
    }

    // ** is RIGHT-associative per §13.6.
    private Expression ParseExponentiation()
    {
        var left = ParseUnary();
        if (Check(JsTokenKind.StarStar))
        {
            var op = _current.Lexeme; Advance();
            var right = ParseExponentiation();
            return new BinaryExpression(op, left, right, left.Start, right.End);
        }
        return left;
    }

    // Unary prefix: typeof, void, delete, !, ~, +, -, ++, --.
    private Expression ParseUnary()
    {
        switch (_current.Kind)
        {
            case JsTokenKind.Typeof:
            case JsTokenKind.Void:
            case JsTokenKind.Delete:
            case JsTokenKind.Bang:
            case JsTokenKind.Tilde:
            case JsTokenKind.Plus:
            case JsTokenKind.Minus:
            {
                var t = Advance();
                var arg = ParseUnary();
                return new UnaryExpression(t.Lexeme, arg, Prefix: true, t.Start, arg.End);
            }
            case JsTokenKind.PlusPlus:
            case JsTokenKind.MinusMinus:
            {
                var t = Advance();
                var arg = ParseUnary();
                return new UpdateExpression(t.Lexeme, arg, Prefix: true, t.Start, arg.End);
            }
        }
        return ParseUpdate();
    }

    // Postfix update: a++, a--. No line terminator allowed between operand
    // and the ++/-- (handled by checking PrecededByLineTerminator).
    private Expression ParseUpdate()
    {
        var arg = ParseLeftHandSide();
        if ((_current.Kind == JsTokenKind.PlusPlus || _current.Kind == JsTokenKind.MinusMinus)
            && !_current.PrecededByLineTerminator)
        {
            var t = Advance();
            return new UpdateExpression(t.Lexeme, arg, Prefix: false, arg.Start, t.End);
        }
        return arg;
    }

    // LeftHandSide: NewExpression | CallExpression | OptionalExpression.
    private Expression ParseLeftHandSide()
    {
        Expression node;
        if (Check(JsTokenKind.New))
        {
            node = ParseNew();
        }
        else
        {
            node = ParsePrimary();
        }
        return ParseCallAndMemberTail(node);
    }

    private NewExpression ParseNew()
    {
        var start = _current.Start;
        Advance(); // 'new'
        // Callee can recurse for `new new X()` etc.
        Expression callee = Check(JsTokenKind.New)
            ? ParseNew()
            : ParseCallAndMemberTailNoCall(ParsePrimary());
        IReadOnlyList<Expression> args = [];
        var end = callee.End;
        if (Match(JsTokenKind.LParen))
        {
            args = ParseArgumentList();
            end = _current.Start;
            Expect(JsTokenKind.RParen, "expected ')' in new arguments");
        }
        return new NewExpression(callee, args, start, end);
    }

    private Expression ParseCallAndMemberTailNoCall(Expression node)
    {
        // For `new X.Y.Z` we want member accesses but not bare calls
        // (those bind to the surrounding `new`).
        while (true)
        {
            if (Match(JsTokenKind.Dot))
            {
                var prop = ExpectIdentifierName("expected property name after '.'");
                node = new MemberExpression(node,
                    new Identifier(prop.Lexeme, prop.Start, prop.End),
                    Computed: false, Optional: false, node.Start, prop.End);
            }
            else if (Match(JsTokenKind.LBracket))
            {
                var idx = ParseAssignment();
                var end = _current.Start;
                Expect(JsTokenKind.RBracket, "expected ']' after computed property");
                node = new MemberExpression(node, idx,
                    Computed: true, Optional: false, node.Start, end);
            }
            else break;
        }
        return node;
    }

    private Expression ParseCallAndMemberTail(Expression node)
    {
        while (true)
        {
            if (Match(JsTokenKind.Dot))
            {
                // §13.3.2 — private name access: obj.#privateName.
                if (_current.Kind == JsTokenKind.PrivateIdentifier)
                {
                    var pt = Advance();
                    node = new MemberExpression(node,
                        new PrivateNameExpression(pt.Lexeme, pt.Start, pt.End),
                        Computed: false, Optional: false, node.Start, pt.End);
                    continue;
                }
                var prop = ExpectIdentifierName("expected property name after '.'");
                node = new MemberExpression(node,
                    new Identifier(prop.Lexeme, prop.Start, prop.End),
                    Computed: false, Optional: false, node.Start, prop.End);
            }
            else if (Match(JsTokenKind.LBracket))
            {
                var idx = ParseAssignment();
                var end = _current.Start;
                Expect(JsTokenKind.RBracket, "expected ']' after computed property");
                node = new MemberExpression(node, idx,
                    Computed: true, Optional: false, node.Start, end);
            }
            else if (Check(JsTokenKind.QuestionDot))
            {
                Advance();
                // ?. can be followed by .ident, [expr], (args).
                if (Match(JsTokenKind.LParen))
                {
                    var args = ParseArgumentList();
                    var end = _current.Start;
                    Expect(JsTokenKind.RParen, "expected ')' after optional call args");
                    node = new CallExpression(node, args, Optional: true, node.Start, end);
                }
                else if (Match(JsTokenKind.LBracket))
                {
                    var idx = ParseAssignment();
                    var end = _current.Start;
                    Expect(JsTokenKind.RBracket, "expected ']' after optional computed access");
                    node = new MemberExpression(node, idx,
                        Computed: true, Optional: true, node.Start, end);
                }
                else
                {
                    var prop = ExpectIdentifierName("expected property name after '?.'");
                    node = new MemberExpression(node,
                        new Identifier(prop.Lexeme, prop.Start, prop.End),
                        Computed: false, Optional: true, node.Start, prop.End);
                }
            }
            else if (Match(JsTokenKind.LParen))
            {
                var args = ParseArgumentList();
                var end = _current.Start;
                Expect(JsTokenKind.RParen, "expected ')' after call args");
                node = new CallExpression(node, args, Optional: false, node.Start, end);
            }
            else break;
        }
        return node;
    }

    private List<Expression> ParseArgumentList()
    {
        var args = new List<Expression>();
        if (Check(JsTokenKind.RParen)) return args;
        while (true)
        {
            if (Check(JsTokenKind.Ellipsis))
            {
                var start = _current.Start;
                Advance();
                var inner = ParseAssignment();
                args.Add(new SpreadElement(inner, start, inner.End));
            }
            else
            {
                args.Add(ParseAssignment());
            }
            if (!Match(JsTokenKind.Comma)) break;
        }
        return args;
    }

    // -----------------------------------------------------------------------
    // Primary expressions
    // -----------------------------------------------------------------------
    private Expression ParsePrimary()
    {
        // A `/` or `/=` token at the start of a primary expression must be the
        // opening of a regex literal — the lexer is intentionally
        // context-free for these punctuators (§11.6 division-vs-regex
        // ambiguity). Stuff the slash back into the lexer's lookahead slot
        // and ask it to rescan the position as a RegExp.
        if (_current.Kind == JsTokenKind.Slash || _current.Kind == JsTokenKind.SlashEq)
        {
            _lex.PushBack(_current);
            _current = _lex.ScanRegExp();
        }
        var t = _current;
        switch (t.Kind)
        {
            case JsTokenKind.NumericLiteral:
                Advance();
                return new NumericLiteral((double)t.Value!, t.Start, t.End);
            case JsTokenKind.BigIntLiteral:
                Advance();
                return new BigIntLiteral((string)t.Value!, t.Start, t.End);
            case JsTokenKind.StringLiteral:
                Advance();
                return new StringLiteral((string)t.Value!, t.Start, t.End);
            case JsTokenKind.RegExpLiteral:
            {
                Advance();
                var (pattern, flags) = ((string, string))t.Value!;
                return new RegExpLiteral(pattern, flags, t.Start, t.End);
            }
            case JsTokenKind.BooleanLiteral:
                Advance();
                return new BooleanLiteral((bool)t.Value!, t.Start, t.End);
            case JsTokenKind.NullLiteral:
                Advance();
                return new NullLiteral(t.Start, t.End);
            case JsTokenKind.This:
                Advance();
                return new ThisExpression(t.Start, t.End);
            case JsTokenKind.Identifier:
                Advance();
                return new Identifier(t.Lexeme, t.Start, t.End);
            case JsTokenKind.LParen:
                Advance();
                var inner = ParseAssignment();
                // Allow comma to form sequence inside parens.
                if (Match(JsTokenKind.Comma))
                {
                    var parts = new List<Expression> { inner };
                    do { parts.Add(ParseAssignment()); }
                    while (Match(JsTokenKind.Comma));
                    Expect(JsTokenKind.RParen, "expected ')' to close grouping");
                    return new SequenceExpression(parts, parts[0].Start, parts[^1].End);
                }
                Expect(JsTokenKind.RParen, "expected ')' to close grouping");
                return inner;
            case JsTokenKind.LBracket:
                return ParseArrayLiteral();
            case JsTokenKind.LBrace:
                return ParseObjectLiteral();
            case JsTokenKind.Function:
                return ParseFunctionExpression();
            case JsTokenKind.Class:
                return ParseClassExpression();
            case JsTokenKind.Super:
                return ParseSuperExpression();
            case JsTokenKind.TemplateNoSubstitution:
            case JsTokenKind.TemplateHead:
                return ParseTemplateLiteral();
        }
        throw new JsParseException(
            $"unexpected token {t.Kind} '{t.Lexeme}'", t.Start);
    }

    private TemplateLiteral ParseTemplateLiteral()
    {
        var startTok = _current;
        var quasis = new List<string>();
        var expressions = new List<Expression>();
        if (_current.Kind == JsTokenKind.TemplateNoSubstitution)
        {
            quasis.Add((string)_current.Value!);
            var end = _current.End;
            Advance();
            return new TemplateLiteral(quasis, expressions, startTok.Start, end);
        }
        // Head ... (expr ... Middle)* expr ... Tail
        quasis.Add((string)_current.Value!);
        Advance();
        while (true)
        {
            expressions.Add(ParseAssignment());
            // The substitution must close on `}`; we consume it then re-enter
            // template-mode via the lexer's continuation entry point.
            if (_current.Kind != JsTokenKind.RBrace)
                throw new JsParseException(
                    $"expected '}}' to close template substitution, got {_current.Kind}", _current.Start);
            // Reset the parser's lookahead to the post-} character, then ask
            // the lexer for the next template segment instead of a normal token.
            _current = _lex.ScanTemplateContinuation();
            if (_current.Kind == JsTokenKind.TemplateTail)
            {
                quasis.Add((string)_current.Value!);
                var endTok = _current;
                Advance();
                return new TemplateLiteral(quasis, expressions, startTok.Start, endTok.End);
            }
            if (_current.Kind == JsTokenKind.TemplateMiddle)
            {
                quasis.Add((string)_current.Value!);
                Advance();
                continue;
            }
            throw new JsParseException(
                $"expected template middle or tail, got {_current.Kind}", _current.Start);
        }
    }

    private ArrayExpression ParseArrayLiteral()
    {
        var start = _current.Start;
        Expect(JsTokenKind.LBracket, "[ expected");
        var elements = new List<Expression?>();
        while (!Check(JsTokenKind.RBracket))
        {
            if (Check(JsTokenKind.Comma))
            {
                elements.Add(null); // elision
                Advance();
                continue;
            }
            if (Check(JsTokenKind.Ellipsis))
            {
                var sstart = _current.Start;
                Advance();
                var inner = ParseAssignment();
                elements.Add(new SpreadElement(inner, sstart, inner.End));
            }
            else
            {
                elements.Add(ParseAssignment());
            }
            if (!Match(JsTokenKind.Comma)) break;
        }
        var end = _current.End;
        Expect(JsTokenKind.RBracket, "expected ']' to close array literal");
        return new ArrayExpression(elements, start, end);
    }

    private ObjectExpression ParseObjectLiteral()
    {
        var start = _current.Start;
        Expect(JsTokenKind.LBrace, "{ expected");
        var props = new List<ObjectProperty>();
        while (!Check(JsTokenKind.RBrace))
        {
            // Object spread: { ...other }
            if (Check(JsTokenKind.Ellipsis))
            {
                var sstart = _current.Start;
                Advance();
                var inner = ParseAssignment();
                var spreadKey = new Identifier("", sstart, sstart);  // sentinel for spread
                var spreadElem = new SpreadElement(inner, sstart, inner.End);
                props.Add(new ObjectProperty(spreadKey, spreadElem,
                    Shorthand: false, Computed: false, sstart, inner.End));
            }
            else
            {
                props.Add(ParseObjectProperty());
            }
            if (!Match(JsTokenKind.Comma)) break;
        }
        var end = _current.End;
        Expect(JsTokenKind.RBrace, "expected '}' to close object literal");
        return new ObjectExpression(props, start, end);
    }

    private ObjectProperty ParseObjectProperty()
    {
        var start = _current.Start;
        var computed = false;
        Expression key;
        if (Match(JsTokenKind.LBracket))
        {
            computed = true;
            key = ParseAssignment();
            Expect(JsTokenKind.RBracket, "expected ']' after computed key");
        }
        else if (_current.Kind == JsTokenKind.StringLiteral)
        {
            var t = Advance();
            key = new StringLiteral((string)t.Value!, t.Start, t.End);
        }
        else if (_current.Kind == JsTokenKind.NumericLiteral)
        {
            var t = Advance();
            key = new NumericLiteral((double)t.Value!, t.Start, t.End);
        }
        else if (_current.Kind == JsTokenKind.Identifier
            || IsReservedNameAllowedAsPropertyName(_current.Kind))
        {
            var t = Advance();
            key = new Identifier(t.Lexeme, t.Start, t.End);
        }
        else
        {
            throw new JsParseException(
                $"expected property name, got {_current.Kind}", _current.Start);
        }

        // Method shorthand: { foo() { … }, [bar](x) { … } }
        if (Check(JsTokenKind.LParen))
        {
            var (parameters, body, endPos) = ParseMethodTail();
            var methodName = key is Identifier ki ? ki : null;
            var method = MakeFnExpression(methodName, parameters, body, start, endPos);
            return new ObjectProperty(key, method,
                Shorthand: false, Computed: computed, start, endPos);
        }

        if (Match(JsTokenKind.Colon))
        {
            var value = ParseAssignment();
            return new ObjectProperty(key, value,
                Shorthand: false, Computed: computed, start, value.End);
        }
        // Shorthand binding default: { foo = expr }. This is only valid when
        // the object literal is later reinterpreted as a destructuring pattern
        // (ECMA-262 §13.15 / §14.3.3), but accepting the cover form here keeps
        // assignment and binding-pattern parsing single-pass.
        if (!computed && key is Identifier id && Match(JsTokenKind.Eq))
        {
            var fallback = ParseAssignment();
            var target = new AssignmentExpression("=", id, fallback, id.Start, fallback.End);
            return new ObjectProperty(key, target,
                Shorthand: true, Computed: false, start, fallback.End);
        }
        // Shorthand: { foo } where foo is an identifier.
        if (!computed && key is Identifier id2)
        {
            return new ObjectProperty(key, id2,
                Shorthand: true, Computed: false, start, key.End);
        }
        throw new JsParseException(
            $"expected ':' or '(' after object property key", _current.Start);
    }

    private static FunctionExpression MakeFnExpression(
        Identifier? name, IReadOnlyList<Expression> @params, BlockStatement body,
        JsPosition start, JsPosition end)
        => new(name, @params, body, Generator: false, start, end);

    /// <summary>Parse the <c>(params) { body }</c> portion of an object-literal
    /// method shorthand, having already consumed the property key.</summary>
    private (List<Expression> Params, BlockStatement Body, JsPosition End) ParseMethodTail()
    {
        Expect(JsTokenKind.LParen, "expected '(' for method parameters");
        var parameters = new List<Expression>();
        if (!Check(JsTokenKind.RParen))
        {
            while (true)
            {
                var pTok = Expect(JsTokenKind.Identifier, "expected parameter identifier");
                parameters.Add(new Identifier(pTok.Lexeme, pTok.Start, pTok.End));
                if (!Match(JsTokenKind.Comma)) break;
            }
        }
        Expect(JsTokenKind.RParen, "expected ')' after method parameters");
        var body = ParseBlock();
        return (parameters, body, body.End);
    }

    /// <summary>
    /// Consume any token that is a valid <c>IdentifierName</c> per ES §12.6 —
    /// i.e. a plain identifier or any reserved word/boolean/null literal.
    /// Used after <c>.</c> and <c>?.</c> in MemberExpression, where the
    /// grammar production is <c>MemberExpression . IdentifierName</c> (not
    /// <c>Identifier</c>), so reserved words like <c>catch</c>, <c>finally</c>,
    /// <c>default</c>, <c>class</c>, <c>with</c> are valid property names.
    /// </summary>
    private JsToken ExpectIdentifierName(string message)
    {
        if (_current.Kind == JsTokenKind.Identifier
            || IsReservedNameAllowedAsPropertyName(_current.Kind))
        {
            return Advance();
        }
        throw new JsParseException($"{message} (got {_current.Kind})", _current.Start);
    }

    private static bool IsReservedNameAllowedAsPropertyName(JsTokenKind k)
    {
        // Any reserved word is allowed as an object property name.
        return k is
            JsTokenKind.Break or JsTokenKind.Case or JsTokenKind.Catch or JsTokenKind.Class
            or JsTokenKind.Const or JsTokenKind.Continue or JsTokenKind.Debugger
            or JsTokenKind.Default or JsTokenKind.Delete or JsTokenKind.Do
            or JsTokenKind.Else or JsTokenKind.Enum or JsTokenKind.Export
            or JsTokenKind.Extends or JsTokenKind.Finally or JsTokenKind.For
            or JsTokenKind.Function or JsTokenKind.If or JsTokenKind.Import
            or JsTokenKind.In or JsTokenKind.Instanceof or JsTokenKind.New
            or JsTokenKind.Return or JsTokenKind.Super or JsTokenKind.Switch
            or JsTokenKind.This or JsTokenKind.Throw or JsTokenKind.Try
            or JsTokenKind.Typeof or JsTokenKind.Var or JsTokenKind.Void
            or JsTokenKind.While or JsTokenKind.With or JsTokenKind.Yield
            or JsTokenKind.BooleanLiteral or JsTokenKind.NullLiteral;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private Expression ParseLeftAssoc(Func<Expression> next, JsTokenKind op)
    {
        var left = next();
        while (_current.Kind == op)
        {
            var t = Advance();
            var right = next();
            left = new BinaryExpression(t.Lexeme, left, right, left.Start, right.End);
        }
        return left;
    }
}

#pragma warning disable RCS1194 // Implement exception constructors — we
                                 // surface only the single (message, position)
                                 // form; the parser never throws bare instances.
public sealed class JsParseException(string message, JsPosition position)
    : Exception($"{message} (at {position})")
{
    public JsPosition Position { get; } = position;
}
#pragma warning restore RCS1194

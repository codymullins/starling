using Starling.Js.Ast;
using Starling.Js.Lex;

namespace Starling.Js.Parse;

/// <summary>
/// Statement-level parsing — wp:M3-02b. Extends the expression parser
/// in <c>JsParser.cs</c> via partial class. Method names mirror
/// ES2024 §14 sub-sections.
/// </summary>
public sealed partial class JsParser
{
    /// <summary>Parse a complete program — top-level statement list.</summary>
    public Program ParseProgram()
    {
        var start = _current.Start;
        var body = new List<Statement>();
        while (!Check(JsTokenKind.EndOfFile))
        {
            body.Add(ParseProgramStatement());
        }
        var end = _current.End;
        return new Program(body, start, end);
    }

    // -----------------------------------------------------------------------
    // Statement dispatch (§14.1)
    // -----------------------------------------------------------------------
    private Statement ParseStatement()
    {
        switch (_current.Kind)
        {
            case JsTokenKind.LBrace:    return ParseBlock();
            case JsTokenKind.Semicolon: return ParseEmpty();
            case JsTokenKind.If:        return ParseIf();
            case JsTokenKind.While:     return ParseWhile();
            case JsTokenKind.Do:        return ParseDoWhile();
            case JsTokenKind.For:       return ParseFor();
            case JsTokenKind.Return:    return ParseReturn();
            case JsTokenKind.Break:     return ParseBreak();
            case JsTokenKind.Continue:  return ParseContinue();
            case JsTokenKind.Throw:     return ParseThrow();
            case JsTokenKind.Try:       return ParseTry();
            case JsTokenKind.Switch:    return ParseSwitch();
            case JsTokenKind.Debugger:  return ParseDebugger();
            case JsTokenKind.Function:  return ParseFunctionDeclaration();
            case JsTokenKind.Class:     return ParseClassDeclarationWithExtendsTracking();
            case JsTokenKind.Var:       return ParseVar("var");
            case JsTokenKind.Const:     return ParseVar("const");
        }
        // 'let' is contextual; treat as variable decl when followed by an
        // identifier or pattern starter, else expression statement.
        if (_current.Kind == JsTokenKind.Identifier && _current.Lexeme == "let"
            && IsLetDeclarationStart())
        {
            return ParseVar("let");
        }
        // B1b-2c — `async function` at statement level → async function decl.
        if (_current.Kind == JsTokenKind.Identifier && _current.Lexeme == "async"
            && _lex.Peek().Kind == JsTokenKind.Function)
        {
            var asyncStart = _current.Start;
            Advance(); // async
            return ParseFunctionDeclaration(asyncStart, isAsync: true);
        }
        // §14.13 LabeledStatement — `identifier : Statement`
        if (_current.Kind == JsTokenKind.Identifier && _lex.Peek().Kind == JsTokenKind.Colon)
        {
            return ParseLabeledStatement();
        }
        return ParseExpressionStatement();
    }

    /// <summary>
    /// Decide whether a contextual 'let' Identifier at the head of a
    /// statement is starting a variable declaration or an expression
    /// statement. ES2024 §14.3.1 says <c>let</c> followed by <c>[</c>,
    /// <c>{</c>, or an Identifier (and not a reserved word) starts a
    /// LexicalDeclaration. We use the lexer's one-token peek to look
    /// past <c>let</c> without consuming it.
    /// </summary>
    private bool IsLetDeclarationStart()
    {
        var next = _lex.Peek();
        return next.Kind == JsTokenKind.Identifier
            || next.Kind == JsTokenKind.LBracket
            || next.Kind == JsTokenKind.LBrace;
    }

    // -----------------------------------------------------------------------
    // Trivial statements
    // -----------------------------------------------------------------------

    private BlockStatement ParseBlock()
    {
        var start = _current.Start;
        Expect(JsTokenKind.LBrace, "{ expected");
        var body = new List<Statement>();
        while (!Check(JsTokenKind.RBrace) && !Check(JsTokenKind.EndOfFile))
            body.Add(ParseStatement());
        var end = _current.End;
        Expect(JsTokenKind.RBrace, "expected '}' to close block");
        return new BlockStatement(body, start, end);
    }

    private EmptyStatement ParseEmpty()
    {
        var t = Advance();
        return new EmptyStatement(t.Start, t.End);
    }

    private ExpressionStatement ParseExpressionStatement()
    {
        var start = _current.Start;
        var expr = ParseExpressionNoEof();
        var end = _current.End;
        ConsumeSemicolonOrAsi();
        return new ExpressionStatement(expr, start, end);
    }

    private DebuggerStatement ParseDebugger()
    {
        var t = Advance();
        ConsumeSemicolonOrAsi();
        return new DebuggerStatement(t.Start, t.End);
    }

    // -----------------------------------------------------------------------
    // Control flow
    // -----------------------------------------------------------------------

    private IfStatement ParseIf()
    {
        var start = _current.Start;
        Advance(); // 'if'
        Expect(JsTokenKind.LParen, "( expected after 'if'");
        var test = ParseExpressionNoEof();
        Expect(JsTokenKind.RParen, "expected ')'");
        var cons = ParseStatement();
        Statement? alt = null;
        if (Match(JsTokenKind.Else))
            alt = ParseStatement();
        return new IfStatement(test, cons, alt, start, (alt ?? cons).End);
    }

    private WhileStatement ParseWhile()
    {
        var start = _current.Start;
        Advance();
        Expect(JsTokenKind.LParen, "( expected after 'while'");
        var test = ParseExpressionNoEof();
        Expect(JsTokenKind.RParen, "expected ')'");
        var body = ParseStatement();
        return new WhileStatement(test, body, start, body.End);
    }

    private DoWhileStatement ParseDoWhile()
    {
        var start = _current.Start;
        Advance(); // 'do'
        var body = ParseStatement();
        Expect(JsTokenKind.While, "expected 'while' after do-block");
        Expect(JsTokenKind.LParen, "( expected after 'while'");
        var test = ParseExpressionNoEof();
        Expect(JsTokenKind.RParen, "expected ')'");
        var end = _current.End;
        ConsumeSemicolonOrAsi();
        return new DoWhileStatement(body, test, start, end);
    }

    private Statement ParseFor()
    {
        var start = _current.Start;
        Advance();
        // wp:M3-04g — `for await (… of …)`. `await` is a contextual keyword
        // (an Identifier here); consume it and require an of-iteration form.
        bool isAwait = false;
        if (_current.Kind == JsTokenKind.Identifier && _current.Lexeme == "await")
        {
            isAwait = true;
            Advance();
        }
        Expect(JsTokenKind.LParen, "( expected after 'for'");

        // Initializer: VariableDeclaration | Expression | empty.
        AstNode? init = null;
        if (!Check(JsTokenKind.Semicolon))
        {
            if (_current.Kind == JsTokenKind.Var
                || _current.Kind == JsTokenKind.Const
                || (_current.Kind == JsTokenKind.Identifier && _current.Lexeme == "let"
                    && IsLetDeclarationStart()))
            {
                var kind = _current.Kind == JsTokenKind.Var ? "var"
                         : _current.Kind == JsTokenKind.Const ? "const"
                         : "let";
                init = ParseVarHeadless(kind);
                // Detect for-in / for-of: after the single declarator, the
                // next token will be 'in' or contextual 'of'.
                if (init is VariableDeclaration vd && vd.Declarations.Count == 1
                    && vd.Declarations[0].Init is null)
                {
                    if (Check(JsTokenKind.In)) return FinishForIn(start, vd);
                    if (IsContextualOf()) return FinishForOf(start, vd, isAwait);
                }
            }
            else
            {
                if (Check(JsTokenKind.LBracket) || Check(JsTokenKind.LBrace))
                {
                    var cover = ParseLeftHandSide();
                    if (Check(JsTokenKind.In)) return FinishForIn(start, cover);
                    if (IsContextualOf()) return FinishForOf(start, cover, isAwait);
                    var exprInit = cover;
                    init = new ExpressionStatement(exprInit, exprInit.Start, exprInit.End);
                }
                else
                {
                    var expr = ParseExpressionNoIn();
                    if (Check(JsTokenKind.In))
                        return FinishForIn(start, expr);
                    if (IsContextualOf())
                        return FinishForOf(start, expr, isAwait);
                    init = new ExpressionStatement(expr, expr.Start, expr.End);
                }
            }
        }
        // wp:M3-04g — `for await` is only valid in the for-of form.
        if (isAwait)
            throw new JsParseException("'for await' requires an 'of' iteration clause", _current.Start);
        Expect(JsTokenKind.Semicolon, "expected ';' in for-loop init");

        Expression? test = null;
        if (!Check(JsTokenKind.Semicolon)) test = ParseExpressionNoEof();
        Expect(JsTokenKind.Semicolon, "expected ';' in for-loop test");

        Expression? update = null;
        if (!Check(JsTokenKind.RParen)) update = ParseExpressionNoEof();
        Expect(JsTokenKind.RParen, "expected ')' to close for-loop header");
        var body = ParseStatement();
        return new ForStatement(init, test, update, body, start, body.End);
    }

    private bool IsContextualOf()
        => _current.Kind == JsTokenKind.Identifier && _current.Lexeme == "of";

    private ForInStatement FinishForIn(JsPosition start, AstNode left)
    {
        if (left is Expression expr) left = ReinterpretAssignmentTarget(expr);
        Advance(); // 'in'
        var right = ParseExpressionNoEof();
        Expect(JsTokenKind.RParen, "expected ')' after for-in head");
        var body = ParseStatement();
        return new ForInStatement(left, right, body, start, body.End);
    }

    private ForOfStatement FinishForOf(JsPosition start, AstNode left, bool isAwait = false)
    {
        if (left is Expression expr) left = ReinterpretAssignmentTarget(expr);
        Advance(); // contextual 'of'
        var right = ParseExpressionNoEof();
        Expect(JsTokenKind.RParen, "expected ')' after for-of head");
        var body = ParseStatement();
        return new ForOfStatement(left, right, body, start, body.End, isAwait);
    }

    // -----------------------------------------------------------------------
    // Jump / control statements
    // -----------------------------------------------------------------------

    private ReturnStatement ParseReturn()
    {
        var start = _current.Start;
        Advance(); // 'return'
        Expression? arg = null;
        if (!_current.PrecededByLineTerminator
            && !Check(JsTokenKind.Semicolon)
            && !Check(JsTokenKind.RBrace)
            && !Check(JsTokenKind.EndOfFile))
        {
            arg = ParseExpressionNoEof();
        }
        var end = _current.End;
        ConsumeSemicolonOrAsi();
        return new ReturnStatement(arg, start, end);
    }

    private BreakStatement ParseBreak()
    {
        var start = _current.Start;
        Advance();
        string? label = null;
        if (!_current.PrecededByLineTerminator && Check(JsTokenKind.Identifier))
        {
            label = _current.Lexeme;
            Advance();
        }
        var end = _current.End;
        ConsumeSemicolonOrAsi();
        return new BreakStatement(label, start, end);
    }

    private ContinueStatement ParseContinue()
    {
        var start = _current.Start;
        Advance();
        string? label = null;
        if (!_current.PrecededByLineTerminator && Check(JsTokenKind.Identifier))
        {
            label = _current.Lexeme;
            Advance();
        }
        var end = _current.End;
        ConsumeSemicolonOrAsi();
        return new ContinueStatement(label, start, end);
    }

    private ThrowStatement ParseThrow()
    {
        var start = _current.Start;
        Advance();
        if (_current.PrecededByLineTerminator)
            throw new JsParseException("illegal newline after 'throw'", start);
        var arg = ParseExpressionNoEof();
        var end = _current.End;
        ConsumeSemicolonOrAsi();
        return new ThrowStatement(arg, start, end);
    }

    // -----------------------------------------------------------------------
    // try / catch / finally
    // -----------------------------------------------------------------------

    private TryStatement ParseTry()
    {
        var start = _current.Start;
        Advance();
        var block = ParseBlock();
        CatchClause? handler = null;
        if (Match(JsTokenKind.Catch))
        {
            var cstart = _current.Start;
            Expression? param = null;
            if (Match(JsTokenKind.LParen))
            {
                param = ParseBindingTarget();
                Expect(JsTokenKind.RParen, "expected ')' after catch parameter");
            }
            var body = ParseBlock();
            handler = new CatchClause(param, body, cstart, body.End);
        }
        BlockStatement? finalizer = null;
        if (Match(JsTokenKind.Finally))
            finalizer = ParseBlock();
        if (handler is null && finalizer is null)
            throw new JsParseException("'try' requires 'catch' or 'finally'", start);
        var end = (Statement?)finalizer ?? handler?.Body ?? block;
        return new TryStatement(block, handler, finalizer, start, end.End);
    }

    // -----------------------------------------------------------------------
    // labeled statement (§14.13)
    // -----------------------------------------------------------------------

    /// <summary>§14.13 LabeledStatement — <c>identifier : Statement</c>.
    /// The caller must have verified that the current token is an Identifier
    /// and the next token is a Colon before calling this method.</summary>
    private LabeledStatement ParseLabeledStatement()
    {
        var start = _current.Start;
        var label = _current.Lexeme;
        Advance(); // identifier
        Advance(); // ':'
        var body = ParseStatement();
        return new LabeledStatement(label, body, start, body.End);
    }

    // -----------------------------------------------------------------------
    // switch
    // -----------------------------------------------------------------------

    private SwitchStatement ParseSwitch()
    {
        var start = _current.Start;
        Advance();
        Expect(JsTokenKind.LParen, "( expected after 'switch'");
        var disc = ParseExpressionNoEof();
        Expect(JsTokenKind.RParen, "expected ')'");
        Expect(JsTokenKind.LBrace, "expected '{' to open switch body");
        var cases = new List<SwitchCase>();
        while (!Check(JsTokenKind.RBrace) && !Check(JsTokenKind.EndOfFile))
        {
            var cstart = _current.Start;
            Expression? test;
            if (Match(JsTokenKind.Case))
            {
                test = ParseExpressionNoEof();
            }
            else
            {
                Expect(JsTokenKind.Default, "expected 'case' or 'default'");
                test = null;
            }
            Expect(JsTokenKind.Colon, "expected ':' after case label");
            var body = new List<Statement>();
            while (!Check(JsTokenKind.Case) && !Check(JsTokenKind.Default)
                && !Check(JsTokenKind.RBrace))
            {
                body.Add(ParseStatement());
            }
            var cend = body.Count > 0 ? body[^1].End : _current.End;
            cases.Add(new SwitchCase(test, body, cstart, cend));
        }
        var end = _current.End;
        Expect(JsTokenKind.RBrace, "expected '}' to close switch");
        return new SwitchStatement(disc, cases, start, end);
    }

    // -----------------------------------------------------------------------
    // Declarations
    // -----------------------------------------------------------------------

    private VariableDeclaration ParseVar(string kind)
    {
        var start = _current.Start;
        Advance(); // var / let / const
        var decl = ParseVarBody(kind, start);
        ConsumeSemicolonOrAsi();
        return decl;
    }

    /// <summary>Same as <see cref="ParseVar"/> but assumes the keyword has
    /// already been consumed (used by <c>for(var x=…)</c>).</summary>
    private VariableDeclaration ParseVarHeadless(string kind)
    {
        var start = _current.Start;
        Advance(); // var / let / const
        return ParseVarBody(kind, start);
    }

    private VariableDeclaration ParseVarBody(string kind, JsPosition start)
    {
        var decls = new List<VariableDeclarator>();
        while (true)
        {
            var dstart = _current.Start;
            var idNode = ParseBindingTarget();
            Expression? init = null;
            if (Match(JsTokenKind.Eq))
                init = ParseAssignment();
            decls.Add(new VariableDeclarator(idNode, init, dstart,
                (init ?? idNode).End));
            if (!Match(JsTokenKind.Comma)) break;
        }
        var end = decls[^1].End;
        return new VariableDeclaration(kind, decls, start, end);
    }

    /// <summary>
    /// Function as an expression: <c>function [name](params) { body }</c>.
    /// Name is optional; when present it's M3-04c work to bind the name
    /// inside the body. For now we accept and ignore the inside-name
    /// binding — outside callers don't see it.
    /// </summary>
    private FunctionExpression ParseFunctionExpression()
        => ParseFunctionExpressionInner(_current.Start, isAsync: false);

    internal FunctionExpression ParseFunctionExpression(JsPosition start, bool isAsync)
        => ParseFunctionExpressionInner(isAsync ? start : _current.Start, isAsync);

    private FunctionExpression ParseFunctionExpressionInner(JsPosition start, bool isAsync)
    {
        Advance(); // 'function'
        var generator = Match(JsTokenKind.Star);
        Identifier? name = null;
        if (Check(JsTokenKind.Identifier))
        {
            var tok = Advance();
            name = new Identifier(tok.Lexeme, tok.Start, tok.End);
        }
        Expect(JsTokenKind.LParen, "( expected after function expression");
        var parameters = ParseParameterList();
        Expect(JsTokenKind.RParen, "expected ')'");
        var body = ParseBlock();
        return new FunctionExpression(name, parameters, body, generator, start, body.End, Async: isAsync);
    }

    private FunctionDeclaration ParseFunctionDeclaration()
        => ParseFunctionDeclarationInner(_current.Start, isAsync: false);

    internal FunctionDeclaration ParseFunctionDeclaration(JsPosition start, bool isAsync)
        => ParseFunctionDeclarationInner(isAsync ? start : _current.Start, isAsync);

    private FunctionDeclaration ParseFunctionDeclarationInner(JsPosition start, bool isAsync)
    {
        Advance(); // function
        var generator = Match(JsTokenKind.Star);
        var nameTok = Expect(JsTokenKind.Identifier, "function name expected");
        var name = new Identifier(nameTok.Lexeme, nameTok.Start, nameTok.End);
        Expect(JsTokenKind.LParen, "( expected after function name");
        var parameters = ParseParameterList();
        Expect(JsTokenKind.RParen, "expected ')'");
        var body = ParseBlock();
        return new FunctionDeclaration(name, parameters, body, generator, start, body.End, Async: isAsync);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private List<Expression> ParseParameterList()
    {
        var parameters = new List<Expression>();
        while (!Check(JsTokenKind.RParen))
        {
            if (Check(JsTokenKind.Ellipsis))
            {
                var sstart = _current.Start;
                Advance();
                var inner = ParseBindingTarget();
                parameters.Add(new SpreadElement(inner, sstart, inner.End));
                break;
            }
            parameters.Add(ParseParameter());
            if (!Match(JsTokenKind.Comma)) break;
        }
        return parameters;
    }

    /// <summary>
    /// Like <see cref="ParseExpression"/> but doesn't require EOF afterwards.
    /// Used inside statement contexts where a terminator is expected.
    /// </summary>
    private Expression ParseExpressionNoEof()
    {
        var expr = ParseAssignment();
        if (Match(JsTokenKind.Comma))
        {
            var parts = new List<Expression> { expr };
            do { parts.Add(ParseAssignment()); }
            while (Match(JsTokenKind.Comma));
            expr = new SequenceExpression(parts, parts[0].Start, parts[^1].End);
        }
        return expr;
    }

    private Expression ParseExpressionNoIn()
    {
        _disallowInDepth++;
        try
        {
            return ParseExpressionNoEof();
        }
        finally
        {
            _disallowInDepth--;
        }
    }

    /// <summary>
    /// Spec §11.9.1 ASI: a semicolon may be omitted before <c>}</c>, before
    /// EOF, or before a line terminator. Anything else after the statement
    /// is a syntax error.
    /// </summary>
    private void ConsumeSemicolonOrAsi()
    {
        if (Match(JsTokenKind.Semicolon)) return;
        if (Check(JsTokenKind.EndOfFile) || Check(JsTokenKind.RBrace)) return;
        if (_current.PrecededByLineTerminator) return;
        throw new JsParseException(
            $"expected ';' after statement (got {_current.Kind} '{_current.Lexeme}')",
            _current.Start);
    }
}

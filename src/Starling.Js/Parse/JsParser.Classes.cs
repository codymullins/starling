using Starling.Js.Ast;
using Starling.Js.Lex;

namespace Starling.Js.Parse;

/// <summary>
/// Class declaration + expression parsing — B1b-2a. Class bodies are
/// implicitly strict-mode per ES2024 §15.7; the engine does not yet enforce
/// strict-only restrictions (e.g. <c>arguments.caller</c>), tracked as a
/// known divergence in <c>tasks/M3/google-com-handoff.md</c>.
/// </summary>
public sealed partial class JsParser
{
    /// <summary>Set while parsing inside a class body so private-name
    /// references can be validated as declared. Each frame represents one
    /// (possibly nested) class scope.</summary>
    private readonly Stack<HashSet<string>> _classPrivateScopes = new();

    /// <summary>Tracks whether the parser is currently inside a derived
    /// class's constructor body so <c>super(...)</c> calls can be
    /// distinguished from <c>super.x()</c> member calls.</summary>
    private int _derivedConstructorDepth;

    /// <summary>Parse a class declaration statement. The keyword
    /// <c>class</c> is the current token.</summary>
    private ClassDeclaration ParseClassDeclarationWithExtendsTracking()
    {
        var start = _current.Start;
        Advance(); // 'class'
        var nameTok = Expect(JsTokenKind.Identifier, "expected class name");
        var name = new Identifier(nameTok.Lexeme, nameTok.Start, nameTok.End);
        // §15.7.1 — a class definition is strict code, so its name binding may
        // not be `eval`/`arguments` or a strict reserved word, regardless of
        // the surrounding scope's strictness.
        CheckClassBindingName(name);
        var (baseClass, body, end) = ParseClassTail(start);
        return new ClassDeclaration(name, baseClass, body, start, end);
    }

    /// <summary>Parse a class expression. The keyword <c>class</c> is the
    /// current token. The optional name binding is visible only inside the
    /// body per §15.7.5 (ClassExpression Static Semantics: BoundNames).</summary>
    private ClassExpression ParseClassExpression()
    {
        var start = _current.Start;
        Advance(); // 'class'
        Identifier? name = null;
        if (_current.Kind == JsTokenKind.Identifier)
        {
            var t = Advance();
            name = new Identifier(t.Lexeme, t.Start, t.End);
            CheckClassBindingName(name);
        }
        var (baseClass, body, end) = ParseClassTail(start);
        return new ClassExpression(name, baseClass, body, start, end);
    }

    private (Expression? BaseClass, ClassBody Body, JsPosition End) ParseClassTail(JsPosition start)
    {
        Expression? baseClass = null;
        if (Match(JsTokenKind.Extends))
        {
            baseClass = ParseLeftHandSide();
        }
        if (baseClass is not null) _baseClassContextDepth++;
        try
        {
            var body = ParseClassBody();
            return (baseClass, body, body.End);
        }
        finally
        {
            if (baseClass is not null) _baseClassContextDepth--;
        }
    }

    private ClassBody ParseClassBody()
    {
        var start = _current.Start;
        Expect(JsTokenKind.LBrace, "expected '{' to open class body");

        // §15.7 — all code in a class body (methods, constructor, field
        // initializers, static blocks) is strict mode code, regardless of the
        // surrounding scope. Save/restore around the whole body.
        var savedStrict = _strict;
        _strict = true;

        // Pre-scan declared private names so any forward reference inside
        // an earlier method body resolves correctly.
        var privateScope = new HashSet<string>(StringComparer.Ordinal);
        _classPrivateScopes.Push(privateScope);

        MethodDefinition? ctor = null;
        var methods = new List<MethodDefinition>();
        var fields = new List<PropertyField>();
        var staticBlocks = new List<BlockStatement>();
        try
        {
            // First-pass private-name pre-scan via a lookahead token by checkpointing.
            // Simpler: walk + collect lazily as we parse, but allow any usage to
            // succeed; we validate at compile time. Doing one-pass parse here.
            while (!Check(JsTokenKind.RBrace) && !Check(JsTokenKind.EndOfFile))
            {
                if (Match(JsTokenKind.Semicolon)) continue; // empty class element
                ParseClassMember(ctor, methods, fields, staticBlocks, privateScope, out var newCtor);
                if (newCtor is not null) ctor = newCtor;
            }
            var end = _current.End;
            Expect(JsTokenKind.RBrace, "expected '}' to close class body");
            return new ClassBody(ctor, methods, fields, staticBlocks, start, end);
        }
        finally
        {
            _classPrivateScopes.Pop();
            _strict = savedStrict;
        }
    }

    private void ParseClassMember(
        MethodDefinition? existingCtor,
        List<MethodDefinition> methods,
        List<PropertyField> fields,
        List<BlockStatement> staticBlocks,
        HashSet<string> privateScope,
        out MethodDefinition? newCtor)
    {
        newCtor = null;
        var memberStart = _current.Start;
        // Detect `static` prefix — contextual identifier. `static() {}` is a
        // method named "static"; `static {` is a static block; `static name`
        // is a static member.
        bool isStatic = false;
        if (_current.Kind == JsTokenKind.Identifier && _current.Lexeme == "static")
        {
            var peek = _lex.Peek();
            // `static` followed by `(` or `=` or `;` is a regular member named "static".
            if (peek.Kind != JsTokenKind.LParen
                && peek.Kind != JsTokenKind.Eq
                && peek.Kind != JsTokenKind.Semicolon
                && peek.Kind != JsTokenKind.RBrace
                && peek.Kind != JsTokenKind.Comma)
            {
                Advance(); // consume 'static'
                isStatic = true;
                // Static initialization block: `static { ... }`
                if (Check(JsTokenKind.LBrace))
                {
                    var block = ParseBlock();
                    staticBlocks.Add(block);
                    return;
                }
            }
        }

        // Detect async / generator method modifiers (ES2024 §15.4):
        //   *gen(){}, async m(){}, async *gen(){}, static async *gen(){}
        // `async` is contextual: a method modifier only when followed (same
        // line) by a method-name start; otherwise it is a member named "async".
        bool isGenerator = false, isAsync = false;
        if (_current.Kind == JsTokenKind.Identifier && _current.Lexeme == "async")
        {
            var peek = _lex.Peek();
            if (!peek.PrecededByLineTerminator && IsMethodNameStartAfterModifier(peek.Kind))
            {
                Advance(); // 'async'
                isAsync = true;
            }
        }
        if (Check(JsTokenKind.Star)) { Advance(); isGenerator = true; }

        // Detect get/set accessor — contextual. Accessors never combine with
        // async/* (those are distinct MethodDefinition productions).
        MethodKind methodKind = MethodKind.Method;
        if (!isAsync && !isGenerator
            && _current.Kind == JsTokenKind.Identifier
            && (_current.Lexeme == "get" || _current.Lexeme == "set"))
        {
            var peek = _lex.Peek();
            // `get name(...)` is an accessor; `get(...)` is a method named "get".
            if (peek.Kind != JsTokenKind.LParen
                && peek.Kind != JsTokenKind.Eq
                && peek.Kind != JsTokenKind.Semicolon
                && peek.Kind != JsTokenKind.RBrace
                && peek.Kind != JsTokenKind.Comma)
            {
                methodKind = _current.Lexeme == "get" ? MethodKind.Get : MethodKind.Set;
                Advance();
            }
        }

        // Parse the key.
        var (key, computed, isPrivate) = ParseClassElementKey(privateScope);

        // Field declaration: `name [= init];` or `name;` (no parens follow).
        // A field is never async/generator (those require a method body).
        if (methodKind == MethodKind.Method && !isAsync && !isGenerator
            && !Check(JsTokenKind.LParen))
        {
            Expression? init = null;
            if (Match(JsTokenKind.Eq))
            {
                init = ParseAssignment();
            }
            var fieldEnd = _current.End;
            ConsumeSemicolonOrAsi();
            fields.Add(new PropertyField(key, isStatic, computed, init, memberStart, fieldEnd));
            return;
        }

        // Method form. Constructor disambiguation by name.
        bool isCtor = !isStatic
            && methodKind == MethodKind.Method
            && !isAsync && !isGenerator
            && !computed
            && !isPrivate
            && key is Identifier { Name: "constructor" };
        if (isCtor) methodKind = MethodKind.Constructor;
        // A generator/async method named "constructor" (non-static) is a
        // SyntaxError (§15.7.1 Early Errors).
        if (!isStatic && (isAsync || isGenerator) && !computed && !isPrivate
            && key is Identifier { Name: "constructor" })
            throw new JsParseException(
                "class constructor may not be a generator or async method", memberStart);

        // Track derived-constructor scope so `super(...)` is allowed only
        // when this is a derived class's constructor.
        var enteredDerivedCtor = false;
        if (isCtor && _baseClassContextDepth > 0)
        {
            _derivedConstructorDepth++;
            enteredDerivedCtor = true;
        }

        IReadOnlyList<Expression> parameters;
        BlockStatement body;
        JsPosition endPos;
        try
        {
            Expect(JsTokenKind.LParen, "expected '(' in method definition");
            parameters = ParseParameterList();
            Expect(JsTokenKind.RParen, "expected ')' after method parameters");
            // Class bodies are always strict (§15.7), so _strict is already
            // true here; ParseFunctionBody preserves it (no prologue can clear
            // it). Validate params under strict semantics.
            (body, _) = ParseFunctionBody();
            ValidateParameters(parameters, strict: true);
            endPos = body.End;
        }
        finally
        {
            if (enteredDerivedCtor) _derivedConstructorDepth--;
        }

        var method = new MethodDefinition(key, methodKind, isStatic, computed,
            parameters, body, memberStart, endPos,
            Generator: isGenerator, Async: isAsync, Strict: true);
        if (isCtor)
        {
            if (existingCtor is not null)
                throw new JsParseException("a class may only have one constructor", memberStart);
            newCtor = method;
        }
        else
        {
            methods.Add(method);
        }
    }

    /// <summary>Parse a class element key: identifier, string, numeric,
    /// computed <c>[expr]</c>, or <c>#privateName</c>.</summary>
    private (Expression Key, bool Computed, bool IsPrivate) ParseClassElementKey(HashSet<string> privateScope)
    {
        if (Match(JsTokenKind.LBracket))
        {
            var expr = ParseAssignment();
            Expect(JsTokenKind.RBracket, "expected ']' after computed key");
            return (expr, true, false);
        }
        if (_current.Kind == JsTokenKind.PrivateIdentifier)
        {
            var t = Advance();
            // Track in the class's private-name scope so referencing this
            // slot from inside the class body succeeds.
            privateScope.Add(t.Lexeme);
            return (new PrivateNameExpression(t.Lexeme, t.Start, t.End), false, true);
        }
        if (_current.Kind == JsTokenKind.StringLiteral)
        {
            var t = Advance();
            return (new StringLiteral((string)t.Value!, t.Start, t.End), false, false);
        }
        if (_current.Kind == JsTokenKind.NumericLiteral)
        {
            var t = Advance();
            return (new NumericLiteral((double)t.Value!, t.Start, t.End), false, false);
        }
        if (_current.Kind == JsTokenKind.Identifier
            || IsReservedNameAllowedAsPropertyName(_current.Kind))
        {
            var t = Advance();
            return (new Identifier(t.Lexeme, t.Start, t.End), false, false);
        }
        throw new JsParseException(
            $"expected class member name, got {_current.Kind}", _current.Start);
    }

    /// <summary>Depth counter set by the parser while it walks a class
    /// declaration / expression that has an <c>extends</c> clause. Used
    /// to enable <c>super(...)</c> parsing inside that scope.</summary>
    private int _baseClassContextDepth;

    /// <summary>Parse a <c>super</c> primary — must be followed by <c>.x</c>,
    /// <c>[expr]</c>, or <c>(args)</c>.</summary>
    private Expression ParseSuperExpression()
    {
        var start = _current.Start;
        Advance(); // 'super'
        if (Match(JsTokenKind.Dot))
        {
            // super.x or super.#x — private super access is rare but valid.
            if (_current.Kind == JsTokenKind.PrivateIdentifier)
            {
                throw new JsParseException("private member access on 'super' is not supported", _current.Start);
            }
            var prop = ExpectIdentifierName("expected property name after 'super.'");
            return new SuperPropertyExpression(
                new Identifier(prop.Lexeme, prop.Start, prop.End),
                Computed: false, start, prop.End);
        }
        if (Match(JsTokenKind.LBracket))
        {
            var expr = ParseAssignment();
            var end = _current.End;
            Expect(JsTokenKind.RBracket, "expected ']' after computed super key");
            return new SuperPropertyExpression(expr, Computed: true, start, end);
        }
        if (Check(JsTokenKind.LParen))
        {
            if (_derivedConstructorDepth == 0)
                throw new JsParseException(
                    "'super(...)' is only allowed inside a derived class constructor", start);
            Advance(); // (
            var args = ParseArgumentList();
            var end = _current.End;
            Expect(JsTokenKind.RParen, "expected ')' after super call args");
            return new SuperCallExpression(args, start, end);
        }
        throw new JsParseException(
            "'super' must be followed by '.', '[', or '('", start);
    }
}

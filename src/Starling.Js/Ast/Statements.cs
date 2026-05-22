using Starling.Js.Lex;

namespace Starling.Js.Ast;

/// <summary>Base for every JS statement node. ES2024 §14.</summary>
public abstract record Statement(JsPosition Start, JsPosition End) : AstNode(Start, End);

// -----------------------------------------------------------------------
// Trivial statements
// -----------------------------------------------------------------------

public sealed record EmptyStatement(JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record BlockStatement(
    IReadOnlyList<Statement> Body, JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record ExpressionStatement(
    Expression Expression, JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record ReturnStatement(
    Expression? Argument, JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record BreakStatement(
    string? Label, JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record ContinueStatement(
    string? Label, JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record ThrowStatement(
    Expression Argument, JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record DebuggerStatement(JsPosition Start, JsPosition End)
    : Statement(Start, End);

// -----------------------------------------------------------------------
// Control flow
// -----------------------------------------------------------------------

public sealed record IfStatement(
    Expression Test, Statement Consequent, Statement? Alternate,
    JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record WhileStatement(
    Expression Test, Statement Body, JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record DoWhileStatement(
    Statement Body, Expression Test, JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record ForStatement(
    AstNode? Init,       // VariableDeclaration or Expression or null
    Expression? Test,
    Expression? Update,
    Statement Body,
    JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record ForInStatement(
    AstNode Left,        // VariableDeclaration with 1 decl, or Expression
    Expression Right,
    Statement Body,
    JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record ForOfStatement(
    AstNode Left,
    Expression Right,
    Statement Body,
    JsPosition Start, JsPosition End,
    // wp:M3-04g — true for `for await (… of …)`; drives the async-iterator
    // protocol (await each iterator-result) instead of the sync one.
    bool Await = false)
    : Statement(Start, End);

public sealed record SwitchStatement(
    Expression Discriminant,
    IReadOnlyList<SwitchCase> Cases,
    JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record SwitchCase(
    Expression? Test,        // null = default clause
    IReadOnlyList<Statement> Consequent,
    JsPosition Start, JsPosition End)
    : AstNode(Start, End);

public sealed record TryStatement(
    BlockStatement Block,
    CatchClause? Handler,
    BlockStatement? Finalizer,
    JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record CatchClause(
    Expression? Param,       // Identifier in this slice (destructuring is M3-02d)
    BlockStatement Body,
    JsPosition Start, JsPosition End)
    : AstNode(Start, End);

public sealed record LabeledStatement(
    string Label, Statement Body, JsPosition Start, JsPosition End)
    : Statement(Start, End);

// -----------------------------------------------------------------------
// Declarations
// -----------------------------------------------------------------------

public sealed record VariableDeclaration(
    string Kind,    // "var" / "let" / "const"
    IReadOnlyList<VariableDeclarator> Declarations,
    JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record VariableDeclarator(
    Expression Id,       // Identifier in this slice
    Expression? Init,
    JsPosition Start, JsPosition End)
    : AstNode(Start, End);

public sealed record FunctionDeclaration(
    Identifier Name,
    IReadOnlyList<Expression> Params,
    BlockStatement Body,
    bool Generator,
    JsPosition Start, JsPosition End,
    bool Async = false,
    // ES strict mode: true when this function's body parses as strict (own
    // "use strict" directive prologue, or lexically nested in strict code).
    bool Strict = false)
    : Statement(Start, End);

/// <summary>
/// <c>class Name [extends Base] { body }</c> as a statement. Per ES2024
/// §15.7, the resulting binding is a <c>let</c> in the enclosing lexical
/// scope.
/// </summary>
public sealed record ClassDeclaration(
    Identifier Name,
    Expression? BaseClass,
    ClassBody Body,
    JsPosition Start, JsPosition End)
    : Statement(Start, End);

// -----------------------------------------------------------------------
// Program root
// -----------------------------------------------------------------------

public sealed record Program(
    IReadOnlyList<Statement> Body,
    JsPosition Start, JsPosition End,
    // ES strict mode: true when the program's directive prologue contains a
    // "use strict" directive, making the entire script strict (§11.2.2).
    bool Strict = false)
    : AstNode(Start, End);

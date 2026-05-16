using Tessera.Js.Lex;

namespace Tessera.Js.Ast;

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
    JsPosition Start, JsPosition End)
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
    JsPosition Start, JsPosition End)
    : Statement(Start, End);

// -----------------------------------------------------------------------
// Program root
// -----------------------------------------------------------------------

public sealed record Program(
    IReadOnlyList<Statement> Body,
    JsPosition Start, JsPosition End)
    : AstNode(Start, End);

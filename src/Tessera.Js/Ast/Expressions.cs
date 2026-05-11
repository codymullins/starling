using Tessera.Js.Lex;

namespace Tessera.Js.Ast;

/// <summary>
/// Base for every JS expression node. ES2024 §13.
/// </summary>
public abstract record Expression(JsPosition Start, JsPosition End) : AstNode(Start, End);

// -----------------------------------------------------------------------
// Literals + identifier + this
// -----------------------------------------------------------------------

public sealed record NumericLiteral(double Value, JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record BigIntLiteral(string Digits, JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record StringLiteral(string Value, JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record BooleanLiteral(bool Value, JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record NullLiteral(JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record Identifier(string Name, JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record ThisExpression(JsPosition Start, JsPosition End)
    : Expression(Start, End);

// -----------------------------------------------------------------------
// Aggregate literals
// -----------------------------------------------------------------------

public sealed record ArrayExpression(
    IReadOnlyList<Expression?> Elements,  // null = elision (hole)
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record ObjectExpression(
    IReadOnlyList<ObjectProperty> Properties,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record ObjectProperty(
    Expression Key,         // Identifier, StringLiteral, or NumericLiteral
    Expression Value,
    bool Shorthand,         // { foo } instead of { foo: foo }
    bool Computed,          // { [key]: v }
    JsPosition Start, JsPosition End)
    : AstNode(Start, End);

// -----------------------------------------------------------------------
// Operators
// -----------------------------------------------------------------------

public sealed record BinaryExpression(
    string Op, Expression Left, Expression Right,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record LogicalExpression(
    string Op, Expression Left, Expression Right,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record UnaryExpression(
    string Op, Expression Argument, bool Prefix,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record UpdateExpression(
    string Op, Expression Argument, bool Prefix,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record AssignmentExpression(
    string Op, Expression Target, Expression Value,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record ConditionalExpression(
    Expression Test, Expression Consequent, Expression Alternate,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record SequenceExpression(
    IReadOnlyList<Expression> Expressions,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

// -----------------------------------------------------------------------
// Access + calls
// -----------------------------------------------------------------------

public sealed record MemberExpression(
    Expression Object, Expression Property, bool Computed, bool Optional,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record CallExpression(
    Expression Callee, IReadOnlyList<Expression> Arguments, bool Optional,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record NewExpression(
    Expression Callee, IReadOnlyList<Expression> Arguments,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record SpreadElement(
    Expression Argument, JsPosition Start, JsPosition End)
    : Expression(Start, End);

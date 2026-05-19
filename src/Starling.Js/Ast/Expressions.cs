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

/// <summary>
/// <c>function () { … }</c> as an expression. Name is optional (named
/// function expressions bind their own name inside the body — but that
/// binding is M3-04c closure work; for now the name is informational).
/// </summary>
public sealed record FunctionExpression(
    Identifier? Name,
    IReadOnlyList<Expression> Params, // Identifier or destructuring binding pattern (ES2024 §14.3.3)
    BlockStatement Body,
    bool Generator,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

/// <summary>
/// Arrow function <c>(x) =&gt; expr</c> or <c>(x) =&gt; { stmts }</c>. Unlike
/// <see cref="FunctionExpression"/>, an arrow body's <c>this</c> binding is
/// captured lexically from the enclosing scope. <see cref="IsExpression"/> is
/// true for concise-body forms (single expression after <c>=&gt;</c>); false for
/// block-body forms.
/// </summary>
public sealed record ArrowFunctionExpression(
    IReadOnlyList<Expression> Params, // Identifier or destructuring binding pattern (ES2024 §14.3.3)
    AstNode Body,                // BlockStatement or Expression
    bool IsExpression,           // true => Body is an Expression (concise body)
    bool Async,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

/// <summary>
/// Template literal <c>`pre ${a} mid ${b} tail`</c>. <see cref="Quasis"/> is
/// the literal string segments; <see cref="Expressions"/> is the substitution
/// expressions. Always <c>Quasis.Count == Expressions.Count + 1</c>.
/// </summary>
public sealed record TemplateLiteral(
    IReadOnlyList<string> Quasis,
    IReadOnlyList<Expression> Expressions,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

/// <summary>Tagged template — <c>tag`...`</c>.</summary>
public sealed record TaggedTemplateExpression(
    Expression Tag,
    TemplateLiteral Quasi,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

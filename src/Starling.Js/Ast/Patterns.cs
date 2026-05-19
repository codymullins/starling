using Starling.Js.Lex;

namespace Starling.Js.Ast;

/// <summary>Base for ES2024 §13.3.3 destructuring binding patterns.</summary>
public abstract record BindingPattern(JsPosition Start, JsPosition End) : Expression(Start, End);

public sealed record ArrayPattern(
    IReadOnlyList<ArrayPatternElement> Elements,
    JsPosition Start, JsPosition End)
    : BindingPattern(Start, End);

public abstract record ArrayPatternElement(JsPosition Start, JsPosition End) : AstNode(Start, End);

public sealed record ArrayPatternHole(JsPosition Start, JsPosition End)
    : ArrayPatternElement(Start, End);

public sealed record ArrayPatternBindingElement(
    Expression Target,
    Expression? Default,
    JsPosition Start, JsPosition End)
    : ArrayPatternElement(Start, End);

public sealed record ArrayPatternRestElement(
    Expression Target,
    JsPosition Start, JsPosition End)
    : ArrayPatternElement(Start, End);

public sealed record ObjectPattern(
    IReadOnlyList<ObjectPatternProperty> Properties,
    RestElement? Rest,
    JsPosition Start, JsPosition End)
    : BindingPattern(Start, End);

public sealed record ObjectPatternProperty(
    Expression Key,
    Expression Target,
    bool Shorthand,
    bool Computed,
    Expression? Default,
    JsPosition Start, JsPosition End)
    : AstNode(Start, End);

public sealed record RestElement(
    Expression Argument,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

public sealed record AssignmentPattern(
    Expression Target,
    Expression Default,
    JsPosition Start, JsPosition End)
    : Expression(Start, End);

using Starling.Js.Lex;

namespace Starling.Js.Ast;

/// <summary>
/// ES2024 §16.2 ModuleItem import declaration: static dependency metadata plus
/// the local bindings introduced by the declaration.
/// </summary>
public sealed record ImportDeclaration(
    IReadOnlyList<ImportSpecifier> Specifiers,
    string Source,
    JsPosition Start,
    JsPosition End)
    : Statement(Start, End);

/// <summary>Base for ES2024 §16.2.2 import specifier forms.</summary>
public abstract record ImportSpecifier(JsPosition Start, JsPosition End)
    : AstNode(Start, End);

public sealed record ImportDefaultSpecifier(
    Identifier Local,
    JsPosition Start,
    JsPosition End)
    : ImportSpecifier(Start, End);

public sealed record ImportNamespaceSpecifier(
    Identifier Local,
    JsPosition Start,
    JsPosition End)
    : ImportSpecifier(Start, End);

public sealed record ImportNamedSpecifier(
    Expression Imported,
    Identifier Local,
    JsPosition Start,
    JsPosition End)
    : ImportSpecifier(Start, End);

public sealed record ImportSideEffectSpecifier(JsPosition Start, JsPosition End)
    : ImportSpecifier(Start, End);

/// <summary>Base for ES2024 §16.2.3 export declaration forms.</summary>
public abstract record ExportDeclaration(JsPosition Start, JsPosition End)
    : Statement(Start, End);

public sealed record ExportLocalDeclaration(
    Statement Declaration,
    JsPosition Start,
    JsPosition End)
    : ExportDeclaration(Start, End);

public sealed record ExportNamedDeclaration(
    IReadOnlyList<ExportSpecifier> Specifiers,
    string? Source,
    JsPosition Start,
    JsPosition End)
    : ExportDeclaration(Start, End);

public sealed record ExportDefaultDeclaration(
    AstNode Declaration,
    JsPosition Start,
    JsPosition End)
    : ExportDeclaration(Start, End);

public sealed record ExportAllDeclaration(
    string Source,
    Identifier? ExportedName,
    JsPosition Start,
    JsPosition End)
    : ExportDeclaration(Start, End);

/// <summary>ES2024 §16.2.3 ExportSpecifier — local/exported module names.</summary>
public sealed record ExportSpecifier(
    Expression Local,
    Expression Exported,
    JsPosition Start,
    JsPosition End)
    : AstNode(Start, End);

/// <summary>
/// wp:M3-03c — ES2024 §13.3.10 ImportCall: a dynamic <c>import(specifier)</c>
/// expression. Unlike a static <see cref="ImportDeclaration"/> this is an
/// ordinary expression valid anywhere (in modules AND classic scripts) and
/// evaluates to a Promise of the imported module's namespace object.
/// </summary>
/// <param name="Specifier">The module-specifier expression (string-coerced at
/// runtime). A rejection — not a synchronous throw — surfaces resolve/fetch/eval
/// failures.</param>
/// <param name="Options">Optional second argument (import attributes / assertions).
/// Parsed for forward compatibility but currently ignored by the runtime.</param>
/// <param name="Start">Source start position.</param>
/// <param name="End">Source end position.</param>
public sealed record ImportCallExpression(
    Expression Specifier,
    Expression? Options,
    JsPosition Start,
    JsPosition End)
    : Expression(Start, End);

/// <summary>
/// wp:M3-03c — ES2024 §13.3.12 ImportMeta: the <c>import.meta</c> meta-property.
/// Only legal in module code; evaluates to the running module's host-populated
/// meta object (at minimum <c>import.meta.url</c>).
/// </summary>
public sealed record ImportMetaExpression(JsPosition Start, JsPosition End)
    : Expression(Start, End);

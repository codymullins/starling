using Tessera.Js.Lex;

namespace Tessera.Js.Ast;

/// <summary>Base for every AST node. Carries source-range information so
/// later passes (compiler, error reporting, source maps) can refer back.</summary>
public abstract record AstNode(JsPosition Start, JsPosition End);

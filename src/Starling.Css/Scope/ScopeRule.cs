using Starling.Css.Parser;

namespace Starling.Css.Scope;

/// <summary>
/// A parsed <c>@scope</c> rule per
/// <see href="https://www.w3.org/TR/css-cascade-6/#scoped-styles">CSS Cascade and Inheritance Level 6 §3</see>.
/// </summary>
/// <param name="ScopeStart">The scope-start selector text (the prelude's first
/// parenthesized selector), or null for a prelude-less <c>@scope</c>.</param>
/// <param name="ScopeEnd">The scope-end selector text (after <c>to</c>), or null
/// when no upper bound is given.</param>
/// <param name="Rules">The scoped style rules in the rule's body.</param>
public sealed record ScopeRule(
    string? ScopeStart,
    string? ScopeEnd,
    IReadOnlyList<CssRule> Rules);

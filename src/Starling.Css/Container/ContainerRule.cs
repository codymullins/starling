using Starling.Css.Parser;

namespace Starling.Css.Container;

/// <summary>
/// A parsed <c>@container</c> rule per
/// <see href="https://www.w3.org/TR/css-contain-3/#container-rule">CSS Containment Level 3 §5</see>.
/// </summary>
/// <param name="Name">The optional container name the query targets, or null to
/// match the nearest ancestor container.</param>
/// <param name="Condition">The container condition text (the size/style query),
/// e.g. <c>(min-width: 400px)</c>.</param>
/// <param name="Rules">The style rules applied when the condition matches.</param>
public sealed record ContainerRule(
    string? Name,
    string Condition,
    IReadOnlyList<CssRule> Rules);

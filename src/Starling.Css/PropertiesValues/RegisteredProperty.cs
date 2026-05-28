namespace Starling.Css.PropertiesValues;

/// <summary>
/// A custom property registered via the <c>@property</c> at-rule, per
/// <see href="https://www.w3.org/TR/css-properties-values-api-1/#at-property-rule">
/// CSS Properties and Values API Level 1 §2</see>.
/// </summary>
/// <param name="Name">The custom property name, including the leading <c>--</c>.</param>
/// <param name="Syntax">The <c>syntax</c> descriptor string (e.g. <c>"&lt;length&gt;"</c>,
/// <c>"*"</c>). Required for a valid rule.</param>
/// <param name="Inherits">The <c>inherits</c> descriptor. Required for a valid rule.</param>
/// <param name="InitialValue">The serialized <c>initial-value</c> descriptor, or null when
/// omitted. The spec requires it for any syntax other than the universal <c>*</c>.</param>
public sealed record RegisteredProperty(
    string Name,
    string Syntax,
    bool Inherits,
    string? InitialValue)
{
    /// <summary>The universal syntax <c>*</c> accepts any token stream and does not
    /// require an <c>initial-value</c> (§2.1).</summary>
    public bool IsUniversal => Syntax.Trim() == "*";
}

using System.Globalization;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Values;

namespace Tessera.Css.Cascade;

public sealed class ComputedStyle
{
    private readonly IReadOnlyDictionary<PropertyId, CssValue> _values;

    internal ComputedStyle(
        IReadOnlyDictionary<PropertyId, CssValue> values,
        IReadOnlyDictionary<string, IReadOnlyList<CssComponentValue>> customProperties)
    {
        _values = values;
        CustomProperties = customProperties;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<CssComponentValue>> CustomProperties { get; }

    public CssValue Get(PropertyId property) => _values[property];

    public bool TryGet(PropertyId property, out CssValue value)
    {
        if (_values.TryGetValue(property, out var v))
        {
            value = v;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>
    /// Returns a new <see cref="ComputedStyle"/> with the given property values
    /// overlaid on top of this one. Used by the animation compositor
    /// (CSS Animations 1 §3.2) to layer in-flight animation + transition
    /// samples over the static cascade. Custom properties are unchanged.
    /// </summary>
    internal ComputedStyle WithOverrides(IReadOnlyDictionary<PropertyId, CssValue> overrides)
    {
        if (overrides.Count == 0) return this;
        var merged = new Dictionary<PropertyId, CssValue>(_values.Count);
        foreach (var kv in _values) merged[kv.Key] = kv.Value;
        foreach (var kv in overrides) merged[kv.Key] = kv.Value;
        return new ComputedStyle(merged, CustomProperties);
    }

    /// <summary>Layout-time used-value resolution. Resolves any remaining
    /// percentages or symbolic units (e.g. percentages, container units when
    /// a container basis is supplied) using <paramref name="ctx"/>.</summary>
    public CssValue UsedValue(PropertyId property, CssResolutionContext ctx)
        => CssCalcResolver.Resolve(Get(property), ctx);

    /// <summary>Resolve a property's value to a px length given a containing-block
    /// basis in pixels. Returns 0 if the value is not length-typed.</summary>
    public double UsedLengthPx(PropertyId property, double containingBlockPx, CssResolutionContext baseCtx)
    {
        var ctx = baseCtx with { PercentageBasisPx = containingBlockPx };
        return UsedValue(property, ctx) switch
        {
            CssLength { Unit: CssLengthUnit.Px } len => len.Value,
            CssNumber n => n.Value,
            _ => 0,
        };
    }

    public CssLength GetLength(PropertyId property)
        => Get(property) as CssLength ?? CssLength.Zero;

    public CssColor GetColor(PropertyId property)
        => Get(property) as CssColor ?? CssColor.Black;

    public string GetPropertyValue(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return PropertyRegistry.TryGetPropertyId(name, out var property)
            ? ToCssText(Get(property))
            : string.Empty;
    }

    public static string ToCssText(CssValue value)
        => value switch
        {
            CssKeyword keyword => keyword.Name,
            CssNumber number => number.Value.ToString(CultureInfo.InvariantCulture),
            CssPercentage percentage => percentage.Value.ToString(CultureInfo.InvariantCulture) + "%",
            CssLength length => length.ToString(),
            CssDimension dimension => dimension.Value.ToString(CultureInfo.InvariantCulture) + dimension.Unit,
            CssColor color => color.ToString(),
            CssString text => text.Value,
            CssUrl url => $"url({url.Value})",
            CssValueList list => string.Join(" ", list.Values.Select(ToCssText)),
            CssFunctionValue function => $"{function.Name}({string.Join(", ", function.Arguments.Select(ToCssText))})",
            CssVarReference var => var.Fallback is null
                ? $"var({var.Name})"
                : $"var({var.Name}, {ToCssText(var.Fallback)})",
            _ => value.ToString() ?? string.Empty,
        };
}

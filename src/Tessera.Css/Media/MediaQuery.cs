namespace Tessera.Css.Media;

// Media Queries Level 5 §2: a media query is "modifier media-type [and condition]"
// or just a "condition without media type".
public sealed record MediaQueryList(IReadOnlyList<MediaQuery> Queries)
{
    public static MediaQueryList All { get; } = new([new MediaQuery(MediaQueryModifier.None, "all", null)]);
}

public sealed record MediaQuery(MediaQueryModifier Modifier, string? MediaType, MediaCondition? Condition);

public enum MediaQueryModifier { None, Not, Only }

public abstract record MediaCondition;

public sealed record MediaConditionFeature(MediaFeature Feature) : MediaCondition;

public sealed record MediaConditionNot(MediaCondition Inner) : MediaCondition;

public sealed record MediaConditionAnd(IReadOnlyList<MediaCondition> Parts) : MediaCondition;

public sealed record MediaConditionOr(IReadOnlyList<MediaCondition> Parts) : MediaCondition;

public abstract record MediaFeature;

public sealed record MediaFeaturePlain(string Name, MediaFeatureValue? Value) : MediaFeature;

public sealed record MediaFeatureBoolean(string Name) : MediaFeature;

// MQ5 §4.3 range syntax: `(width >= 400px)` or `(400px <= width <= 800px)`.
public sealed record MediaFeatureRange(string Name, RangeOp Op1, MediaFeatureValue? V1, RangeOp Op2, MediaFeatureValue? V2) : MediaFeature;

public enum RangeOp { None, Less, LessOrEqual, Greater, GreaterOrEqual, Equal }

public abstract record MediaFeatureValue;

public sealed record MediaFeatureNumber(double Value) : MediaFeatureValue;

public sealed record MediaFeatureDimension(double Value, string Unit) : MediaFeatureValue;

public sealed record MediaFeatureIdent(string Name) : MediaFeatureValue;

public sealed record MediaFeatureRatio(double Numerator, double Denominator) : MediaFeatureValue
{
    public double AsDouble => Denominator == 0 ? double.PositiveInfinity : Numerator / Denominator;
}

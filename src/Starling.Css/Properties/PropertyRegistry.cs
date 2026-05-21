using Starling.Css.Parser;
using Starling.Css.Values;

namespace Starling.Css.Properties;

public static class PropertyRegistry
{
    private static readonly Dictionary<string, PropertyId> Names =
        Enum.GetValues<PropertyId>().ToDictionary(ToCssName, id => id, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<PropertyId> Inherited =
    [
        PropertyId.Color,
        PropertyId.FontFamily,
        PropertyId.FontSize,
        PropertyId.FontStretch,
        PropertyId.FontStyle,
        PropertyId.FontVariationSettings,
        PropertyId.FontWeight,
        PropertyId.LineHeight,
        PropertyId.TextAlign,
        PropertyId.TextAlignLast,
        PropertyId.TextDecoration,
        PropertyId.TextTransform,
        PropertyId.TextIndent,
        PropertyId.TextOrientation,
        PropertyId.WhiteSpace,
        PropertyId.WhiteSpaceCollapse,
        PropertyId.TextWrap,
        PropertyId.WordBreak,
        PropertyId.OverflowWrap,
        PropertyId.Hyphens,
        PropertyId.LineBreak,
        PropertyId.TabSize,
        PropertyId.WordSpacing,
        PropertyId.LetterSpacing,
        PropertyId.Direction,
        PropertyId.WritingMode,
        PropertyId.Visibility,
        PropertyId.Cursor,
        PropertyId.PointerEvents,
        PropertyId.CaretColor,
        PropertyId.ColorScheme,
    ];

    public static IReadOnlyList<PropertyId> All { get; } = Enum.GetValues<PropertyId>();

    public static bool TryGetPropertyId(string name, out PropertyId id)
        => Names.TryGetValue(name, out id);

    public static string Name(PropertyId id) => ToCssName(id);

    public static bool Inherits(PropertyId id) => Inherited.Contains(id);

    public static IEnumerable<PropertyDeclaration> Parse(CssDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (declaration.Name.StartsWith("--", StringComparison.Ordinal))
            yield break;

        var name = declaration.Name.ToLowerInvariant();
        // font-family idents are family names, not CSS keywords — the value
        // parser lowercases idents which would mangle "Helvetica Neue" to
        // "helvetica neue". Family matching is case-insensitive at lookup
        // time, but we keep the authored case here so the cascaded value
        // round-trips and DevTools-style inspection sees the original text.
        var values = name == "font-family"
            ? FontFamilyValueParser.Parse(declaration.Value)
            : CssValueParser.ParseList(declaration.Value).ToList();
        foreach (var parsed in Expand(name, values, declaration.Important))
            yield return parsed;
    }

    public static CssValue InitialValue(PropertyId id)
        => id switch
        {
            PropertyId.Display => new CssKeyword("inline"),
            PropertyId.Position => new CssKeyword("static"),
            PropertyId.Top or PropertyId.Right or PropertyId.Bottom or PropertyId.Left => new CssKeyword("auto"),
            PropertyId.ZIndex => new CssKeyword("auto"),
            PropertyId.Float or PropertyId.Clear => new CssKeyword("none"),
            PropertyId.Width or PropertyId.Height => new CssKeyword("auto"),
            PropertyId.MinWidth or PropertyId.MinHeight => CssLength.Zero,
            PropertyId.MaxWidth or PropertyId.MaxHeight => new CssKeyword("none"),
            PropertyId.MarginTop or PropertyId.MarginRight or PropertyId.MarginBottom or PropertyId.MarginLeft => CssLength.Zero,
            PropertyId.PaddingTop or PropertyId.PaddingRight or PropertyId.PaddingBottom or PropertyId.PaddingLeft => CssLength.Zero,
            PropertyId.BoxSizing => new CssKeyword("content-box"),
            PropertyId.OverflowX or PropertyId.OverflowY => new CssKeyword("visible"),
            PropertyId.OverflowClipMargin => CssLength.Zero,
            PropertyId.BorderTopWidth or PropertyId.BorderRightWidth or PropertyId.BorderBottomWidth or PropertyId.BorderLeftWidth => new CssLength(3, CssLengthUnit.Px),
            PropertyId.BorderTopStyle or PropertyId.BorderRightStyle or PropertyId.BorderBottomStyle or PropertyId.BorderLeftStyle => new CssKeyword("none"),
            PropertyId.BorderTopColor or PropertyId.BorderRightColor or PropertyId.BorderBottomColor or PropertyId.BorderLeftColor => new CssKeyword("currentColor"),
            PropertyId.BorderTopLeftRadius or PropertyId.BorderTopRightRadius or PropertyId.BorderBottomRightRadius or PropertyId.BorderBottomLeftRadius => CssLength.Zero,
            PropertyId.Color => CssColor.Black,
            PropertyId.BackgroundColor => CssColor.Transparent,
            PropertyId.BackgroundImage => new CssKeyword("none"),
            PropertyId.BackgroundPosition => new CssKeyword("0% 0%"),
            PropertyId.BackgroundSize => new CssKeyword("auto"),
            PropertyId.BackgroundRepeat => new CssKeyword("repeat"),
            PropertyId.Opacity => new CssNumber(1),
            PropertyId.Visibility => new CssKeyword("visible"),
            PropertyId.FontFamily => new CssKeyword("serif"),
            PropertyId.FontSize => new CssLength(16, CssLengthUnit.Px),
            PropertyId.FontStretch => new CssKeyword("normal"),
            PropertyId.FontStyle => new CssKeyword("normal"),
            PropertyId.FontVariationSettings => new CssKeyword("normal"),
            PropertyId.FontWeight => new CssNumber(400),
            PropertyId.LineHeight => new CssKeyword("normal"),
            PropertyId.TextAlign => new CssKeyword("start"),
            PropertyId.TextAlignLast => new CssKeyword("auto"),
            PropertyId.TextDecoration => new CssKeyword("none"),
            PropertyId.TextDecorationLine => new CssKeyword("none"),
            PropertyId.TextDecorationStyle => new CssKeyword("solid"),
            PropertyId.TextDecorationColor => new CssKeyword("currentColor"),
            PropertyId.TextDecorationThickness => new CssKeyword("auto"),
            PropertyId.TextUnderlineOffset => new CssKeyword("auto"),
            PropertyId.TextUnderlinePosition => new CssKeyword("auto"),
            PropertyId.TextTransform => new CssKeyword("none"),
            PropertyId.TextIndent => CssLength.Zero,
            PropertyId.WhiteSpace => new CssKeyword("normal"),
            PropertyId.WhiteSpaceCollapse => new CssKeyword("collapse"),
            PropertyId.TextWrap => new CssKeyword("wrap"),
            PropertyId.WordBreak => new CssKeyword("normal"),
            PropertyId.OverflowWrap => new CssKeyword("normal"),
            PropertyId.Hyphens => new CssKeyword("manual"),
            PropertyId.TabSize => new CssNumber(8),
            PropertyId.LineBreak => new CssKeyword("auto"),
            PropertyId.WordSpacing => new CssKeyword("normal"),
            PropertyId.LetterSpacing => new CssKeyword("normal"),
            PropertyId.Direction => new CssKeyword("ltr"),
            PropertyId.WritingMode => new CssKeyword("horizontal-tb"),
            PropertyId.TextOrientation => new CssKeyword("mixed"),
            PropertyId.UnicodeBidi => new CssKeyword("normal"),

            // Flexbox
            PropertyId.FlexDirection => new CssKeyword("row"),
            PropertyId.FlexWrap => new CssKeyword("nowrap"),
            PropertyId.FlexGrow => new CssNumber(0),
            PropertyId.FlexShrink => new CssNumber(1),
            PropertyId.FlexBasis => new CssKeyword("auto"),
            PropertyId.Order => new CssNumber(0),
            PropertyId.JustifyContent => new CssKeyword("normal"),
            PropertyId.AlignItems => new CssKeyword("normal"),
            PropertyId.AlignSelf => new CssKeyword("auto"),
            PropertyId.AlignContent => new CssKeyword("normal"),
            PropertyId.JustifyItems => new CssKeyword("legacy"),
            PropertyId.JustifySelf => new CssKeyword("auto"),

            // Grid
            PropertyId.GridTemplateColumns or PropertyId.GridTemplateRows or PropertyId.GridTemplateAreas => new CssKeyword("none"),
            PropertyId.GridAutoColumns or PropertyId.GridAutoRows => new CssKeyword("auto"),
            PropertyId.GridAutoFlow => new CssKeyword("row"),
            PropertyId.GridColumnStart or PropertyId.GridColumnEnd or PropertyId.GridRowStart or PropertyId.GridRowEnd => new CssKeyword("auto"),

            // Gap
            PropertyId.RowGap or PropertyId.ColumnGap => new CssKeyword("normal"),

            // Sizing
            PropertyId.AspectRatio => new CssKeyword("auto"),
            PropertyId.ObjectFit => new CssKeyword("fill"),
            PropertyId.ObjectPosition => new CssKeyword("50% 50%"),

            // Visual effects
            PropertyId.Transform or PropertyId.Translate or PropertyId.Scale or PropertyId.Rotate => new CssKeyword("none"),
            PropertyId.TransformOrigin => new CssKeyword("50% 50% 0"),
            PropertyId.TransformBox => new CssKeyword("view-box"),
            PropertyId.Perspective => new CssKeyword("none"),
            PropertyId.PerspectiveOrigin => new CssKeyword("50% 50%"),
            PropertyId.Filter or PropertyId.BackdropFilter => new CssKeyword("none"),
            PropertyId.MixBlendMode or PropertyId.BackgroundBlendMode => new CssKeyword("normal"),
            PropertyId.ClipPath => new CssKeyword("none"),
            PropertyId.MaskImage => new CssKeyword("none"),
            PropertyId.MaskPosition => new CssKeyword("0% 0%"),
            PropertyId.MaskSize => new CssKeyword("auto"),
            PropertyId.MaskRepeat => new CssKeyword("repeat"),
            PropertyId.MaskClip or PropertyId.MaskOrigin => new CssKeyword("border-box"),
            PropertyId.MaskComposite => new CssKeyword("add"),
            PropertyId.MaskMode => new CssKeyword("match-source"),

            // Containment / rendering
            PropertyId.Contain => new CssKeyword("none"),
            PropertyId.ContentVisibility => new CssKeyword("visible"),
            PropertyId.WillChange => new CssKeyword("auto"),
            PropertyId.Isolation => new CssKeyword("auto"),
            PropertyId.Container => new CssKeyword("none"),
            PropertyId.ContainerType => new CssKeyword("normal"),
            PropertyId.ContainerName => new CssKeyword("none"),

            // Scrolling
            PropertyId.ScrollBehavior => new CssKeyword("auto"),
            PropertyId.ScrollSnapType => new CssKeyword("none"),
            PropertyId.ScrollSnapAlign => new CssKeyword("none"),
            PropertyId.ScrollSnapStop => new CssKeyword("normal"),
            PropertyId.ScrollMarginTop or PropertyId.ScrollMarginRight or PropertyId.ScrollMarginBottom or PropertyId.ScrollMarginLeft => CssLength.Zero,
            PropertyId.ScrollPaddingTop or PropertyId.ScrollPaddingRight or PropertyId.ScrollPaddingBottom or PropertyId.ScrollPaddingLeft => new CssKeyword("auto"),
            PropertyId.OverscrollBehaviorX or PropertyId.OverscrollBehaviorY => new CssKeyword("auto"),

            // Forms / UI
            PropertyId.AccentColor or PropertyId.CaretColor => new CssKeyword("auto"),
            PropertyId.ColorScheme => new CssKeyword("normal"),
            PropertyId.Appearance => new CssKeyword("none"),
            PropertyId.PointerEvents => new CssKeyword("auto"),
            PropertyId.UserSelect => new CssKeyword("auto"),
            PropertyId.Cursor => new CssKeyword("auto"),

            // Logical longhands — default to physical equivalents.
            PropertyId.MarginInlineStart or PropertyId.MarginInlineEnd or PropertyId.MarginBlockStart or PropertyId.MarginBlockEnd => CssLength.Zero,
            PropertyId.PaddingInlineStart or PropertyId.PaddingInlineEnd or PropertyId.PaddingBlockStart or PropertyId.PaddingBlockEnd => CssLength.Zero,
            PropertyId.BorderInlineStartWidth or PropertyId.BorderInlineEndWidth or PropertyId.BorderBlockStartWidth or PropertyId.BorderBlockEndWidth => new CssLength(3, CssLengthUnit.Px),
            PropertyId.BorderInlineStartStyle or PropertyId.BorderInlineEndStyle or PropertyId.BorderBlockStartStyle or PropertyId.BorderBlockEndStyle => new CssKeyword("none"),
            PropertyId.BorderInlineStartColor or PropertyId.BorderInlineEndColor or PropertyId.BorderBlockStartColor or PropertyId.BorderBlockEndColor => new CssKeyword("currentColor"),
            PropertyId.BorderStartStartRadius or PropertyId.BorderStartEndRadius or PropertyId.BorderEndStartRadius or PropertyId.BorderEndEndRadius => CssLength.Zero,
            PropertyId.InsetInlineStart or PropertyId.InsetInlineEnd or PropertyId.InsetBlockStart or PropertyId.InsetBlockEnd => new CssKeyword("auto"),
            PropertyId.InlineSize or PropertyId.BlockSize => new CssKeyword("auto"),
            PropertyId.MinInlineSize or PropertyId.MinBlockSize => CssLength.Zero,
            PropertyId.MaxInlineSize or PropertyId.MaxBlockSize => new CssKeyword("none"),

            // Transitions
            PropertyId.TransitionProperty => new CssKeyword("all"),
            PropertyId.TransitionDuration or PropertyId.TransitionDelay => new CssDimension(0, "s"),
            PropertyId.TransitionTimingFunction => new CssKeyword("ease"),
            PropertyId.TransitionBehavior => new CssKeyword("normal"),

            // Animations
            PropertyId.AnimationName => new CssKeyword("none"),
            PropertyId.AnimationDuration or PropertyId.AnimationDelay => new CssDimension(0, "s"),
            PropertyId.AnimationTimingFunction => new CssKeyword("ease"),
            PropertyId.AnimationIterationCount => new CssNumber(1),
            PropertyId.AnimationDirection => new CssKeyword("normal"),
            PropertyId.AnimationFillMode => new CssKeyword("none"),
            PropertyId.AnimationPlayState => new CssKeyword("running"),
            PropertyId.AnimationComposition => new CssKeyword("replace"),

            _ => new CssKeyword("initial"),
        };

    private static IEnumerable<PropertyDeclaration> Expand(
        string name,
        List<CssValue> values,
        bool important)
    {
        if (values.Count == 0)
            yield break;

        // CSS Variables L1 §3.7 — when a shorthand contains var(), every longhand
        // it maps to is set to a pending-substitution value; the shorthand cannot
        // be expanded until the var() references resolve at computed-value time.
        if (ShorthandLonghands.TryGetValue(name, out var longhands)
            && values.Any(ContainsVarReference))
        {
            foreach (var longhand in longhands)
                yield return new PropertyDeclaration(
                    longhand,
                    new CssPendingSubstitution(name, values, longhand),
                    important);
            yield break;
        }

        foreach (var item in ExpandResolved(name, values, important))
            yield return item;
    }

    /// <summary>
    /// Re-runs the shorthand expander after var() substitution. Used by the
    /// cascade to resolve <see cref="CssPendingSubstitution"/> values. Callers
    /// must have already substituted all var() references in <paramref name="values"/>;
    /// any remaining <see cref="CssVarReference"/>s are treated as invalid components.
    /// </summary>
    internal static IEnumerable<PropertyDeclaration> ExpandResolved(
        string name,
        IReadOnlyList<CssValue> values,
        bool important)
    {
        if (values.Count == 0)
            yield break;
        var list = values as List<CssValue> ?? values.ToList();
        foreach (var item in ExpandSwitch(name, list, important))
            yield return item;
    }

    private static IEnumerable<PropertyDeclaration> ExpandSwitch(
        string name,
        List<CssValue> values,
        bool important)
    {
        switch (name)
        {
            case "margin":
                foreach (var item in Box(PropertyId.MarginTop, PropertyId.MarginRight, PropertyId.MarginBottom, PropertyId.MarginLeft, values, important))
                    yield return item;
                break;
            case "padding":
                foreach (var item in Box(PropertyId.PaddingTop, PropertyId.PaddingRight, PropertyId.PaddingBottom, PropertyId.PaddingLeft, values, important))
                    yield return item;
                break;
            case "border-width":
                foreach (var item in Box(PropertyId.BorderTopWidth, PropertyId.BorderRightWidth, PropertyId.BorderBottomWidth, PropertyId.BorderLeftWidth, values, important))
                    yield return item;
                break;
            case "border-style":
                foreach (var item in Box(PropertyId.BorderTopStyle, PropertyId.BorderRightStyle, PropertyId.BorderBottomStyle, PropertyId.BorderLeftStyle, values, important))
                    yield return item;
                break;
            case "border-color":
                foreach (var item in Box(PropertyId.BorderTopColor, PropertyId.BorderRightColor, PropertyId.BorderBottomColor, PropertyId.BorderLeftColor, values, important))
                    yield return item;
                break;
            case "border-radius":
                foreach (var item in Box(PropertyId.BorderTopLeftRadius, PropertyId.BorderTopRightRadius, PropertyId.BorderBottomRightRadius, PropertyId.BorderBottomLeftRadius, values, important))
                    yield return item;
                break;
            case "overflow":
                yield return new PropertyDeclaration(PropertyId.OverflowX, values[0], important);
                yield return new PropertyDeclaration(PropertyId.OverflowY, values.Count > 1 ? values[1] : values[0], important);
                break;
            case "background":
                foreach (var item in ExpandBackground(values, important))
                    yield return item;
                break;
            case "border":
                foreach (var value in values)
                {
                    if (IsBorderStyle(value))
                    {
                        foreach (var item in Box(PropertyId.BorderTopStyle, PropertyId.BorderRightStyle, PropertyId.BorderBottomStyle, PropertyId.BorderLeftStyle, [value], important))
                            yield return item;
                    }
                    else if (IsColorLike(value))
                    {
                        foreach (var item in Box(PropertyId.BorderTopColor, PropertyId.BorderRightColor, PropertyId.BorderBottomColor, PropertyId.BorderLeftColor, [value], important))
                            yield return item;
                    }
                    else
                    {
                        foreach (var item in Box(PropertyId.BorderTopWidth, PropertyId.BorderRightWidth, PropertyId.BorderBottomWidth, PropertyId.BorderLeftWidth, [value], important))
                            yield return item;
                    }
                }
                break;

            // ---- Flexbox shorthands ----
            case "flex":
                foreach (var item in ExpandFlex(values, important))
                    yield return item;
                break;
            case "flex-flow":
                foreach (var item in ExpandFlexFlow(values, important))
                    yield return item;
                break;

            // ---- Gap shorthands ----
            case "gap":
                yield return new PropertyDeclaration(PropertyId.RowGap, values[0], important);
                yield return new PropertyDeclaration(PropertyId.ColumnGap, values.Count > 1 ? values[1] : values[0], important);
                break;
            case "grid-gap":
                yield return new PropertyDeclaration(PropertyId.RowGap, values[0], important);
                yield return new PropertyDeclaration(PropertyId.ColumnGap, values.Count > 1 ? values[1] : values[0], important);
                break;
            case "grid-row-gap":
                yield return new PropertyDeclaration(PropertyId.RowGap, values[0], important);
                break;
            case "grid-column-gap":
                yield return new PropertyDeclaration(PropertyId.ColumnGap, values[0], important);
                break;

            // ---- Grid shorthands ----
            // TODO(lane-B): Refine grid-template / grid shorthand once track-list value parser exists.
            case "grid-template":
                foreach (var item in ExpandGridTemplate(values, important))
                    yield return item;
                break;
            case "grid":
                foreach (var item in ExpandGrid(values, important))
                    yield return item;
                break;
            case "grid-column":
                foreach (var item in ExpandSlashPair(PropertyId.GridColumnStart, PropertyId.GridColumnEnd, values, important))
                    yield return item;
                break;
            case "grid-row":
                foreach (var item in ExpandSlashPair(PropertyId.GridRowStart, PropertyId.GridRowEnd, values, important))
                    yield return item;
                break;
            case "grid-area":
                foreach (var item in ExpandGridArea(values, important))
                    yield return item;
                break;

            // ---- Place-* shorthands ----
            case "place-items":
                yield return new PropertyDeclaration(PropertyId.AlignItems, values[0], important);
                yield return new PropertyDeclaration(PropertyId.JustifyItems, values.Count > 1 ? values[1] : values[0], important);
                break;
            case "place-content":
                yield return new PropertyDeclaration(PropertyId.AlignContent, values[0], important);
                yield return new PropertyDeclaration(PropertyId.JustifyContent, values.Count > 1 ? values[1] : values[0], important);
                break;
            case "place-self":
                yield return new PropertyDeclaration(PropertyId.AlignSelf, values[0], important);
                yield return new PropertyDeclaration(PropertyId.JustifySelf, values.Count > 1 ? values[1] : values[0], important);
                break;

            // ---- Logical: margin/padding 2-value shorthands ----
            case "margin-inline":
                foreach (var item in TwoValue(PropertyId.MarginInlineStart, PropertyId.MarginInlineEnd, values, important))
                    yield return item;
                break;
            case "margin-block":
                foreach (var item in TwoValue(PropertyId.MarginBlockStart, PropertyId.MarginBlockEnd, values, important))
                    yield return item;
                break;
            case "padding-inline":
                foreach (var item in TwoValue(PropertyId.PaddingInlineStart, PropertyId.PaddingInlineEnd, values, important))
                    yield return item;
                break;
            case "padding-block":
                foreach (var item in TwoValue(PropertyId.PaddingBlockStart, PropertyId.PaddingBlockEnd, values, important))
                    yield return item;
                break;
            case "inset":
                foreach (var item in Box(PropertyId.Top, PropertyId.Right, PropertyId.Bottom, PropertyId.Left, values, important))
                    yield return item;
                break;
            case "inset-inline":
                foreach (var item in TwoValue(PropertyId.InsetInlineStart, PropertyId.InsetInlineEnd, values, important))
                    yield return item;
                break;
            case "inset-block":
                foreach (var item in TwoValue(PropertyId.InsetBlockStart, PropertyId.InsetBlockEnd, values, important))
                    yield return item;
                break;

            // ---- Logical: border shorthands ----
            case "border-inline-width":
                foreach (var item in TwoValue(PropertyId.BorderInlineStartWidth, PropertyId.BorderInlineEndWidth, values, important))
                    yield return item;
                break;
            case "border-inline-style":
                foreach (var item in TwoValue(PropertyId.BorderInlineStartStyle, PropertyId.BorderInlineEndStyle, values, important))
                    yield return item;
                break;
            case "border-inline-color":
                foreach (var item in TwoValue(PropertyId.BorderInlineStartColor, PropertyId.BorderInlineEndColor, values, important))
                    yield return item;
                break;
            case "border-block-width":
                foreach (var item in TwoValue(PropertyId.BorderBlockStartWidth, PropertyId.BorderBlockEndWidth, values, important))
                    yield return item;
                break;
            case "border-block-style":
                foreach (var item in TwoValue(PropertyId.BorderBlockStartStyle, PropertyId.BorderBlockEndStyle, values, important))
                    yield return item;
                break;
            case "border-block-color":
                foreach (var item in TwoValue(PropertyId.BorderBlockStartColor, PropertyId.BorderBlockEndColor, values, important))
                    yield return item;
                break;
            case "border-inline-start":
                foreach (var item in ExpandBorderSide(PropertyId.BorderInlineStartWidth, PropertyId.BorderInlineStartStyle, PropertyId.BorderInlineStartColor, values, important))
                    yield return item;
                break;
            case "border-inline-end":
                foreach (var item in ExpandBorderSide(PropertyId.BorderInlineEndWidth, PropertyId.BorderInlineEndStyle, PropertyId.BorderInlineEndColor, values, important))
                    yield return item;
                break;
            case "border-block-start":
                foreach (var item in ExpandBorderSide(PropertyId.BorderBlockStartWidth, PropertyId.BorderBlockStartStyle, PropertyId.BorderBlockStartColor, values, important))
                    yield return item;
                break;
            case "border-block-end":
                foreach (var item in ExpandBorderSide(PropertyId.BorderBlockEndWidth, PropertyId.BorderBlockEndStyle, PropertyId.BorderBlockEndColor, values, important))
                    yield return item;
                break;
            case "border-inline":
                foreach (var item in ExpandBorderSide(PropertyId.BorderInlineStartWidth, PropertyId.BorderInlineStartStyle, PropertyId.BorderInlineStartColor, values, important))
                    yield return item;
                foreach (var item in ExpandBorderSide(PropertyId.BorderInlineEndWidth, PropertyId.BorderInlineEndStyle, PropertyId.BorderInlineEndColor, values, important))
                    yield return item;
                break;
            case "border-block":
                foreach (var item in ExpandBorderSide(PropertyId.BorderBlockStartWidth, PropertyId.BorderBlockStartStyle, PropertyId.BorderBlockStartColor, values, important))
                    yield return item;
                foreach (var item in ExpandBorderSide(PropertyId.BorderBlockEndWidth, PropertyId.BorderBlockEndStyle, PropertyId.BorderBlockEndColor, values, important))
                    yield return item;
                break;

            // ---- Physical border-side shorthands (border-top, border-right, border-bottom, border-left) ----
            case "border-top":
                foreach (var item in ExpandBorderSide(PropertyId.BorderTopWidth, PropertyId.BorderTopStyle, PropertyId.BorderTopColor, values, important))
                    yield return item;
                break;
            case "border-right":
                foreach (var item in ExpandBorderSide(PropertyId.BorderRightWidth, PropertyId.BorderRightStyle, PropertyId.BorderRightColor, values, important))
                    yield return item;
                break;
            case "border-bottom":
                foreach (var item in ExpandBorderSide(PropertyId.BorderBottomWidth, PropertyId.BorderBottomStyle, PropertyId.BorderBottomColor, values, important))
                    yield return item;
                break;
            case "border-left":
                foreach (var item in ExpandBorderSide(PropertyId.BorderLeftWidth, PropertyId.BorderLeftStyle, PropertyId.BorderLeftColor, values, important))
                    yield return item;
                break;

            // ---- Scroll-margin / scroll-padding 4-value shorthands ----
            case "scroll-margin":
                foreach (var item in Box(PropertyId.ScrollMarginTop, PropertyId.ScrollMarginRight, PropertyId.ScrollMarginBottom, PropertyId.ScrollMarginLeft, values, important))
                    yield return item;
                break;
            case "scroll-padding":
                foreach (var item in Box(PropertyId.ScrollPaddingTop, PropertyId.ScrollPaddingRight, PropertyId.ScrollPaddingBottom, PropertyId.ScrollPaddingLeft, values, important))
                    yield return item;
                break;
            case "overscroll-behavior":
                yield return new PropertyDeclaration(PropertyId.OverscrollBehaviorX, values[0], important);
                yield return new PropertyDeclaration(PropertyId.OverscrollBehaviorY, values.Count > 1 ? values[1] : values[0], important);
                break;

            // ---- Text decoration shorthand ----
            case "text-decoration":
                foreach (var item in ExpandTextDecoration(values, important))
                    yield return item;
                break;

            // ---- white-space shorthand (CSS Text 4 §3) ----
            // Sets the white-space-collapse + text-wrap longhands from the legacy
            // single keyword, while preserving the `white-space` value itself so
            // existing consumers (and serialization) still see it.
            case "white-space":
                foreach (var item in ExpandWhiteSpace(values, important))
                    yield return item;
                break;

            // ---- Transition / Animation shorthands (simplified: single layer only) ----
            // TODO(lane-B): Multi-layer comma-separated transition/animation requires CssValueList splitting on top-level commas.
            case "transition":
                foreach (var item in ExpandTransition(values, important))
                    yield return item;
                break;
            case "animation":
                foreach (var item in ExpandAnimation(values, important))
                    yield return item;
                break;

            default:
                if (TryGetPropertyId(name, out var id))
                    yield return new PropertyDeclaration(id, values.Count == 1 ? values[0] : new CssValueList(values), important);
                break;
        }
    }

    private static IEnumerable<PropertyDeclaration> Box(
        PropertyId top,
        PropertyId right,
        PropertyId bottom,
        PropertyId left,
        List<CssValue> values,
        bool important)
    {
        CssValue[] actual = values.Count switch
        {
            1 => [values[0], values[0], values[0], values[0]],
            2 => [values[0], values[1], values[0], values[1]],
            3 => [values[0], values[1], values[2], values[1]],
            _ => [values[0], values[1], values[2], values[3]],
        };

        yield return new PropertyDeclaration(top, actual[0], important);
        yield return new PropertyDeclaration(right, actual[1], important);
        yield return new PropertyDeclaration(bottom, actual[2], important);
        yield return new PropertyDeclaration(left, actual[3], important);
    }

    private static IEnumerable<PropertyDeclaration> TwoValue(
        PropertyId start,
        PropertyId end,
        List<CssValue> values,
        bool important)
    {
        yield return new PropertyDeclaration(start, values[0], important);
        yield return new PropertyDeclaration(end, values.Count > 1 ? values[1] : values[0], important);
    }

    private static IEnumerable<PropertyDeclaration> ExpandBackground(List<CssValue> values, bool important)
    {
        // CSS Backgrounds 3 §3.10 — the `background` shorthand sets multiple
        // background-* longhands. Full layered parsing (split on top-level
        // commas, per-layer position/size with slash separator) is deferred;
        // for now we collect each component once across the value list. This
        // is enough for the common single-layer authoring style used by sites
        // like mcmaster.com: `background: url(sprite.png) -60px 0 no-repeat`.
        CssValue? color = null;
        CssValue? image = null;
        CssValue? repeat = null;
        var positionValues = new List<CssValue>();

        foreach (var v in values)
        {
            if (color is null && IsColorLike(v))
            {
                color = v;
            }
            else if (image is null && (v is CssUrl || v is CssFunctionValue { Name: "linear-gradient" or "radial-gradient" or "conic-gradient" or "repeating-linear-gradient" or "repeating-radial-gradient" or "repeating-conic-gradient" or "image-set" or "url" }))
            {
                image = v;
            }
            else if (v is CssKeyword k && IsBackgroundRepeatKeyword(k.Name))
            {
                repeat = v;
            }
            else if (v is CssLength or CssPercentage or CssNumber
                || (v is CssKeyword pk && IsBackgroundPositionKeyword(pk.Name)))
            {
                positionValues.Add(v);
            }
        }

        if (color is not null)
            yield return new PropertyDeclaration(PropertyId.BackgroundColor, color, important);
        if (image is not null)
            yield return new PropertyDeclaration(PropertyId.BackgroundImage, image, important);
        if (repeat is not null)
            yield return new PropertyDeclaration(PropertyId.BackgroundRepeat, repeat, important);
        if (positionValues.Count > 0)
        {
            var pos = positionValues.Count == 1 ? positionValues[0] : new CssValueList(positionValues);
            yield return new PropertyDeclaration(PropertyId.BackgroundPosition, pos, important);
        }
    }

    private static bool IsBackgroundRepeatKeyword(string name)
        => name is "repeat" or "no-repeat" or "repeat-x" or "repeat-y" or "space" or "round";

    private static bool IsBackgroundPositionKeyword(string name)
        => name is "left" or "right" or "top" or "bottom" or "center";

    private static IEnumerable<PropertyDeclaration> ExpandFlex(List<CssValue> values, bool important)
    {
        // flex: none | <flex-grow> [<flex-shrink> || <flex-basis>] | auto
        if (values.Count == 1 && values[0] is CssKeyword { Name: "none" })
        {
            yield return new PropertyDeclaration(PropertyId.FlexGrow, new CssNumber(0), important);
            yield return new PropertyDeclaration(PropertyId.FlexShrink, new CssNumber(0), important);
            yield return new PropertyDeclaration(PropertyId.FlexBasis, new CssKeyword("auto"), important);
            yield break;
        }
        if (values.Count == 1 && values[0] is CssKeyword { Name: "auto" })
        {
            yield return new PropertyDeclaration(PropertyId.FlexGrow, new CssNumber(1), important);
            yield return new PropertyDeclaration(PropertyId.FlexShrink, new CssNumber(1), important);
            yield return new PropertyDeclaration(PropertyId.FlexBasis, new CssKeyword("auto"), important);
            yield break;
        }
        if (values.Count == 1 && values[0] is CssKeyword { Name: "initial" })
        {
            yield return new PropertyDeclaration(PropertyId.FlexGrow, new CssNumber(0), important);
            yield return new PropertyDeclaration(PropertyId.FlexShrink, new CssNumber(1), important);
            yield return new PropertyDeclaration(PropertyId.FlexBasis, new CssKeyword("auto"), important);
            yield break;
        }

        CssValue grow = new CssNumber(1);
        CssValue shrink = new CssNumber(1);
        CssValue basis = new CssLength(0, CssLengthUnit.Px);
        var seenGrow = false;
        var seenShrink = false;
        var seenBasis = false;

        foreach (var v in values)
        {
            if (!seenGrow && v is CssNumber gNum)
            {
                grow = gNum;
                seenGrow = true;
            }
            else if (seenGrow && !seenShrink && v is CssNumber sNum)
            {
                shrink = sNum;
                seenShrink = true;
            }
            else if (!seenBasis && IsBasisValue(v))
            {
                basis = v;
                seenBasis = true;
            }
        }

        // single number means flex-grow only; basis becomes 0.
        if (values.Count == 1 && seenGrow && !seenBasis)
            basis = new CssLength(0, CssLengthUnit.Px);

        yield return new PropertyDeclaration(PropertyId.FlexGrow, grow, important);
        yield return new PropertyDeclaration(PropertyId.FlexShrink, shrink, important);
        yield return new PropertyDeclaration(PropertyId.FlexBasis, basis, important);
    }

    private static bool IsBasisValue(CssValue value)
        => value is CssLength or CssPercentage or CssKeyword { Name: "auto" or "content" or "min-content" or "max-content" or "fit-content" } or CssFunctionValue { Name: "fit-content" or "calc" };

    private static IEnumerable<PropertyDeclaration> ExpandFlexFlow(List<CssValue> values, bool important)
    {
        foreach (var v in values)
        {
            if (v is CssKeyword k)
            {
                if (k.Name is "row" or "row-reverse" or "column" or "column-reverse")
                    yield return new PropertyDeclaration(PropertyId.FlexDirection, v, important);
                else if (k.Name is "nowrap" or "wrap" or "wrap-reverse")
                    yield return new PropertyDeclaration(PropertyId.FlexWrap, v, important);
            }
        }
    }

    private static IEnumerable<PropertyDeclaration> ExpandSlashPair(
        PropertyId start,
        PropertyId end,
        List<CssValue> values,
        bool important)
    {
        var (before, after) = SplitOnSlash(values);
        var startValue = before.Count == 1 ? before[0] : new CssValueList(before);
        yield return new PropertyDeclaration(start, startValue, important);
        if (after is null)
            yield return new PropertyDeclaration(end, startValue, important);
        else
        {
            var endValue = after.Count == 1 ? after[0] : new CssValueList(after);
            yield return new PropertyDeclaration(end, endValue, important);
        }
    }

    private static (List<CssValue> Before, List<CssValue>? After) SplitOnSlash(List<CssValue> values)
    {
        var idx = values.FindIndex(v => v is CssKeyword { Name: "/" });
        if (idx < 0)
            return (values, null);
        return (values.Take(idx).ToList(), values.Skip(idx + 1).ToList());
    }

    private static IEnumerable<PropertyDeclaration> ExpandGridArea(List<CssValue> values, bool important)
    {
        // grid-area: <row-start> [/ <column-start> [/ <row-end> [/ <column-end>]]]
        var parts = new List<List<CssValue>>();
        var current = new List<CssValue>();
        foreach (var v in values)
        {
            if (v is CssKeyword { Name: "/" })
            {
                parts.Add(current);
                current = [];
            }
            else
                current.Add(v);
        }
        parts.Add(current);

        CssValue Pick(int i) => parts.Count > i && parts[i].Count > 0
            ? (parts[i].Count == 1 ? parts[i][0] : new CssValueList(parts[i]))
            : new CssKeyword("auto");

        var rowStart = Pick(0);
        var colStart = parts.Count > 1 ? Pick(1) : rowStart;
        var rowEnd = parts.Count > 2 ? Pick(2) : rowStart;
        var colEnd = parts.Count > 3 ? Pick(3) : colStart;

        yield return new PropertyDeclaration(PropertyId.GridRowStart, rowStart, important);
        yield return new PropertyDeclaration(PropertyId.GridColumnStart, colStart, important);
        yield return new PropertyDeclaration(PropertyId.GridRowEnd, rowEnd, important);
        yield return new PropertyDeclaration(PropertyId.GridColumnEnd, colEnd, important);
    }

    private static IEnumerable<PropertyDeclaration> ExpandGridTemplate(List<CssValue> values, bool important)
    {
        // Minimal: grid-template: none | <rows> / <columns>
        if (values.Count == 1 && values[0] is CssKeyword { Name: "none" })
        {
            yield return new PropertyDeclaration(PropertyId.GridTemplateRows, new CssKeyword("none"), important);
            yield return new PropertyDeclaration(PropertyId.GridTemplateColumns, new CssKeyword("none"), important);
            yield return new PropertyDeclaration(PropertyId.GridTemplateAreas, new CssKeyword("none"), important);
            yield break;
        }

        var (before, after) = SplitOnSlash(values);
        if (after is not null)
        {
            yield return new PropertyDeclaration(PropertyId.GridTemplateRows,
                before.Count == 1 ? before[0] : new CssValueList(before), important);
            yield return new PropertyDeclaration(PropertyId.GridTemplateColumns,
                after.Count == 1 ? after[0] : new CssValueList(after), important);
        }
        else
        {
            yield return new PropertyDeclaration(PropertyId.GridTemplateRows,
                before.Count == 1 ? before[0] : new CssValueList(before), important);
        }
    }

    private static IEnumerable<PropertyDeclaration> ExpandGrid(List<CssValue> values, bool important)
    {
        // TODO(lane-B): Full grid shorthand parsing — for now route as grid-template.
        foreach (var item in ExpandGridTemplate(values, important))
            yield return item;
    }

    private static IEnumerable<PropertyDeclaration> ExpandBorderSide(
        PropertyId widthId,
        PropertyId styleId,
        PropertyId colorId,
        List<CssValue> values,
        bool important)
    {
        foreach (var value in values)
        {
            if (IsBorderStyle(value))
                yield return new PropertyDeclaration(styleId, value, important);
            else if (IsColorLike(value))
                yield return new PropertyDeclaration(colorId, value, important);
            else
                yield return new PropertyDeclaration(widthId, value, important);
        }
    }

    private static IEnumerable<PropertyDeclaration> ExpandTextDecoration(List<CssValue> values, bool important)
    {
        foreach (var v in values)
        {
            if (v is CssKeyword k)
            {
                if (k.Name is "none" or "underline" or "overline" or "line-through" or "blink")
                    yield return new PropertyDeclaration(PropertyId.TextDecorationLine, v, important);
                else if (k.Name is "solid" or "double" or "dotted" or "dashed" or "wavy")
                    yield return new PropertyDeclaration(PropertyId.TextDecorationStyle, v, important);
                else if (IsColorLike(v))
                    yield return new PropertyDeclaration(PropertyId.TextDecorationColor, v, important);
                else
                    yield return new PropertyDeclaration(PropertyId.TextDecorationLine, v, important);
            }
            else if (IsColorLike(v))
                yield return new PropertyDeclaration(PropertyId.TextDecorationColor, v, important);
            else if (v is CssLength or CssPercentage)
                yield return new PropertyDeclaration(PropertyId.TextDecorationThickness, v, important);
        }
    }

    private static IEnumerable<PropertyDeclaration> ExpandWhiteSpace(List<CssValue> values, bool important)
    {
        // CSS Text 4 §3 maps the legacy `white-space` keyword onto the
        // white-space-collapse + text-wrap longhands. We emit those longhands
        // *and* keep the `white-space` longhand itself so layout's legacy
        // fast-path and DevTools-style serialization both keep working.
        var keyword = values[0] is CssKeyword k ? k.Name.ToLowerInvariant() : "normal";

        // Pass values like `normal` / global keywords (inherit/initial/unset)
        // through to the white-space longhand untouched.
        yield return new PropertyDeclaration(PropertyId.WhiteSpace, values[0], important);

        (string collapse, string wrap)? mapped = keyword switch
        {
            "normal" => ("collapse", "wrap"),
            "nowrap" => ("collapse", "nowrap"),
            "pre" => ("preserve", "nowrap"),
            "pre-wrap" => ("preserve", "wrap"),
            "pre-line" => ("preserve-breaks", "wrap"),
            _ => null,
        };
        if (mapped is { } m)
        {
            yield return new PropertyDeclaration(PropertyId.WhiteSpaceCollapse, new CssKeyword(m.collapse), important);
            yield return new PropertyDeclaration(PropertyId.TextWrap, new CssKeyword(m.wrap), important);
        }
    }

    private static IEnumerable<PropertyDeclaration> ExpandTransition(List<CssValue> values, bool important)
    {
        // Simplified single-layer transition shorthand.
        var sawDuration = false;
        foreach (var v in values)
        {
            if (v is CssTime || v is CssDimension { Unit: "s" or "ms" })
            {
                if (!sawDuration)
                {
                    yield return new PropertyDeclaration(PropertyId.TransitionDuration, v, important);
                    sawDuration = true;
                }
                else
                    yield return new PropertyDeclaration(PropertyId.TransitionDelay, v, important);
            }
            else if (v is CssKeyword k && IsTimingFunctionKeyword(k.Name))
                yield return new PropertyDeclaration(PropertyId.TransitionTimingFunction, v, important);
            else if (v is CssFunctionValue f && IsTimingFunctionName(f.Name))
                yield return new PropertyDeclaration(PropertyId.TransitionTimingFunction, v, important);
            else if (v is CssKeyword propKw)
                yield return new PropertyDeclaration(PropertyId.TransitionProperty, propKw, important);
        }
    }

    private static IEnumerable<PropertyDeclaration> ExpandAnimation(List<CssValue> values, bool important)
    {
        // Split on top-level commas. The value parser emits empty-name keywords
        // for separator commas (verified by probe). Each segment is one layer
        // per CSS Animations 1 §4.1.
        var layers = SplitTopLevelCommas(values);

        // Collect per-layer values for each Animation* longhand.
        var names = new List<CssValue>();
        var durations = new List<CssValue>();
        var delays = new List<CssValue>();
        var timings = new List<CssValue>();
        var iterations = new List<CssValue>();
        var directions = new List<CssValue>();
        var fills = new List<CssValue>();
        var playStates = new List<CssValue>();

        foreach (var layer in layers)
        {
            string? name = null;
            CssValue? duration = null, delay = null, timing = null, iteration = null;
            CssValue? direction = null, fill = null, playState = null;

            foreach (var v in layer)
            {
                if (v is CssTime || v is CssDimension { Unit: "s" or "ms" })
                {
                    if (duration is null) duration = v;
                    else delay ??= v;
                }
                else if (v is CssNumber)
                {
                    iteration ??= v;
                }
                else if (v is CssKeyword k)
                {
                    if (k.Name is "infinite")
                        iteration ??= v;
                    else if (IsTimingFunctionKeyword(k.Name))
                        timing ??= v;
                    else if (k.Name is "normal" or "reverse" or "alternate" or "alternate-reverse")
                        direction ??= v;
                    else if (k.Name is "forwards" or "backwards" or "both")
                        fill ??= v;
                    else if (k.Name is "none")
                    {
                        // Ambiguous: name "none" or fill-mode "none". Per spec
                        // §4.1 the first encountered keyword that fits an
                        // un-set slot wins; prefer fill-mode if not yet set,
                        // else name.
                        if (fill is null) fill = v;
                        else name ??= k.Name;
                    }
                    else if (k.Name is "running" or "paused")
                        playState ??= v;
                    else
                        name ??= k.Name;
                }
                else if (v is CssFunctionValue f && IsTimingFunctionName(f.Name))
                {
                    timing ??= v;
                }
            }

            names.Add(new CssKeyword(name ?? "none"));
            durations.Add(duration ?? new CssTime(0, CssTimeUnit.Seconds));
            delays.Add(delay ?? new CssTime(0, CssTimeUnit.Seconds));
            timings.Add(timing ?? new CssKeyword("ease"));
            iterations.Add(iteration ?? new CssNumber(1));
            directions.Add(direction ?? new CssKeyword("normal"));
            fills.Add(fill ?? new CssKeyword("none"));
            playStates.Add(playState ?? new CssKeyword("running"));
        }

        if (names.Count == 0)
            yield break;

        yield return Emit(PropertyId.AnimationName, names, important);
        yield return Emit(PropertyId.AnimationDuration, durations, important);
        yield return Emit(PropertyId.AnimationDelay, delays, important);
        yield return Emit(PropertyId.AnimationTimingFunction, timings, important);
        yield return Emit(PropertyId.AnimationIterationCount, iterations, important);
        yield return Emit(PropertyId.AnimationDirection, directions, important);
        yield return Emit(PropertyId.AnimationFillMode, fills, important);
        yield return Emit(PropertyId.AnimationPlayState, playStates, important);

        static PropertyDeclaration Emit(PropertyId id, List<CssValue> layers, bool important)
            => new(id, layers.Count == 1 ? layers[0] : new CssValueList(layers), important);
    }

    private static List<List<CssValue>> SplitTopLevelCommas(List<CssValue> values)
    {
        var layers = new List<List<CssValue>>();
        var current = new List<CssValue>();
        foreach (var v in values)
        {
            if (v is CssKeyword { Name: "" })
            {
                layers.Add(current);
                current = new List<CssValue>();
            }
            else
            {
                current.Add(v);
            }
        }
        layers.Add(current);
        // Drop fully empty trailing layers (e.g. trailing comma with no
        // values after it) but keep empty intermediates so the layer index
        // matches author intent.
        if (layers.Count > 0 && layers[^1].Count == 0)
            layers.RemoveAt(layers.Count - 1);
        return layers;
    }

    private static bool IsTimingFunctionKeyword(string name)
        => name is "linear" or "ease" or "ease-in" or "ease-out" or "ease-in-out" or "step-start" or "step-end";

    private static bool IsTimingFunctionName(string name)
        => name is "cubic-bezier" or "steps" or "linear";

    /// <summary>
    /// Map from shorthand property name to the set of longhand properties it
    /// resets. Used to seed pending-substitution values when a shorthand
    /// contains a <c>var()</c> reference (CSS Variables L1 §3.7). Per spec the
    /// shorthand resets every mapped longhand; longhands that the resolved
    /// shorthand doesn't explicitly populate fall back to their initial value
    /// at computed time.
    /// </summary>
    private static readonly Dictionary<string, PropertyId[]> ShorthandLonghands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["margin"] = [PropertyId.MarginTop, PropertyId.MarginRight, PropertyId.MarginBottom, PropertyId.MarginLeft],
        ["padding"] = [PropertyId.PaddingTop, PropertyId.PaddingRight, PropertyId.PaddingBottom, PropertyId.PaddingLeft],
        ["border-width"] = [PropertyId.BorderTopWidth, PropertyId.BorderRightWidth, PropertyId.BorderBottomWidth, PropertyId.BorderLeftWidth],
        ["border-style"] = [PropertyId.BorderTopStyle, PropertyId.BorderRightStyle, PropertyId.BorderBottomStyle, PropertyId.BorderLeftStyle],
        ["border-color"] = [PropertyId.BorderTopColor, PropertyId.BorderRightColor, PropertyId.BorderBottomColor, PropertyId.BorderLeftColor],
        ["border-radius"] = [PropertyId.BorderTopLeftRadius, PropertyId.BorderTopRightRadius, PropertyId.BorderBottomRightRadius, PropertyId.BorderBottomLeftRadius],
        ["overflow"] = [PropertyId.OverflowX, PropertyId.OverflowY],
        ["background"] = [PropertyId.BackgroundColor, PropertyId.BackgroundImage, PropertyId.BackgroundPosition, PropertyId.BackgroundSize, PropertyId.BackgroundRepeat],
        ["border"] =
        [
            PropertyId.BorderTopWidth, PropertyId.BorderRightWidth, PropertyId.BorderBottomWidth, PropertyId.BorderLeftWidth,
            PropertyId.BorderTopStyle, PropertyId.BorderRightStyle, PropertyId.BorderBottomStyle, PropertyId.BorderLeftStyle,
            PropertyId.BorderTopColor, PropertyId.BorderRightColor, PropertyId.BorderBottomColor, PropertyId.BorderLeftColor,
        ],
        ["flex"] = [PropertyId.FlexGrow, PropertyId.FlexShrink, PropertyId.FlexBasis],
        ["flex-flow"] = [PropertyId.FlexDirection, PropertyId.FlexWrap],
        ["gap"] = [PropertyId.RowGap, PropertyId.ColumnGap],
        ["grid-gap"] = [PropertyId.RowGap, PropertyId.ColumnGap],
        ["grid-row-gap"] = [PropertyId.RowGap],
        ["grid-column-gap"] = [PropertyId.ColumnGap],
        ["grid-template"] = [PropertyId.GridTemplateRows, PropertyId.GridTemplateColumns, PropertyId.GridTemplateAreas],
        ["grid"] = [PropertyId.GridTemplateRows, PropertyId.GridTemplateColumns, PropertyId.GridTemplateAreas],
        ["grid-column"] = [PropertyId.GridColumnStart, PropertyId.GridColumnEnd],
        ["grid-row"] = [PropertyId.GridRowStart, PropertyId.GridRowEnd],
        ["grid-area"] = [PropertyId.GridRowStart, PropertyId.GridColumnStart, PropertyId.GridRowEnd, PropertyId.GridColumnEnd],
        ["place-items"] = [PropertyId.AlignItems, PropertyId.JustifyItems],
        ["place-content"] = [PropertyId.AlignContent, PropertyId.JustifyContent],
        ["place-self"] = [PropertyId.AlignSelf, PropertyId.JustifySelf],
        ["margin-inline"] = [PropertyId.MarginInlineStart, PropertyId.MarginInlineEnd],
        ["margin-block"] = [PropertyId.MarginBlockStart, PropertyId.MarginBlockEnd],
        ["padding-inline"] = [PropertyId.PaddingInlineStart, PropertyId.PaddingInlineEnd],
        ["padding-block"] = [PropertyId.PaddingBlockStart, PropertyId.PaddingBlockEnd],
        ["inset"] = [PropertyId.Top, PropertyId.Right, PropertyId.Bottom, PropertyId.Left],
        ["inset-inline"] = [PropertyId.InsetInlineStart, PropertyId.InsetInlineEnd],
        ["inset-block"] = [PropertyId.InsetBlockStart, PropertyId.InsetBlockEnd],
        ["border-inline-width"] = [PropertyId.BorderInlineStartWidth, PropertyId.BorderInlineEndWidth],
        ["border-inline-style"] = [PropertyId.BorderInlineStartStyle, PropertyId.BorderInlineEndStyle],
        ["border-inline-color"] = [PropertyId.BorderInlineStartColor, PropertyId.BorderInlineEndColor],
        ["border-block-width"] = [PropertyId.BorderBlockStartWidth, PropertyId.BorderBlockEndWidth],
        ["border-block-style"] = [PropertyId.BorderBlockStartStyle, PropertyId.BorderBlockEndStyle],
        ["border-block-color"] = [PropertyId.BorderBlockStartColor, PropertyId.BorderBlockEndColor],
        ["border-inline-start"] = [PropertyId.BorderInlineStartWidth, PropertyId.BorderInlineStartStyle, PropertyId.BorderInlineStartColor],
        ["border-inline-end"] = [PropertyId.BorderInlineEndWidth, PropertyId.BorderInlineEndStyle, PropertyId.BorderInlineEndColor],
        ["border-block-start"] = [PropertyId.BorderBlockStartWidth, PropertyId.BorderBlockStartStyle, PropertyId.BorderBlockStartColor],
        ["border-block-end"] = [PropertyId.BorderBlockEndWidth, PropertyId.BorderBlockEndStyle, PropertyId.BorderBlockEndColor],
        ["border-inline"] =
        [
            PropertyId.BorderInlineStartWidth, PropertyId.BorderInlineStartStyle, PropertyId.BorderInlineStartColor,
            PropertyId.BorderInlineEndWidth, PropertyId.BorderInlineEndStyle, PropertyId.BorderInlineEndColor,
        ],
        ["border-block"] =
        [
            PropertyId.BorderBlockStartWidth, PropertyId.BorderBlockStartStyle, PropertyId.BorderBlockStartColor,
            PropertyId.BorderBlockEndWidth, PropertyId.BorderBlockEndStyle, PropertyId.BorderBlockEndColor,
        ],
        ["border-top"] = [PropertyId.BorderTopWidth, PropertyId.BorderTopStyle, PropertyId.BorderTopColor],
        ["border-right"] = [PropertyId.BorderRightWidth, PropertyId.BorderRightStyle, PropertyId.BorderRightColor],
        ["border-bottom"] = [PropertyId.BorderBottomWidth, PropertyId.BorderBottomStyle, PropertyId.BorderBottomColor],
        ["border-left"] = [PropertyId.BorderLeftWidth, PropertyId.BorderLeftStyle, PropertyId.BorderLeftColor],
        ["scroll-margin"] = [PropertyId.ScrollMarginTop, PropertyId.ScrollMarginRight, PropertyId.ScrollMarginBottom, PropertyId.ScrollMarginLeft],
        ["scroll-padding"] = [PropertyId.ScrollPaddingTop, PropertyId.ScrollPaddingRight, PropertyId.ScrollPaddingBottom, PropertyId.ScrollPaddingLeft],
        ["overscroll-behavior"] = [PropertyId.OverscrollBehaviorX, PropertyId.OverscrollBehaviorY],
        ["text-decoration"] = [PropertyId.TextDecorationLine, PropertyId.TextDecorationStyle, PropertyId.TextDecorationColor, PropertyId.TextDecorationThickness],
        ["transition"] = [PropertyId.TransitionProperty, PropertyId.TransitionDuration, PropertyId.TransitionTimingFunction, PropertyId.TransitionDelay],
        ["animation"] =
        [
            PropertyId.AnimationName, PropertyId.AnimationDuration, PropertyId.AnimationTimingFunction,
            PropertyId.AnimationDelay, PropertyId.AnimationIterationCount, PropertyId.AnimationDirection,
            PropertyId.AnimationFillMode, PropertyId.AnimationPlayState,
        ],
    };

    private static bool ContainsVarReference(CssValue value)
        => value switch
        {
            CssVarReference => true,
            CssValueList list => list.Values.Any(ContainsVarReference),
            CssFunctionValue fn => fn.Arguments.Any(ContainsVarReference),
            _ => false,
        };

    private static bool IsColorLike(CssValue value)
        => value is CssColor or CssKeyword { Name: "currentColor" or "transparent" } or CssFunctionValue { Name: "rgb" or "rgba" or "hsl" or "hsla" or "hwb" or "lab" or "lch" or "oklab" or "oklch" or "color" };

    private static bool IsBorderStyle(CssValue value)
        => value is CssKeyword { Name: "none" or "hidden" or "dotted" or "dashed" or "solid" or "double" or "groove" or "ridge" or "inset" or "outset" };

    private static string ToCssName(PropertyId id)
    {
        var name = id.ToString();
        var chars = new List<char>(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                chars.Add('-');
            chars.Add(char.ToLowerInvariant(name[i]));
        }

        return new string(chars.ToArray());
    }
}

public sealed record PropertyDeclaration(PropertyId Id, CssValue Value, bool Important);

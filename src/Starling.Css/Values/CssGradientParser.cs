namespace Starling.Css.Values;

/// <summary>
/// Parses the CSS Images 3 <c>&lt;gradient&gt;</c> functions from the generic
/// <see cref="CssFunctionValue"/> produced by <see cref="CssValueParser"/> into
/// a typed <see cref="CssGradient"/>. Mirrors <see cref="CssTransformParser"/>:
/// fail-soft — any unrecognised syntax returns <c>null</c> so the caller leaves
/// the property unset (CSS Images 3 §3, invalid gradient → not painted).
/// <para>
/// <see cref="CssValueParser"/> splits gradient arguments on top-level commas,
/// so each entry is either a single value (e.g. <c>red</c>, <c>90deg</c>) or a
/// <see cref="CssValueList"/> of whitespace-separated values (e.g.
/// <c>red 10%</c>, <c>to top right</c>, <c>circle at center</c>).
/// </para>
/// </summary>
public static class CssGradientParser
{
    public static bool TryParse(CssValue value, out CssGradient gradient)
    {
        gradient = null!;
        if (value is not CssFunctionValue fn)
            return false;
        return TryParseFunction(fn, out gradient);
    }

    public static bool TryParseFunction(CssFunctionValue fn, out CssGradient gradient)
    {
        gradient = null!;
        var name = fn.Name.ToLowerInvariant();
        var repeating = name.StartsWith("repeating-", StringComparison.Ordinal);
        var bare = repeating ? name["repeating-".Length..] : name;

        return bare switch
        {
            "linear-gradient" => TryParseLinear(fn.Arguments, repeating, out gradient),
            "radial-gradient" => TryParseRadial(fn.Arguments, repeating, out gradient),
            "conic-gradient" => TryParseConic(fn.Arguments, repeating, out gradient),
            _ => false,
        };
    }

    private static bool TryParseLinear(IReadOnlyList<CssValue> args, bool repeating, out CssGradient gradient)
    {
        gradient = null!;
        if (args.Count == 0)
            return false;

        CssGradientLine? line = null;
        var stopStart = 0;

        // First argument may be the gradient line: an <angle> or `to <side>`.
        if (TryParseLine(args[0], out var parsedLine))
        {
            line = parsedLine;
            stopStart = 1;
        }

        if (!TryParseStops(args, stopStart, out var stops))
            return false;

        gradient = new CssGradient(CssGradientKind.Linear, repeating, stops, Line: line);
        return true;
    }

    private static bool TryParseRadial(IReadOnlyList<CssValue> args, bool repeating, out CssGradient gradient)
    {
        gradient = null!;
        if (args.Count == 0)
            return false;

        var shape = CssRadialShape.Ellipse;
        var size = CssRadialSize.FarthestCorner;
        CssGradientPosition? position = null;
        var stopStart = 0;

        // First argument may describe the ending shape: `[<shape> || <size>] [at <position>]`.
        if (TryParseRadialPrelude(args[0], ref shape, ref size, ref position))
            stopStart = 1;

        if (!TryParseStops(args, stopStart, out var stops))
            return false;

        gradient = new CssGradient(CssGradientKind.Radial, repeating, stops, Shape: shape, Size: size, Position: position);
        return true;
    }

    private static bool TryParseConic(IReadOnlyList<CssValue> args, bool repeating, out CssGradient gradient)
    {
        gradient = null!;
        // We model conic so the value round-trips, but it has no paintable brush.
        // Skip any `from <angle> at <position>` prelude tokens that are not colors.
        var stopStart = 0;
        if (args.Count > 0 && !LooksLikeColorStop(args[0]))
            stopStart = 1;
        if (!TryParseStops(args, stopStart, out var stops))
            return false;
        gradient = new CssGradient(CssGradientKind.Conic, repeating, stops);
        return true;
    }

    // --- gradient line ---

    private static bool TryParseLine(CssValue value, out CssGradientLine line)
    {
        line = null!;
        switch (value)
        {
            case CssAngle angle:
                line = CssGradientLine.FromAngle(angle.InDegrees);
                return true;
            case CssNumber n when n.Value == 0:
                line = CssGradientLine.FromAngle(0);
                return true;
            case CssValueList list when IsToSide(list.Values, out var sx, out var sy):
                line = CssGradientLine.FromSide(sx, sy);
                return true;
            default:
                return false;
        }
    }

    private static bool IsToSide(IReadOnlyList<CssValue> values, out CssGradientSideX sx, out CssGradientSideY sy)
    {
        sx = CssGradientSideX.None;
        sy = CssGradientSideY.None;
        if (values.Count < 2)
            return false;
        if (values[0] is not CssKeyword { Name: "to" })
            return false;

        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] is not CssKeyword kw)
                return false;
            switch (kw.Name)
            {
                case "left": sx = CssGradientSideX.Left; break;
                case "right": sx = CssGradientSideX.Right; break;
                case "top": sy = CssGradientSideY.Top; break;
                case "bottom": sy = CssGradientSideY.Bottom; break;
                default: return false;
            }
        }
        return sx != CssGradientSideX.None || sy != CssGradientSideY.None;
    }

    // --- radial prelude: shape / size / position ---

    private static bool TryParseRadialPrelude(CssValue value, ref CssRadialShape shape, ref CssRadialSize size, ref CssGradientPosition? position)
    {
        var tokens = value switch
        {
            CssValueList list => list.Values,
            _ => new[] { value },
        };

        // If the first token already looks like a color stop, this isn't a prelude.
        if (LooksLikeColorStop(value))
            return false;

        var matchedAny = false;
        var i = 0;
        while (i < tokens.Count)
        {
            var t = tokens[i];
            if (t is CssKeyword kw)
            {
                switch (kw.Name)
                {
                    case "circle": shape = CssRadialShape.Circle; matchedAny = true; i++; continue;
                    case "ellipse": shape = CssRadialShape.Ellipse; matchedAny = true; i++; continue;
                    case "closest-side": size = CssRadialSize.ClosestSide; matchedAny = true; i++; continue;
                    case "closest-corner": size = CssRadialSize.ClosestCorner; matchedAny = true; i++; continue;
                    case "farthest-side": size = CssRadialSize.FarthestSide; matchedAny = true; i++; continue;
                    case "farthest-corner": size = CssRadialSize.FarthestCorner; matchedAny = true; i++; continue;
                    case "at":
                        // Remaining tokens form the position.
                        if (TryParsePosition(tokens, i + 1, out var pos))
                        {
                            position = pos;
                            matchedAny = true;
                        }
                        return matchedAny;
                    default:
                        // Unknown keyword in prelude position — treat as not-a-prelude.
                        return matchedAny;
                }
            }

            // Explicit radius lengths (e.g. `100px 50px`) are accepted as a
            // prelude marker but not yet honored for sizing; treat as ellipse.
            if (t is CssLength or CssPercentage)
            {
                matchedAny = true;
                i++;
                continue;
            }

            return matchedAny;
        }
        return matchedAny;
    }

    private static bool TryParsePosition(IReadOnlyList<CssValue> tokens, int start, out CssGradientPosition position)
    {
        position = CssGradientPosition.Center;
        double? x = null, y = null;
        for (var i = start; i < tokens.Count; i++)
        {
            switch (tokens[i])
            {
                case CssKeyword { Name: "left" }: x = 0; break;
                case CssKeyword { Name: "right" }: x = 1; break;
                case CssKeyword { Name: "top" }: y = 0; break;
                case CssKeyword { Name: "bottom" }: y = 1; break;
                case CssKeyword { Name: "center" }:
                    if (x is null) x = 0.5; else y = 0.5;
                    break;
                case CssPercentage pct:
                    if (x is null) x = pct.Value / 100.0; else y = pct.Value / 100.0;
                    break;
                default:
                    return false;
            }
        }
        position = new CssGradientPosition(x ?? 0.5, y ?? 0.5);
        return true;
    }

    // --- color stops ---

    private static bool TryParseStops(IReadOnlyList<CssValue> args, int start, out IReadOnlyList<CssColorStop> stops)
    {
        var result = new List<CssColorStop>(Math.Max(0, args.Count - start));
        for (var i = start; i < args.Count; i++)
        {
            if (!TryParseStop(args[i], result))
                return Fail(out stops);
        }

        if (result.Count < 2)
            return Fail(out stops);

        stops = result;
        return true;

        static bool Fail(out IReadOnlyList<CssColorStop> s)
        {
            s = [];
            return false;
        }
    }

    private static bool TryParseStop(CssValue arg, List<CssColorStop> into)
    {
        switch (arg)
        {
            case CssColor color:
                into.Add(new CssColorStop(color));
                return true;
            case CssValueList list:
                {
                    CssColor? color = null;
                    CssGradientStopPosition? firstPos = null;
                    CssGradientStopPosition? secondPos = null;
                    foreach (var item in list.Values)
                    {
                        switch (item)
                        {
                            case CssColor c when color is null:
                                color = c;
                                break;
                            case CssLength or CssPercentage when TryStopPosition(item, out var p):
                                if (firstPos is null) firstPos = p; else secondPos = p;
                                break;
                            case CssLength:
                                // Relative-unit position we can't resolve at parse
                                // time — keep the stop but drop the position (auto).
                                break;
                            default:
                                return false;
                        }
                    }
                    if (color is null)
                        return false;
                    // CSS Images 4 two-position color-stop shorthand: `red 10% 40%`
                    // expands to two stops with the same color.
                    into.Add(new CssColorStop(color, firstPos));
                    if (secondPos is not null)
                        into.Add(new CssColorStop(color, secondPos));
                    return true;
                }
            default:
                return false;
        }
    }

    private static bool TryStopPosition(CssValue value, out CssGradientStopPosition position)
    {
        switch (value)
        {
            case CssPercentage pct:
                position = new CssGradientStopPosition(pct.Value, IsPercent: true);
                return true;
            case CssLength len when len.Unit.IsAbsolute():
                position = new CssGradientStopPosition(len.Unit.AbsoluteToPx(len.Value), IsPercent: false);
                return true;
            case CssLength:
                // Relative units need a resolution context we don't have here;
                // treat as unset (auto) rather than rejecting the whole gradient.
                position = default;
                return false;
            default:
                position = default;
                return false;
        }
    }

    private static bool LooksLikeColorStop(CssValue value)
        => value switch
        {
            CssColor => true,
            CssValueList list => list.Values.Count > 0 && list.Values[0] is CssColor,
            _ => false,
        };
}

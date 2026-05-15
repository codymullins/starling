using Tessera.Css.Media;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Selectors;
using Tessera.Css.Tokenizer;
using Tessera.Css.UserAgent;
using Tessera.Css.Values;
using Tessera.Dom;

namespace Tessera.Css.Cascade;

public sealed class StyleEngine
{
    private readonly List<StyleSheet> _sheets = [];
    private readonly Dictionary<StyleOrigin, LayerOrder> _layerOrders = new();
    private MediaContext _mediaContext = MediaContext.Default;

    public StyleEngine(bool includeUserAgentStyleSheet = true)
    {
        foreach (var origin in Enum.GetValues<StyleOrigin>())
            _layerOrders[origin] = new LayerOrder();

        if (includeUserAgentStyleSheet)
            AddStyleSheet(UaStyleSheet.Parse());
    }

    public MediaContext MediaContext
    {
        get => _mediaContext;
        set => _mediaContext = value ?? MediaContext.Default;
    }

    public void AddStyleSheet(StyleSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _sheets.Add(sheet);
        RegisterLayers(sheet.Rules, sheet.Origin, currentPath: null);
    }

    public void RemoveStyleSheet(StyleSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _sheets.Remove(sheet);
    }

    public bool MatchMedia(string query) => MatchMedia(query, _mediaContext);

    public bool MatchMedia(string query, MediaContext ctx)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(ctx);
        // Reuse the full CSS parser so `(...)` becomes a CssSimpleBlock.
        var sheet = CssParser.ParseStyleSheet($"@media {query} {{ }}");
        var at = sheet.Rules.OfType<AtRule>().FirstOrDefault();
        if (at is null) return false;
        var list = MediaQueryParser.ParseList(at.Prelude);
        return MediaQueryEvaluator.Evaluate(list, ctx);
    }

    public IReadOnlyDictionary<string, int> GetLayersForOrigin(StyleOrigin origin)
        => _layerOrders[origin].AllLayers;

    public ComputedStyle Compute(Element element)
        => Compute(element, context: null);

    /// <summary>
    /// Compute styles for <paramref name="element"/>, optionally honouring an
    /// interactive <see cref="SelectorMatchContext"/> so <c>:hover</c>,
    /// <c>:focus</c>, and <c>:active</c> selectors fire. Interactive shells
    /// pass a context with <see cref="SelectorMatchContext.HoveredElement"/>
    /// (etc.) set, then ask the engine for an updated style to push to the
    /// affected view.
    /// </summary>
    public ComputedStyle Compute(Element element, SelectorMatchContext? context)
    {
        ArgumentNullException.ThrowIfNull(element);
        var parent = element.ParentNode as Element;
        var parentStyle = parent is null ? null : Compute(parent, context);
        return Compute(element, parentStyle, context);
    }

    public void Invalidate(Element root)
    {
        ArgumentNullException.ThrowIfNull(root);
    }

    private ComputedStyle Compute(Element element, ComputedStyle? parentStyle, SelectorMatchContext? context = null)
    {
        var allCandidates = new Dictionary<PropertyId, List<CascadedValue>>();
        var customCandidates = new Dictionary<string, List<CustomPropertyValue>>(StringComparer.Ordinal);
        var customProperties = parentStyle?.CustomProperties.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal) ?? new Dictionary<string, IReadOnlyList<CssComponentValue>>(StringComparer.Ordinal);
        var order = 0;

        foreach (var sheet in _sheets)
        {
            var layerOrder = _layerOrders[sheet.Origin];
            GatherFromRules(
                sheet.Rules,
                sheet.Origin,
                element,
                allCandidates,
                customCandidates,
                context,
                ref order,
                layerOrder,
                currentLayerPath: null,
                parentSelectors: null);
        }

        var inlineStyle = element.GetAttribute("style");
        if (!string.IsNullOrWhiteSpace(inlineStyle))
        {
            var parser = new CssParser(inlineStyle);
            var declarations = parser.ParseDeclarationList();
            AddDeclarations(
                declarations,
                StyleOrigin.Author,
                inline: true,
                new Specificity(1, 0, 0),
                allCandidates,
                customCandidates,
                ref order,
                layerIndex: LayerOrder.UnlayeredIndex);
        }

        // Pick winners for each property.
        var winners = new Dictionary<PropertyId, CascadedValue>();
        foreach (var kvp in allCandidates)
        {
            CascadedValue? best = null;
            foreach (var cand in kvp.Value)
                if (best is null || cand.IsStrongerThan(best))
                    best = cand;
            if (best is not null)
                winners[kvp.Key] = best;
        }
        var customWinners = new Dictionary<string, CustomPropertyValue>(StringComparer.Ordinal);
        foreach (var kvp in customCandidates)
        {
            CustomPropertyValue? best = null;
            foreach (var cand in kvp.Value)
                if (best is null || cand.IsStrongerThan(best))
                    best = cand;
            if (best is not null)
                customWinners[kvp.Key] = best;
        }

        foreach (var pair in customWinners)
            customProperties[pair.Key] = pair.Value.Value;

        var values = new Dictionary<PropertyId, CssValue>();
        foreach (var property in PropertyRegistry.All)
        {
            CssValue value;
            if (winners.TryGetValue(property, out var cascaded))
                value = ResolveSpecialKeywords(cascaded, property, parentStyle, customProperties, allCandidates);
            else if (PropertyRegistry.Inherits(property) && parentStyle is not null)
                value = parentStyle.Get(property);
            else
                value = PropertyRegistry.InitialValue(property);

            values[property] = ResolveVariables(value, customProperties);
        }

        ResolveFontRelativeLengths(values, parentStyle);

        return new ComputedStyle(values, customProperties);
    }

    /// <summary>
    /// Resolve <c>em</c>/<c>rem</c> lengths to absolute <c>px</c> at computed-value
    /// time, per CSS Values §5. <c>font-size</c> is resolved first — <c>em</c> on it
    /// is relative to the <em>parent's</em> font-size — and every other property's
    /// <c>em</c> then resolves against <em>this</em> element's font-size. Without
    /// this, layout would treat every <c>em</c> as a flat 16px, so e.g. an
    /// <c>h1 { font-size: 2em; margin: 0.67em 0 }</c> would get a 10.7px margin
    /// instead of the correct 21.4px.
    /// </summary>
    private static void ResolveFontRelativeLengths(
        Dictionary<PropertyId, CssValue> values,
        ComputedStyle? parentStyle)
    {
        var parentFontPx = parentStyle is not null
            ? AbsolutePx(parentStyle.Get(PropertyId.FontSize), emBasis: 16d)
            : 16d;

        var fontPx = ResolveFontSize(values[PropertyId.FontSize], parentFontPx);
        values[PropertyId.FontSize] = new CssLength(fontPx, CssLengthUnit.Px);

        foreach (var property in PropertyRegistry.All)
        {
            if (property == PropertyId.FontSize) continue;
            values[property] = ResolveEm(values[property], fontPx);
        }
    }

    private static double ResolveFontSize(CssValue value, double parentFontPx)
        => value switch
        {
            CssLength { Unit: CssLengthUnit.Em } len => len.Value * parentFontPx,
            CssLength { Unit: CssLengthUnit.Rem } len => len.Value * 16d,
            CssLength len => AbsolutePx(len, parentFontPx),
            CssPercentage pct => parentFontPx * pct.Value / 100d,
            CssNumber n => n.Value,
            _ => 16d,
        };

    private static CssValue ResolveEm(CssValue value, double fontPx)
        => value switch
        {
            CssLength { Unit: CssLengthUnit.Em } len => new CssLength(len.Value * fontPx, CssLengthUnit.Px),
            CssLength { Unit: CssLengthUnit.Rem } len => new CssLength(len.Value * 16d, CssLengthUnit.Px),
            CssValueList list => new CssValueList(list.Values.Select(v => ResolveEm(v, fontPx)).ToList()),
            _ => value,
        };

    /// <summary>Convert an absolute (or font-relative) length to px. Viewport- and
    /// glyph-relative units (vw/vh/ch/ex) are left to layout, which has the
    /// viewport.</summary>
    private static double AbsolutePx(CssValue value, double emBasis)
        => value switch
        {
            CssLength len => len.Unit switch
            {
                CssLengthUnit.Px => len.Value,
                CssLengthUnit.Pt => len.Value * 4d / 3d,
                CssLengthUnit.Pc => len.Value * 16d,
                CssLengthUnit.In => len.Value * 96d,
                CssLengthUnit.Cm => len.Value * 96d / 2.54d,
                CssLengthUnit.Mm => len.Value * 96d / 25.4d,
                CssLengthUnit.Q => len.Value * 96d / 101.6d,
                CssLengthUnit.Em => len.Value * emBasis,
                CssLengthUnit.Rem => len.Value * 16d,
                _ => len.Value,
            },
            CssNumber n => n.Value,
            _ => emBasis,
        };

    private void RegisterLayers(IReadOnlyList<CssRule> rules, StyleOrigin origin, string? currentPath)
    {
        foreach (var rule in rules)
        {
            if (rule is AtRule at && at.Name.Equals("layer", StringComparison.OrdinalIgnoreCase))
            {
                var paths = ParseLayerNamesFromPrelude(at.Prelude);
                if (at.Rules.Count == 0 && at.Declarations.Count == 0)
                {
                    // Statement form: `@layer a, b, c;` — just registers names.
                    foreach (var p in paths)
                        _layerOrders[origin].RegisterLayer(Combine(currentPath, p));
                }
                else if (paths.Count == 0)
                {
                    // Anonymous block — register an anonymous layer for ordering, recurse.
                    var anon = "__anon" + Guid.NewGuid().ToString("N");
                    var path = Combine(currentPath, anon);
                    _layerOrders[origin].RegisterLayer(path);
                    RegisterLayers(at.Rules, origin, path);
                }
                else
                {
                    var path = Combine(currentPath, paths[0]);
                    _layerOrders[origin].RegisterLayer(path);
                    RegisterLayers(at.Rules, origin, path);
                }
            }
            else if (rule is AtRule { Name: "media" or "supports" } container)
            {
                RegisterLayers(container.Rules, origin, currentPath);
            }
        }
    }

    private static List<string> ParseLayerNamesFromPrelude(IReadOnlyList<CssComponentValue> prelude)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        void Flush()
        {
            var s = current.ToString().Trim();
            if (s.Length > 0) result.Add(s);
            current.Clear();
        }
        foreach (var v in prelude)
        {
            if (v is CssTokenValue tv)
            {
                if (tv.Token.Type == CssTokenType.Comma) { Flush(); continue; }
                if (tv.Token.Type == CssTokenType.Whitespace) continue;
                if (tv.Token.Type == CssTokenType.Ident) current.Append(tv.Token.Value);
                else if (tv.Token.Type == CssTokenType.Delim && tv.Token.Delimiter == '.') current.Append('.');
            }
        }
        Flush();
        return result;
    }

    private static string Combine(string? parent, string child)
        => string.IsNullOrEmpty(parent) ? child : parent + "." + child;

    private void GatherFromRules(
        IReadOnlyList<CssRule> rules,
        StyleOrigin origin,
        Element element,
        Dictionary<PropertyId, List<CascadedValue>> candidates,
        Dictionary<string, List<CustomPropertyValue>> customCandidates,
        SelectorMatchContext? context,
        ref int order,
        LayerOrder layerOrder,
        string? currentLayerPath,
        SelectorList? parentSelectors)
    {
        foreach (var rule in rules)
        {
            switch (rule)
            {
                case StyleRule styleRule:
                    ProcessStyleRule(
                        styleRule,
                        origin,
                        element,
                        candidates,
                        customCandidates,
                        context,
                        ref order,
                        layerOrder,
                        currentLayerPath,
                        parentSelectors);
                    break;
                case AtRule { Name: var name } atRule when name.Equals("media", StringComparison.OrdinalIgnoreCase):
                    var mqList = MediaQueryParser.ParseList(atRule.Prelude);
                    if (MediaQueryEvaluator.Evaluate(mqList, _mediaContext))
                    {
                        GatherFromRules(
                            atRule.Rules,
                            origin,
                            element,
                            candidates,
                            customCandidates,
                            context,
                            ref order,
                            layerOrder,
                            currentLayerPath,
                            parentSelectors);
                    }
                    break;
                case AtRule { Name: var name2 } atRule2 when name2.Equals("supports", StringComparison.OrdinalIgnoreCase):
                    if (SupportsEvaluator.Evaluate(atRule2.Prelude))
                    {
                        GatherFromRules(
                            atRule2.Rules,
                            origin,
                            element,
                            candidates,
                            customCandidates,
                            context,
                            ref order,
                            layerOrder,
                            currentLayerPath,
                            parentSelectors);
                    }
                    break;
                case AtRule { Name: var name3 } atRule3 when name3.Equals("layer", StringComparison.OrdinalIgnoreCase):
                    var layerNames = ParseLayerNamesFromPrelude(atRule3.Prelude);
                    if (atRule3.Rules.Count == 0 && atRule3.Declarations.Count == 0)
                    {
                        // Statement form — no rules to gather.
                        break;
                    }
                    string layerPath;
                    if (layerNames.Count == 0)
                    {
                        // Anonymous — already registered an anon path; we cannot recover it exactly here,
                        // so pick a "fresh" unique anon for ordering purposes. We register and reuse the
                        // index by registering with a stable-per-rule key derived from the rule reference.
                        layerPath = Combine(currentLayerPath, "__anon" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(atRule3).ToString());
                        layerOrder.RegisterLayer(layerPath);
                    }
                    else
                    {
                        layerPath = Combine(currentLayerPath, layerNames[0]);
                    }
                    GatherFromRules(
                        atRule3.Rules,
                        origin,
                        element,
                        candidates,
                        customCandidates,
                        context,
                        ref order,
                        layerOrder,
                        layerPath,
                        parentSelectors);
                    break;
            }
        }
    }

    private void ProcessStyleRule(
        StyleRule styleRule,
        StyleOrigin origin,
        Element element,
        Dictionary<PropertyId, List<CascadedValue>> candidates,
        Dictionary<string, List<CustomPropertyValue>> customCandidates,
        SelectorMatchContext? context,
        ref int order,
        LayerOrder layerOrder,
        string? currentLayerPath,
        SelectorList? parentSelectors)
    {
        var effectiveSelectorList = ResolveSelectorList(styleRule.Prelude, parentSelectors);

        var layerIndex = layerOrder.GetIndex(currentLayerPath);
        foreach (var selector in effectiveSelectorList.Selectors)
        {
            if (!SelectorMatcher.Matches(selector, element, context))
                continue;
            AddDeclarations(
                styleRule.Declarations,
                origin,
                inline: false,
                selector.Specificity,
                candidates,
                customCandidates,
                ref order,
                layerIndex);
        }

        if (styleRule.NestedRulesOrEmpty.Count > 0)
        {
            GatherFromRules(
                styleRule.NestedRulesOrEmpty,
                origin,
                element,
                candidates,
                customCandidates,
                context,
                ref order,
                layerOrder,
                currentLayerPath,
                effectiveSelectorList);
        }
    }

    private static SelectorList ResolveSelectorList(
        IReadOnlyList<CssComponentValue> prelude,
        SelectorList? parentSelectors)
    {
        if (parentSelectors is null)
            return SelectorParser.ParseSelectorList(prelude);

        // CSS Nesting 1 §3: textually desugar `&` and implicit-`&` against parent selectors,
        // then reparse via SelectorParser.
        var rawText = ComponentValuesToText(prelude).Trim();
        var parentText = SelectorListToText(parentSelectors);
        // Split on top-level commas to handle `& .a, & .b` correctly.
        var pieces = SplitTopLevelCommas(rawText);
        var rebuilt = new System.Text.StringBuilder();
        for (var i = 0; i < pieces.Count; i++)
        {
            if (i > 0) rebuilt.Append(", ");
            var piece = pieces[i].Trim();
            if (piece.Contains('&', StringComparison.Ordinal))
            {
                rebuilt.Append(piece.Replace("&", $":is({parentText})", StringComparison.Ordinal));
            }
            else
            {
                rebuilt.Append($":is({parentText}) {piece}");
            }
        }
        return SelectorParser.ParseSelectorList(rebuilt.ToString());
    }

    private static List<string> SplitTopLevelCommas(string text)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        var depth = 0;
        foreach (var c in text)
        {
            if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']') depth = Math.Max(0, depth - 1);
            if (c == ',' && depth == 0)
            {
                result.Add(sb.ToString());
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }

    private static string ComponentValuesToText(IReadOnlyList<CssComponentValue> values)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var v in values) AppendComponentValueText(sb, v);
        return sb.ToString();
    }

    private static void AppendComponentValueText(System.Text.StringBuilder sb, CssComponentValue value)
    {
        switch (value)
        {
            case CssTokenValue tv:
                AppendTokenText(sb, tv.Token);
                break;
            case CssFunction fn:
                sb.Append(fn.Name).Append('(');
                foreach (var v in fn.Values) AppendComponentValueText(sb, v);
                sb.Append(')');
                break;
            case CssSimpleBlock block:
                sb.Append(block.StartToken switch
                {
                    CssTokenType.LeftParen => '(',
                    CssTokenType.LeftSquare => '[',
                    CssTokenType.LeftBrace => '{',
                    _ => '(',
                });
                foreach (var v in block.Values) AppendComponentValueText(sb, v);
                sb.Append(block.StartToken switch
                {
                    CssTokenType.LeftParen => ')',
                    CssTokenType.LeftSquare => ']',
                    CssTokenType.LeftBrace => '}',
                    _ => ')',
                });
                break;
        }
    }

    private static void AppendTokenText(System.Text.StringBuilder sb, CssToken token)
    {
        switch (token.Type)
        {
            case CssTokenType.Ident: sb.Append(token.Value); break;
            case CssTokenType.Hash: sb.Append('#').Append(token.Value); break;
            case CssTokenType.String: sb.Append('"').Append(token.Value).Append('"'); break;
            case CssTokenType.Number: sb.Append(token.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
            case CssTokenType.Percentage: sb.Append(token.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('%'); break;
            case CssTokenType.Dimension: sb.Append(token.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(token.Unit); break;
            case CssTokenType.Delim: sb.Append(token.Delimiter); break;
            case CssTokenType.Whitespace: sb.Append(' '); break;
            case CssTokenType.Colon: sb.Append(':'); break;
            case CssTokenType.Semicolon: sb.Append(';'); break;
            case CssTokenType.Comma: sb.Append(','); break;
            case CssTokenType.Url: sb.Append("url(").Append(token.Value).Append(')'); break;
        }
    }

    private static string SelectorListToText(SelectorList list)
        => string.Join(", ", list.Selectors.Select(ComplexSelectorToText));

    private static string ComplexSelectorToText(ComplexSelector selector)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < selector.Parts.Count; i++)
        {
            var part = selector.Parts[i];
            if (i > 0)
            {
                sb.Append(part.CombinatorFromPrevious switch
                {
                    SelectorCombinator.Descendant => " ",
                    SelectorCombinator.Child => " > ",
                    SelectorCombinator.NextSibling => " + ",
                    SelectorCombinator.SubsequentSibling => " ~ ",
                    _ => " ",
                });
            }
            sb.Append(CompoundToText(part.Compound));
        }
        return sb.ToString();
    }

    private static string CompoundToText(CompoundSelector compound)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var simple in compound.SimpleSelectors)
        {
            switch (simple)
            {
                case TypeSelector t: sb.Append(t.LocalName); break;
                case UniversalSelector: sb.Append('*'); break;
                case IdSelector i: sb.Append('#').Append(i.Id); break;
                case ClassSelector c: sb.Append('.').Append(c.ClassName); break;
                case AttributeSelector a:
                    sb.Append('[').Append(a.Name);
                    if (a.Operator != AttributeOperator.Exists)
                    {
                        sb.Append(a.Operator switch
                        {
                            AttributeOperator.Equals => "=",
                            AttributeOperator.Includes => "~=",
                            AttributeOperator.DashMatch => "|=",
                            AttributeOperator.Prefix => "^=",
                            AttributeOperator.Suffix => "$=",
                            AttributeOperator.Substring => "*=",
                            _ => "=",
                        });
                        sb.Append('"').Append(a.Value).Append('"');
                    }
                    sb.Append(']');
                    break;
                case PseudoClassSelector pc:
                    sb.Append(':').Append(pc.Name);
                    if (pc.Argument is SelectorList sl) sb.Append('(').Append(SelectorListToText(sl)).Append(')');
                    else if (pc.Argument is NthPattern np) sb.Append('(').Append(np.A).Append('n').Append('+').Append(np.B).Append(')');
                    else if (pc.Argument is string s) sb.Append('(').Append(s).Append(')');
                    break;
                case PseudoElementSelector pe:
                    sb.Append("::").Append(pe.Name);
                    break;
            }
        }
        return sb.ToString();
    }

    private static void AddDeclarations(
        IReadOnlyList<CssDeclaration> declarations,
        StyleOrigin origin,
        bool inline,
        Specificity specificity,
        Dictionary<PropertyId, List<CascadedValue>> candidates,
        Dictionary<string, List<CustomPropertyValue>> customCandidates,
        ref int order,
        int layerIndex)
    {
        foreach (var declaration in declarations)
        {
            var currentOrder = order++;
            if (declaration.Name.StartsWith("--", StringComparison.Ordinal))
            {
                var custom = new CustomPropertyValue(
                    declaration.Value,
                    declaration.Important,
                    origin,
                    inline,
                    specificity,
                    currentOrder,
                    layerIndex);
                if (!customCandidates.TryGetValue(declaration.Name, out var list))
                    customCandidates[declaration.Name] = list = new List<CustomPropertyValue>();
                list.Add(custom);
                continue;
            }

            // `all: <wide-keyword>` — expand to every property.
            if (declaration.Name.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                TryParseWideKeyword(declaration.Value, out var allKeyword))
            {
                foreach (var p in PropertyRegistry.All)
                {
                    var candidate = new CascadedValue(
                        new CssKeyword(allKeyword),
                        declaration.Important,
                        origin,
                        inline,
                        specificity,
                        currentOrder,
                        layerIndex);
                    if (!candidates.TryGetValue(p, out var list))
                        candidates[p] = list = new List<CascadedValue>();
                    list.Add(candidate);
                }
                continue;
            }

            foreach (var parsed in PropertyRegistry.Parse(declaration))
            {
                var candidate = new CascadedValue(
                    parsed.Value,
                    parsed.Important,
                    origin,
                    inline,
                    specificity,
                    currentOrder,
                    layerIndex);
                if (!candidates.TryGetValue(parsed.Id, out var list))
                    candidates[parsed.Id] = list = new List<CascadedValue>();
                list.Add(candidate);
            }
        }
    }

    private static bool TryParseWideKeyword(IReadOnlyList<CssComponentValue> values, out string keyword)
    {
        keyword = string.Empty;
        var first = values.FirstOrDefault(v => v is not CssTokenValue { Token.Type: CssTokenType.Whitespace });
        if (first is CssTokenValue { Token.Type: CssTokenType.Ident } tok)
        {
            var name = tok.Token.Value.ToLowerInvariant();
            if (name is "initial" or "inherit" or "unset" or "revert" or "revert-layer")
            {
                keyword = name;
                return true;
            }
        }
        return false;
    }

    private static CssValue ResolveSpecialKeywords(
        CascadedValue cascaded,
        PropertyId property,
        ComputedStyle? parentStyle,
        IReadOnlyDictionary<string, IReadOnlyList<CssComponentValue>> customProperties,
        Dictionary<PropertyId, List<CascadedValue>> allCandidates)
    {
        var value = ResolveVariables(cascaded.Value, customProperties);
        switch (value)
        {
            case CssKeyword { Name: "inherit" } when parentStyle is not null:
                return parentStyle.Get(property);
            case CssKeyword { Name: "initial" }:
                return PropertyRegistry.InitialValue(property);
            case CssKeyword { Name: "unset" } when PropertyRegistry.Inherits(property) && parentStyle is not null:
                return parentStyle.Get(property);
            case CssKeyword { Name: "unset" }:
                return PropertyRegistry.InitialValue(property);
            case CssKeyword { Name: "revert" }:
                return ResolveRevert(property, cascaded, parentStyle, allCandidates, sameOriginOnly: false);
            case CssKeyword { Name: "revert-layer" }:
                return ResolveRevert(property, cascaded, parentStyle, allCandidates, sameOriginOnly: true);
            default:
                return value;
        }
    }

    private static CssValue ResolveRevert(
        PropertyId property,
        CascadedValue current,
        ComputedStyle? parentStyle,
        Dictionary<PropertyId, List<CascadedValue>> allCandidates,
        bool sameOriginOnly)
    {
        if (!allCandidates.TryGetValue(property, out var list))
            return DefaultForProperty(property, parentStyle);

        CascadedValue? best = null;
        foreach (var cand in list)
        {
            if (sameOriginOnly)
            {
                // revert-layer: same origin, earlier layer (or different importance).
                if (cand.Origin != current.Origin) continue;
                if (cand.Important != current.Important) continue;
                // strictly weaker than current in cascade ordering.
                if (cand.IsStrongerThan(current) || cand.SameAs(current)) continue;
            }
            else
            {
                // revert: previous origin (lower origin rank than current).
                if (OriginRank(cand.Origin, cand.Important) >= OriginRank(current.Origin, current.Important)) continue;
            }
            if (best is null || cand.IsStrongerThan(best))
                best = cand;
        }
        if (best is null)
            return DefaultForProperty(property, parentStyle);
        var inner = ResolveSpecialKeywords(best, property, parentStyle, parentStyle?.CustomProperties ?? new Dictionary<string, IReadOnlyList<CssComponentValue>>(StringComparer.Ordinal), allCandidates);
        return inner;
    }

    private static CssValue DefaultForProperty(PropertyId property, ComputedStyle? parentStyle)
    {
        if (PropertyRegistry.Inherits(property) && parentStyle is not null)
            return parentStyle.Get(property);
        return PropertyRegistry.InitialValue(property);
    }

    private static CssValue ResolveVariables(
        CssValue value,
        IReadOnlyDictionary<string, IReadOnlyList<CssComponentValue>> customProperties)
        => value switch
        {
            CssVarReference var when customProperties.TryGetValue(var.Name, out var tokens) =>
                ResolveVariables(CssValueParser.Parse(tokens), customProperties),
            CssVarReference { Fallback: not null } var => ResolveVariables(var.Fallback, customProperties),
            CssValueList list => new CssValueList(list.Values.Select(v => ResolveVariables(v, customProperties)).ToList()),
            CssFunctionValue function => new CssFunctionValue(
                function.Name,
                function.Arguments.Select(v => ResolveVariables(v, customProperties)).ToList()),
            _ => value,
        };

    private sealed record CascadedValue(
        CssValue Value,
        bool Important,
        StyleOrigin Origin,
        bool Inline,
        Specificity Specificity,
        int Order,
        int LayerIndex)
    {
        public bool IsStrongerThan(CascadedValue other)
        {
            var origin = OriginRank(Origin, Important).CompareTo(OriginRank(other.Origin, other.Important));
            if (origin != 0) return origin > 0;
            if (Inline != other.Inline) return Inline;
            // Layer: per spec, layered styles are weaker than unlayered (non-important);
            // for !important the order is inverted.
            var layer = LayerOrder.Compare(LayerIndex, other.LayerIndex);
            if (layer != 0)
                return Important ? layer < 0 : layer > 0;
            var specificity = Specificity.CompareTo(other.Specificity);
            if (specificity != 0) return specificity > 0;
            return Order > other.Order;
        }

        public bool SameAs(CascadedValue other) => Order == other.Order;
    }

    private sealed record CustomPropertyValue(
        IReadOnlyList<CssComponentValue> Value,
        bool Important,
        StyleOrigin Origin,
        bool Inline,
        Specificity Specificity,
        int Order,
        int LayerIndex)
    {
        public bool IsStrongerThan(CustomPropertyValue other)
        {
            var origin = OriginRank(Origin, Important).CompareTo(OriginRank(other.Origin, other.Important));
            if (origin != 0) return origin > 0;
            if (Inline != other.Inline) return Inline;
            var layer = LayerOrder.Compare(LayerIndex, other.LayerIndex);
            if (layer != 0)
                return Important ? layer < 0 : layer > 0;
            var specificity = Specificity.CompareTo(other.Specificity);
            if (specificity != 0) return specificity > 0;
            return Order > other.Order;
        }
    }

    private static int OriginRank(StyleOrigin origin, bool important)
        => (origin, important) switch
        {
            (StyleOrigin.UserAgent, false) => 0,
            (StyleOrigin.User, false) => 1,
            (StyleOrigin.Author, false) => 2,
            (StyleOrigin.Author, true) => 3,
            (StyleOrigin.User, true) => 4,
            (StyleOrigin.UserAgent, true) => 5,
            _ => 0,
        };
}

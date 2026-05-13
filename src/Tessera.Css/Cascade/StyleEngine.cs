using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Selectors;
using Tessera.Css.UserAgent;
using Tessera.Css.Values;
using Tessera.Dom;

namespace Tessera.Css.Cascade;

public sealed class StyleEngine
{
    private readonly List<StyleSheet> _sheets = [];

    public StyleEngine(bool includeUserAgentStyleSheet = true)
    {
        if (includeUserAgentStyleSheet)
            AddStyleSheet(UaStyleSheet.Parse());
    }

    public void AddStyleSheet(StyleSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _sheets.Add(sheet);
    }

    public void RemoveStyleSheet(StyleSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _sheets.Remove(sheet);
    }

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
        var winners = new Dictionary<PropertyId, CascadedValue>();
        var customProperties = parentStyle?.CustomProperties.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal) ?? new Dictionary<string, IReadOnlyList<CssComponentValue>>(StringComparer.Ordinal);
        var customWinners = new Dictionary<string, CustomPropertyValue>(StringComparer.Ordinal);
        var order = 0;

        foreach (var sheet in _sheets)
            GatherFromRules(sheet.Rules, sheet.Origin, element, winners, customWinners, context, ref order);

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
                winners,
                customWinners,
                ref order);
        }

        foreach (var pair in customWinners)
            customProperties[pair.Key] = pair.Value.Value;

        var values = new Dictionary<PropertyId, CssValue>();
        foreach (var property in PropertyRegistry.All)
        {
            CssValue value;
            if (winners.TryGetValue(property, out var cascaded))
                value = ResolveSpecialKeywords(cascaded.Value, property, parentStyle, customProperties);
            else if (PropertyRegistry.Inherits(property) && parentStyle is not null)
                value = parentStyle.Get(property);
            else
                value = PropertyRegistry.InitialValue(property);

            values[property] = ResolveVariables(value, customProperties);
        }

        return new ComputedStyle(values, customProperties);
    }

    private static void GatherFromRules(
        IReadOnlyList<CssRule> rules,
        StyleOrigin origin,
        Element element,
        Dictionary<PropertyId, CascadedValue> winners,
        Dictionary<string, CustomPropertyValue> customWinners,
        SelectorMatchContext? context,
        ref int order)
    {
        foreach (var rule in rules)
        {
            switch (rule)
            {
                case StyleRule styleRule:
                    var selectorList = SelectorParser.ParseSelectorList(styleRule.Prelude);
                    foreach (var selector in selectorList.Selectors)
                    {
                        if (!SelectorMatcher.Matches(selector, element, context))
                            continue;
                        AddDeclarations(
                            styleRule.Declarations,
                            origin,
                            inline: false,
                            selector.Specificity,
                            winners,
                            customWinners,
                            ref order);
                    }
                    break;
                case AtRule { Name: "media" or "supports", Rules.Count: > 0 } atRule:
                    GatherFromRules(atRule.Rules, origin, element, winners, customWinners, context, ref order);
                    break;
            }
        }
    }

    private static void AddDeclarations(
        IReadOnlyList<CssDeclaration> declarations,
        StyleOrigin origin,
        bool inline,
        Specificity specificity,
        Dictionary<PropertyId, CascadedValue> winners,
        Dictionary<string, CustomPropertyValue> customWinners,
        ref int order)
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
                    currentOrder);
                if (!customWinners.TryGetValue(declaration.Name, out var oldCustom) ||
                    custom.IsStrongerThan(oldCustom))
                    customWinners[declaration.Name] = custom;
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
                    currentOrder);
                if (!winners.TryGetValue(parsed.Id, out var old) || candidate.IsStrongerThan(old))
                    winners[parsed.Id] = candidate;
            }
        }
    }

    private static CssValue ResolveSpecialKeywords(
        CssValue value,
        PropertyId property,
        ComputedStyle? parentStyle,
        IReadOnlyDictionary<string, IReadOnlyList<CssComponentValue>> customProperties)
    {
        value = ResolveVariables(value, customProperties);
        return value switch
        {
            CssKeyword { Name: "inherit" } when parentStyle is not null => parentStyle.Get(property),
            CssKeyword { Name: "initial" } => PropertyRegistry.InitialValue(property),
            CssKeyword { Name: "unset" } when PropertyRegistry.Inherits(property) && parentStyle is not null => parentStyle.Get(property),
            CssKeyword { Name: "unset" } => PropertyRegistry.InitialValue(property),
            _ => value,
        };
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
        int Order)
    {
        public bool IsStrongerThan(CascadedValue other)
        {
            var origin = OriginRank(Origin, Important).CompareTo(OriginRank(other.Origin, other.Important));
            if (origin != 0) return origin > 0;
            if (Inline != other.Inline) return Inline;
            var specificity = Specificity.CompareTo(other.Specificity);
            if (specificity != 0) return specificity > 0;
            return Order > other.Order;
        }
    }

    private sealed record CustomPropertyValue(
        IReadOnlyList<CssComponentValue> Value,
        bool Important,
        StyleOrigin Origin,
        bool Inline,
        Specificity Specificity,
        int Order)
    {
        public bool IsStrongerThan(CustomPropertyValue other)
        {
            var origin = OriginRank(Origin, Important).CompareTo(OriginRank(other.Origin, other.Important));
            if (origin != 0) return origin > 0;
            if (Inline != other.Inline) return Inline;
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

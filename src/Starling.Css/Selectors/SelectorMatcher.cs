using Tessera.Dom;

namespace Tessera.Css.Selectors;

public sealed class SelectorMatchContext
{
    public static SelectorMatchContext Default { get; } = new();

    public Element? HoveredElement { get; init; }
    public Element? ActiveElement { get; init; }
    public Element? FocusedElement { get; init; }
    public Element? TargetElement { get; init; }
    /// <summary>The :scope reference element (Selectors 4 §13.4). If null, the document element acts as scope.</summary>
    public Element? ScopeElement { get; init; }
    /// <summary>Target pseudo-element being matched against, or null for the originating element.</summary>
    public PseudoElement? PseudoElement { get; init; }
    /// <summary>Document URL for :local-link resolution.</summary>
    public Uri? DocumentUrl { get; init; }
    /// <summary>Set of href strings considered visited. Default privacy stance: empty/null.</summary>
    public IReadOnlySet<string>? VisitedHrefs { get; init; }

    internal int HasDepth { get; init; }

    internal SelectorMatchContext WithHasDepth(int depth) => new()
    {
        HoveredElement = HoveredElement,
        ActiveElement = ActiveElement,
        FocusedElement = FocusedElement,
        TargetElement = TargetElement,
        ScopeElement = ScopeElement,
        PseudoElement = PseudoElement,
        DocumentUrl = DocumentUrl,
        VisitedHrefs = VisitedHrefs,
        HasDepth = depth,
    };

    internal SelectorMatchContext WithScope(Element? scope) => new()
    {
        HoveredElement = HoveredElement,
        ActiveElement = ActiveElement,
        FocusedElement = FocusedElement,
        TargetElement = TargetElement,
        ScopeElement = scope,
        PseudoElement = PseudoElement,
        DocumentUrl = DocumentUrl,
        VisitedHrefs = VisitedHrefs,
        HasDepth = HasDepth,
    };
}

public readonly record struct SelectorMatchResult(bool Matched, PseudoElement? Pseudo)
{
    public static SelectorMatchResult NoMatch => new(false, null);
    public static SelectorMatchResult Element => new(true, null);
    public static SelectorMatchResult ForPseudo(PseudoElement? pseudo) => new(true, pseudo);
}

public static class SelectorMatcher
{
    private const int MaxHasDepth = 16;

    public static bool Matches(SelectorList selectorList, Element element, SelectorMatchContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(selectorList);
        ArgumentNullException.ThrowIfNull(element);
        context ??= SelectorMatchContext.Default;
        return selectorList.Selectors.Any(selector => Matches(selector, element, context));
    }

    public static bool Matches(ComplexSelector selector, Element element, SelectorMatchContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(element);
        context ??= SelectorMatchContext.Default;
        return MatchWithResult(selector, element, context).Matched;
    }

    /// <summary>Match and return the targeted pseudo-element (if any) for cascade plumbing.</summary>
    public static SelectorMatchResult MatchWithResult(
        ComplexSelector selector,
        Element element,
        SelectorMatchContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(element);
        context ??= SelectorMatchContext.Default;
        if (selector.Parts.Count == 0) return SelectorMatchResult.NoMatch;

        // Caller filters by pseudo-element kind: if the caller supplied a PseudoElement filter,
        // the selector must target the same one (or null = originating-element-only rules).
        var target = selector.TargetPseudoElement;
        if (context.PseudoElement is { } requested)
        {
            if (target != requested) return SelectorMatchResult.NoMatch;
        }
        else
        {
            if (target is not null) return SelectorMatchResult.NoMatch;
        }

        return MatchesFrom(selector, selector.Parts.Count - 1, element, context)
            ? SelectorMatchResult.ForPseudo(target)
            : SelectorMatchResult.NoMatch;
    }

    private static bool MatchesFrom(
        ComplexSelector selector,
        int partIndex,
        Element element,
        SelectorMatchContext context)
    {
        var part = selector.Parts[partIndex];
        if (!MatchesCompound(part.Compound, element, context))
            return false;
        if (partIndex == 0)
            return true;

        return part.CombinatorFromPrevious switch
        {
            SelectorCombinator.Child => ParentElement(element) is { } parent &&
                MatchesFrom(selector, partIndex - 1, parent, context),
            SelectorCombinator.Descendant => Ancestors(element)
                .Any(ancestor => MatchesFrom(selector, partIndex - 1, ancestor, context)),
            SelectorCombinator.NextSibling => PreviousElementSibling(element) is { } sibling &&
                MatchesFrom(selector, partIndex - 1, sibling, context),
            SelectorCombinator.SubsequentSibling => PreviousElementSiblings(element)
                .Any(sibling => MatchesFrom(selector, partIndex - 1, sibling, context)),
            SelectorCombinator.Column => false, // column combinator '||' stubbed (table-only)
            _ => false,
        };
    }

    public static bool Matches(CompoundSelector selector, Element element, SelectorMatchContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(element);
        context ??= SelectorMatchContext.Default;
        return MatchesCompound(selector, element, context);
    }

    private static bool MatchesCompound(CompoundSelector selector, Element element, SelectorMatchContext context)
    {
        foreach (var simple in selector.SimpleSelectors)
        {
            // Pseudo-element selectors are filtered at the ComplexSelector level via MatchWithResult.
            // They don't add a per-element constraint, so they pass here as long as they're terminal.
            if (simple is PseudoElementSelector)
                continue;
            if (!MatchesSimple(simple, element, context))
                return false;
        }
        return true;
    }

    private static bool MatchesSimple(SimpleSelector selector, Element element, SelectorMatchContext context)
        => selector switch
        {
            TypeSelector type => MatchesType(type, element),
            UniversalSelector universal => MatchesUniversal(universal, element),
            IdSelector id => element.Id.Equals(id.Id, StringComparison.Ordinal),
            ClassSelector @class => element.ClassList.Contains(@class.ClassName),
            AttributeSelector attribute => MatchesAttribute(attribute, element),
            PseudoClassSelector pseudo => MatchesPseudoClass(pseudo, element, context),
            PseudoElementSelector => true, // handled at complex-selector level
            _ => false,
        };

    private static bool MatchesType(TypeSelector selector, Element element)
    {
        if (!element.LocalName.Equals(selector.LocalName, StringComparison.OrdinalIgnoreCase))
            return false;
        return MatchesNamespace(selector.Namespace, element);
    }

    private static bool MatchesUniversal(UniversalSelector selector, Element element)
        => MatchesNamespace(selector.Namespace, element);

    private static bool MatchesNamespace(string? ns, Element element)
    {
        // v1: treat namespace as opaque; only enforce when an explicit prefix is supplied.
        // TODO: integrate @namespace at-rule + default namespace once the rule pipeline exposes it.
        if (ns is null) return true;
        if (ns == "*") return true;
        if (ns.Length == 0) return string.IsNullOrEmpty(element.Namespace);
        return element.Prefix?.Equals(ns, StringComparison.OrdinalIgnoreCase) ?? true;
    }

    private static bool MatchesAttribute(AttributeSelector selector, Element element)
    {
        var actual = element.GetAttribute(selector.Name);
        if (actual is null)
            return false;
        if (selector.Operator == AttributeOperator.Exists)
            return true;

        var expected = selector.Value ?? string.Empty;
        var comparison = selector.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return selector.Operator switch
        {
            AttributeOperator.Equals => actual.Equals(expected, comparison),
            AttributeOperator.Includes => actual
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(expected, selector.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal),
            AttributeOperator.DashMatch => actual.Equals(expected, comparison) ||
                actual.StartsWith(expected + "-", comparison),
            AttributeOperator.Prefix => actual.StartsWith(expected, comparison),
            AttributeOperator.Suffix => actual.EndsWith(expected, comparison),
            AttributeOperator.Substring => actual.Contains(expected, comparison),
            _ => false,
        };
    }

    private static bool MatchesPseudoClass(
        PseudoClassSelector selector,
        Element element,
        SelectorMatchContext context)
        => selector.Name switch
        {
            "root" => element.OwnerDocument?.DocumentElement == element,
            "scope" => ScopeElement(context) == element,
            "empty" => IsEmpty(element),
            "first-child" => PreviousElementSibling(element) is null,
            "last-child" => NextElementSibling(element) is null,
            "only-child" => PreviousElementSibling(element) is null && NextElementSibling(element) is null,
            "first-of-type" => PreviousElementSiblings(element).All(s => !SameType(s, element)),
            "last-of-type" => NextElementSiblings(element).All(s => !SameType(s, element)),
            "only-of-type" => PreviousElementSiblings(element).All(s => !SameType(s, element)) &&
                NextElementSiblings(element).All(s => !SameType(s, element)),
            "nth-child" => MatchesNth(selector.Argument, element, context, ofType: false, fromEnd: false),
            "nth-last-child" => MatchesNth(selector.Argument, element, context, ofType: false, fromEnd: true),
            "nth-of-type" => selector.Argument is NthPattern nth1 && nth1.Matches(ElementIndex(element, ofType: true, fromEnd: false)),
            "nth-last-of-type" => selector.Argument is NthPattern nth2 && nth2.Matches(ElementIndex(element, ofType: true, fromEnd: true)),
            "is" or "where" or "matches" => selector.Argument is SelectorList list && Matches(list, element, context),
            "not" => selector.Argument is SelectorList listN && !Matches(listN, element, context),
            "has" => MatchesHas(selector.Argument, element, context),
            "lang" => selector.Argument is string lang && MatchesLanguage(element, lang),
            "dir" => selector.Argument is string dir && MatchesDirection(element, dir),
            "hover" => context.HoveredElement == element,
            "active" => context.ActiveElement == element,
            "focus" => context.FocusedElement == element,
            "focus-visible" => context.FocusedElement == element,
            "focus-within" => context.FocusedElement is not null &&
                (context.FocusedElement == element || Ancestors(context.FocusedElement).Contains(element)),
            "target" => context.TargetElement == element,
            "target-within" => context.TargetElement is not null &&
                (context.TargetElement == element || Ancestors(context.TargetElement).Contains(element)),
            "checked" => element.HasAttribute("checked") || element.HasAttribute("selected"),
            "disabled" => element.HasAttribute("disabled"),
            "enabled" => !element.HasAttribute("disabled") && IsFormElement(element),
            "required" => element.HasAttribute("required"),
            "optional" => !element.HasAttribute("required") && IsFormElement(element),
            "placeholder-shown" => element.HasAttribute("placeholder") &&
                string.IsNullOrEmpty(element.GetAttribute("value")),
            "read-only" => IsReadOnly(element),
            "read-write" => !IsReadOnly(element) && IsEditable(element),
            "indeterminate" => element.HasAttribute("indeterminate"),
            "default" => element.HasAttribute("selected") || element.HasAttribute("checked") ||
                (IsFormElement(element) && string.Equals(element.GetAttribute("type"), "submit", StringComparison.OrdinalIgnoreCase)),
            "defined" => true, // built-in elements are always defined; custom-element state is downstream
            "any-link" => IsAnyLink(element),
            "link" => IsAnyLink(element) && !IsVisited(element, context),
            "visited" => false, // privacy default: never match
            "local-link" => IsLocalLink(element, context),
            // Stubs that parse but never match until state model lands:
            "valid" or "invalid" or "user-valid" or "user-invalid" or "in-range" or "out-of-range" or
            "blank" or "autofill" or "fullscreen" or "modal" or "picture-in-picture" or
            "playing" or "paused" or "muted" or "volume-locked" or "current" or "past" or "future" => false,
            _ => false,
        };

    private static Element? ScopeElement(SelectorMatchContext context)
        => context.ScopeElement;

    private static bool MatchesNth(
        object? argument,
        Element element,
        SelectorMatchContext context,
        bool ofType,
        bool fromEnd)
    {
        var (pattern, ofSelector) = argument switch
        {
            NthArgument na => (na.Pattern, na.OfSelector),
            NthPattern np => (np, (SelectorList?)null),
            _ => (null!, (SelectorList?)null),
        };
        if (pattern is null) return false;

        if (ofSelector is null)
            return pattern.Matches(ElementIndex(element, ofType, fromEnd));

        // "of S" form: index counts only siblings matching S.
        var siblings = fromEnd
            ? ElementSiblingsFromEnd(element)
            : ElementSiblingsFromStart(element);
        var index = 0;
        foreach (var sibling in siblings)
        {
            if (Matches(ofSelector, sibling, context))
                index++;
            if (sibling == element)
                return Matches(ofSelector, element, context) && pattern.Matches(index);
        }
        return false;
    }

    private static bool MatchesHas(object? argument, Element element, SelectorMatchContext context)
    {
        if (argument is not SelectorList list) return false;
        if (context.HasDepth >= MaxHasDepth) return false; // depth bound — Selectors 4 §16.8

        var childContext = context.WithHasDepth(context.HasDepth + 1).WithScope(element);

        foreach (var selector in list.Selectors)
        {
            if (selector.Parts.Count == 0) continue;
            var leadingCombinator = selector.Parts[0].CombinatorFromPrevious;
            var search = ElementsForHas(element, leadingCombinator);
            foreach (var candidate in search)
            {
                if (MatchesScoped(selector, candidate, element, childContext))
                    return true;
            }
        }
        return false;
    }

    private static IEnumerable<Element> ElementsForHas(Element scope, SelectorCombinator leading)
        => leading switch
        {
            SelectorCombinator.NextSibling or SelectorCombinator.SubsequentSibling =>
                NextElementSiblingsAndDescendants(scope),
            _ => scope.Descendants().OfType<Element>(),
        };

    private static IEnumerable<Element> NextElementSiblingsAndDescendants(Element scope)
    {
        for (var sibling = NextElementSibling(scope); sibling is not null; sibling = NextElementSibling(sibling))
        {
            yield return sibling;
            foreach (var d in sibling.Descendants().OfType<Element>())
                yield return d;
        }
    }

    /// <summary>Match a selector whose leftmost combinator is anchored to <paramref name="scope"/>.</summary>
    private static bool MatchesScoped(
        ComplexSelector selector,
        Element element,
        Element scope,
        SelectorMatchContext context)
    {
        // Walk right-to-left like MatchesFrom but stop when the leftmost part anchors against scope.
        return MatchScopedFrom(selector, selector.Parts.Count - 1, element, scope, context);
    }

    private static bool MatchScopedFrom(
        ComplexSelector selector,
        int partIndex,
        Element element,
        Element scope,
        SelectorMatchContext context)
    {
        var part = selector.Parts[partIndex];
        if (!MatchesCompound(part.Compound, element, context))
            return false;

        if (partIndex == 0)
        {
            // For :has(F), the leftmost part is anchored relative to scope according to its leading combinator.
            return part.CombinatorFromPrevious switch
            {
                SelectorCombinator.None => true, // descendant of scope is implicit
                SelectorCombinator.Child => ParentElement(element) == scope,
                SelectorCombinator.Descendant => Ancestors(element).Contains(scope) || element == scope,
                SelectorCombinator.NextSibling => PreviousElementSibling(element) == scope,
                SelectorCombinator.SubsequentSibling => PreviousElementSiblings(element).Contains(scope),
                _ => false,
            };
        }

        return part.CombinatorFromPrevious switch
        {
            SelectorCombinator.Child => ParentElement(element) is { } parent &&
                MatchScopedFrom(selector, partIndex - 1, parent, scope, context),
            SelectorCombinator.Descendant => Ancestors(element)
                .Any(ancestor => MatchScopedFrom(selector, partIndex - 1, ancestor, scope, context)),
            SelectorCombinator.NextSibling => PreviousElementSibling(element) is { } sibling &&
                MatchScopedFrom(selector, partIndex - 1, sibling, scope, context),
            SelectorCombinator.SubsequentSibling => PreviousElementSiblings(element)
                .Any(sibling => MatchScopedFrom(selector, partIndex - 1, sibling, scope, context)),
            _ => false,
        };
    }

    private static bool IsEmpty(Element element)
        => element.ChildNodes.All(child => child switch
        {
            Element => false,
            Text text => text.Data.Length == 0,
            CData cdata => cdata.Data.Length == 0,
            _ => true,
        });

    private static bool MatchesLanguage(Element element, string language)
    {
        for (Element? current = element; current is not null; current = ParentElement(current))
        {
            var actual = current.GetAttribute("lang");
            if (actual is null)
                continue;
            return actual.Equals(language, StringComparison.OrdinalIgnoreCase) ||
                actual.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool MatchesDirection(Element element, string direction)
    {
        for (Element? current = element; current is not null; current = ParentElement(current))
        {
            var attr = current.GetAttribute("dir");
            if (attr is null) continue;
            return attr.Equals(direction, StringComparison.OrdinalIgnoreCase);
        }
        // Default for HTML is ltr.
        return direction.Equals("ltr", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFormElement(Element element)
        => element.LocalName is "input" or "select" or "textarea" or "button" or "fieldset" or "option";

    private static bool IsReadOnly(Element element)
    {
        if (element.LocalName is "input" or "textarea")
            return element.HasAttribute("readonly") || element.HasAttribute("disabled");
        // Non-form, non-contenteditable elements are :read-only by default.
        return !IsEditable(element);
    }

    private static bool IsEditable(Element element)
    {
        if (element.LocalName is "input" or "textarea")
            return !element.HasAttribute("readonly") && !element.HasAttribute("disabled");
        for (Element? cur = element; cur is not null; cur = ParentElement(cur))
        {
            var ce = cur.GetAttribute("contenteditable");
            if (ce is null) continue;
            if (ce.Length == 0 || ce.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (ce.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return false;
    }

    private static bool IsAnyLink(Element element)
        => element.LocalName is "a" or "area" && element.HasAttribute("href");

    private static bool IsVisited(Element element, SelectorMatchContext context)
    {
        if (context.VisitedHrefs is null) return false;
        var href = element.GetAttribute("href");
        return href is not null && context.VisitedHrefs.Contains(href);
    }

    private static bool IsLocalLink(Element element, SelectorMatchContext context)
    {
        if (!IsAnyLink(element)) return false;
        if (context.DocumentUrl is null) return false;
        var href = element.GetAttribute("href");
        if (string.IsNullOrEmpty(href)) return false;
        if (!Uri.TryCreate(context.DocumentUrl, href, out var resolved)) return false;
        return resolved.Scheme == context.DocumentUrl.Scheme &&
            resolved.Host.Equals(context.DocumentUrl.Host, StringComparison.OrdinalIgnoreCase) &&
            resolved.AbsolutePath.Equals(context.DocumentUrl.AbsolutePath, StringComparison.Ordinal);
    }

    private static int ElementIndex(Element element, bool ofType, bool fromEnd)
    {
        var siblings = fromEnd ? ElementSiblingsFromEnd(element) : ElementSiblingsFromStart(element);
        var index = 0;
        foreach (var sibling in siblings)
        {
            if (!ofType || SameType(sibling, element))
                index++;
            if (sibling == element)
                return index;
        }

        return -1;
    }

    private static IEnumerable<Element> ElementSiblingsFromStart(Element element)
        => element.ParentNode?.ChildNodes.OfType<Element>() ?? [];

    private static IEnumerable<Element> ElementSiblingsFromEnd(Element element)
        => ElementSiblingsFromStart(element).Reverse();

    private static bool SameType(Element left, Element right)
        => left.Namespace == right.Namespace &&
           left.LocalName.Equals(right.LocalName, StringComparison.OrdinalIgnoreCase);

    private static Element? ParentElement(Element element) => element.ParentNode as Element;

    private static IEnumerable<Element> Ancestors(Element element)
    {
        for (var current = ParentElement(element); current is not null; current = ParentElement(current))
            yield return current;
    }

    private static Element? PreviousElementSibling(Element element)
    {
        for (var sibling = element.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
            if (sibling is Element siblingElement)
                return siblingElement;
        return null;
    }

    private static Element? NextElementSibling(Element element)
    {
        for (var sibling = element.NextSibling; sibling is not null; sibling = sibling.NextSibling)
            if (sibling is Element siblingElement)
                return siblingElement;
        return null;
    }

    private static IEnumerable<Element> PreviousElementSiblings(Element element)
    {
        for (var sibling = PreviousElementSibling(element); sibling is not null; sibling = PreviousElementSibling(sibling))
            yield return sibling;
    }

    private static IEnumerable<Element> NextElementSiblings(Element element)
    {
        for (var sibling = NextElementSibling(element); sibling is not null; sibling = NextElementSibling(sibling))
            yield return sibling;
    }
}

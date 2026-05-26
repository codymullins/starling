using System.Runtime.CompilerServices;
using Starling.Css;
using Starling.Css.Cssom;
using Starling.Css.Parser;
using Starling.Dom;
using Starling.Js.Runtime;

namespace Starling.Bindings;

/// <summary>
/// CSSOM host objects (CSSOM §6): <c>document.styleSheets</c> →
/// <c>StyleSheetList</c> → <c>CSSStyleSheet</c> → <c>cssRules</c> →
/// <c>CSSStyleRule</c> (<c>.selectorText</c>, <c>.style</c>, <c>.cssText</c>).
///
/// The model is backed by a live, mutable <see cref="CssomStyleSheet"/> per
/// <c>&lt;style&gt;</c>/<c>&lt;link&gt;</c>, cached per document so CSSOM edits
/// (setProperty / selectorText) round-trip. JS wrappers are thin and re-read the
/// host model on every access, so they can be rebuilt freely without losing state.
/// </summary>
internal static class CssomBinding
{
    // Per-document CSSOM stylesheet list, built lazily from the document's
    // <style>/<link rel=stylesheet> elements in tree order.
    private static readonly ConditionalWeakTable<Document, List<CssomStyleSheet>> SheetsPerDocument = new();

    /// <summary>document.styleSheets accessor body.</summary>
    public static JsValue StyleSheetsAccessor(JsRealm realm, JsValue thisV)
    {
        var doc = DomWrappers.UnwrapDocument(thisV);
        if (doc is null) return JsValue.Null;
        var sheets = GetOrBuildSheets(doc);
        return JsValue.Object(BuildStyleSheetList(realm, sheets));
    }

    private static List<CssomStyleSheet> GetOrBuildSheets(Document doc)
    {
        if (SheetsPerDocument.TryGetValue(doc, out var existing))
            return existing;
        var list = new List<CssomStyleSheet>();
        foreach (var el in doc.DescendantElements())
        {
            if (el.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                var source = el.TextContent;
                var parsed = CssParser.ParseStyleSheet(source ?? string.Empty, StyleOrigin.Author);
                list.Add(new CssomStyleSheet(parsed));
            }
            // <link rel=stylesheet> external sheets have no inline text available
            // in this binding context; they are omitted (out of scope for the
            // CSSOM read/mutate WPT cases, which all use inline <style>).
        }
        SheetsPerDocument.Add(doc, list);
        return list;
    }

    private static JsObject BuildStyleSheetList(JsRealm realm, List<CssomStyleSheet> sheets)
    {
        var obj = new JsObject(realm.ObjectPrototype);
        for (var i = 0; i < sheets.Count; i++)
        {
            var sheet = sheets[i];
            obj.DefineOwnProperty(i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PropertyDescriptor.Data(JsValue.Object(BuildStyleSheet(realm, sheet)),
                    writable: false, enumerable: true, configurable: true));
        }
        obj.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(sheets.Count), writable: false, enumerable: false, configurable: true));
        EventTargetBinding.DefineMethod(realm, obj, "item", (_, args) =>
        {
            if (args.Length == 0) return JsValue.Null;
            var idx = (int)JsValue.ToNumber(args[0]);
            return idx >= 0 && idx < sheets.Count
                ? JsValue.Object(BuildStyleSheet(realm, sheets[idx])) : JsValue.Null;
        }, length: 1);
        return obj;
    }

    private static JsObject BuildStyleSheet(JsRealm realm, CssomStyleSheet sheet)
    {
        var obj = new JsObject(realm.ObjectPrototype);
        EventTargetBinding.DefineAccessor(realm, obj, "cssRules",
            (_, _) => JsValue.Object(BuildRuleList(realm, sheet)));
        EventTargetBinding.DefineAccessor(realm, obj, "rules",
            (_, _) => JsValue.Object(BuildRuleList(realm, sheet)));
        EventTargetBinding.DefineAccessor(realm, obj, "type", (_, _) => JsValue.String("text/css"));
        EventTargetBinding.DefineAccessor(realm, obj, "href", (_, _) => JsValue.Null);
        EventTargetBinding.DefineAccessor(realm, obj, "title", (_, _) => JsValue.Null);
        EventTargetBinding.DefineAccessor(realm, obj, "disabled", (_, _) => JsValue.False);
        return obj;
    }

    private static JsObject BuildRuleList(JsRealm realm, CssomStyleSheet sheet)
    {
        var obj = new JsObject(realm.ObjectPrototype);
        var rules = sheet.Rules;
        // Expose every rule (including at-rule placeholders) at its correct
        // integer index so that cssRules[0] is always the first rule even when
        // the first rule is an @font-face or other at-rule.
        for (var i = 0; i < rules.Count; i++)
        {
            var ruleObj = rules[i] is { } styleRule
                ? JsValue.Object(BuildStyleRule(realm, styleRule))
                : JsValue.Object(BuildAtRulePlaceholder(realm));
            obj.DefineOwnProperty(i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PropertyDescriptor.Data(ruleObj, writable: false, enumerable: true, configurable: true));
        }
        obj.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(rules.Count), writable: false, enumerable: false, configurable: true));
        EventTargetBinding.DefineMethod(realm, obj, "item", (_, args) =>
        {
            if (args.Length == 0) return JsValue.Null;
            var idx = (int)JsValue.ToNumber(args[0]);
            if (idx < 0 || idx >= rules.Count) return JsValue.Null;
            return rules[idx] is { } r
                ? JsValue.Object(BuildStyleRule(realm, r))
                : JsValue.Object(BuildAtRulePlaceholder(realm));
        }, length: 1);
        return obj;
    }

    /// <summary>Build a minimal CSSRule-shaped object for an at-rule that the
    /// CSSOM model keeps as an opaque placeholder. Exposes a read-only
    /// <c>style</c> (an empty declaration) so @font-face rules can accept
    /// <c>setProperty("unicode-range", …)</c> calls from tests.</summary>
    private static JsObject BuildAtRulePlaceholder(JsRealm realm)
    {
        var obj = new JsObject(realm.ObjectPrototype);
        var emptyBlock = new Starling.Css.Cssom.CssomDeclarationBlock();
        // type = 5 = CSSFontFaceRule (most common at-rule placeholder).
        EventTargetBinding.DefineAccessor(realm, obj, "type", (_, _) => JsValue.Number(5));
        EventTargetBinding.DefineAccessor(realm, obj, "cssText", (_, _) => JsValue.String(""));
        EventTargetBinding.DefineAccessor(realm, obj, "style",
            (_, _) => JsValue.Object(BuildDeclaration(realm, emptyBlock)));
        return obj;
    }

    private static JsObject BuildStyleRule(JsRealm realm, CssomStyleRule rule)
    {
        var obj = new JsObject(realm.ObjectPrototype);
        EventTargetBinding.DefineAccessor(realm, obj, "selectorText",
            (_, _) => JsValue.String(rule.SelectorTextRaw),
            (_, args) =>
            {
                if (args.Length > 0)
                    rule.TrySetSelectorText(JsValue.ToStringValue(args[0]));
                return JsValue.Undefined;
            });
        EventTargetBinding.DefineAccessor(realm, obj, "style",
            (_, _) => JsValue.Object(BuildDeclaration(realm, rule.Style)));
        EventTargetBinding.DefineAccessor(realm, obj, "cssText",
            (_, _) =>
            {
                var decls = rule.Style.CssText;
                return JsValue.String(decls.Length == 0
                    ? rule.SelectorTextRaw + " { }"
                    : rule.SelectorTextRaw + " { " + decls + " }");
            });
        // CSSStyleRule.type == 1 (legacy CSSRule.STYLE_RULE).
        EventTargetBinding.DefineAccessor(realm, obj, "type", (_, _) => JsValue.Number(1));
        return obj;
    }

    /// <summary>Build a CSSStyleDeclaration over a live declaration block.</summary>
    public static JsObject BuildDeclaration(JsRealm realm, CssomDeclarationBlock block)
    {
        var obj = new JsObject(realm.ObjectPrototype);

        EventTargetBinding.DefineAccessor(realm, obj, "cssText",
            (_, _) => JsValue.String(block.CssText),
            (_, args) => { block.CssText = args.Length > 0 ? JsValue.ToStringValue(args[0]) : ""; return JsValue.Undefined; });

        EventTargetBinding.DefineAccessor(realm, obj, "length",
            (_, _) => JsValue.Number(block.Count));

        EventTargetBinding.DefineMethod(realm, obj, "getPropertyValue", (_, args) =>
            JsValue.String(args.Length == 0 ? "" : block.GetPropertyValue(JsValue.ToStringValue(args[0]))), length: 1);

        EventTargetBinding.DefineMethod(realm, obj, "getPropertyPriority", (_, args) =>
            JsValue.String(args.Length == 0 ? "" : block.GetPropertyPriority(JsValue.ToStringValue(args[0]))), length: 1);

        EventTargetBinding.DefineMethod(realm, obj, "setProperty", (_, args) =>
        {
            if (args.Length < 2) return JsValue.Undefined;
            var name = JsValue.ToStringValue(args[0]);
            var value = JsValue.ToStringValue(args[1]);
            var priority = args.Length > 2 && !args[2].IsNullish ? JsValue.ToStringValue(args[2]) : null;
            block.SetProperty(name, value, priority);
            return JsValue.Undefined;
        }, length: 2);

        EventTargetBinding.DefineMethod(realm, obj, "removeProperty", (_, args) =>
            JsValue.String(args.Length == 0 ? "" : block.RemoveProperty(JsValue.ToStringValue(args[0]))), length: 1);

        EventTargetBinding.DefineMethod(realm, obj, "item", (_, args) =>
        {
            if (args.Length == 0) return JsValue.String("");
            var idx = (int)JsValue.ToNumber(args[0]);
            return JsValue.String(block.ItemName(idx));
        }, length: 1);

        // camelCase / kebab-case property accessors backed by the declaration
        // block (CSSOM §6.6 — used by tests like inclusive-ranges via rule.style.zIndex).
        foreach (var kebab in NodeBindings.InlineStylePropertyNames)
        {
            var capturedKebab = kebab;
            var camel = NodeBindings.KebabToCamelPublic(kebab);
            EventTargetBinding.DefineAccessor(realm, obj, capturedKebab,
                (_, _) => JsValue.String(block.GetPropertyValue(capturedKebab)),
                (_, a) => { block.SetProperty(capturedKebab, a.Length > 0 ? JsValue.ToStringValue(a[0]) : "", null); return JsValue.Undefined; });
            if (camel != capturedKebab)
            {
                EventTargetBinding.DefineAccessor(realm, obj, camel,
                    (_, _) => JsValue.String(block.GetPropertyValue(capturedKebab)),
                    (_, a) => { block.SetProperty(capturedKebab, a.Length > 0 ? JsValue.ToStringValue(a[0]) : "", null); return JsValue.Undefined; });
            }
        }

        return obj;
    }
}

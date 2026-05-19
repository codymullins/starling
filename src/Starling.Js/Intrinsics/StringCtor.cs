using System.Globalization;
using System.Text;
using Tessera.Js.Runtime;

namespace Tessera.Js.Intrinsics;

/// <summary>
/// §22.1 The String constructor and §22.1.3 String.prototype. Strings are
/// represented as .NET UTF-16 strings, matching ECMAScript's code-unit model.
/// RegExp-specific branches are intentionally deferred to B4-1.
/// </summary>
public static class StringCtor
{
    private const int MaxRepeatLength = 1_000_000;

    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var stringProto = realm.StringPrototype;

        var ctor = new JsNativeFunction(realm, "String", length: 1, (thisV, args) =>
        {
            // §22.1.1.1 String(value): no argument returns the empty string;
            // otherwise route through §7.1.17 ToString.
            var text = args.Length == 0 ? string.Empty : JsValue.ToStringValue(args[0]);
            if (thisV.IsObject && thisV.AsObject is JsNativeFunction native && native.Name == "String")
                return JsValue.Object(CreateStringObject(realm, text));
            return JsValue.String(text);
        }, isConstructor: true);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(stringProto), writable: false, enumerable: false, configurable: false));

        IntrinsicHelpers.DefineMethod(realm, ctor, "fromCharCode", 1, (_, args) => FromCharCode(args));
        IntrinsicHelpers.DefineMethod(realm, ctor, "fromCodePoint", 1, (_, args) => FromCodePoint(realm, args));
        IntrinsicHelpers.DefineMethod(realm, ctor, "raw", 1, (_, args) => Raw(realm, args));

        stringProto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "at", 1, (thisV, args) => At(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "charAt", 1, (thisV, args) => CharAt(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "charCodeAt", 1, (thisV, args) => CharCodeAt(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "codePointAt", 1, (thisV, args) => CodePointAt(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "concat", 1, (thisV, args) => Concat(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "endsWith", 1, (thisV, args) => EndsWith(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "includes", 1, (thisV, args) => Includes(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "indexOf", 1, (thisV, args) => IndexOf(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "lastIndexOf", 1, (thisV, args) => LastIndexOf(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "localeCompare", 1, (thisV, args) => LocaleCompare(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "normalize", 0, (thisV, args) => Normalize(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "padEnd", 1, (thisV, args) => Pad(realm, thisV, args, atStart: false));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "padStart", 1, (thisV, args) => Pad(realm, thisV, args, atStart: true));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "repeat", 1, (thisV, args) => Repeat(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "match", 1, (thisV, args) => Match(realm, thisV, args, all: false));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "matchAll", 1, (thisV, args) => Match(realm, thisV, args, all: true));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "search", 1, (thisV, args) => Search(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "replace", 2, (thisV, args) => Replace(realm, thisV, args, replaceAll: false));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "replaceAll", 2, (thisV, args) => Replace(realm, thisV, args, replaceAll: true));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "slice", 2, (thisV, args) => Slice(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "split", 2, (thisV, args) => Split(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "startsWith", 1, (thisV, args) => StartsWith(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "substring", 2, (thisV, args) => Substring(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "toLowerCase", 0, (thisV, args) => JsValue.String(ThisStringValue(realm, thisV).ToLowerInvariant()));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "toUpperCase", 0, (thisV, args) => JsValue.String(ThisStringValue(realm, thisV).ToUpperInvariant()));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "toLocaleLowerCase", 0, (thisV, args) => JsValue.String(ThisStringValue(realm, thisV).ToLower(CultureInfo.InvariantCulture)));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "toLocaleUpperCase", 0, (thisV, args) => JsValue.String(ThisStringValue(realm, thisV).ToUpper(CultureInfo.InvariantCulture)));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "trim", 0, (thisV, args) => JsValue.String(TrimJs(ThisStringValue(realm, thisV), trimStart: true, trimEnd: true)));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "trimStart", 0, (thisV, args) => JsValue.String(TrimJs(ThisStringValue(realm, thisV), trimStart: true, trimEnd: false)));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "trimEnd", 0, (thisV, args) => JsValue.String(TrimJs(ThisStringValue(realm, thisV), trimStart: false, trimEnd: true)));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "toString", 0, (thisV, args) => JsValue.String(ThisStringValue(realm, thisV)));
        IntrinsicHelpers.DefineMethod(realm, stringProto, "valueOf", 0, (thisV, args) => JsValue.String(ThisStringValue(realm, thisV)));

        // §22.1.3.34 String.prototype[@@iterator] — walks the string by
        // Unicode code points (not UTF-16 code units), e.g.
        // [..."😀ab"].length === 3.
        var stringIterator = new JsNativeFunction(realm, "[Symbol.iterator]", 0, (thisV, _) =>
        {
            var s = ThisStringValue(realm, thisV);
            return IteratorIntrinsics.CreateStringIterator(realm, s);
        }, isConstructor: false);
        stringProto.DefineOwnProperty(SymbolCtor.Iterator,
            PropertyDescriptor.BuiltinMethod(JsValue.Object(stringIterator)));

        realm.StringConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("String",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    private static JsObject CreateStringObject(JsRealm realm, string text)
    {
        var obj = realm.NewObjectWithProto(realm.StringPrototype);
        DefineStringData(obj, text);
        return obj;
    }

    internal static void DefineStringData(JsObject obj, string text)
    {
        obj.DefineOwnProperty("__primitiveValue",
            PropertyDescriptor.Data(JsValue.String(text), writable: false, enumerable: false, configurable: false));
        obj.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(text.Length), writable: false, enumerable: false, configurable: false));
        for (var i = 0; i < text.Length; i++)
        {
            obj.DefineOwnProperty(i.ToString(CultureInfo.InvariantCulture),
                PropertyDescriptor.Data(JsValue.String(text[i].ToString()), writable: false, enumerable: true, configurable: false));
        }
    }

    private static string ThisStringValue(JsRealm realm, JsValue thisV)
    {
        // §22.1.3.4 thisStringValue: accept primitive strings and String exotic
        // wrappers; otherwise RequireObjectCoercible then ToString.
        if (thisV.IsNullish)
            throw new JsThrow(realm.NewTypeError("String.prototype method called on null or undefined"));
        if (thisV.IsString) return thisV.AsString;
        if (thisV.IsObject)
        {
            var slot = thisV.AsObject.Get("__primitiveValue");
            if (slot.IsString) return slot.AsString;
            return JsValue.ToStringValue(AbstractOperations.ToPrimitive(thisV, "string"));
        }
        return JsValue.ToStringValue(thisV);
    }

    private static JsValue FromCharCode(JsValue[] args)
    {
        var chars = new char[args.Length];
        for (var i = 0; i < args.Length; i++)
            chars[i] = (char)ToUint16(args[i]);
        return JsValue.String(new string(chars));
    }

    private static JsValue FromCodePoint(JsRealm realm, JsValue[] args)
    {
        var sb = new StringBuilder();
        foreach (var arg in args)
        {
            var n = JsValue.ToNumber(arg);
            if (!IsIntegral(n) || n < 0 || n > 0x10FFFF)
                throw new JsThrow(realm.NewRangeError("Invalid code point"));
            sb.Append(char.ConvertFromUtf32((int)n));
        }
        return JsValue.String(sb.ToString());
    }

    private static JsValue Raw(JsRealm realm, JsValue[] args)
    {
        if (args.Length == 0) throw new JsThrow(realm.NewTypeError("String.raw requires a template object"));
        var cooked = AbstractOperations.ToObject(realm, args[0]);
        var raw = AbstractOperations.ToObject(realm, cooked.Get("raw"));
        var literalSegments = ToLength(raw.Get("length"));
        if (literalSegments == 0) return JsValue.String(string.Empty);
        var sb = new StringBuilder();
        for (var i = 0; i < literalSegments; i++)
        {
            sb.Append(JsValue.ToStringValue(raw.Get(i.ToString(CultureInfo.InvariantCulture))));
            if (i + 1 < literalSegments)
                sb.Append(i + 1 < args.Length ? JsValue.ToStringValue(args[i + 1]) : string.Empty);
        }
        return JsValue.String(sb.ToString());
    }

    private static JsValue At(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var i = ToIntegerOrInfinity(args.Length > 0 ? args[0] : JsValue.Undefined);
        var k = i >= 0 ? i : s.Length + i;
        return k < 0 || k >= s.Length ? JsValue.Undefined : JsValue.String(s[(int)k].ToString());
    }

    private static JsValue CharAt(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var pos = ToIntegerOrInfinity(args.Length > 0 ? args[0] : JsValue.Undefined);
        return pos < 0 || pos >= s.Length ? JsValue.String(string.Empty) : JsValue.String(s[(int)pos].ToString());
    }

    private static JsValue CharCodeAt(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var pos = ToIntegerOrInfinity(args.Length > 0 ? args[0] : JsValue.Undefined);
        return pos < 0 || pos >= s.Length ? JsValue.NaN : JsValue.Number(s[(int)pos]);
    }

    private static JsValue CodePointAt(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var pos = ToIntegerOrInfinity(args.Length > 0 ? args[0] : JsValue.Undefined);
        if (pos < 0 || pos >= s.Length) return JsValue.Undefined;
        var first = s[(int)pos];
        if (char.IsHighSurrogate(first) && pos + 1 < s.Length && char.IsLowSurrogate(s[(int)pos + 1]))
            return JsValue.Number(char.ConvertToUtf32(first, s[(int)pos + 1]));
        return JsValue.Number(first);
    }

    private static JsValue Concat(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var sb = new StringBuilder(ThisStringValue(realm, thisV));
        foreach (var arg in args) sb.Append(JsValue.ToStringValue(arg));
        return JsValue.String(sb.ToString());
    }

    private static JsValue EndsWith(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var search = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        var end = args.Length > 1 && !args[1].IsUndefined ? Clamp(ToIntegerOrInfinity(args[1]), 0, s.Length) : s.Length;
        return JsValue.Boolean(s[..(int)end].EndsWith(search, StringComparison.Ordinal));
    }

    private static JsValue Includes(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var search = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        var pos = Clamp(ToIntegerOrInfinity(args.Length > 1 ? args[1] : JsValue.Undefined), 0, s.Length);
        return JsValue.Boolean(s.IndexOf(search, (int)pos, StringComparison.Ordinal) >= 0);
    }

    private static JsValue IndexOf(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var search = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        var pos = Clamp(ToIntegerOrInfinity(args.Length > 1 ? args[1] : JsValue.Undefined), 0, s.Length);
        return JsValue.Number(s.IndexOf(search, (int)pos, StringComparison.Ordinal));
    }

    private static JsValue LastIndexOf(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var search = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        var raw = args.Length > 1 ? JsValue.ToNumber(args[1]) : double.NaN;
        var pos = double.IsNaN(raw) ? s.Length : Clamp(ToIntegerOrInfinity(args[1]), 0, s.Length);
        if (search.Length == 0) return JsValue.Number(pos);
        var start = Math.Min((int)pos + search.Length - 1, s.Length - 1);
        return JsValue.Number(start < 0 ? -1 : s.LastIndexOf(search, start, StringComparison.Ordinal));
    }

    private static JsValue LocaleCompare(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var that = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        return JsValue.Number(string.Compare(s, that, CultureInfo.InvariantCulture, CompareOptions.None));
    }

    private static JsValue Normalize(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var formName = args.Length == 0 || args[0].IsUndefined ? "NFC" : JsValue.ToStringValue(args[0]);
        var form = formName switch
        {
            "NFC" => NormalizationForm.FormC,
            "NFD" => NormalizationForm.FormD,
            "NFKC" => NormalizationForm.FormKC,
            "NFKD" => NormalizationForm.FormKD,
            _ => throw new JsThrow(realm.NewRangeError("Invalid normalization form")),
        };
        return JsValue.String(s.Normalize(form));
    }

    private static JsValue Pad(JsRealm realm, JsValue thisV, JsValue[] args, bool atStart)
    {
        var s = ThisStringValue(realm, thisV);
        var maxLength = ToLength(args.Length > 0 ? args[0] : JsValue.Undefined);
        if (maxLength <= s.Length) return JsValue.String(s);
        var fill = args.Length > 1 && !args[1].IsUndefined ? JsValue.ToStringValue(args[1]) : " ";
        if (fill.Length == 0) return JsValue.String(s);
        var needed = (int)Math.Min(maxLength - s.Length, MaxRepeatLength);
        var filler = RepeatToLength(fill, needed);
        return JsValue.String(atStart ? filler + s : s + filler);
    }

    private static JsValue Repeat(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var n = JsValue.ToNumber(args.Length > 0 ? args[0] : JsValue.Undefined);
        var count = ToIntegerOrInfinity(args.Length > 0 ? args[0] : JsValue.Undefined);
        if (double.IsInfinity(n) || count < 0) throw new JsThrow(realm.NewRangeError("Invalid repeat count"));
        if (s.Length != 0 && count > MaxRepeatLength / s.Length)
            throw new JsThrow(realm.NewRangeError("Repeat count too large"));
        var sb = new StringBuilder(s.Length * (int)count);
        for (var i = 0; i < count; i++) sb.Append(s);
        return JsValue.String(sb.ToString());
    }

    private static JsValue Replace(JsRealm realm, JsValue thisV, JsValue[] args, bool replaceAll)
    {
        var s = ThisStringValue(realm, thisV);
        // RegExp path: route through the regex's @@replace protocol. B4-1.
        if (args.Length > 0 && RegExpCtor.IsRegExp(args[0]))
        {
            var re = (JsRegExp)args[0].AsObject;
            if (replaceAll && (re.Flags & Tessera.Js.RegExp.RegexFlags.Global) == 0)
                throw new JsThrow(realm.NewTypeError("String.prototype.replaceAll called with a non-global RegExp"));
            var replaceFn = re.Get(SymbolCtor.Replace);
            if (replaceFn.IsObject)
                return AbstractOperations.Call(realm.ActiveVm, replaceFn, args[0],
                    new[] { JsValue.String(s), args.Length > 1 ? args[1] : JsValue.Undefined });
        }
        var search = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        var replacement = args.Length > 1 ? args[1] : JsValue.Undefined;
        if (!replaceAll)
        {
            var pos = s.IndexOf(search, StringComparison.Ordinal);
            if (pos < 0) return JsValue.String(s);
            return JsValue.String(s[..pos] + ReplacementText(realm, replacement, search, pos, s) + s[(pos + search.Length)..]);
        }

        if (search.Length == 0)
        {
            var sbEmpty = new StringBuilder();
            for (var i = 0; i <= s.Length; i++)
            {
                sbEmpty.Append(ReplacementText(realm, replacement, search, i, s));
                if (i < s.Length) sbEmpty.Append(s[i]);
            }
            return JsValue.String(sbEmpty.ToString());
        }

        var sb = new StringBuilder();
        var cursor = 0;
        while (cursor <= s.Length)
        {
            var pos = s.IndexOf(search, cursor, StringComparison.Ordinal);
            if (pos < 0) break;
            sb.Append(s, cursor, pos - cursor);
            sb.Append(ReplacementText(realm, replacement, search, pos, s));
            cursor = pos + search.Length;
        }
        sb.Append(s, cursor, s.Length - cursor);
        return JsValue.String(sb.ToString());
    }

    private static string ReplacementText(JsRealm realm, JsValue replacement, string matched, int position, string whole)
    {
        if (AbstractOperations.IsCallable(replacement))
        {
            var result = AbstractOperations.Call(realm.ActiveVm, replacement, JsValue.Undefined,
                new[] { JsValue.String(matched), JsValue.Number(position), JsValue.String(whole) });
            return JsValue.ToStringValue(result);
        }
        return GetSubstitution(JsValue.ToStringValue(replacement), matched, position, whole);
    }

    private static string GetSubstitution(string replacement, string matched, int position, string whole)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < replacement.Length; i++)
        {
            if (replacement[i] != '$' || i + 1 >= replacement.Length)
            {
                sb.Append(replacement[i]);
                continue;
            }
            var next = replacement[++i];
            switch (next)
            {
                case '$': sb.Append('$'); break;
                case '&': sb.Append(matched); break;
                case '`': sb.Append(whole[..position]); break;
                case '\'': sb.Append(whole[(position + matched.Length)..]); break;
                default:
                    sb.Append('$').Append(next);
                    break;
            }
        }
        return sb.ToString();
    }

    private static JsValue Slice(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var len = s.Length;
        var start = ToIntegerOrInfinity(args.Length > 0 ? args[0] : JsValue.Undefined);
        var from = start < 0 ? Math.Max(len + start, 0) : Math.Min(start, len);
        var end = args.Length > 1 && !args[1].IsUndefined ? ToIntegerOrInfinity(args[1]) : len;
        var to = end < 0 ? Math.Max(len + end, 0) : Math.Min(end, len);
        return JsValue.String(from >= to ? string.Empty : s.Substring((int)from, (int)(to - from)));
    }

    private static JsValue Match(JsRealm realm, JsValue thisV, JsValue[] args, bool all)
    {
        var s = ThisStringValue(realm, thisV);
        var re = args.Length > 0 && RegExpCtor.IsRegExp(args[0])
            ? (JsRegExp)args[0].AsObject
            : RegExpCtor.Create(realm, args.Length > 0 && !args[0].IsUndefined ? JsValue.ToStringValue(args[0]) : "",
                all ? "g" : "");
        if (all)
        {
            // B4-1-followup-b: return a real RegExp String Iterator instead of
            // an array. Spec §22.1.3.13 requires matchAll to throw TypeError if
            // the regex is not global; mirror that here for parity with the
            // Symbol.matchAll path.
            if ((re.Flags & Tessera.Js.RegExp.RegexFlags.Global) == 0)
                throw new JsThrow(realm.NewTypeError("matchAll requires a global regular expression"));
            var unicode = (re.Flags & Tessera.Js.RegExp.RegexFlags.Unicode) != 0;
            return JsValue.Object(new JsRegExpStringIterator(realm, re, s, global: true, unicode: unicode));
        }
        var fn = re.Get(SymbolCtor.Match);
        if (!fn.IsObject) return JsValue.Null;
        return AbstractOperations.Call(realm.ActiveVm, fn, JsValue.Object(re), new[] { JsValue.String(s) });
    }

    private static JsValue Search(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var re = args.Length > 0 && RegExpCtor.IsRegExp(args[0])
            ? (JsRegExp)args[0].AsObject
            : RegExpCtor.Create(realm, args.Length > 0 && !args[0].IsUndefined ? JsValue.ToStringValue(args[0]) : "", "");
        var fn = re.Get(SymbolCtor.Search);
        if (!fn.IsObject) return JsValue.Number(-1);
        return AbstractOperations.Call(realm.ActiveVm, fn, JsValue.Object(re), new[] { JsValue.String(s) });
    }

    private static JsValue Split(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        // RegExp path
        if (args.Length > 0 && RegExpCtor.IsRegExp(args[0]))
        {
            var re = (JsRegExp)args[0].AsObject;
            var fn = re.Get(SymbolCtor.Split);
            if (fn.IsObject)
                return AbstractOperations.Call(realm.ActiveVm, fn, args[0],
                    new[] { JsValue.String(s), args.Length > 1 ? args[1] : JsValue.Undefined });
        }
        var limit = args.Length > 1 && !args[1].IsUndefined ? ToUint32(args[1]) : uint.MaxValue;
        var result = new List<JsValue>();
        if (limit == 0) return MakeArrayLike(realm, result);
        if (args.Length == 0 || args[0].IsUndefined)
        {
            result.Add(JsValue.String(s));
            return MakeArrayLike(realm, result);
        }
        var sep = JsValue.ToStringValue(args[0]);
        if (sep.Length == 0)
        {
            for (var i = 0; i < s.Length && result.Count < limit; i++) result.Add(JsValue.String(s[i].ToString()));
            return MakeArrayLike(realm, result);
        }
        var cursor = 0;
        while (result.Count < limit)
        {
            var pos = s.IndexOf(sep, cursor, StringComparison.Ordinal);
            if (pos < 0) break;
            result.Add(JsValue.String(s[cursor..pos]));
            cursor = pos + sep.Length;
        }
        if (result.Count < limit) result.Add(JsValue.String(s[cursor..]));
        return MakeArrayLike(realm, result);
    }

    private static JsValue StartsWith(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var search = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        var pos = Clamp(ToIntegerOrInfinity(args.Length > 1 ? args[1] : JsValue.Undefined), 0, s.Length);
        return JsValue.Boolean(s[(int)pos..].StartsWith(search, StringComparison.Ordinal));
    }

    private static JsValue Substring(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var s = ThisStringValue(realm, thisV);
        var start = Clamp(ToIntegerOrInfinity(args.Length > 0 ? args[0] : JsValue.Undefined), 0, s.Length);
        var end = args.Length > 1 && !args[1].IsUndefined
            ? Clamp(ToIntegerOrInfinity(args[1]), 0, s.Length)
            : s.Length;
        if (start > end) (start, end) = (end, start);
        return JsValue.String(s.Substring((int)start, (int)(end - start)));
    }

    private static JsValue MakeArrayLike(JsRealm realm, List<JsValue> items)
    {
        var arr = realm.NewOrdinaryObject();
        for (var i = 0; i < items.Count; i++)
            arr.DefineOwnProperty(i.ToString(CultureInfo.InvariantCulture),
                PropertyDescriptor.Data(items[i], writable: true, enumerable: true, configurable: true));
        arr.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(items.Count), writable: true, enumerable: false, configurable: false));
        return JsValue.Object(arr);
    }

    private static string RepeatToLength(string fill, int length)
    {
        var sb = new StringBuilder(length);
        while (sb.Length < length) sb.Append(fill);
        return sb.ToString(0, length);
    }

    private static string TrimJs(string s, bool trimStart, bool trimEnd)
    {
        var start = 0;
        var end = s.Length - 1;
        if (trimStart) while (start <= end && IsJsWhiteSpace(s[start])) start++;
        if (trimEnd) while (end >= start && IsJsWhiteSpace(s[end])) end--;
        return start > end ? string.Empty : s[start..(end + 1)];
    }

    private static bool IsJsWhiteSpace(char c) => char.IsWhiteSpace(c) || c == '\uFEFF';

    private static long Clamp(long value, long min, long max) => Math.Min(Math.Max(value, min), max);

    private static bool IsIntegral(double d) => !double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Truncate(d);

    /// <summary>§7.1.5 ToIntegerOrInfinity.</summary>
    private static long ToIntegerOrInfinity(JsValue value)
    {
        var n = JsValue.ToNumber(value);
        if (double.IsNaN(n) || n == 0) return 0;
        if (double.IsPositiveInfinity(n)) return long.MaxValue;
        if (double.IsNegativeInfinity(n)) return long.MinValue;
        return (long)Math.Truncate(n);
    }

    /// <summary>§7.1.20 LengthOfArrayLike's ToLength clamp.</summary>
    private static long ToLength(JsValue value)
    {
        var len = ToIntegerOrInfinity(value);
        if (len <= 0) return 0;
        return Math.Min(len, int.MaxValue);
    }

    /// <summary>§7.1.6 ToInt32/§7.1.7 ToUint32 modulo arithmetic.</summary>
    private static uint ToUint32(JsValue value)
    {
        var n = JsValue.ToNumber(value);
        if (double.IsNaN(n) || double.IsInfinity(n) || n == 0) return 0;
        var i = Math.Truncate(n);
        var mod = i - Math.Floor(i / 4294967296.0) * 4294967296.0;
        return (uint)mod;
    }

    private static ushort ToUint16(JsValue value)
    {
        var n = JsValue.ToNumber(value);
        if (double.IsNaN(n) || double.IsInfinity(n) || n == 0) return 0;
        var i = Math.Truncate(n);
        var mod = i - Math.Floor(i / 65536.0) * 65536.0;
        return (ushort)mod;
    }
}

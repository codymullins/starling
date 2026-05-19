using System.Text;
using Tessera.Js.RegExp;
using Tessera.Js.Runtime;

namespace Tessera.Js.Intrinsics;

/// <summary>
/// §22.2 The RegExp constructor and §22.2.6 RegExp.prototype. Wires the parser
/// → Pike-VM pipeline behind the JS-visible <c>new RegExp(…)</c>,
/// <c>RegExp.prototype.exec</c>, <c>.test</c>, and Symbol.* protocol hooks.
/// </summary>
public static class RegExpCtor
{
    public static void Install(JsRealm realm)
    {
        System.ArgumentNullException.ThrowIfNull(realm);
        var proto = realm.RegExpPrototype;

        var ctor = new JsNativeFunction(realm, "RegExp", length: 2, (thisV, args) => Construct(realm, args), isConstructor: true);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));

        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));

        IntrinsicHelpers.DefineMethod(realm, proto, "exec", 1, (thisV, args) => Exec(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, proto, "test", 1, (thisV, args) => Test(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, proto, "toString", 0, (thisV, _) =>
        {
            var re = RequireRegExp(realm, thisV);
            return JsValue.String(re.ToString());
        });

        // Flag getters
        DefineFlagGetter(realm, proto, "global", RegexFlags.Global);
        DefineFlagGetter(realm, proto, "ignoreCase", RegexFlags.IgnoreCase);
        DefineFlagGetter(realm, proto, "multiline", RegexFlags.Multiline);
        DefineFlagGetter(realm, proto, "dotAll", RegexFlags.DotAll);
        DefineFlagGetter(realm, proto, "unicode", RegexFlags.Unicode);
        DefineFlagGetter(realm, proto, "unicodeSets", RegexFlags.UnicodeSets);
        DefineFlagGetter(realm, proto, "sticky", RegexFlags.Sticky);
        DefineFlagGetter(realm, proto, "hasIndices", RegexFlags.HasIndices);

        DefineGetter(realm, proto, "source", (thisV) =>
        {
            var re = RequireRegExp(realm, thisV);
            return JsValue.String(re.Source);
        });
        DefineGetter(realm, proto, "flags", (thisV) =>
        {
            var re = RequireRegExp(realm, thisV);
            return JsValue.String(RegexFlagParser.ToFlagString(re.Flags));
        });

        // Symbol.match / Symbol.replace / Symbol.search / Symbol.split / Symbol.matchAll
        DefineSymbolMethod(realm, proto, SymbolCtor.Match, "[Symbol.match]", 1,
            (thisV, args) => SymbolMatch(realm, thisV, args));
        DefineSymbolMethod(realm, proto, SymbolCtor.MatchAll, "[Symbol.matchAll]", 1,
            (thisV, args) => SymbolMatchAll(realm, thisV, args));
        DefineSymbolMethod(realm, proto, SymbolCtor.Replace, "[Symbol.replace]", 2,
            (thisV, args) => SymbolReplace(realm, thisV, args));
        DefineSymbolMethod(realm, proto, SymbolCtor.Search, "[Symbol.search]", 1,
            (thisV, args) => SymbolSearch(realm, thisV, args));
        DefineSymbolMethod(realm, proto, SymbolCtor.Split, "[Symbol.split]", 2,
            (thisV, args) => SymbolSplit(realm, thisV, args));

        realm.RegExpConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("RegExp",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    // ------------------------------------------------------------------
    //                       Constructor
    // ------------------------------------------------------------------
    private static JsValue Construct(JsRealm realm, JsValue[] args)
    {
        var patternArg = args.Length > 0 ? args[0] : JsValue.Undefined;
        var flagsArg = args.Length > 1 ? args[1] : JsValue.Undefined;
        string source;
        RegexFlags flags;

        if (patternArg.IsObject && patternArg.AsObject is JsRegExp existing)
        {
            source = existing.Source;
            if (flagsArg.IsUndefined) flags = existing.Flags;
            else
            {
                if (!RegexFlagParser.TryParse(JsValue.ToStringValue(flagsArg), out flags, out var err))
                    throw new JsThrow(realm.NewSyntaxError(err!));
            }
        }
        else
        {
            source = patternArg.IsUndefined ? string.Empty : JsValue.ToStringValue(patternArg);
            var flagsStr = flagsArg.IsUndefined ? string.Empty : JsValue.ToStringValue(flagsArg);
            if (!RegexFlagParser.TryParse(flagsStr, out flags, out var err))
                throw new JsThrow(realm.NewSyntaxError(err!));
        }

        CompiledRegex compiled;
        try
        {
            compiled = CompiledRegex.Compile(source, flags);
        }
        catch (RegexSyntaxException ex)
        {
            throw new JsThrow(realm.NewSyntaxError($"Invalid regular expression: /{source}/: {ex.Message}"));
        }
        return JsValue.Object(new JsRegExp(realm, compiled));
    }

    // ------------------------------------------------------------------
    //                       prototype.exec / test
    // ------------------------------------------------------------------
    internal static JsValue Exec(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var re = RequireRegExp(realm, thisV);
        var input = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        var start = 0;
        var advancing = (re.Flags & (RegexFlags.Global | RegexFlags.Sticky)) != 0;
        if (advancing)
        {
            start = (int)System.Math.Max(0, re.LastIndex);
            if (start > input.Length)
            {
                re.LastIndex = 0;
                return JsValue.Null;
            }
        }
        var m = re.Compiled.Exec(input, start);
        if (m is null)
        {
            if (advancing) re.LastIndex = 0;
            return JsValue.Null;
        }
        if (advancing) re.LastIndex = m.End;
        return JsValue.Object(BuildMatchArray(realm, re, m));
    }

    internal static JsValue Test(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var result = Exec(realm, thisV, args);
        return JsValue.Boolean(!result.IsNull);
    }

    // Public-internal helper so JsRegExpStringIterator can build per-match
    // arrays without rerouting through prototype.exec (avoids re-entering the
    // dispatcher just to construct the same object).
    internal static JsArray BuildMatchArrayForIterator(JsRealm realm, JsRegExp re, RegexMatch m)
        => BuildMatchArray(realm, re, m);

    private static JsArray BuildMatchArray(JsRealm realm, JsRegExp re, RegexMatch m)
    {
        var arr = new JsArray(realm);
        // index 0 = full match, then group captures
        arr.Push(JsValue.String(m.Group(0) ?? string.Empty));
        for (var i = 1; i <= re.Compiled.CaptureCount; i++)
        {
            var g = m.Group(i);
            arr.Push(g is null ? JsValue.Undefined : JsValue.String(g));
        }
        // index / input / groups
        arr.DefineOwnProperty("index",
            PropertyDescriptor.Data(JsValue.Number(m.Start), writable: true, enumerable: true, configurable: true));
        arr.DefineOwnProperty("input",
            PropertyDescriptor.Data(JsValue.String(m.Input), writable: true, enumerable: true, configurable: true));
        JsValue groups = JsValue.Undefined;
        if (re.Compiled.NamedCaptures.Count > 0)
        {
            var g = realm.NewOrdinaryObject();
            foreach (var (name, idx) in re.Compiled.NamedCaptures)
            {
                var text = m.Group(idx);
                g.DefineOwnProperty(name,
                    PropertyDescriptor.Data(text is null ? JsValue.Undefined : JsValue.String(text),
                        writable: true, enumerable: true, configurable: true));
            }
            groups = JsValue.Object(g);
        }
        arr.DefineOwnProperty("groups",
            PropertyDescriptor.Data(groups, writable: true, enumerable: true, configurable: true));

        if ((re.Flags & RegexFlags.HasIndices) != 0)
        {
            var indicesArr = new JsArray(realm);
            for (var i = 0; i <= re.Compiled.CaptureCount; i++)
            {
                var span = m.GroupSpan(i);
                if (span is null) indicesArr.Push(JsValue.Undefined);
                else
                {
                    var pair = new JsArray(realm);
                    pair.Push(JsValue.Number(span.Value.Start));
                    pair.Push(JsValue.Number(span.Value.End));
                    indicesArr.Push(JsValue.Object(pair));
                }
            }
            arr.DefineOwnProperty("indices",
                PropertyDescriptor.Data(JsValue.Object(indicesArr), writable: true, enumerable: true, configurable: true));
        }
        return arr;
    }

    // ------------------------------------------------------------------
    //                       Symbol.* protocols
    // ------------------------------------------------------------------
    private static JsValue SymbolMatch(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var re = RequireRegExp(realm, thisV);
        var s = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        if ((re.Flags & RegexFlags.Global) == 0)
        {
            return Exec(realm, thisV, new[] { JsValue.String(s) });
        }
        // Global: collect every match's [0]; set lastIndex to 0 at end; return
        // null if no matches found.
        re.LastIndex = 0;
        var results = new JsArray(realm);
        int pos = 0;
        while (true)
        {
            var m = re.Compiled.Exec(s, pos);
            if (m is null) break;
            results.Push(JsValue.String(m.Group(0) ?? string.Empty));
            pos = m.End;
            if (m.End == m.Start) pos++; // zero-width safety
        }
        re.LastIndex = 0;
        return results.Length == 0 ? JsValue.Null : JsValue.Object(results);
    }

    private static JsValue SymbolMatchAll(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var re = RequireRegExp(realm, thisV);
        if ((re.Flags & RegexFlags.Global) == 0)
            throw new JsThrow(realm.NewTypeError("matchAll requires a global regular expression"));
        var s = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        // Iterator protocol lands in B3-2; for now return an array of all matches.
        var arr = new JsArray(realm);
        int pos = 0;
        while (true)
        {
            var m = re.Compiled.Exec(s, pos);
            if (m is null) break;
            arr.Push(JsValue.Object(BuildMatchArray(realm, re, m)));
            pos = m.End;
            if (m.End == m.Start) pos++;
        }
        return JsValue.Object(arr);
    }

    private static JsValue SymbolReplace(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var re = RequireRegExp(realm, thisV);
        var s = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        var replacement = args.Length > 1 ? args[1] : JsValue.Undefined;
        bool functional = AbstractOperations.IsCallable(replacement);
        string replStr = functional ? null! : JsValue.ToStringValue(replacement);
        bool global = (re.Flags & RegexFlags.Global) != 0;

        var sb = new StringBuilder();
        int pos = 0;
        while (pos <= s.Length)
        {
            var m = re.Compiled.Exec(s, pos);
            if (m is null) break;
            sb.Append(s, pos, m.Start - pos);
            string result;
            if (functional)
            {
                var fnArgs = new List<JsValue> { JsValue.String(m.Group(0) ?? string.Empty) };
                for (var i = 1; i <= re.Compiled.CaptureCount; i++)
                {
                    var g = m.Group(i);
                    fnArgs.Add(g is null ? JsValue.Undefined : JsValue.String(g));
                }
                fnArgs.Add(JsValue.Number(m.Start));
                fnArgs.Add(JsValue.String(s));
                var r = AbstractOperations.Call(realm.ActiveVm, replacement, JsValue.Undefined, fnArgs.ToArray());
                result = JsValue.ToStringValue(r);
            }
            else
            {
                result = GetSubstitution(replStr, m, s, re);
            }
            sb.Append(result);
            if (m.End > pos) pos = m.End;
            else { if (m.End < s.Length) sb.Append(s[m.End]); pos = m.End + 1; }
            if (!global) break;
        }
        if (pos < s.Length) sb.Append(s, pos, s.Length - pos);
        return JsValue.String(sb.ToString());
    }

    private static string GetSubstitution(string replacement, RegexMatch m, string whole, JsRegExp re)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < replacement.Length; i++)
        {
            if (replacement[i] != '$' || i + 1 >= replacement.Length)
            {
                sb.Append(replacement[i]);
                continue;
            }
            var next = replacement[i + 1];
            switch (next)
            {
                case '$': sb.Append('$'); i++; break;
                case '&': sb.Append(m.Group(0)); i++; break;
                case '`': sb.Append(whole, 0, m.Start); i++; break;
                case '\'':
                    sb.Append(whole, m.End, whole.Length - m.End); i++; break;
                case '<':
                    {
                        var close = replacement.IndexOf('>', i + 2);
                        if (close < 0) { sb.Append('$'); break; }
                        var name = replacement.Substring(i + 2, close - (i + 2));
                        if (re.Compiled.NamedCaptures.TryGetValue(name, out var ni))
                        {
                            var g = m.Group(ni);
                            if (g is not null) sb.Append(g);
                        }
                        i = close;
                        break;
                    }
                default:
                    if (next >= '0' && next <= '9')
                    {
                        // Try two-digit group first
                        int n = next - '0';
                        int consumed = 1;
                        if (i + 2 < replacement.Length && replacement[i + 2] >= '0' && replacement[i + 2] <= '9')
                        {
                            var n2 = n * 10 + (replacement[i + 2] - '0');
                            if (n2 <= re.Compiled.CaptureCount)
                            {
                                n = n2;
                                consumed = 2;
                            }
                        }
                        if (n > 0 && n <= re.Compiled.CaptureCount)
                        {
                            var g = m.Group(n);
                            if (g is not null) sb.Append(g);
                            i += consumed;
                        }
                        else
                        {
                            sb.Append('$').Append(next);
                            i++;
                        }
                    }
                    else
                    {
                        sb.Append('$').Append(next);
                        i++;
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    private static JsValue SymbolSearch(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var re = RequireRegExp(realm, thisV);
        var s = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        // search ignores lastIndex; it always starts at 0 and does not advance.
        var savedLastIndex = re.LastIndex;
        re.LastIndex = 0;
        var m = re.Compiled.Exec(s, 0);
        re.LastIndex = savedLastIndex;
        return JsValue.Number(m is null ? -1 : m.Start);
    }

    private static JsValue SymbolSplit(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var re = RequireRegExp(realm, thisV);
        var s = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        var limit = args.Length > 1 && !args[1].IsUndefined ? (uint)System.Math.Max(0, (int)JsValue.ToNumber(args[1])) : uint.MaxValue;
        var arr = new JsArray(realm);
        if (limit == 0) return JsValue.Object(arr);
        if (s.Length == 0)
        {
            // Per spec, if pattern matches empty string return [], else [""]
            var m0 = re.Compiled.Exec(s, 0);
            if (m0 is null) arr.Push(JsValue.String(string.Empty));
            return JsValue.Object(arr);
        }
        int pos = 0;
        int prev = 0;
        while (pos < s.Length)
        {
            var m = re.Compiled.Exec(s, pos);
            if (m is null) break;
            if (m.End == prev) { pos++; continue; } // zero-width: advance one
            arr.Push(JsValue.String(s[prev..m.Start]));
            if (arr.Length >= limit) return JsValue.Object(arr);
            // Push captures
            for (var i = 1; i <= re.Compiled.CaptureCount; i++)
            {
                var g = m.Group(i);
                arr.Push(g is null ? JsValue.Undefined : JsValue.String(g));
                if (arr.Length >= limit) return JsValue.Object(arr);
            }
            prev = m.End;
            pos = m.End;
        }
        arr.Push(JsValue.String(s[prev..]));
        return JsValue.Object(arr);
    }

    // ------------------------------------------------------------------
    //                          Helpers
    // ------------------------------------------------------------------

    /// <summary>Compile a JS pattern string with default flags. Used by
    /// String.prototype methods to coerce non-RegExp first arguments.</summary>
    public static JsRegExp Create(JsRealm realm, string source, string flags = "")
    {
        if (!RegexFlagParser.TryParse(flags, out var f, out var err))
            throw new JsThrow(realm.NewSyntaxError(err!));
        CompiledRegex compiled;
        try
        {
            compiled = CompiledRegex.Compile(source, f);
        }
        catch (RegexSyntaxException ex)
        {
            throw new JsThrow(realm.NewSyntaxError($"Invalid regular expression: /{source}/: {ex.Message}"));
        }
        return new JsRegExp(realm, compiled);
    }

    public static bool IsRegExp(JsValue v) => v.IsObject && v.AsObject is JsRegExp;

    private static JsRegExp RequireRegExp(JsRealm realm, JsValue v)
    {
        if (v.IsObject && v.AsObject is JsRegExp r) return r;
        throw new JsThrow(realm.NewTypeError("Method called on non-RegExp object"));
    }

    private static void DefineFlagGetter(JsRealm realm, JsObject target, string name, RegexFlags flag)
    {
        DefineGetter(realm, target, name, (thisV) =>
        {
            if (thisV.IsObject && thisV.AsObject is JsRegExp r)
                return JsValue.Boolean((r.Flags & flag) != 0);
            throw new JsThrow(realm.NewTypeError($"Getter '{name}' called on non-RegExp"));
        });
    }

    private static void DefineGetter(JsRealm realm, JsObject target, string name, System.Func<JsValue, JsValue> body)
    {
        var fn = new JsNativeFunction(realm, "get " + name, 0, (thisV, _) => body(thisV), isConstructor: false);
        target.DefineOwnProperty(name, PropertyDescriptor.Accessor(fn, null, enumerable: false, configurable: true));
    }

    private static void DefineSymbolMethod(JsRealm realm, JsObject target, JsSymbol key, string name, int length, System.Func<JsValue, JsValue[], JsValue> body)
    {
        var fn = new JsNativeFunction(realm, name, length, body, isConstructor: false);
        target.DefineOwnProperty(key, PropertyDescriptor.BuiltinMethod(JsValue.Object(fn)));
    }
}

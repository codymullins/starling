using System.Text;
using Starling.Js.RegExp;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

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

        var ctor = new JsNativeFunction(realm, "RegExp", length: 2,
            (newTarget, args) => Construct(realm,
                IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto), args),
            isConstructor: true);
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
    private static JsValue Construct(JsRealm realm, JsObject instProto, JsValue[] args)
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
        var re = new JsRegExp(realm, compiled);
        if (!ReferenceEquals(instProto, realm.RegExpPrototype)) re.SetPrototypeOf(instProto);
        return JsValue.Object(re);
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
    // §22.2.6.8 RegExp.prototype [ @@match ] ( string ). Each match MUST be
    // obtained through RegExpExec(R, S) (§22.2.7.1), which honors the receiver's
    // (possibly overridden) `exec`; the global/unicode flags are read generically
    // via Get. The previous implementation re-matched through the internal
    // engine and ignored a user `exec` — core-js's DELEGATES_TO_EXEC
    // feature-detect saw that as a broken native and installed a recursing
    // @@match polyfill (jQuery↔core-js infinite recursion → RangeError).
    private static JsValue SymbolMatch(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        // Step 2: `this` must be an Object (need not be a genuine RegExp — a
        // user object whose `exec`/`global`/`unicode` we read is allowed).
        if (!thisV.IsObject)
            throw new JsThrow(realm.NewTypeError("RegExp.prototype[Symbol.match] called on non-object"));
        var rx = thisV.AsObject;
        var vm = realm.ActiveVm;
        var s = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";

        // Step 5: global flag.
        bool global = JsValue.ToBoolean(AbstractOperations.Get(vm, rx, "global"));
        if (!global)
        {
            // Step 6: non-global — return RegExpExec(rx, S) directly.
            return RegExpExec(realm, rx, s);
        }

        // Step 7: global — read the unicode flag, reset lastIndex, then loop
        // RegExpExec collecting each result's [0].
        bool fullUnicode = JsValue.ToBoolean(AbstractOperations.Get(vm, rx, "unicode"));
        AbstractOperations.Set(vm, rx, "lastIndex", JsValue.Number(0));
        var results = new JsArray(realm);
        while (true)
        {
            var result = RegExpExec(realm, rx, s);
            if (result.IsNull) break;
            var matchStr = JsValue.ToStringValue(
                AbstractOperations.Get(vm, result.AsObject, "0"));
            results.Push(JsValue.String(matchStr));
            // Step 7.g.iv: empty match → advance lastIndex so the loop terminates.
            if (matchStr.Length == 0)
            {
                var li = (int)ToLengthLocal(AbstractOperations.Get(vm, rx, "lastIndex"));
                AbstractOperations.Set(vm, rx, "lastIndex",
                    JsValue.Number(AdvanceStringIndex(s, li, fullUnicode)));
            }
        }
        return results.Length == 0 ? JsValue.Null : JsValue.Object(results);
    }

    // §22.2.7.3 AdvanceStringIndex — step over a full code point when the
    // `unicode` flag is set and `index` sits on a leading surrogate.
    private static int AdvanceStringIndex(string s, int index, bool unicode)
    {
        if (!unicode || index + 1 >= s.Length) return index + 1;
        var first = s[index];
        if (first < 0xD800 || first > 0xDBFF) return index + 1;
        var second = s[index + 1];
        if (second < 0xDC00 || second > 0xDFFF) return index + 1;
        return index + 2;
    }

    // §22.2.6.9 RegExp.prototype [ @@matchAll ] ( string ). Returns a
    // RegExpStringIterator (the same iterator String.prototype.matchAll
    // builds), so delegating String#matchAll through this method preserves the
    // iterator shape (Array.isArray(...) === false) the spec mandates.
    private static JsValue SymbolMatchAll(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var re = RequireRegExp(realm, thisV);
        if ((re.Flags & RegexFlags.Global) == 0)
            throw new JsThrow(realm.NewTypeError("matchAll requires a global regular expression"));
        var s = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        var unicode = (re.Flags & RegexFlags.Unicode) != 0;
        return JsValue.Object(new JsRegExpStringIterator(realm, re, s, global: true, unicode: unicode));
    }

    // §22.2.7.1 RegExpExec ( R, S ): read R.exec; if callable, call it and
    // require an Object-or-null result; otherwise fall back to the built-in
    // RegExp.prototype.exec. Honoring the user-visible `exec` property is what
    // lets a subclass / monkeypatched exec drive @@replace/@@match/@@split —
    // core-js feature-detects this delegation (DELEGATES_TO_EXEC) and installs
    // a recursing polyfill when it is missing.
    internal static JsValue RegExpExec(JsRealm realm, JsObject r, string s)
    {
        var vm = realm.ActiveVm;
        var exec = AbstractOperations.Get(vm, r, "exec");
        if (AbstractOperations.IsCallable(exec))
        {
            var result = AbstractOperations.Call(vm, exec, JsValue.Object(r),
                new[] { JsValue.String(s) });
            if (!result.IsObject && !result.IsNull)
                throw new JsThrow(realm.NewTypeError(
                    "RegExp exec method returned something other than an Object or null"));
            return result;
        }
        // Fallback: must be a real RegExp to run the built-in.
        if (r is not JsRegExp)
            throw new JsThrow(realm.NewTypeError("RegExp#exec called on incompatible receiver"));
        return Exec(realm, JsValue.Object(r), new[] { JsValue.String(s) });
    }

    private static JsValue SymbolReplace(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        // §22.2.6.11 RegExp.prototype[@@replace]. `this` must be an Object but
        // need not be a genuine RegExp (it can be a user object whose `exec`
        // and `global` we read), so we do not RequireRegExp here.
        if (!thisV.IsObject)
            throw new JsThrow(realm.NewTypeError("RegExp.prototype[Symbol.replace] called on non-object"));
        var rx = thisV.AsObject;
        var vm = realm.ActiveVm;
        var s = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "undefined";
        var replacement = args.Length > 1 ? args[1] : JsValue.Undefined;
        bool functional = AbstractOperations.IsCallable(replacement);
        string replStr = functional ? null! : JsValue.ToStringValue(replacement);

        bool global = JsValue.ToBoolean(AbstractOperations.Get(vm, rx, "global"));
        if (global) rx.Set("lastIndex", JsValue.Number(0));

        // Step 14: gather every match through RegExpExec (which calls rx.exec).
        var results = new List<JsValue>();
        while (true)
        {
            var result = RegExpExec(realm, rx, s);
            if (result.IsNull) break;
            results.Add(result);
            if (!global) break;
            // Empty-match advance (§22.2.6.11 step 14.d.iii).
            var resObj = result.AsObject;
            var matchStr = JsValue.ToStringValue(AbstractOperations.Get(vm, resObj, "0"));
            if (matchStr.Length == 0)
            {
                var li = (int)ToLengthLocal(AbstractOperations.Get(vm, rx, "lastIndex"));
                rx.Set("lastIndex", JsValue.Number(li + 1));
            }
        }

        var sb = new StringBuilder();
        int nextSourcePosition = 0;
        foreach (var result in results)
        {
            var resObj = result.AsObject;
            var matched = JsValue.ToStringValue(AbstractOperations.Get(vm, resObj, "0"));
            int matchLength = matched.Length;
            // position = clamp(ToInteger(result.index), 0, s.Length)
            var rawIndex = (int)JsValue.ToNumber(AbstractOperations.Get(vm, resObj, "index"));
            int position = System.Math.Max(0, System.Math.Min(rawIndex, s.Length));
            // Captures: result[1 .. length-1].
            int nCaptures = System.Math.Max(0,
                (int)ToLengthLocal(AbstractOperations.Get(vm, resObj, "length")) - 1);
            var captures = new List<JsValue>(nCaptures);
            for (var n = 1; n <= nCaptures; n++)
            {
                var cap = AbstractOperations.Get(vm, resObj, n.ToString(System.Globalization.CultureInfo.InvariantCulture));
                captures.Add(cap.IsUndefined ? JsValue.Undefined : JsValue.String(JsValue.ToStringValue(cap)));
            }
            var namedCaptures = AbstractOperations.Get(vm, resObj, "groups");

            string replacementText;
            if (functional)
            {
                var fnArgs = new List<JsValue> { JsValue.String(matched) };
                fnArgs.AddRange(captures);
                fnArgs.Add(JsValue.Number(position));
                fnArgs.Add(JsValue.String(s));
                if (!namedCaptures.IsUndefined) fnArgs.Add(namedCaptures);
                var r = AbstractOperations.Call(vm, replacement, JsValue.Undefined, fnArgs.ToArray());
                replacementText = JsValue.ToStringValue(r);
            }
            else
            {
                replacementText = GetSubstitution(realm, replStr, matched, s, position, captures, namedCaptures);
            }

            if (position >= nextSourcePosition)
            {
                sb.Append(s, nextSourcePosition, position - nextSourcePosition);
                sb.Append(replacementText);
                nextSourcePosition = position + matchLength;
            }
        }
        if (nextSourcePosition < s.Length)
            sb.Append(s, nextSourcePosition, s.Length - nextSourcePosition);
        return JsValue.String(sb.ToString());
    }

    private static double ToLengthLocal(JsValue v)
    {
        var n = JsValue.ToNumber(v);
        if (double.IsNaN(n) || n <= 0) return 0;
        return System.Math.Min(System.Math.Floor(n), 9007199254740991d);
    }

    // §22.2.6.11.1 GetSubstitution — operates on the captures list + namedCaptures
    // object pulled from the exec result, per spec (not on the raw RegexMatch).
    private static string GetSubstitution(JsRealm realm, string replacement, string matched,
        string str, int position, List<JsValue> captures, JsValue namedCaptures)
    {
        var vm = realm.ActiveVm;
        int tailPos = position + matched.Length;
        int m = captures.Count;
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
                case '&': sb.Append(matched); i++; break;
                case '`': sb.Append(str, 0, position); i++; break;
                case '\'': sb.Append(str, tailPos, str.Length - tailPos); i++; break;
                case '<':
                    {
                        // Named captures only honored when groups is not undefined
                        // (§22.2.6.11.1 step 11). Otherwise "$<" is literal.
                        if (namedCaptures.IsUndefined) { sb.Append('$'); i++; break; }
                        var close = replacement.IndexOf('>', i + 2);
                        if (close < 0) { sb.Append('$'); i++; break; }
                        var name = replacement.Substring(i + 2, close - (i + 2));
                        var groupsObj = AbstractOperations.ToObject(realm, namedCaptures);
                        var cap = AbstractOperations.Get(vm, groupsObj, name);
                        if (!cap.IsUndefined) sb.Append(JsValue.ToStringValue(cap));
                        i = close;
                        break;
                    }
                default:
                    if (next >= '0' && next <= '9')
                    {
                        int n = next - '0';
                        int consumed = 1;
                        if (i + 2 < replacement.Length && replacement[i + 2] >= '0' && replacement[i + 2] <= '9')
                        {
                            var n2 = n * 10 + (replacement[i + 2] - '0');
                            if (n2 >= 1 && n2 <= m) { n = n2; consumed = 2; }
                        }
                        if (n >= 1 && n <= m)
                        {
                            var cap = captures[n - 1];
                            if (!cap.IsUndefined) sb.Append(JsValue.ToStringValue(cap));
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

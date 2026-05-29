using Starling.Dom;
using Starling.Js.Runtime;

namespace Starling.Bindings;

/// <summary>
/// Web Animations API §4 JS surface: <c>element.animate(keyframes, options)</c>
/// returning an <c>Animation</c> with an associated <c>KeyframeEffect</c>.
/// Keyframes + timing are translated into the neutral <see cref="IAnimationHost"/>
/// payloads and registered with the engine, which renders them through the same
/// compositor the declarative <c>@keyframes</c> path uses. With no host installed
/// the Animation object is still returned with inert (no-op) controls.
/// </summary>
internal static class WebAnimationsBinding
{
    /// <summary>Implements <c>element.animate(keyframes, options)</c>.</summary>
    public static JsValue Animate(JsRealm realm, Element element, JsValue[] args)
    {
        var keyframes = ParseKeyframes(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var timing = ParseTiming(args.Length > 1 ? args[1] : JsValue.Undefined);

        var host = WindowBinding.AnimationHostForRealm(realm);
        var id = host?.Animate(element, keyframes, timing) ?? -1;
        return JsValue.Object(BuildAnimation(realm, host, id, keyframes, timing));
    }

    // ----- keyframe parsing --------------------------------------------------

    private static IReadOnlyList<AnimationKeyframeSpec> ParseKeyframes(JsRealm realm, JsValue value)
    {
        if (!value.IsObject) return Array.Empty<AnimationKeyframeSpec>();

        // Array form: [{opacity:0, offset:0}, {opacity:1}].
        if (JsArray.IsArray(value))
        {
            var arr = (JsArray)value.AsObject;
            var frames = new List<(double? Offset, List<KeyValuePair<string, string>> Decls)>(arr.Length);
            for (var i = 0; i < arr.Length; i++)
            {
                var item = arr[i];
                if (!item.IsObject) continue;
                var obj = item.AsObject;
                double? offset = null;
                var off = obj.Get("offset");
                if (off.IsNumber) offset = off.AsNumber;
                var decls = new List<KeyValuePair<string, string>>();
                foreach (var key in obj.EnumerableKeys())
                {
                    if (key is "offset" or "easing" or "composite") continue;
                    decls.Add(new(CamelToKebab(key), CssText(realm, obj.Get(key))));
                }
                frames.Add((offset, decls));
            }
            return DistributeOffsets(frames);
        }

        // Property-indexed form: {opacity:[0,1], transform:['none','scale(2)']}.
        var src = value.AsObject;
        var props = new List<(string Name, List<string> Values)>();
        var maxLen = 0;
        foreach (var key in src.EnumerableKeys())
        {
            if (key is "offset" or "easing" or "composite") continue;
            var v = src.Get(key);
            var values = new List<string>();
            if (JsArray.IsArray(v))
            {
                var a = (JsArray)v.AsObject;
                for (var i = 0; i < a.Length; i++) values.Add(CssText(realm, a[i]));
            }
            else
            {
                values.Add(CssText(realm, v));
            }
            if (values.Count > 0) { props.Add((CamelToKebab(key), values)); maxLen = Math.Max(maxLen, values.Count); }
        }
        if (maxLen == 0) return Array.Empty<AnimationKeyframeSpec>();

        var result = new List<AnimationKeyframeSpec>(maxLen);
        for (var j = 0; j < maxLen; j++)
        {
            var offset = maxLen == 1 ? 1.0 : (double)j / (maxLen - 1);
            var decls = new List<KeyValuePair<string, string>>(props.Count);
            foreach (var (name, values) in props)
                decls.Add(new(name, values[Math.Min(j, values.Count - 1)]));
            result.Add(new AnimationKeyframeSpec(offset, decls));
        }
        return result;
    }

    private static List<AnimationKeyframeSpec> DistributeOffsets(
        List<(double? Offset, List<KeyValuePair<string, string>> Decls)> frames)
    {
        var n = frames.Count;
        var result = new List<AnimationKeyframeSpec>(n);
        for (var i = 0; i < n; i++)
        {
            var offset = frames[i].Offset
                ?? (n == 1 ? 1.0 : (double)i / (n - 1));
            result.Add(new AnimationKeyframeSpec(Math.Clamp(offset, 0, 1), frames[i].Decls));
        }
        return result;
    }

    // ----- timing parsing ----------------------------------------------------

    private static AnimationEffectTimingSpec ParseTiming(JsValue value)
    {
        double duration = 0, delay = 0, iterations = 1;
        string direction = "normal", fill = "none", easing = "linear";

        if (value.IsNumber)
        {
            duration = value.AsNumber;
        }
        else if (value.IsObject)
        {
            var o = value.AsObject;
            var dur = o.Get("duration");
            if (dur.IsNumber) duration = dur.AsNumber;
            var del = o.Get("delay");
            if (del.IsNumber) delay = del.AsNumber;
            var iter = o.Get("iterations");
            if (iter.IsNumber) iterations = iter.AsNumber;
            var dir = o.Get("direction"); if (!dir.IsNullish) direction = JsValue.ToStringValue(dir);
            var fl = o.Get("fill"); if (!fl.IsNullish) fill = JsValue.ToStringValue(fl);
            var ea = o.Get("easing"); if (!ea.IsNullish) easing = JsValue.ToStringValue(ea);
        }

        return new AnimationEffectTimingSpec(duration, delay, iterations, direction, fill, easing);
    }

    // ----- Animation / KeyframeEffect objects --------------------------------

    private static JsObject BuildAnimation(
        JsRealm realm, IAnimationHost? host, int id,
        IReadOnlyList<AnimationKeyframeSpec> keyframes, AnimationEffectTimingSpec timing)
    {
        var anim = new JsObject(realm.ObjectPrototype);

        EventTargetBinding.DefineMethod(realm, anim, "play", (_, _) => { host?.Play(id); return JsValue.Undefined; }, length: 0);
        EventTargetBinding.DefineMethod(realm, anim, "pause", (_, _) => { host?.Pause(id); return JsValue.Undefined; }, length: 0);
        EventTargetBinding.DefineMethod(realm, anim, "cancel", (_, _) => { host?.Cancel(id); return JsValue.Undefined; }, length: 0);
        EventTargetBinding.DefineMethod(realm, anim, "finish", (_, _) => { host?.Finish(id); return JsValue.Undefined; }, length: 0);
        EventTargetBinding.DefineMethod(realm, anim, "reverse", (_, _) => { host?.Play(id); return JsValue.Undefined; }, length: 0);

        EventTargetBinding.DefineAccessor(realm, anim, "currentTime",
            (_, _) => JsValue.Number(host?.CurrentTime(id) ?? 0),
            (_, a) => { if (host is not null && a.Length > 0 && a[0].IsNumber) host.SetCurrentTime(id, a[0].AsNumber); return JsValue.Undefined; });
        EventTargetBinding.DefineAccessor(realm, anim, "startTime", (_, _) => JsValue.Number(host?.TimelineNow ?? 0));
        EventTargetBinding.DefineAccessor(realm, anim, "playState", (_, _) => JsValue.String(host?.PlayState(id) ?? "idle"));
        EventTargetBinding.DefineAccessor(realm, anim, "playbackRate", (_, _) => JsValue.Number(1));
        EventTargetBinding.DefineAccessor(realm, anim, "id", (_, _) => JsValue.String(id < 0 ? "" : id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        // finished/ready: resolved promises (event/promise integration is a follow-up).
        EventTargetBinding.DefineAccessor(realm, anim, "finished", (_, _) => FetchBinding.ResolvedPromise(realm, JsValue.Object(anim)));
        EventTargetBinding.DefineAccessor(realm, anim, "ready", (_, _) => FetchBinding.ResolvedPromise(realm, JsValue.Object(anim)));
        anim.DefineOwnProperty("onfinish", PropertyDescriptor.Data(JsValue.Null, writable: true, enumerable: true, configurable: true));
        anim.DefineOwnProperty("oncancel", PropertyDescriptor.Data(JsValue.Null, writable: true, enumerable: true, configurable: true));

        anim.DefineOwnProperty("effect",
            PropertyDescriptor.Data(JsValue.Object(BuildKeyframeEffect(realm, keyframes, timing)),
                writable: true, enumerable: true, configurable: true));

        // EventTarget surface — inert (finish/cancel events not yet fired).
        foreach (var m in new[] { "addEventListener", "removeEventListener" })
            EventTargetBinding.DefineMethod(realm, anim, m, (_, _) => JsValue.Undefined, length: 2);

        return anim;
    }

    private static JsObject BuildKeyframeEffect(
        JsRealm realm, IReadOnlyList<AnimationKeyframeSpec> keyframes, AnimationEffectTimingSpec timing)
    {
        var effect = new JsObject(realm.ObjectPrototype);

        EventTargetBinding.DefineMethod(realm, effect, "getKeyframes", (_, _) =>
        {
            var items = new List<JsValue>(keyframes.Count);
            foreach (var kf in keyframes)
            {
                var o = new JsObject(realm.ObjectPrototype);
                o.DefineOwnProperty("offset", PropertyDescriptor.Data(JsValue.Number(kf.Offset), writable: true, enumerable: true, configurable: true));
                foreach (var d in kf.Declarations)
                    o.DefineOwnProperty(KebabToCamel(d.Key), PropertyDescriptor.Data(JsValue.String(d.Value), writable: true, enumerable: true, configurable: true));
                items.Add(JsValue.Object(o));
            }
            return JsValue.Object(new JsArray(realm, items));
        }, length: 0);

        EventTargetBinding.DefineMethod(realm, effect, "getTiming", (_, _) => JsValue.Object(BuildTiming(realm, timing, computed: false)), length: 0);
        EventTargetBinding.DefineMethod(realm, effect, "getComputedTiming", (_, _) => JsValue.Object(BuildTiming(realm, timing, computed: true)), length: 0);

        return effect;
    }

    private static JsObject BuildTiming(JsRealm realm, AnimationEffectTimingSpec t, bool computed)
    {
        var o = new JsObject(realm.ObjectPrototype);
        void Num(string k, double v) => o.DefineOwnProperty(k, PropertyDescriptor.Data(JsValue.Number(v), writable: true, enumerable: true, configurable: true));
        void Str(string k, string v) => o.DefineOwnProperty(k, PropertyDescriptor.Data(JsValue.String(v), writable: true, enumerable: true, configurable: true));
        Num("delay", t.DelayMs);
        Num("endDelay", 0);
        Num("duration", t.DurationMs);
        Num("iterations", t.Iterations);
        Num("iterationStart", 0);
        Str("direction", t.Direction);
        Str("fill", t.Fill);
        Str("easing", t.Easing);
        if (computed)
        {
            var active = t.DurationMs * (double.IsInfinity(t.Iterations) ? 1 : Math.Max(0, t.Iterations));
            Num("activeDuration", active);
            Num("endTime", t.DelayMs + active);
        }
        return o;
    }

    // ----- helpers -----------------------------------------------------------

    private static string CssText(JsRealm realm, JsValue v)
    {
        if (v.IsObject)
        {
            var ts = v.AsObject.Get("toString");
            if (AbstractOperations.IsCallable(ts))
                return JsValue.ToStringValue(AbstractOperations.Call(realm.ActiveVm, ts, v, Array.Empty<JsValue>())).Trim();
        }
        return JsValue.ToStringValue(v).Trim();
    }

    private static string CamelToKebab(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsUpper(c)) { if (i > 0) sb.Append('-'); sb.Append(char.ToLowerInvariant(c)); }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string KebabToCamel(string s)
    {
        if (s.IndexOf('-') < 0) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        var upper = false;
        foreach (var c in s)
        {
            if (c == '-') { upper = true; continue; }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        return sb.ToString();
    }
}

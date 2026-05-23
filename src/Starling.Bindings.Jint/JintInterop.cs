using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using Starling.Js.Hosting;

namespace Starling.Bindings.Jint;

/// <summary>
/// Low-level Jint interop helpers shared by every binding family. These wrap
/// Jint's <see cref="ClrFunction"/> / <see cref="PropertyDescriptor"/> /
/// <see cref="GetSetPropertyDescriptor"/> so Wave-2 code defines Web-IDL-correct
/// methods, accessors, and data properties without re-deriving the descriptor
/// flags each time.
/// </summary>
/// <remarks>
/// FROZEN J2a contract — the helper signatures below match
/// <c>DESIGN.md</c> ("Helpers"). We define explicit prototypes/properties rather
/// than relying on Jint's CLR auto-interop, because reflection over CLR objects
/// produces wrong property names, enumerability, and identity (see the Web-IDL
/// fidelity rules in DESIGN.md).
/// </remarks>
public static class JintInterop
{
    /// <summary>Define a method <paramref name="name"/> on
    /// <paramref name="proto"/> backed by <paramref name="body"/>
    /// (<c>(thisValue, args) =&gt; result</c>) with the given <c>length</c>.
    /// The method property is writable + configurable + non-enumerable, matching
    /// Web-IDL operation semantics.</summary>
    public static void DefineMethod(
        global::Jint.Engine engine, ObjectInstance proto, string name,
        Func<JsValue, JsValue[], JsValue> body, int length)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(proto);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(body);

        var fn = new ClrFunction(engine, name, (thisVal, args) => body(thisVal, args), length, PropertyFlag.Configurable);
        // Web-IDL operations are { writable: true, enumerable: false, configurable: true }.
        proto.FastSetProperty(name, new PropertyDescriptor(fn, writable: true, enumerable: false, configurable: true));
    }

    /// <summary>Define an accessor property <paramref name="name"/> on
    /// <paramref name="proto"/>. <paramref name="getter"/> is required;
    /// <paramref name="setter"/> may be <c>null</c> for a read-only attribute.
    /// Web-IDL attributes are enumerable + configurable.</summary>
    public static void DefineAccessor(
        global::Jint.Engine engine, ObjectInstance proto, string name,
        Func<JsValue, JsValue[], JsValue> getter,
        Func<JsValue, JsValue[], JsValue>? setter = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(proto);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(getter);

        var get = new ClrFunction(engine, "get " + name, (thisVal, args) => getter(thisVal, args), 0, PropertyFlag.Configurable);
        JsValue set = setter is null
            ? JsValue.Undefined
            : new ClrFunction(engine, "set " + name, (thisVal, args) => setter(thisVal, args), 1, PropertyFlag.Configurable);

        // Web-IDL attributes are { enumerable: true, configurable: true }.
        proto.FastSetProperty(name, new GetSetPropertyDescriptor(get, set, enumerable: true, configurable: true));
    }

    /// <summary>Define a plain data property <paramref name="name"/> =
    /// <paramref name="value"/> on <paramref name="target"/>. Defaults match the
    /// ordinary writable/enumerable/configurable data-property shape.</summary>
    public static void DefineDataProp(
        ObjectInstance target, string name, JsValue value,
        bool writable = true, bool enumerable = true, bool configurable = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        target.FastSetProperty(name, new PropertyDescriptor(value, writable, enumerable, configurable));
    }

    // ---- value helpers (thin, so Wave-2 call sites read clearly) ----

    public static JsValue Str(string? s) => s is null ? JsValue.Null : new JsString(s);
    public static JsValue Num(double d) => new JsNumber(d);
    public static JsValue Bool(bool b) => b ? JsBoolean.True : JsBoolean.False;

    /// <summary>Normalize a Jint <see cref="global::Jint.Runtime.JavaScriptException"/>
    /// into the engine-neutral <see cref="ScriptThrow"/>, preserving the JS-side
    /// stack trace. Mirrors the Starling backend's JsThrow→ScriptThrow mapping so
    /// the engine's fail-soft logging path is identical across engines.</summary>
    public static ScriptThrow Normalize(global::Jint.Runtime.JavaScriptException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return new ScriptThrow(DescribeError(ex.Error, ex.Message), ex.JavaScriptStackTrace, ex);
    }

    /// <summary>Render a thrown JS value legibly, pulling
    /// <c>name</c>/<c>message</c> out of Error objects (otherwise an Error
    /// stringifies to "[object Object]").</summary>
    public static string DescribeError(JsValue error, string fallback)
    {
        try
        {
            if (error is ObjectInstance o)
            {
                var name = o.Get("name");
                var message = o.Get("message");
                if (!name.IsUndefined() || !message.IsUndefined())
                {
                    var n = name.IsUndefined() ? "Error" : name.ToString();
                    var m = message.IsUndefined() ? "" : message.ToString();
                    return string.IsNullOrEmpty(m) ? n : $"{n}: {m}";
                }
            }
            return error.ToString();
        }
        catch
        {
            return fallback;
        }
    }
}

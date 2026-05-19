using Tessera.Js.Runtime;

namespace Tessera.Js.Intrinsics;

/// <summary>§21.2 Boolean Objects. Installs Boolean and Boolean.prototype.</summary>
public static class BooleanCtor
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var proto = realm.BooleanPrototype;

        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction("Boolean", (thisV, args) =>
        {
            var b = JsValue.ToBoolean(args.Length > 0 ? args[0] : JsValue.Undefined);
            if (thisV.IsObject && ReferenceEquals(thisV.AsObject, ctor))
                return JsValue.Object(realm.BoxBoolean(JsValue.Boolean(b)));
            return JsValue.Boolean(b);
        }, isConstructor: true);
        ctor.SetPrototypeOf(realm.FunctionPrototype);
        DefineData(ctor, "prototype", JsValue.Object(proto), false, false, false);
        DefineData(ctor, "name", JsValue.String("Boolean"), false, false, true);
        DefineData(ctor, "length", JsValue.Number(1), false, false, true);
        DefineData(proto, "constructor", JsValue.Object(ctor), true, false, true);

        DefineMethod(proto, "toString", (thisV, _) => JsValue.String(ThisBoolean(realm, thisV) ? "true" : "false"), 0);
        DefineMethod(proto, "valueOf", (thisV, _) => JsValue.Boolean(ThisBoolean(realm, thisV)), 0);

        realm.BooleanConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("Boolean", PropertyDescriptor.Data(JsValue.Object(ctor), true, false, true));
    }

    private static bool ThisBoolean(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsBoolean) return thisV.AsBool;
        if (thisV.IsObject)
        {
            var slot = thisV.AsObject.GetOwnPropertyDescriptor("__primitiveValue");
            if (slot is { } d && d.Value.IsBoolean) return d.Value.AsBool;
        }
        throw new JsThrow(realm.NewTypeError("Boolean.prototype method called on incompatible receiver"));
    }

    private static void DefineMethod(JsObject target, string name, Func<JsValue, JsValue[], JsValue> body, int length)
    {
        var fn = new JsNativeFunction(name, body, isConstructor: false);
        DefineData(fn, "name", JsValue.String(name), false, false, true);
        DefineData(fn, "length", JsValue.Number(length), false, false, true);
        target.DefineOwnProperty(name, PropertyDescriptor.BuiltinMethod(JsValue.Object(fn)));
    }

    private static void DefineData(JsObject target, string name, JsValue value, bool writable, bool enumerable, bool configurable)
        => target.DefineOwnProperty(name, PropertyDescriptor.Data(value, writable, enumerable, configurable));
}

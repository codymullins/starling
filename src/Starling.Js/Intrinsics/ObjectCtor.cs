using Tessera.Js.Runtime;

namespace Tessera.Js.Intrinsics;

/// <summary>
/// §20.1 The Object intrinsic. Populates <see cref="JsRealm.ObjectConstructor"/>
/// and <see cref="JsRealm.ObjectPrototype"/>, then registers <c>Object</c> on
/// the realm's global object.
/// </summary>
/// <remarks>
/// <para>
/// Array-returning methods (<c>keys</c>, <c>values</c>, <c>entries</c>,
/// <c>getOwnPropertyNames</c>, <c>getOwnPropertySymbols</c>) currently return
/// ordinary objects with integer-string keys and a <c>length</c> data slot —
/// the dedicated <c>JsArray</c> exotic object lands in B2-4. Until then the
/// shape is observationally compatible with array indexing + <c>length</c>.
/// </para>
/// <para>
/// Strict-throw on write to non-writable properties is not yet wired in the
/// VM — <c>JsObject.Set</c> silently no-ops on rejected writes per
/// §10.1.9 sloppy semantics. Once strict-mode emission lands, this surface
/// becomes spec-correct without further changes here.
/// </para>
/// </remarks>
public static class ObjectCtor
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var objectProto = realm.ObjectPrototype;

        // ----------------------------------------------------------- Constructor
        // §20.1.1 The Object Constructor — called as Object(v) or new Object(v).
        var ctor = new JsNativeFunction("Object", (thisV, args) =>
        {
            var value = args.Length > 0 ? args[0] : JsValue.Undefined;
            // When `value` is undefined or null → fresh ordinary object.
            if (value.IsNullish) return JsValue.Object(realm.NewOrdinaryObject());
            // Otherwise per §7.1.18 ToObject (objects pass through unchanged).
            return JsValue.Object(AbstractOperations.ToObject(realm, value));
        }, isConstructor: true);
        // Object.[[Prototype]] = Function.prototype
        ctor.SetPrototypeOf(realm.FunctionPrototype);
        // Object.prototype is a non-writable, non-enumerable, non-configurable slot
        // on the constructor (§20.1.2.1).
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(objectProto), writable: false, enumerable: false, configurable: false));
        ctor.DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String("Object"), writable: false, enumerable: false, configurable: true));
        ctor.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(1), writable: false, enumerable: false, configurable: true));
        // Object.prototype.constructor → Object (writable, non-enumerable, configurable).
        objectProto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));

        // -------------------------------------------------------- Static methods
        DefineMethod(realm, ctor, "assign", Assign, length: 2);
        DefineMethod(realm, ctor, "create", (thisV, args) => Create(realm, args), length: 2);
        DefineMethod(realm, ctor, "defineProperty", (thisV, args) => DefineProperty(realm, args), length: 3);
        DefineMethod(realm, ctor, "defineProperties", (thisV, args) => DefineProperties(realm, args), length: 2);
        DefineMethod(realm, ctor, "getOwnPropertyDescriptor", (thisV, args) => GetOwnPropertyDescriptor(realm, args), length: 2);
        DefineMethod(realm, ctor, "getOwnPropertyDescriptors", (thisV, args) => GetOwnPropertyDescriptors(realm, args), length: 1);
        DefineMethod(realm, ctor, "getOwnPropertyNames", (thisV, args) => GetOwnPropertyNames(realm, args), length: 1);
        DefineMethod(realm, ctor, "getOwnPropertySymbols", (thisV, args) => GetOwnPropertySymbols(realm, args), length: 1);
        DefineMethod(realm, ctor, "getPrototypeOf", (thisV, args) => GetPrototypeOf(realm, args), length: 1);
        DefineMethod(realm, ctor, "setPrototypeOf", (thisV, args) => SetPrototypeOf(realm, args), length: 2);
        DefineMethod(realm, ctor, "keys", (thisV, args) => Keys(realm, args), length: 1);
        DefineMethod(realm, ctor, "values", (thisV, args) => Values(realm, args), length: 1);
        DefineMethod(realm, ctor, "entries", (thisV, args) => Entries(realm, args), length: 1);
        DefineMethod(realm, ctor, "freeze", (thisV, args) => Freeze(args), length: 1);
        DefineMethod(realm, ctor, "isFrozen", (thisV, args) => IsFrozen(args), length: 1);
        DefineMethod(realm, ctor, "seal", (thisV, args) => Seal(args), length: 1);
        DefineMethod(realm, ctor, "isSealed", (thisV, args) => IsSealed(args), length: 1);
        DefineMethod(realm, ctor, "preventExtensions", (thisV, args) => PreventExtensions(args), length: 1);
        DefineMethod(realm, ctor, "isExtensible", (thisV, args) => IsExtensible(args), length: 1);
        DefineMethod(realm, ctor, "is", (thisV, args) => Is(args), length: 2);
        DefineMethod(realm, ctor, "fromEntries", (thisV, args) => FromEntries(realm, args), length: 1);
        DefineMethod(realm, ctor, "hasOwn", (thisV, args) => HasOwn(args), length: 2);

        // -------------------------------------------------------- Prototype methods
        DefineMethod(realm, objectProto, "hasOwnProperty", (thisV, args) => ProtoHasOwnProperty(realm, thisV, args), length: 1);
        DefineMethod(realm, objectProto, "isPrototypeOf", (thisV, args) => ProtoIsPrototypeOf(realm, thisV, args), length: 1);
        DefineMethod(realm, objectProto, "propertyIsEnumerable", (thisV, args) => ProtoPropertyIsEnumerable(realm, thisV, args), length: 1);
        DefineMethod(realm, objectProto, "toString", (thisV, args) => ProtoToString(thisV), length: 0);
        DefineMethod(realm, objectProto, "valueOf", (thisV, args) => ProtoValueOf(realm, thisV), length: 0);
        DefineMethod(realm, objectProto, "toLocaleString", (thisV, args) => ProtoToLocaleString(thisV), length: 0);

        realm.ObjectConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("Object",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    // ====================================================================
    //                            Helpers
    // ====================================================================

    /// <summary>Install a builtin method descriptor (W=true, E=false, C=true)
    /// per §17 default attributes for built-in functions. B2-2: routed through
    /// the realm-aware <see cref="JsNativeFunction"/> ctor so each method
    /// inherits from <c>Function.prototype</c>.</summary>
    private static void DefineMethod(JsRealm realm, JsObject target, string name,
        Func<JsValue, JsValue[], JsValue> body, int length)
    {
        var fn = new JsNativeFunction(realm, name, length, body, isConstructor: false);
        target.DefineOwnProperty(name, PropertyDescriptor.BuiltinMethod(JsValue.Object(fn)));
    }

    /// <summary>RequireObjectCoercible-then-ToObject. Mirrors what most spec
    /// statics do at entry.</summary>
    private static JsObject RequireObject(JsRealm realm, JsValue v)
    {
        if (v.IsNullish)
            throw new JsThrow(realm.NewTypeError("Cannot convert undefined or null to object"));
        if (!v.IsObject) return AbstractOperations.ToObject(realm, v);
        return v.AsObject;
    }

    /// <summary>Build a freshly-allocated ordinary object that behaves as an
    /// array (integer-keyed slots + <c>length</c>). Used by every list-shaped
    /// return until <c>JsArray</c> arrives in B2-4.</summary>
    private static JsValue MakeArrayLike(JsRealm realm, IReadOnlyList<JsValue> items)
    {
        var arr = realm.NewOrdinaryObject();
        for (var i = 0; i < items.Count; i++)
        {
            arr.DefineOwnProperty(i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PropertyDescriptor.Data(items[i], writable: true, enumerable: true, configurable: true));
        }
        arr.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(items.Count), writable: true, enumerable: false, configurable: false));
        return JsValue.Object(arr);
    }

    /// <summary>§6.2.5.5 FromPropertyDescriptor — build a plain JS object from
    /// a host descriptor (data → {value, writable, enumerable, configurable},
    /// accessor → {get, set, enumerable, configurable}).</summary>
    private static JsValue FromPropertyDescriptor(JsRealm realm, PropertyDescriptor d)
    {
        var obj = realm.NewOrdinaryObject();
        if (d.IsAccessor)
        {
            obj.Set("get", d.Getter is null ? JsValue.Undefined : JsValue.Object(d.Getter));
            obj.Set("set", d.Setter is null ? JsValue.Undefined : JsValue.Object(d.Setter));
        }
        else
        {
            obj.Set("value", d.Value);
            obj.Set("writable", JsValue.Boolean(d.Writable));
        }
        obj.Set("enumerable", JsValue.Boolean(d.Enumerable));
        obj.Set("configurable", JsValue.Boolean(d.Configurable));
        return JsValue.Object(obj);
    }

    /// <summary>§6.2.5.6 ToPropertyDescriptor — read a JS object's hint
    /// fields and produce a host <see cref="PropertyDescriptor"/>. Throws
    /// TypeError on data+accessor mix per spec.</summary>
    private static PropertyDescriptor ToPropertyDescriptor(JsRealm realm, JsValue input)
    {
        if (!input.IsObject)
            throw new JsThrow(realm.NewTypeError("Property descriptor must be an object"));
        var obj = input.AsObject;

        var hasValue = obj.Has("value");
        var hasWritable = obj.Has("writable");
        var hasGet = obj.Has("get");
        var hasSet = obj.Has("set");
        var hasEnumerable = obj.Has("enumerable");
        var hasConfigurable = obj.Has("configurable");

        if ((hasValue || hasWritable) && (hasGet || hasSet))
            throw new JsThrow(realm.NewTypeError(
                "Invalid property descriptor. Cannot both specify accessors and a value or writable attribute"));

        var enumerable = hasEnumerable && JsValue.ToBoolean(obj.Get("enumerable"));
        var configurable = hasConfigurable && JsValue.ToBoolean(obj.Get("configurable"));

        if (hasGet || hasSet)
        {
            JsObject? getter = null;
            JsObject? setter = null;
            if (hasGet)
            {
                var g = obj.Get("get");
                if (!g.IsUndefined)
                {
                    if (!AbstractOperations.IsCallable(g))
                        throw new JsThrow(realm.NewTypeError("Getter must be a function"));
                    getter = g.AsObject;
                }
            }
            if (hasSet)
            {
                var s = obj.Get("set");
                if (!s.IsUndefined)
                {
                    if (!AbstractOperations.IsCallable(s))
                        throw new JsThrow(realm.NewTypeError("Setter must be a function"));
                    setter = s.AsObject;
                }
            }
            return PropertyDescriptor.Accessor(getter, setter, enumerable, configurable);
        }

        var writable = hasWritable && JsValue.ToBoolean(obj.Get("writable"));
        var value = hasValue ? obj.Get("value") : JsValue.Undefined;
        return PropertyDescriptor.Data(value, writable, enumerable, configurable);
    }

    // ====================================================================
    //                          Static implementations
    // ====================================================================

    /// <summary>§20.1.2.1 Object.assign — copy own enumerable string-keyed
    /// properties from each source to target.</summary>
    private static JsValue Assign(JsValue thisV, JsValue[] args)
    {
        if (args.Length == 0) throw new JsThrow(JsValue.String("Object.assign requires a target"));
        var target = args[0];
        if (!target.IsObject)
            throw new JsThrow(JsValue.String("Object.assign target must be an object"));
        var targetObj = target.AsObject;

        for (var i = 1; i < args.Length; i++)
        {
            var src = args[i];
            if (src.IsNullish) continue;
            if (!src.IsObject) continue;
            var srcObj = src.AsObject;
            foreach (var key in srcObj.EnumerableKeys())
            {
                var v = srcObj.Get(key);
                AbstractOperations.Set(vm: null, targetObj, key, v);
            }
            foreach (var key in srcObj.EnumerableSymbolKeys())
            {
                var v = srcObj.Get(key);
                AbstractOperations.Set(vm: null, targetObj, JsPropertyKey.Symbol(key), v);
            }
        }
        return target;
    }

    /// <summary>§20.1.2.2 Object.create — create a fresh object with the
    /// supplied prototype and optionally apply a descriptor map.</summary>
    private static JsValue Create(JsRealm realm, JsValue[] args)
    {
        if (args.Length == 0)
            throw new JsThrow(realm.NewTypeError("Object.create: prototype must be Object or null"));
        var proto = args[0];
        JsObject? protoObj;
        if (proto.IsNull) protoObj = null;
        else if (proto.IsObject) protoObj = proto.AsObject;
        else throw new JsThrow(realm.NewTypeError("Object.create: prototype must be Object or null"));

        var obj = realm.NewObjectWithProto(protoObj);
        if (args.Length >= 2 && !args[1].IsUndefined)
        {
            ApplyDescriptors(realm, obj, args[1]);
        }
        return JsValue.Object(obj);
    }

    /// <summary>§20.1.2.3 Object.defineProperties.</summary>
    private static JsValue DefineProperties(JsRealm realm, JsValue[] args)
    {
        if (args.Length == 0 || !args[0].IsObject)
            throw new JsThrow(realm.NewTypeError("Object.defineProperties called on non-object"));
        var target = args[0].AsObject;
        var props = args.Length > 1 ? args[1] : JsValue.Undefined;
        if (props.IsUndefined)
            throw new JsThrow(realm.NewTypeError("Object.defineProperties: descriptors must be an object"));
        ApplyDescriptors(realm, target, props);
        return args[0];
    }

    private static void ApplyDescriptors(JsRealm realm, JsObject target, JsValue props)
    {
        if (!props.IsObject)
            throw new JsThrow(realm.NewTypeError("Property descriptors must be an object"));
        var p = props.AsObject;
        foreach (var key in p.EnumerableKeys())
        {
            var descObj = p.Get(key);
            var desc = ToPropertyDescriptor(realm, descObj);
            if (!target.DefineOwnProperty(key, desc))
                throw new JsThrow(realm.NewTypeError($"Cannot define property '{key}'"));
        }
    }

    /// <summary>§20.1.2.4 Object.defineProperty.</summary>
    private static JsValue DefineProperty(JsRealm realm, JsValue[] args)
    {
        if (args.Length == 0 || !args[0].IsObject)
            throw new JsThrow(realm.NewTypeError("Object.defineProperty called on non-object"));
        if (args.Length < 2)
            throw new JsThrow(realm.NewTypeError("Object.defineProperty requires a property key"));
        if (args.Length < 3)
            throw new JsThrow(realm.NewTypeError("Object.defineProperty requires a descriptor"));
        var target = args[0].AsObject;
        var key = AbstractOperations.ToPropertyKey(args[1]);
        var desc = ToPropertyDescriptor(realm, args[2]);
        if (!target.DefineOwnProperty(key, desc))
            throw new JsThrow(realm.NewTypeError($"Cannot redefine property '{key}'"));
        return args[0];
    }

    /// <summary>§20.1.2.8 Object.getOwnPropertyDescriptor.</summary>
    private static JsValue GetOwnPropertyDescriptor(JsRealm realm, JsValue[] args)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var key = AbstractOperations.ToPropertyKey(args.Length > 1 ? args[1] : JsValue.Undefined);
        var d = target.GetOwnPropertyDescriptor(key);
        if (d is null) return JsValue.Undefined;
        return FromPropertyDescriptor(realm, d.Value);
    }

    /// <summary>§20.1.2.9 Object.getOwnPropertyDescriptors.</summary>
    private static JsValue GetOwnPropertyDescriptors(JsRealm realm, JsValue[] args)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var bag = realm.NewOrdinaryObject();
        foreach (var key in target.OwnPropertyKeys)
        {
            var d = target.GetOwnPropertyDescriptor(key);
            if (d is null) continue;
            bag.DefineOwnProperty(key,
                PropertyDescriptor.Data(FromPropertyDescriptor(realm, d.Value), writable: true, enumerable: true, configurable: true));
        }
        return JsValue.Object(bag);
    }

    /// <summary>§20.1.2.10 Object.getOwnPropertyNames.</summary>
    private static JsValue GetOwnPropertyNames(JsRealm realm, JsValue[] args)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var names = new List<JsValue>();
        foreach (var k in target.Keys) names.Add(JsValue.String(k));
        return MakeArrayLike(realm, names);
    }

    /// <summary>§20.1.2.11 Object.getOwnPropertySymbols.</summary>
    private static JsValue GetOwnPropertySymbols(JsRealm realm, JsValue[] args)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var symbols = new List<JsValue>();
        foreach (var k in target.SymbolKeys) symbols.Add(JsValue.Symbol(k));
        return MakeArrayLike(realm, symbols);
    }

    /// <summary>§20.1.2.12 Object.getPrototypeOf.</summary>
    private static JsValue GetPrototypeOf(JsRealm realm, JsValue[] args)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        return target.Prototype is null ? JsValue.Null : JsValue.Object(target.Prototype);
    }

    /// <summary>§20.1.2.22 Object.setPrototypeOf.</summary>
    private static JsValue SetPrototypeOf(JsRealm realm, JsValue[] args)
    {
        if (args.Length == 0 || args[0].IsNullish)
            throw new JsThrow(realm.NewTypeError("Object.setPrototypeOf called on null or undefined"));
        if (args.Length < 2)
            throw new JsThrow(realm.NewTypeError("Object.setPrototypeOf requires a prototype argument"));
        var proto = args[1];
        JsObject? protoObj;
        if (proto.IsNull) protoObj = null;
        else if (proto.IsObject) protoObj = proto.AsObject;
        else throw new JsThrow(realm.NewTypeError("Object.setPrototypeOf: prototype must be Object or null"));
        // If target is a primitive, ToObject would box it (and the box is
        // discarded). Spec: SetPrototypeOf on a non-object is a no-op return.
        if (!args[0].IsObject) return args[0];
        if (!args[0].AsObject.SetPrototypeOf(protoObj))
            throw new JsThrow(realm.NewTypeError("Object.setPrototypeOf: cycle detected or non-extensible"));
        return args[0];
    }

    /// <summary>§20.1.2.18 Object.keys.</summary>
    private static JsValue Keys(JsRealm realm, JsValue[] args)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var keys = new List<JsValue>();
        foreach (var k in target.EnumerableKeys()) keys.Add(JsValue.String(k));
        return MakeArrayLike(realm, keys);
    }

    /// <summary>§20.1.2.23 Object.values.</summary>
    private static JsValue Values(JsRealm realm, JsValue[] args)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var values = new List<JsValue>();
        foreach (var k in target.EnumerableKeys())
            values.Add(AbstractOperations.Get(vm: null, target, k));
        return MakeArrayLike(realm, values);
    }

    /// <summary>§20.1.2.5 Object.entries.</summary>
    private static JsValue Entries(JsRealm realm, JsValue[] args)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var entries = new List<JsValue>();
        foreach (var k in target.EnumerableKeys())
        {
            var v = AbstractOperations.Get(vm: null, target, k);
            entries.Add(MakeArrayLike(realm, new[] { JsValue.String(k), v }));
        }
        return MakeArrayLike(realm, entries);
    }

    /// <summary>§20.1.2.6 Object.freeze — set [[Writable]] and
    /// [[Configurable]] to false on every own property, then prevent
    /// extensions.</summary>
    private static JsValue Freeze(JsValue[] args)
    {
        var v = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!v.IsObject) return v;
        var obj = v.AsObject;
        // Snapshot keys to avoid mutation-during-enumeration.
        var keys = new List<JsPropertyKey>(obj.OwnPropertyKeys);
        foreach (var key in keys)
        {
            var d = obj.GetOwnPropertyDescriptor(key);
            if (d is null) continue;
            var desc = d.Value;
            if (desc.IsAccessor)
                obj.DefineOwnProperty(key, PropertyDescriptor.Accessor(desc.Getter, desc.Setter, desc.Enumerable, configurable: false));
            else
                obj.DefineOwnProperty(key, PropertyDescriptor.Data(desc.Value, writable: false, enumerable: desc.Enumerable, configurable: false));
        }
        obj.PreventExtensions();
        return v;
    }

    /// <summary>§20.1.2.15 Object.isFrozen.</summary>
    private static JsValue IsFrozen(JsValue[] args)
    {
        var v = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!v.IsObject) return JsValue.True; // primitives are frozen by definition
        var obj = v.AsObject;
        if (obj.Extensible) return JsValue.False;
        foreach (var key in obj.OwnPropertyKeys)
        {
            var d = obj.GetOwnPropertyDescriptor(key);
            if (d is null) continue;
            var desc = d.Value;
            if (desc.Configurable) return JsValue.False;
            if (!desc.IsAccessor && desc.Writable) return JsValue.False;
        }
        return JsValue.True;
    }

    /// <summary>§20.1.2.21 Object.seal — set [[Configurable]] to false; keep
    /// [[Writable]] alone; prevent extensions.</summary>
    private static JsValue Seal(JsValue[] args)
    {
        var v = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!v.IsObject) return v;
        var obj = v.AsObject;
        var keys = new List<JsPropertyKey>(obj.OwnPropertyKeys);
        foreach (var key in keys)
        {
            var d = obj.GetOwnPropertyDescriptor(key);
            if (d is null) continue;
            var desc = d.Value;
            if (desc.IsAccessor)
                obj.DefineOwnProperty(key, PropertyDescriptor.Accessor(desc.Getter, desc.Setter, desc.Enumerable, configurable: false));
            else
                obj.DefineOwnProperty(key, PropertyDescriptor.Data(desc.Value, writable: desc.Writable, enumerable: desc.Enumerable, configurable: false));
        }
        obj.PreventExtensions();
        return v;
    }

    /// <summary>§20.1.2.16 Object.isSealed.</summary>
    private static JsValue IsSealed(JsValue[] args)
    {
        var v = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!v.IsObject) return JsValue.True;
        var obj = v.AsObject;
        if (obj.Extensible) return JsValue.False;
        foreach (var key in obj.OwnPropertyKeys)
        {
            var d = obj.GetOwnPropertyDescriptor(key);
            if (d is null) continue;
            if (d.Value.Configurable) return JsValue.False;
        }
        return JsValue.True;
    }

    /// <summary>§20.1.2.19 Object.preventExtensions.</summary>
    private static JsValue PreventExtensions(JsValue[] args)
    {
        var v = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!v.IsObject) return v;
        v.AsObject.PreventExtensions();
        return v;
    }

    /// <summary>§20.1.2.14 Object.isExtensible.</summary>
    private static JsValue IsExtensible(JsValue[] args)
    {
        var v = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!v.IsObject) return JsValue.False;
        return JsValue.Boolean(v.AsObject.Extensible);
    }

    /// <summary>§20.1.2.13 Object.is.</summary>
    private static JsValue Is(JsValue[] args)
    {
        var a = args.Length > 0 ? args[0] : JsValue.Undefined;
        var b = args.Length > 1 ? args[1] : JsValue.Undefined;
        return JsValue.Boolean(AbstractOperations.SameValue(a, b));
    }

    /// <summary>§20.1.2.7 Object.fromEntries — accepts an array of [k,v] pairs
    /// represented as our array-like ordinary objects. Full iterator-protocol
    /// support lands in B3-2.</summary>
    private static JsValue FromEntries(JsRealm realm, JsValue[] args)
    {
        if (args.Length == 0 || !args[0].IsObject)
            throw new JsThrow(realm.NewTypeError("Object.fromEntries requires an iterable"));
        var src = args[0].AsObject;
        var result = realm.NewOrdinaryObject();
        var lengthV = src.Get("length");
        if (!lengthV.IsNumber)
            throw new JsThrow(realm.NewTypeError("Object.fromEntries: iterable has no length (full iterator support arrives in B3-2)"));
        var len = (int)lengthV.AsNumber;
        for (var i = 0; i < len; i++)
        {
            var entryV = src.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!entryV.IsObject)
                throw new JsThrow(realm.NewTypeError("Object.fromEntries: entry must be an object"));
            var entry = entryV.AsObject;
            var key = AbstractOperations.ToPropertyKey(entry.Get("0"));
            var val = entry.Get("1");
            result.DefineOwnProperty(key,
                PropertyDescriptor.Data(val, writable: true, enumerable: true, configurable: true));
        }
        return JsValue.Object(result);
    }

    /// <summary>§20.1.2.13 Object.hasOwn(obj, key).</summary>
    private static JsValue HasOwn(JsValue[] args)
    {
        if (args.Length == 0 || !args[0].IsObject) return JsValue.False;
        var key = AbstractOperations.ToPropertyKey(args.Length > 1 ? args[1] : JsValue.Undefined);
        return JsValue.Boolean(args[0].AsObject.HasOwn(key));
    }

    // ====================================================================
    //                       Prototype implementations
    // ====================================================================

    /// <summary>§20.1.3.2 Object.prototype.hasOwnProperty.</summary>
    private static JsValue ProtoHasOwnProperty(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var key = AbstractOperations.ToPropertyKey(args.Length > 0 ? args[0] : JsValue.Undefined);
        var obj = RequireObject(realm, thisV);
        return JsValue.Boolean(obj.HasOwn(key));
    }

    /// <summary>§20.1.3.3 Object.prototype.isPrototypeOf — walks <c>V</c>'s
    /// prototype chain looking for <c>this</c>.</summary>
    private static JsValue ProtoIsPrototypeOf(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        if (args.Length == 0 || !args[0].IsObject) return JsValue.False;
        var self = RequireObject(realm, thisV);
        for (var p = args[0].AsObject.Prototype; p is not null; p = p.Prototype)
        {
            if (ReferenceEquals(p, self)) return JsValue.True;
        }
        return JsValue.False;
    }

    /// <summary>§20.1.3.4 Object.prototype.propertyIsEnumerable.</summary>
    private static JsValue ProtoPropertyIsEnumerable(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var key = AbstractOperations.ToPropertyKey(args.Length > 0 ? args[0] : JsValue.Undefined);
        var obj = RequireObject(realm, thisV);
        var d = obj.GetOwnPropertyDescriptor(key);
        return JsValue.Boolean(d is { } dv && dv.Enumerable);
    }

    /// <summary>§20.1.3.6 Object.prototype.toString. The full <c>@@toStringTag</c>
    /// path arrives with Symbol (B3-1); for now plain objects yield
    /// <c>"[object Object]"</c> and primitives are stringified via the spec
    /// fast paths.</summary>
    private static JsValue ProtoToString(JsValue thisV) => thisV.Kind switch
    {
        JsValueKind.Undefined => JsValue.String("[object Undefined]"),
        JsValueKind.Null => JsValue.String("[object Null]"),
        _ => JsValue.String("[object Object]"),
    };

    /// <summary>§20.1.3.7 Object.prototype.valueOf — return <c>this</c> coerced
    /// to an object (primitives box).</summary>
    private static JsValue ProtoValueOf(JsRealm realm, JsValue thisV)
        => JsValue.Object(RequireObject(realm, thisV));

    /// <summary>§20.1.3.5 Object.prototype.toLocaleString — defaults to
    /// invoking <c>this.toString()</c>.</summary>
    private static JsValue ProtoToLocaleString(JsValue thisV)
    {
        if (thisV.IsObject)
        {
            var ts = thisV.AsObject.Get("toString");
            if (ts.IsObject && ts.AsObject is JsNativeFunction nat)
                return nat.Body(thisV, Array.Empty<JsValue>());
        }
        return ProtoToString(thisV);
    }
}

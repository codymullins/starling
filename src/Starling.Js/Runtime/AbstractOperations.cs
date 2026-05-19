namespace Tessera.Js.Runtime;

/// <summary>
/// ES2024 abstract operations not pinned to a single value (those live on
/// <see cref="JsValue"/>). VM-aware variants (Get/Set/Call/Construct) take an
/// optional <see cref="JsVm"/> so accessor invocation and JS-callable dispatch
/// can recurse into the interpreter. Native callers can pass <c>null</c> and
/// stay on the data-only fast path.
/// </summary>
public static class AbstractOperations
{
    /// <summary>§7.1.1 ToPrimitive — invariant locale fast path. The "hint"
    /// matters only when an object overrides <c>Symbol.toPrimitive</c>, which
    /// lands with the Symbol intrinsic. Today: returns the value unchanged
    /// for primitives, falls back to <c>toString</c>/<c>valueOf</c> for
    /// objects via the host bridge when available.</summary>
    public static JsValue ToPrimitive(JsValue input, string hint = "default")
    {
        if (input.Kind != JsValueKind.Object) return input;
        var obj = input.AsObject;
        // OrdinaryToPrimitive: try toString or valueOf in order.
        var first = hint == "string" ? "toString" : "valueOf";
        var second = hint == "string" ? "valueOf" : "toString";
        var v = obj.Get(first);
        if (v.IsObject && v.AsObject is JsNativeFunction nat1)
        {
            var r = nat1.Body(input, Array.Empty<JsValue>());
            if (r.Kind != JsValueKind.Object) return r;
        }
        v = obj.Get(second);
        if (v.IsObject && v.AsObject is JsNativeFunction nat2)
        {
            var r = nat2.Body(input, Array.Empty<JsValue>());
            if (r.Kind != JsValueKind.Object) return r;
        }
        return JsValue.String(obj.ToString() ?? "[object Object]");
    }

    /// <summary>§7.1.18 ToObject — boxes primitives to their wrapper. Throws
    /// for undefined/null per spec. Realm needed so we know which intrinsic
    /// prototype to box against.</summary>
    public static JsObject ToObject(JsRealm realm, JsValue value)
    {
        ArgumentNullException.ThrowIfNull(realm);
        return value.Kind switch
        {
            JsValueKind.Undefined => throw new JsThrow(realm.NewTypeError("Cannot convert undefined to object")),
            JsValueKind.Null => throw new JsThrow(realm.NewTypeError("Cannot convert null to object")),
            JsValueKind.Object => value.AsObject,
            JsValueKind.Boolean => realm.BoxBoolean(value),
            JsValueKind.Number => realm.BoxNumber(value),
            JsValueKind.String => realm.BoxString(value),
            JsValueKind.BigInt => realm.BoxBigInt(value),
            _ => throw new InvalidOperationException($"unhandled kind {value.Kind}"),
        };
    }

    /// <summary>§7.1.19 ToPropertyKey — strings stay strings, numbers/etc.
    /// stringify per <c>ToString</c>. Symbols land later.</summary>
    public static string ToPropertyKey(JsValue value)
        => value.Kind == JsValueKind.String ? value.AsString : JsValue.ToStringValue(value);

    /// <summary>§7.2.3 IsCallable — true if the value is an object with a
    /// callable internal method ([[Call]]). For us: a JsFunction, a
    /// JsNativeFunction, or a bound function.</summary>
    public static bool IsCallable(JsValue value)
    {
        if (!value.IsObject) return false;
        var obj = value.AsObject;
        return obj is JsFunction or JsNativeFunction or JsBoundFunction;
    }

    /// <summary>§7.2.4 IsConstructor.</summary>
    public static bool IsConstructor(JsValue value)
    {
        if (!value.IsObject) return false;
        return value.AsObject switch
        {
            JsFunction => true,
            JsNativeFunction nat => nat.IsConstructor,
            JsBoundFunction bf => IsConstructor(JsValue.Object(bf.Target)),
            _ => false,
        };
    }

    /// <summary>§10.1.8 OrdinaryGet — chain-walking property lookup that
    /// invokes accessor getters via <paramref name="vm"/> when present.
    /// Passing a null VM falls back to the data-only path.</summary>
    public static JsValue Get(JsVm? vm, JsObject obj, string key, JsValue receiver = default)
    {
        if (receiver.IsUndefined) receiver = JsValue.Object(obj);
        for (var o = obj; o is not null; o = o.Prototype)
        {
            var desc = o.GetOwnPropertyDescriptor(key);
            if (desc is null) continue;
            var d = desc.Value;
            if (d.IsAccessor)
            {
                if (d.Getter is null) return JsValue.Undefined;
                return Call(vm, JsValue.Object(d.Getter), receiver, Array.Empty<JsValue>());
            }
            return d.Value;
        }
        return JsValue.Undefined;
    }

    /// <summary>§10.1.9 OrdinarySet — chain-walking write that respects
    /// accessor setters. Returns false if the write was rejected.</summary>
    public static bool Set(JsVm? vm, JsObject obj, string key, JsValue value, JsValue receiver = default)
    {
        if (receiver.IsUndefined) receiver = JsValue.Object(obj);
        // Find existing descriptor anywhere on the chain.
        for (var o = obj; o is not null; o = o.Prototype)
        {
            var desc = o.GetOwnPropertyDescriptor(key);
            if (desc is null) continue;
            var d = desc.Value;
            if (d.IsAccessor)
            {
                if (d.Setter is null) return false;
                Call(vm, JsValue.Object(d.Setter), receiver, new[] { value });
                return true;
            }
            if (!d.Writable) return false;
            // Fall through to write on the receiver's own slot.
            break;
        }
        if (!obj.Extensible && !obj.HasOwn(key)) return false;
        return obj.DefineOwnProperty(key, PropertyDescriptor.Data(value));
    }

    /// <summary>§7.3.14 Call — dispatch to native or JS callables. For JS
    /// functions, requires the VM. For native functions, the VM is optional.</summary>
    public static JsValue Call(JsVm? vm, JsValue callee, JsValue thisValue, JsValue[] args)
    {
        if (!callee.IsObject)
            throw new JsThrow(JsValue.String($"not a function: {JsValue.ToStringValue(callee)}"));
        return callee.AsObject switch
        {
            JsNativeFunction nat => nat.Body(thisValue, args),
            JsFunction fn => vm is not null
                ? vm.CallFunction(fn, thisValue, args)
                : throw new InvalidOperationException("VM required to call JS function"),
            JsBoundFunction bf => Call(vm, JsValue.Object(bf.Target), bf.BoundThis,
                ConcatBoundArgs(bf.BoundArgs, args)),
            _ => throw new JsThrow(JsValue.String($"not a function: {callee.AsObject}")),
        };
    }

    /// <summary>§7.3.15 Construct — analogous for <c>new</c>.</summary>
    public static JsValue Construct(JsVm? vm, JsValue ctor, JsValue[] args, JsObject? newTarget = null)
    {
        if (!IsConstructor(ctor))
            throw new JsThrow(JsValue.String($"not a constructor: {JsValue.ToStringValue(ctor)}"));
        newTarget ??= ctor.AsObject;
        return ctor.AsObject switch
        {
            JsNativeFunction nat => nat.Body(JsValue.Object(newTarget), args),
            JsFunction fn => vm is not null
                ? vm.ConstructFunction(fn, args, newTarget)
                : throw new InvalidOperationException("VM required to construct JS function"),
            JsBoundFunction bf => Construct(vm, JsValue.Object(bf.Target),
                ConcatBoundArgs(bf.BoundArgs, args), newTarget),
            _ => throw new JsThrow(JsValue.String($"not a constructor: {ctor.AsObject}")),
        };
    }

    /// <summary>§7.2.10 SameValue — like StrictEqual but +0 ≠ -0 and NaN = NaN.</summary>
    public static bool SameValue(JsValue a, JsValue b)
    {
        if (a.Kind != b.Kind) return false;
        if (a.Kind == JsValueKind.Number)
        {
            var na = a.AsNumber; var nb = b.AsNumber;
            if (double.IsNaN(na) && double.IsNaN(nb)) return true;
            if (na == 0 && nb == 0)
                return double.IsNegative(na) == double.IsNegative(nb);
            return na == nb;
        }
        return JsValue.StrictEquals(a, b);
    }

    /// <summary>§7.2.11 SameValueZero — SameValue but +0 = -0. Used by Map/Set.</summary>
    public static bool SameValueZero(JsValue a, JsValue b)
    {
        if (a.Kind != b.Kind) return false;
        if (a.Kind == JsValueKind.Number)
        {
            var na = a.AsNumber; var nb = b.AsNumber;
            if (double.IsNaN(na) && double.IsNaN(nb)) return true;
            return na == nb;
        }
        return JsValue.StrictEquals(a, b);
    }

    private static JsValue[] ConcatBoundArgs(IReadOnlyList<JsValue> bound, JsValue[] extra)
    {
        if (bound.Count == 0) return extra;
        var combined = new JsValue[bound.Count + extra.Length];
        for (var i = 0; i < bound.Count; i++) combined[i] = bound[i];
        Array.Copy(extra, 0, combined, bound.Count, extra.Length);
        return combined;
    }
}

namespace Starling.Js.Runtime;

/// <summary>
/// ES2024 abstract operations not pinned to a single value (those live on
/// <see cref="JsValue"/>). VM-aware variants (Get/Set/Call/Construct) take an
/// optional <see cref="JsVm"/> so accessor invocation and JS-callable dispatch
/// can recurse into the interpreter. Native callers can pass <c>null</c> and
/// stay on the data-only fast path.
/// </summary>
public static class AbstractOperations
{
    /// <summary>§7.1.1 ToPrimitive — checks ECMA-262 §20.4's
    /// <c>@@toPrimitive</c> well-known Symbol first, then falls back to
    /// <c>toString</c>/<c>valueOf</c> for objects via the host bridge when
    /// available.</summary>
    public static JsValue ToPrimitive(JsValue input, string hint = "default")
        => ToPrimitive(null, input, hint);

    public static JsValue ToPrimitive(JsVm? vm, JsValue input, string hint = "default")
    {
        if (input.Kind != JsValueKind.Object) return input;
        var obj = input.AsObject;
        var exotic = obj.Get(Starling.Js.Intrinsics.SymbolCtor.ToPrimitive);
        if (IsCallable(exotic))
        {
            var r = Call(vm, exotic, input, new[] { JsValue.String(hint) });
            if (r.Kind != JsValueKind.Object) return r;
        }
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
            JsValueKind.Symbol => realm.BoxSymbol(value),
            _ => throw new InvalidOperationException($"unhandled kind {value.Kind}"),
        };
    }

    /// <summary>§7.1.13 ToBigInt — coerce to BigInt. Delegates to
    /// <see cref="BigIntOps.ToBigInt"/> so the parsing/range logic lives next
    /// to the rest of the BigInt operator suite.</summary>
    public static System.Numerics.BigInteger ToBigInt(JsRealm realm, JsValue value)
        => BigIntOps.ToBigInt(realm, value);

    /// <summary>§7.1.22 ToNumeric — converts the input to a BigInt-or-Number
    /// primitive. Used by the arithmetic operator boundary so the spec-faithful
    /// "no implicit Number↔BigInt coercion" rule has a single seam.</summary>
    public static JsValue ToNumeric(JsRealm realm, JsValue value)
    {
        var prim = ToPrimitive(value, "number");
        if (prim.IsBigInt) return prim;
        return JsValue.Number(JsValue.ToNumber(prim));
    }

    /// <summary>§7.1.19 ToPropertyKey — Symbols are already property keys;
    /// every other primitive is stringified.</summary>
    public static JsPropertyKey ToPropertyKey(JsValue value)
        => ToPropertyKey(null, value);

    /// <summary>§7.1.19 ToPropertyKey with a VM so the §7.1.1 ToPrimitive step
    /// can dispatch a user/exotic <c>Symbol.toPrimitive</c> on an object key
    /// (used by computed class/object keys — wp:M3-04f).</summary>
    public static JsPropertyKey ToPropertyKey(JsVm? vm, JsValue value)
    {
        if (value.IsObject) value = ToPrimitive(vm, value, "string");
        return value.IsSymbol ? JsPropertyKey.Symbol(value.AsSymbol)
            : JsPropertyKey.String(value.Kind == JsValueKind.String ? value.AsString : JsValue.ToStringValue(value));
    }

    /// <summary>§7.2.3 IsCallable — true if the value is an object with a
    /// callable internal method ([[Call]]). For us: a JsFunction, a
    /// JsNativeFunction, a bound function, or a proxy whose target is callable.</summary>
    public static bool IsCallable(JsValue value)
    {
        if (!value.IsObject) return false;
        var obj = value.AsObject;
        if (obj is JsProxy proxy) return !proxy.IsRevoked && proxy.TargetIsCallable;
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
            JsProxy proxy => !proxy.IsRevoked && proxy.TargetIsConstructor,
            _ => false,
        };
    }

    /// <summary>§10.1.8 OrdinaryGet — chain-walking property lookup that
    /// invokes accessor getters via <paramref name="vm"/> when present.
    /// Passing a null VM falls back to the data-only path.</summary>
    public static JsValue Get(JsVm? vm, JsObject obj, string key, JsValue receiver = default)
        => Get(vm, obj, JsPropertyKey.String(key), receiver);

    public static JsValue Get(JsVm? vm, JsObject obj, JsPropertyKey key, JsValue receiver = default)
    {
        if (receiver.IsUndefined) receiver = JsValue.Object(obj);
        // §10.5.8: Proxy exotic objects route property reads through the [[Get]]
        // internal method (which consults the `get` trap). Done at the AO entry
        // so every call site picks it up — the VM and intrinsics all call here
        // rather than virtual JsObject.Get(string), so dispatching via virtual
        // override is not enough.
        if (obj is JsProxy proxy)
            return proxy.GetWithReceiver(key, receiver);
        // ECMA-262 §25.2.5: integer-indexed exotic element access is handled
        // by the typed-array object before ordinary descriptor lookup.
        if (obj is JsTypedArray ta && key.IsString && IsCanonicalArrayIndex(key.AsString))
            return ta.Get(key.AsString);
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

    /// <summary>§7.3.12 HasProperty — chain-walking presence check. Routes
    /// through <see cref="JsProxy"/> when the target is a Proxy so the
    /// <c>has</c> trap fires.</summary>
    public static bool HasProperty(JsObject obj, JsPropertyKey key)
    {
        ArgumentNullException.ThrowIfNull(obj);
        // JsObject.Has already walks the prototype chain and JsProxy
        // overrides Has to dispatch the `has` trap.
        return obj.Has(key);
    }

    public static bool HasProperty(JsObject obj, string key)
        => HasProperty(obj, JsPropertyKey.String(key));

    /// <summary>§10.1.9 OrdinarySet — chain-walking write that respects
    /// accessor setters. Returns false if the write was rejected.</summary>
    public static bool Set(JsVm? vm, JsObject obj, string key, JsValue value, JsValue receiver = default)
        => Set(vm, obj, JsPropertyKey.String(key), value, receiver);

    public static bool Set(JsVm? vm, JsObject obj, JsPropertyKey key, JsValue value, JsValue receiver = default)
    {
        if (receiver.IsUndefined) receiver = JsValue.Object(obj);
        // §10.5.9: Proxy exotic objects route writes through the [[Set]] internal
        // method (which consults the `set` trap). See note on Get above.
        if (obj is JsProxy proxy)
            return proxy.SetWithReceiver(key, value, receiver);
        // ECMA-262 §25.2.5 integer-indexed exotic writes go to the backing
        // ArrayBuffer instead of creating ordinary own properties.
        if (obj is JsTypedArray ta && key.IsString && IsCanonicalArrayIndex(key.AsString))
        {
            ta.Set(key.AsString, value);
            return true;
        }
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
        if (obj.HasOwn(key))
        {
            obj.Set(key, value);
            return true;
        }
        if (!obj.Extensible) return false;
        return obj.DefineOwnProperty(key, PropertyDescriptor.Data(value));
    }

    /// <summary>§7.3.14 Call — dispatch to native or JS callables. For JS
    /// functions, requires the VM. For native functions, the VM is optional.</summary>
    public static JsValue Call(JsVm? vm, JsValue callee, JsValue thisValue, JsValue[] args)
    {
        if (!callee.IsObject)
            throw NotAFunction(vm, JsValue.ToStringValue(callee));
        return callee.AsObject switch
        {
            JsNativeFunction nat => nat.Body(thisValue, args),
            JsFunction fn => vm is not null
                ? vm.CallFunction(fn, thisValue, args)
                : throw new InvalidOperationException("VM required to call JS function"),
            JsBoundFunction bf => Call(vm, JsValue.Object(bf.Target), bf.BoundThis,
                ConcatBoundArgs(bf.BoundArgs, args)),
            JsProxy proxy => proxy.ProxyCall(thisValue, args),
            _ => throw NotAFunction(vm, callee.AsObject.ToString() ?? "object"),
        };
    }

    /// <summary>§7.3.14 Call step 2 — calling a non-callable is a TypeError.
    /// Uses the realm's TypeError when a VM is available so embedders observe a
    /// real <c>TypeError</c> (string fallback only for the realm-less path).</summary>
    private static JsThrow NotAFunction(JsVm? vm, string detail) =>
        vm is not null
            ? new JsThrow(vm.Realm.NewTypeError($"not a function: {detail}"))
            : new JsThrow(JsValue.String($"not a function: {detail}"));

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
            JsProxy proxy => proxy.ProxyConstruct(args, newTarget),
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

    // ==========================================================
    //               §7.4 Iterator abstract operations (B3-2)
    // ==========================================================

    /// <summary>§7.4.1 GetIterator(obj, hint). Resolves
    /// <c>obj[@@iterator]</c>, invokes it with <paramref name="value"/> as
    /// <c>this</c>, and validates the result is an Object. The companion
    /// <c>NextMethod</c> is pre-resolved once so <see cref="IteratorNext"/>
    /// does not re-walk the prototype chain on every step.</summary>
    public static IteratorRecord GetIterator(JsRealm realm, JsVm? vm, JsValue value, string hint = "sync")
    {
        ArgumentNullException.ThrowIfNull(realm);
        if (hint != "sync")
            throw new NotSupportedException("async iterator hint not supported yet");
        // §7.3.11: for primitive receivers we need to walk the wrapper
        // prototype (e.g. String.prototype[@@iterator]). Box via ToObject;
        // the iterator method is called with the original primitive as `this`
        // (wrapper methods unbox as needed).
        JsValue method;
        if (value.IsObject)
        {
            method = GetMethod(vm, value, Starling.Js.Intrinsics.SymbolCtor.Iterator);
        }
        else if (value.IsNullish)
        {
            throw new JsThrow(realm.NewTypeError("value is not iterable"));
        }
        else
        {
            var boxed = ToObject(realm, value);
            method = GetMethod(vm, JsValue.Object(boxed), Starling.Js.Intrinsics.SymbolCtor.Iterator);
        }
        if (method.IsUndefined || method.IsNull)
            throw new JsThrow(realm.NewTypeError("value is not iterable"));
        var iter = Call(vm, method, value, Array.Empty<JsValue>());
        if (!iter.IsObject)
            throw new JsThrow(realm.NewTypeError("iterator method did not return an object"));
        var nextMethod = Get(vm, iter.AsObject, "next");
        return new IteratorRecord(iter, nextMethod, Done: false);
    }

    /// <summary>§7.4.4 IteratorNext.</summary>
    public static JsValue IteratorNext(JsRealm realm, JsVm? vm, IteratorRecord record, JsValue? value = null)
    {
        var args = value is null ? Array.Empty<JsValue>() : new[] { value.Value };
        var result = Call(vm, record.NextMethod, record.Iterator, args);
        if (!result.IsObject)
            throw new JsThrow(realm.NewTypeError("iterator.next() did not return an object"));
        return result;
    }

    /// <summary>§7.4.5 IteratorComplete.</summary>
    public static bool IteratorComplete(JsVm? vm, JsValue iteratorResult)
    {
        if (!iteratorResult.IsObject) return true;
        return JsValue.ToBoolean(Get(vm, iteratorResult.AsObject, "done"));
    }

    /// <summary>§7.4.6 IteratorValue.</summary>
    public static JsValue IteratorValue(JsVm? vm, JsValue iteratorResult)
    {
        if (!iteratorResult.IsObject) return JsValue.Undefined;
        return Get(vm, iteratorResult.AsObject, "value");
    }

    /// <summary>§7.4.7 IteratorStep. Returns the iterator-result object, or
    /// <c>null</c> when the iterator has signalled completion.</summary>
    public static JsValue? IteratorStep(JsRealm realm, JsVm? vm, ref IteratorRecord record)
    {
        var result = IteratorNext(realm, vm, record);
        var done = IteratorComplete(vm, result);
        if (done)
        {
            record = record with { Done = true };
            return null;
        }
        return result;
    }

    /// <summary>§7.4.10 IteratorClose. Invokes the iterator's <c>return</c>
    /// method if present. Used by <c>for…of</c> and destructuring on abrupt
    /// completion. When the wrapped completion is NOT itself throwing
    /// (<paramref name="isThrowing"/> false), a <c>return()</c> that throws
    /// propagates (step 8) and a <c>return()</c> whose result is not an Object
    /// is a TypeError (step 9); when the completion IS throwing, the original
    /// error wins and any close error is swallowed (step 7).</summary>
    public static void IteratorClose(JsVm? vm, IteratorRecord record, bool isThrowing = false)
    {
        if (!record.Iterator.IsObject) return;
        JsValue ret;
        try
        {
            ret = GetMethod(vm, record.Iterator, "return");
        }
        catch
        {
            // Resolving `return` (a Get + IsCallable check) may itself throw.
            if (isThrowing) return;
            throw;
        }
        if (ret.IsUndefined || ret.IsNull) return;
        JsValue innerResult;
        try
        {
            innerResult = Call(vm, ret, record.Iterator, Array.Empty<JsValue>());
        }
        catch
        {
            // §7.4.10 step 7/8 — swallow only when the inner completion is
            // already a throw; otherwise the return() error propagates.
            if (isThrowing) return;
            throw;
        }
        // §7.4.10 step 9 — only validated on a normal inner completion.
        if (!isThrowing && !innerResult.IsObject)
            throw new JsThrow(vm is not null
                ? vm.Realm.NewTypeError("iterator return() result is not an object")
                : JsValue.String("iterator return() result is not an object"));
    }

    /// <summary>§7.3.11 GetMethod — returns <see cref="JsValue.Undefined"/>
    /// when the resolved value is null/undefined; throws TypeError when the
    /// slot exists but isn't callable.</summary>
    public static JsValue GetMethod(JsVm? vm, JsValue value, JsPropertyKey key)
    {
        if (!value.IsObject)
        {
            // Primitives need to box to reach prototype methods (e.g. String[Symbol.iterator]).
            if (value.IsNullish) return JsValue.Undefined;
            // Caller is expected to ToObject before calling for primitives;
            // we don't have a realm reference, so just return undefined for
            // non-object primitives here.
            return JsValue.Undefined;
        }
        var v = Get(vm, value.AsObject, key);
        if (v.IsUndefined || v.IsNull) return JsValue.Undefined;
        if (!IsCallable(v))
            throw vm is not null
                ? new JsThrow(vm.Realm.NewTypeError($"{key} is not a function"))
                : new JsThrow(JsValue.String("property is not callable"));
        return v;
    }

    public static JsValue GetMethod(JsVm? vm, JsValue value, JsSymbol symbol)
        => GetMethod(vm, value, JsPropertyKey.Symbol(symbol));

    private static bool IsCanonicalArrayIndex(string key)
    {
        if (key.Length == 0 || key[0] == '-' || key.Contains('.', StringComparison.Ordinal)) return false;
        return int.TryParse(key, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out _);
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

/// <summary>§7.4.1 IteratorRecord — bundles the iterator object, the
/// pre-resolved <c>next</c> method, and the <c>done</c> flag. The
/// <c>NextMethod</c> is cached at <c>GetIterator</c> time per spec so the
/// per-step <c>iterator.next</c> property walk doesn't happen on every loop
/// iteration.</summary>
public readonly record struct IteratorRecord(JsValue Iterator, JsValue NextMethod, bool Done);

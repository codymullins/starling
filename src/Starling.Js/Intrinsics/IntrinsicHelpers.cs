using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>
/// Shared helpers used by every intrinsic install path. The B2-2 follow-up
/// migrated <see cref="JsNativeFunction"/> construction to the realm-aware
/// overload so every built-in method inherits from
/// <c>realm.FunctionPrototype</c> and therefore exposes
/// <c>call</c>/<c>apply</c>/<c>bind</c> out of the box.
/// </summary>
internal static class IntrinsicHelpers
{
    /// <summary>
    /// Install a writable + non-enumerable + configurable method on a
    /// prototype/namespace object (the §17 default attributes for built-in
    /// functions). The function inherits from <c>realm.FunctionPrototype</c>
    /// and has its <c>name</c> + <c>length</c> own properties stamped by the
    /// realm-aware <see cref="JsNativeFunction"/> ctor.
    /// </summary>
    public static JsNativeFunction DefineMethod(
        JsRealm realm,
        JsObject target,
        string name,
        int length,
        Func<JsValue, JsValue[], JsValue> body,
        bool isConstructor = false)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(target);
        var fn = new JsNativeFunction(realm, name, length, body, isConstructor);
        target.DefineOwnProperty(name, PropertyDescriptor.BuiltinMethod(JsValue.Object(fn)));
        return fn;
    }

    /// <summary>Install a constant data property (non-writable, non-enumerable, non-configurable).</summary>
    public static void DefineConstant(JsObject target, string name, JsValue value)
    {
        target.DefineOwnProperty(name,
            PropertyDescriptor.Data(value, writable: false, enumerable: false, configurable: false));
    }

    // ====================================================================
    //  Subclassing support — new.target threading for native constructors
    // ====================================================================
    //
    // A native constructor body is `Func<JsValue thisV, JsValue[] args>`. The
    // VM's calling convention puts new.target in the `thisV` slot when the
    // builtin is invoked through [[Construct]]: `AbstractOperations.Construct`
    // calls `nat.Body(JsValue.Object(newTarget), args)`. So:
    //   * `new X()`            → thisV is the ctor object X (its own new.target).
    //   * derived `super(...)` → thisV is the DERIVED class constructor.
    //   * plain `X()` call     → thisV is undefined / the receiver (NOT a ctor).
    // A construct invocation is therefore detected by `thisV` being a
    // constructor object; the same object is the new.target whose `.prototype`
    // OrdinaryCreateFromConstructor uses for the instance's [[Prototype]].

    /// <summary>True iff a native constructor body was reached as a
    /// [[Construct]] (`new`/derived `super()`) rather than a plain call — i.e.
    /// the first arg (new.target) is a constructor object.</summary>
    public static bool IsConstructInvocation(JsValue thisV)
        => thisV.IsObject && AbstractOperations.IsConstructor(thisV);

    /// <summary>§10.1.13 OrdinaryCreateFromConstructor prototype resolution for a
    /// native constructor. Given the new.target carried in <paramref name="thisV"/>
    /// (see notes above), returns <c>newTarget.prototype</c> when it is an object,
    /// otherwise <paramref name="defaultProto"/>. When not a construct invocation
    /// (plain call) the default is returned. This is what lets
    /// `class X extends Builtin {}` produce instances whose [[Prototype]] is
    /// <c>X.prototype</c> while still carrying the builtin's internal slots.</summary>
    public static JsObject NewTargetPrototype(JsVm? vm, JsValue thisV, JsObject defaultProto)
    {
        if (thisV.IsObject && AbstractOperations.IsConstructor(thisV))
        {
            var proto = AbstractOperations.Get(vm, thisV.AsObject, "prototype");
            if (proto.IsObject) return proto.AsObject;
        }
        return defaultProto;
    }
}

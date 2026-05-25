using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>
/// Spec section 20.2 — the Function intrinsic. Populates
/// <see cref="JsRealm.FunctionConstructor"/> and <see cref="JsRealm.FunctionPrototype"/>,
/// then registers the constructor on the realm's global object.
/// </summary>
/// <remarks>
/// <para>
/// The constructor itself (called as <c>Function(src)</c> or
/// <c>new Function(src)</c>) currently raises a <c>TypeError</c> — dynamic
/// code evaluation is deferred to B-9999. The constructor object still has
/// to exist with a correct <c>prototype</c> slot so feature-detection and
/// <c>x instanceof Function</c> checks work.
/// </para>
/// <para><b>Two footguns this install fixes (per B2-2 hand-off):</b></para>
/// <list type="number">
///   <item><c>Function.prototype</c> is now populated with
///   <c>call</c>/<c>apply</c>/<c>bind</c>/<c>toString</c>, so every callable
///   that inherits from it picks the methods up automatically.</item>
///   <item><c>JsFunction</c> instances built at <c>LoadFunction</c> /
///   <c>MakeClosure</c> time are wired to <c>realm.FunctionPrototype</c>
///   (see <c>JsFunction.CreateInstance</c>) and carry their own
///   <c>prototype</c>/<c>name</c>/<c>length</c> data slots.</item>
/// </list>
/// <para><b>@@hasInstance deferral (B3-1):</b> the spec installs
/// <c>Function.prototype[@@hasInstance]</c>; we skip it because Symbol does
/// not exist yet, and the VM's <c>instanceof</c> short-circuits via the
/// <c>prototype</c> own property of the right-hand operand. Re-wire when
/// Symbol lands.</para>
/// </remarks>
public static class FunctionCtor
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var funcProto = realm.FunctionPrototype;

        // ----------------------------------------------------------- Constructor
        // Spec 20.2.1 — `Function(p1, …, pN, body)` builds a function from source:
        // the last argument is the body, the rest are comma-joined into the
        // parameter list. We synthesize `(function anonymous(<params>){<body>})`,
        // compile it in global (eval) scope, and return the resulting function.
        var ctor = new JsNativeFunction("Function", (newTarget, args) =>
        {
            var fn = BuildDynamicFunction(realm, args);
            // §20.2.1.1 CreateDynamicFunction uses OrdinaryCreateFromConstructor
            // for the new function's [[Prototype]]. When subclassed
            // (`class S extends Function {}`), super()'s new.target is S, so the
            // produced function is re-parented to S.prototype for instanceof.
            if (fn.IsObject)
            {
                var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, funcProto);
                if (!ReferenceEquals(instProto, funcProto)) fn.AsObject.SetPrototypeOf(instProto);
            }
            return fn;
        }, isConstructor: true);
        // The constructor inherits from its own prototype object.
        ctor.SetPrototypeOf(funcProto);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(funcProto), writable: false, enumerable: false, configurable: false));
        ctor.DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String("Function"), writable: false, enumerable: false, configurable: true));
        ctor.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(1), writable: false, enumerable: false, configurable: true));

        // Function.prototype.[[Prototype]] is already wired in JsRealm's
        // bootstrap (chain to Object.prototype). Stamp `constructor` and a
        // length of 0 + name "" per spec 20.2.3.
        funcProto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
        funcProto.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(0), writable: false, enumerable: false, configurable: true));
        funcProto.DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String(""), writable: false, enumerable: false, configurable: true));

        // -------------------------------------------------------- Prototype methods
        DefineMethod(realm, funcProto, "call", (thisV, args) => ProtoCall(realm, thisV, args), length: 1);
        DefineMethod(realm, funcProto, "apply", (thisV, args) => ProtoApply(realm, thisV, args), length: 2);
        DefineMethod(realm, funcProto, "bind", (thisV, args) => ProtoBind(realm, thisV, args), length: 1);
        DefineMethod(realm, funcProto, "toString", ProtoToString, length: 0);

        realm.FunctionConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("Function",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    /// <summary>Spec 20.2.1.1 CreateDynamicFunction — assemble source from the
    /// argument list (last arg = body, the rest = parameters) and compile it as a
    /// function expression in global scope. A malformed parameter list or body
    /// surfaces as a SyntaxError via the shared eval path.</summary>
    private static JsValue BuildDynamicFunction(JsRealm realm, JsValue[] args)
    {
        var body = args.Length > 0 ? JsValue.ToStringValue(args[^1]) : "";
        var paramList = args.Length > 1
            ? string.Join(",", args[..^1].Select(JsValue.ToStringValue))
            : "";
        // The trailing newline before ')' guards against a `//`-comment in the
        // last parameter chunk; the newline before '}' likewise guards the body.
        var source = "(function anonymous(" + paramList + "\n) {\n" + body + "\n})";
        return Globals.RunGlobalSource(realm, source, "<anonymous>");
    }

    // ====================================================================
    //                          Prototype implementations
    // ====================================================================

    /// <summary>Spec 20.2.3.3 — Function.prototype.call(thisArg, ...args).</summary>
    private static JsValue ProtoCall(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        if (!AbstractOperations.IsCallable(thisV))
            throw new JsThrow(realm.NewTypeError("Function.prototype.call called on non-callable"));
        var thisArg = args.Length > 0 ? args[0] : JsValue.Undefined;
        var rest = args.Length > 1 ? args[1..] : Array.Empty<JsValue>();
        return AbstractOperations.Call(realm.ActiveVm, thisV, thisArg, rest);
    }

    /// <summary>Spec 20.2.3.1 — Function.prototype.apply(thisArg, argsArray).</summary>
    private static JsValue ProtoApply(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        if (!AbstractOperations.IsCallable(thisV))
            throw new JsThrow(realm.NewTypeError("Function.prototype.apply called on non-callable"));
        var thisArg = args.Length > 0 ? args[0] : JsValue.Undefined;
        var argsArrayV = args.Length > 1 ? args[1] : JsValue.Undefined;

        JsValue[] callArgs;
        if (argsArrayV.IsNullish)
        {
            callArgs = Array.Empty<JsValue>();
        }
        else if (argsArrayV.IsObject)
        {
            var obj = argsArrayV.AsObject;
            var lenV = obj.Get("length");
            var len = (int)JsValue.ToNumber(lenV);
            if (len < 0) len = 0;
            callArgs = new JsValue[len];
            for (var i = 0; i < len; i++)
                callArgs[i] = obj.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            // Per spec, non-object non-nullish argsArray is a TypeError.
            throw new JsThrow(realm.NewTypeError("Function.prototype.apply: argsArray must be an object or null/undefined"));
        }

        return AbstractOperations.Call(realm.ActiveVm, thisV, thisArg, callArgs);
    }

    /// <summary>Spec 20.2.3.2 — Function.prototype.bind(thisArg, ...boundArgs).
    /// Returns a fresh bound function whose <c>[[Prototype]]</c> chains to
    /// <c>Function.prototype</c> so subsequent <c>.bind()</c> calls work.</summary>
    private static JsValue ProtoBind(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        if (!thisV.IsObject || !AbstractOperations.IsCallable(thisV))
            throw new JsThrow(realm.NewTypeError("Function.prototype.bind called on non-callable"));
        var target = thisV.AsObject;
        var boundThis = args.Length > 0 ? args[0] : JsValue.Undefined;
        var boundArgs = args.Length > 1 ? args[1..] : Array.Empty<JsValue>();
        var bound = new JsBoundFunction(realm, target, boundThis, boundArgs);
        // Per spec 20.2.3.2, the bound function has its own `length` and
        // `name` derived from the target. We give sensible defaults for our
        // current surface (Google's bundles rely on `.length` existing).
        var targetLen = (int)JsValue.ToNumber(target.Get("length"));
        var lengthMinusBound = Math.Max(0, targetLen - boundArgs.Length);
        bound.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(lengthMinusBound), writable: false, enumerable: false, configurable: true));
        var targetName = target.Get("name");
        var nameStr = targetName.IsString ? targetName.AsString : "";
        bound.DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String("bound " + nameStr), writable: false, enumerable: false, configurable: true));
        return JsValue.Object(bound);
    }

    /// <summary>Spec 20.2.3.5 — Function.prototype.toString. Yields a feature-
    /// detection-friendly source string. We don't keep raw source bytes
    /// around, so user functions get a placeholder that still matches the
    /// <c>function name() { … }</c> shape that sniffers regex against.</summary>
    private static JsValue ProtoToString(JsValue thisV, JsValue[] args)
    {
        if (!thisV.IsObject) return JsValue.String("function () { [native code] }");
        return thisV.AsObject switch
        {
            JsNativeFunction nat => JsValue.String($"function {nat.Name}() {{ [native code] }}"),
            JsFunction fn => JsValue.String($"function {fn.Name}() {{ [bytecode] }}"),
            JsBoundFunction => JsValue.String("function () { [native code] }"),
            _ => JsValue.String("function () { [native code] }"),
        };
    }

    // ====================================================================
    //                                Helpers
    // ====================================================================

    private static void DefineMethod(JsRealm realm, JsObject target, string name,
        Func<JsValue, JsValue[], JsValue> body, int length)
    {
        var fn = new JsNativeFunction(realm, name, length, body, isConstructor: false);
        target.DefineOwnProperty(name, PropertyDescriptor.BuiltinMethod(JsValue.Object(fn)));
    }
}

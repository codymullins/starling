namespace Tessera.Js.Runtime;

/// <summary>
/// §9.3 Realm Record. Owns the global object plus every intrinsic prototype /
/// constructor used by the engine. Created once per <see cref="JsRuntime"/>;
/// each intrinsic family populates its slots during install
/// (<c>ObjectCtor.Install(realm)</c>, etc.).
/// </summary>
/// <remarks>
/// Today the realm is constructed with bare-bones slots — prototypes are empty
/// objects, error constructors return plain objects with a <c>message</c>
/// property. The full intrinsic surface lands incrementally in B2+.
/// </remarks>
public sealed class JsRealm
{
    public JsObject GlobalObject { get; }

    /// <summary>Host hook for WHATWG Console Standard §2.3 printer output.</summary>
    public ConsoleSink ConsoleSink { get; set; } = DefaultConsoleSink;

    /// <summary>Optional host hook for <c>console.clear()</c>.</summary>
    public Action? ConsoleClear { get; set; }

    internal Dictionary<string, System.Diagnostics.Stopwatch> ConsoleTimers { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, int> ConsoleCounts { get; } = new(StringComparer.Ordinal);
    internal int ConsoleGroupDepth { get; set; }

    // §9.3.1 — Intrinsic prototypes. Populated by each intrinsic's Install pass.
    public JsObject ObjectPrototype { get; }
    public JsObject FunctionPrototype { get; }

    // §9.3.2 — Intrinsic constructors. Each lands with its intrinsic install
    // pass (B2-*). Nullable until populated; consumers should null-check or
    // install order their dependencies.
    public JsObject? ObjectConstructor { get; internal set; }
    public JsObject? StringConstructor { get; internal set; }
    public JsObject? NumberConstructor { get; internal set; }
    public JsObject? BooleanConstructor { get; internal set; }
    public JsObject ArrayPrototype { get; internal set; }
    public JsObject StringPrototype { get; internal set; }
    public JsObject NumberPrototype { get; internal set; }
    public JsObject BooleanPrototype { get; internal set; }
    public JsObject BigIntPrototype { get; internal set; }
    public JsObject SymbolPrototype { get; internal set; }
    public JsObject ErrorPrototype { get; internal set; }
    public JsObject TypeErrorPrototype { get; internal set; }
    public JsObject RangeErrorPrototype { get; internal set; }
    public JsObject ReferenceErrorPrototype { get; internal set; }
    public JsObject SyntaxErrorPrototype { get; internal set; }
    public JsObject UriErrorPrototype { get; internal set; }
    public JsObject EvalErrorPrototype { get; internal set; }
    public JsObject AggregateErrorPrototype { get; internal set; }
    public JsObject IteratorPrototype { get; internal set; }
    public JsObject ArrayIteratorPrototype { get; internal set; }
    public JsObject StringIteratorPrototype { get; internal set; }
    public JsObject MapPrototype { get; internal set; }
    public JsObject MapIteratorPrototype { get; internal set; }
    public JsObject SetPrototype { get; internal set; }
    public JsObject SetIteratorPrototype { get; internal set; }
    public JsObject WeakMapPrototype { get; internal set; }
    public JsObject WeakSetPrototype { get; internal set; }
    public JsObject WeakRefPrototype { get; internal set; }
    public JsObject FinalizationRegistryPrototype { get; internal set; }
    public JsObject PromisePrototype { get; internal set; }
    public JsObject RegExpPrototype { get; internal set; }
    public JsObject DatePrototype { get; internal set; }
    public JsObject ArrayBufferPrototype { get; internal set; }
    public JsObject DataViewPrototype { get; internal set; }
    public JsObject TypedArrayPrototype { get; internal set; }
    public JsObject ProxyPrototype { get; internal set; }
    public JsObject GeneratorPrototype { get; internal set; }
    public JsObject AsyncFunctionPrototype { get; internal set; }
    public JsObject AsyncGeneratorPrototype { get; internal set; }
    public JsObject AsyncIteratorPrototype { get; internal set; }

    /// <summary>The VM currently executing against this realm, if any. Set
    /// by <see cref="JsVm"/> on entry to <c>Run</c>. Native intrinsics that
    /// need to call back into JS (e.g. <c>JSON.parse</c>'s reviver,
    /// <c>JSON.stringify</c>'s replacer / <c>toJSON</c>) read this so they
    /// can pass a VM to <see cref="AbstractOperations.Call"/> for
    /// <see cref="JsFunction"/> dispatch.</summary>
    public JsVm? ActiveVm { get; internal set; }

    public JsRealm()
    {
        // Bootstrap order matters: Object.prototype is the root; everything
        // else inherits from it. Function.prototype inherits from
        // Object.prototype and is itself callable (the empty function) — for
        // bare-bones bootstrap it's just an object.
        ObjectPrototype = new JsObject();
        FunctionPrototype = new JsObject(ObjectPrototype);

        // All other prototypes default to Object.prototype-inheriting empties.
        // Intrinsic install passes replace these with fully-populated objects.
        ArrayPrototype = new JsObject(ObjectPrototype);
        StringPrototype = new JsObject(ObjectPrototype);
        NumberPrototype = new JsObject(ObjectPrototype);
        BooleanPrototype = new JsObject(ObjectPrototype);
        BigIntPrototype = new JsObject(ObjectPrototype);
        SymbolPrototype = new JsObject(ObjectPrototype);
        ErrorPrototype = new JsObject(ObjectPrototype);
        TypeErrorPrototype = new JsObject(ErrorPrototype);
        RangeErrorPrototype = new JsObject(ErrorPrototype);
        ReferenceErrorPrototype = new JsObject(ErrorPrototype);
        SyntaxErrorPrototype = new JsObject(ErrorPrototype);
        UriErrorPrototype = new JsObject(ErrorPrototype);
        EvalErrorPrototype = new JsObject(ErrorPrototype);
        AggregateErrorPrototype = new JsObject(ErrorPrototype);
        IteratorPrototype = new JsObject(ObjectPrototype);
        ArrayIteratorPrototype = new JsObject(IteratorPrototype);
        StringIteratorPrototype = new JsObject(IteratorPrototype);
        MapPrototype = new JsObject(ObjectPrototype);
        MapIteratorPrototype = new JsObject(IteratorPrototype);
        SetPrototype = new JsObject(ObjectPrototype);
        SetIteratorPrototype = new JsObject(IteratorPrototype);
        WeakMapPrototype = new JsObject(ObjectPrototype);
        WeakSetPrototype = new JsObject(ObjectPrototype);
        WeakRefPrototype = new JsObject(ObjectPrototype);
        FinalizationRegistryPrototype = new JsObject(ObjectPrototype);
        PromisePrototype = new JsObject(ObjectPrototype);
        RegExpPrototype = new JsObject(ObjectPrototype);
        DatePrototype = new JsObject(ObjectPrototype);
        ArrayBufferPrototype = new JsObject(ObjectPrototype);
        DataViewPrototype = new JsObject(ObjectPrototype);
        TypedArrayPrototype = new JsObject(ObjectPrototype);
        ProxyPrototype = new JsObject(ObjectPrototype);
        GeneratorPrototype = new JsObject(IteratorPrototype);
        AsyncFunctionPrototype = new JsObject(FunctionPrototype);
        AsyncIteratorPrototype = new JsObject(ObjectPrototype);
        AsyncGeneratorPrototype = new JsObject(AsyncIteratorPrototype);

        GlobalObject = new JsObject(ObjectPrototype);
    }

    /// <summary>Allocate a fresh ordinary object inheriting from
    /// <see cref="ObjectPrototype"/>.</summary>
    public JsObject NewOrdinaryObject() => new JsObject(ObjectPrototype);

    /// <summary>Allocate a fresh ordinary object inheriting from <paramref name="proto"/>.</summary>
    public JsObject NewObjectWithProto(JsObject? proto) => new JsObject(proto);

    /// <summary>Construct a generic Error-like object with a <c>message</c>
    /// data slot. Used by abstract operations to surface engine-level errors
    /// before the full Error intrinsic lands.</summary>
    public JsValue NewError(JsObject errorPrototype, string message)
    {
        var err = new JsObject(errorPrototype);
        err.DefineOwnProperty("message",
            PropertyDescriptor.Data(JsValue.String(message), writable: true, enumerable: false, configurable: true));
        return JsValue.Object(err);
    }

    public JsValue NewTypeError(string message) => NewError(TypeErrorPrototype, message);
    public JsValue NewRangeError(string message) => NewError(RangeErrorPrototype, message);
    public JsValue NewReferenceError(string message) => NewError(ReferenceErrorPrototype, message);
    public JsValue NewSyntaxError(string message) => NewError(SyntaxErrorPrototype, message);
    public JsValue NewUriError(string message) => NewError(UriErrorPrototype, message);

    /// <summary>§7.1.18 boxing for primitives. Placeholder: returns a wrapper
    /// object whose [[Prototype]] is the matching <c>*Prototype</c> and which
    /// stores the primitive in an internal slot under <c>__primitiveValue</c>
    /// until the typed wrapper subclasses land.</summary>
    internal JsObject BoxBoolean(JsValue v) => BoxPrimitive(BooleanPrototype, v);
    internal JsObject BoxNumber(JsValue v) => BoxPrimitive(NumberPrototype, v);
    internal JsObject BoxString(JsValue v)
    {
        var box = BoxPrimitive(StringPrototype, v);
        if (v.IsString)
            Tessera.Js.Intrinsics.StringCtor.DefineStringData(box, v.AsString);
        return box;
    }
    internal JsObject BoxBigInt(JsValue v) => BoxPrimitive(BigIntPrototype, v);

    private static JsObject BoxPrimitive(JsObject proto, JsValue value)
    {
        var box = new JsObject(proto);
        box.DefineOwnProperty("__primitiveValue",
            PropertyDescriptor.Data(value, writable: false, enumerable: false, configurable: false));
        return box;
    }

    private static void DefaultConsoleSink(ConsoleLevel level, string message)
    {
        var writer = level is ConsoleLevel.Warn or ConsoleLevel.Error ? Console.Error : Console.Out;
        writer.WriteLine(message);
    }
}

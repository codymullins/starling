namespace Starling.Js.Runtime;

/// <summary>
/// Ordinary JS object per §10.1. Property-bag with descriptors and a prototype
/// chain. Subclasses override behavior for arrays, functions, proxies, etc.
/// </summary>
/// <remarks>
/// String and Symbol property keys live in separate namespaces per ECMA-262
/// §6.1.7. Indexed-integer fast paths land with <c>JsArray</c>'s dense storage
/// subclass.
/// </remarks>
public class JsObject
{
    private readonly Dictionary<string, PropertyDescriptor> _properties =
        new(StringComparer.Ordinal);
    private readonly Dictionary<JsSymbol, PropertyDescriptor> _symbolProperties =
        new();

    /// <summary>The [[Prototype]] internal slot. Mutate via
    /// <see cref="SetPrototypeOf"/> so subclasses can override.</summary>
    public JsObject? Prototype { get; private set; }

    /// <summary>The [[Extensible]] internal slot. When false, new own properties
    /// are rejected. Virtual so exotic objects (notably <see cref="JsProxy"/>)
    /// can route through the <c>isExtensible</c> trap.</summary>
    public virtual bool Extensible => _extensible;

    /// <summary>Backing field for <see cref="Extensible"/>; mutated only by
    /// <see cref="PreventExtensions"/> on ordinary objects.</summary>
    private bool _extensible = true;

    /// <summary>Models the spec's <c>[[ParameterMap]]</c> internal slot: true for
    /// an <c>arguments</c> exotic object. Read by §20.1.3.6 step 5 so
    /// <c>Object.prototype.toString</c> reports <c>"[object Arguments]"</c>
    /// (which legacy feature-detection — e.g. underscore.js <c>isArguments</c> —
    /// relies on instead of poking the poisoned <c>callee</c> accessor).</summary>
    public bool IsArgumentsExotic { get; internal set; }

    /// <summary>Models the spec's per-object [[PrivateElements]] brand set: the
    /// set of mangled private-name keys this object <em>itself</em> carries.
    /// A private field adds its mangled key when defined; instance private
    /// methods/accessors add their brands when the instance is initialized
    /// (post-<c>super()</c> for derived classes); static private members brand
    /// the constructor object only. The brand check for <c>obj.#x</c> /
    /// <c>obj.#x = v</c> / <c>obj.#m()</c> / <c>#x in obj</c> consults THIS set
    /// directly (never walking the prototype chain), so a subclass constructor
    /// or a Proxy — which do not carry the brand — correctly throw a TypeError
    /// per §13.3.4 PrivateGet/PrivateSet/PrivateElementFind. Null until the
    /// first brand is added, to keep ordinary objects allocation-free.</summary>
    private HashSet<string>? _privateBrands;

    /// <summary>True iff this object itself carries the brand for the given
    /// mangled private-name key (see <see cref="_privateBrands"/>). Does NOT
    /// walk the prototype chain — a derived/other-receiver constructor or a
    /// Proxy lacks the brand and must throw a TypeError on private access.</summary>
    public bool HasPrivateBrand(string mangledName)
        => _privateBrands is not null && _privateBrands.Contains(mangledName);

    /// <summary>Install the brand for a mangled private-name key on this object
    /// (idempotent). Called when a private field is defined and when an
    /// instance is initialized with private methods/accessors, or to brand a
    /// constructor with its static private members.</summary>
    public void AddPrivateBrand(string mangledName)
        => (_privateBrands ??= new HashSet<string>(StringComparer.Ordinal)).Add(mangledName);

    public JsObject() { }

    public JsObject(JsObject? prototype) { Prototype = prototype; }

    /// <summary>§10.1.2 [[SetPrototypeOf]]. Returns true on success.</summary>
    public virtual bool SetPrototypeOf(JsObject? proto)
    {
        if (!Extensible && !ReferenceEquals(proto, Prototype)) return false;
        // Cycle check: walking up the new chain must not lead back to this.
        for (var p = proto; p is not null; p = p.Prototype)
            if (ReferenceEquals(p, this)) return false;
        Prototype = proto;
        return true;
    }

    /// <summary>§10.1.1 [[GetPrototypeOf]]. Virtual hook so
    /// <see cref="JsProxy"/> can route through the <c>getPrototypeOf</c> trap;
    /// ordinary objects return the <see cref="Prototype"/> slot directly.</summary>
    public virtual JsObject? GetPrototypeOf() => Prototype;

    /// <summary>§10.1.3 [[PreventExtensions]]. Virtual so <see cref="JsProxy"/>
    /// can route through the <c>preventExtensions</c> trap.</summary>
    public virtual bool PreventExtensions() { _extensible = false; return true; }

    /// <summary>Spec [[Get]] simplified to data-only resolution: walks the
    /// prototype chain and returns the data slot's value, or Undefined.
    /// Accessor invocation lives in <c>AbstractOperations.Get</c> which has the
    /// VM in scope to dispatch the getter.</summary>
    public virtual JsValue Get(string name)
    {
        for (var o = this; o is not null; o = o.Prototype)
        {
            if (o._properties.TryGetValue(name, out var desc))
                return desc.IsAccessor ? JsValue.Undefined : desc.Value;
        }
        return JsValue.Undefined;
    }

    public virtual JsValue Get(JsSymbol symbol)
    {
        for (var o = this; o is not null; o = o.Prototype)
        {
            if (o._symbolProperties.TryGetValue(symbol, out var desc))
                return desc.IsAccessor ? JsValue.Undefined : desc.Value;
        }
        return JsValue.Undefined;
    }

    public JsValue Get(JsPropertyKey key) => key.IsSymbol ? Get(key.AsSymbol) : Get(key.AsString);

    /// <summary>Spec [[Set]] simplified: own-data-property fast path for the
    /// vast majority of writes. Accessor + cross-chain writes go through
    /// <c>AbstractOperations.Set</c> (VM-aware).</summary>
    public virtual void Set(string name, JsValue value)
    {
        if (_properties.TryGetValue(name, out var desc))
        {
            if (desc.IsAccessor) return; // accessor path — caller should use AbstractOperations.Set
            if (!desc.Writable) return;
            _properties[name] = desc.WithValue(value);
            return;
        }
        if (!Extensible) return;
        _properties[name] = PropertyDescriptor.Data(value);
    }

    public virtual void Set(JsSymbol symbol, JsValue value)
    {
        if (_symbolProperties.TryGetValue(symbol, out var desc))
        {
            if (desc.IsAccessor) return;
            if (!desc.Writable) return;
            _symbolProperties[symbol] = desc.WithValue(value);
            return;
        }
        if (!Extensible) return;
        _symbolProperties[symbol] = PropertyDescriptor.Data(value);
    }

    public void Set(JsPropertyKey key, JsValue value)
    {
        if (key.IsSymbol) Set(key.AsSymbol, value);
        else Set(key.AsString, value);
    }

    /// <summary>Spec [[HasProperty]] — walks the prototype chain. Virtual so
    /// <see cref="JsProxy"/> can route through the <c>has</c> trap. Uses
    /// <see cref="HasOwn(string)"/> so exotic subclasses (JsArray indices,
    /// JsTypedArray elements) participate in the walk.</summary>
    public virtual bool Has(string name)
    {
        for (var o = this; o is not null; o = o.Prototype)
            if (o.HasOwn(name)) return true;
        return false;
    }

    public virtual bool Has(JsSymbol symbol)
    {
        for (var o = this; o is not null; o = o.Prototype)
            if (o.HasOwn(symbol)) return true;
        return false;
    }

    public bool Has(JsPropertyKey key) => key.IsSymbol ? Has(key.AsSymbol) : Has(key.AsString);

    /// <summary>§10.1.5 [[GetOwnProperty]] — own slot only, no chain walk.</summary>
    public virtual bool HasOwn(string name) => _properties.ContainsKey(name);
    public virtual bool HasOwn(JsSymbol symbol) => _symbolProperties.ContainsKey(symbol);
    public bool HasOwn(JsPropertyKey key) => key.IsSymbol ? HasOwn(key.AsSymbol) : HasOwn(key.AsString);

    /// <summary>Returns the own descriptor, or <c>null</c> if no own property.</summary>
    public virtual PropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _properties.TryGetValue(name, out var d) ? d : null;
    public virtual PropertyDescriptor? GetOwnPropertyDescriptor(JsSymbol symbol)
        => _symbolProperties.TryGetValue(symbol, out var d) ? d : null;
    public PropertyDescriptor? GetOwnPropertyDescriptor(JsPropertyKey key)
        => key.IsSymbol ? GetOwnPropertyDescriptor(key.AsSymbol) : GetOwnPropertyDescriptor(key.AsString);

    /// <summary>§10.1.6 [[DefineOwnProperty]] (simplified validator). Returns
    /// false when the operation is rejected (e.g. non-configurable conflict
    /// or non-extensible object).</summary>
    public virtual bool DefineOwnProperty(string name, PropertyDescriptor desc)
        => DefineOwnPropertyCore(_properties, name, desc);

    public virtual bool DefineOwnProperty(JsSymbol symbol, PropertyDescriptor desc)
        => DefineOwnPropertyCore(_symbolProperties, symbol, desc);

    public bool DefineOwnProperty(JsPropertyKey key, PropertyDescriptor desc)
        => key.IsSymbol ? DefineOwnProperty(key.AsSymbol, desc) : DefineOwnProperty(key.AsString, desc);

    private bool DefineOwnPropertyCore<TKey>(Dictionary<TKey, PropertyDescriptor> table, TKey key, PropertyDescriptor desc)
        where TKey : notnull
    {
        if (table.TryGetValue(key, out var existing))
        {
            if (!existing.Configurable)
            {
                // Allow same-value re-define; reject otherwise. Spec §10.1.6.3
                // is more nuanced but this covers the common cases.
                if (existing.IsAccessor != desc.IsAccessor) return false;
                if (existing.Configurable != desc.Configurable) return false;
                if (existing.Enumerable != desc.Enumerable) return false;
                if (!existing.IsAccessor && existing.Writable != desc.Writable) return false;
            }
        }
        else if (!Extensible)
        {
            return false;
        }
        table[key] = desc;
        return true;
    }

    /// <summary>§10.1.10 [[Delete]] — returns true on success, false if the
    /// property exists and is non-configurable.</summary>
    public virtual bool Delete(string name)
    {
        if (!_properties.TryGetValue(name, out var desc)) return true;
        if (!desc.Configurable) return false;
        return _properties.Remove(name);
    }

    public virtual bool Delete(JsSymbol symbol)
    {
        if (!_symbolProperties.TryGetValue(symbol, out var desc)) return true;
        if (!desc.Configurable) return false;
        return _symbolProperties.Remove(symbol);
    }

    public bool Delete(JsPropertyKey key) => key.IsSymbol ? Delete(key.AsSymbol) : Delete(key.AsString);

    /// <summary>All own keys, in insertion order (Dictionary preserves it
    /// since .NET 6).</summary>
    public virtual IEnumerable<string> Keys => _properties.Keys;
    public IEnumerable<JsSymbol> SymbolKeys => _symbolProperties.Keys;
    public virtual IEnumerable<JsPropertyKey> OwnPropertyKeys
    {
        get
        {
            foreach (var key in _properties.Keys) yield return JsPropertyKey.String(key);
            foreach (var key in _symbolProperties.Keys) yield return JsPropertyKey.Symbol(key);
        }
    }

    /// <summary>Own keys filtered to enumerable data properties — used by
    /// <c>Object.keys</c> and friends.</summary>
    public virtual IEnumerable<string> EnumerableKeys()
    {
        foreach (var pair in _properties)
            if (pair.Value.Enumerable) yield return pair.Key;
    }

    public IEnumerable<JsSymbol> EnumerableSymbolKeys()
    {
        foreach (var pair in _symbolProperties)
            if (pair.Value.Enumerable) yield return pair.Key;
    }

    public override string ToString() => "[object Object]";
}

/// <summary>
/// A native (host-side) function callable from JS. The body receives the
/// resolved <c>this</c> value and an argument array; returns a
/// <see cref="JsValue"/>. <see cref="IsConstructor"/> opts into being callable
/// via <c>new</c> (defaults to true so intrinsic constructors like
/// <c>new Array</c> work).
/// </summary>
/// <remarks>
/// <para><b>Prototype chain (B2-2):</b> when constructed with a non-null
/// <c>realm</c>, the function's <c>[[Prototype]]</c> is set to
/// <see cref="JsRealm.FunctionPrototype"/> so it inherits <c>call</c>,
/// <c>apply</c>, <c>bind</c>, and <c>toString</c>. Pass the realm in
/// whenever the call site has one in scope (every <c>Install(realm)</c>
/// intrinsic does). Legacy call sites that pass no realm produce a
/// function whose prototype chain is empty; that's tolerated for now but
/// will silently break <c>fn.call(...)</c> against the resulting instance,
/// so prefer the realm-aware overload for new code.</para>
/// </remarks>
public sealed class JsNativeFunction : JsObject
{
    public string Name { get; }
    public Func<JsValue, JsValue[], JsValue> Body { get; }
    public bool IsConstructor { get; }

    /// <summary>Declared <c>length</c> (declared positional arity). Mirrored
    /// as an own non-enumerable property when set via the realm-aware
    /// constructor; intrinsics that build descriptors by hand may also stamp
    /// it directly via DefineOwnProperty.</summary>
    public int Length { get; }

    /// <summary>Construct with a (thisValue, args) callable.</summary>
    public JsNativeFunction(string name, Func<JsValue, JsValue[], JsValue> body, bool isConstructor = true)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        IsConstructor = isConstructor;
        Length = 0;
    }

    /// <summary>Realm-aware constructor — wires
    /// <c>[[Prototype]] = realm.FunctionPrototype</c> and stamps
    /// own <c>name</c> + <c>length</c> data properties (W=false, E=false,
    /// C=true per §17 builtin function defaults). Prefer this overload when
    /// the realm is in scope.</summary>
    public JsNativeFunction(JsRealm realm, string name, int length, Func<JsValue, JsValue[], JsValue> body, bool isConstructor = false)
        : base(realm?.FunctionPrototype)
    {
        ArgumentNullException.ThrowIfNull(realm);
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        IsConstructor = isConstructor;
        Length = length;
        DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String(name), writable: false, enumerable: false, configurable: true));
        DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(length), writable: false, enumerable: false, configurable: true));
    }

    /// <summary>Convenience overload for legacy (args-only) host functions.</summary>
    public JsNativeFunction(string name, Func<JsValue[], JsValue> body)
        : this(name, (_, a) => body(a), isConstructor: false) { }

    public override string ToString() => $"function {Name}() {{ [native code] }}";
}

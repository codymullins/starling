namespace Tessera.Js.Runtime;

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
    /// are rejected.</summary>
    public bool Extensible { get; private set; } = true;

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

    /// <summary>§10.1.3 [[PreventExtensions]].</summary>
    public bool PreventExtensions() { Extensible = false; return true; }

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

    /// <summary>Spec [[HasProperty]] — walks the prototype chain.</summary>
    public bool Has(string name)
    {
        for (var o = this; o is not null; o = o.Prototype)
            if (o._properties.ContainsKey(name)) return true;
        return false;
    }

    public bool Has(JsSymbol symbol)
    {
        for (var o = this; o is not null; o = o.Prototype)
            if (o._symbolProperties.ContainsKey(symbol)) return true;
        return false;
    }

    public bool Has(JsPropertyKey key) => key.IsSymbol ? Has(key.AsSymbol) : Has(key.AsString);

    /// <summary>§10.1.5 [[GetOwnProperty]] — own slot only, no chain walk.</summary>
    public bool HasOwn(string name) => _properties.ContainsKey(name);
    public bool HasOwn(JsSymbol symbol) => _symbolProperties.ContainsKey(symbol);
    public bool HasOwn(JsPropertyKey key) => key.IsSymbol ? HasOwn(key.AsSymbol) : HasOwn(key.AsString);

    /// <summary>Returns the own descriptor, or <c>null</c> if no own property.</summary>
    public PropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _properties.TryGetValue(name, out var d) ? d : null;
    public PropertyDescriptor? GetOwnPropertyDescriptor(JsSymbol symbol)
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
    public bool Delete(string name)
    {
        if (!_properties.TryGetValue(name, out var desc)) return true;
        if (!desc.Configurable) return false;
        return _properties.Remove(name);
    }

    public bool Delete(JsSymbol symbol)
    {
        if (!_symbolProperties.TryGetValue(symbol, out var desc)) return true;
        if (!desc.Configurable) return false;
        return _symbolProperties.Remove(symbol);
    }

    public bool Delete(JsPropertyKey key) => key.IsSymbol ? Delete(key.AsSymbol) : Delete(key.AsString);

    /// <summary>All own keys, in insertion order (Dictionary preserves it
    /// since .NET 6).</summary>
    public IEnumerable<string> Keys => _properties.Keys;
    public IEnumerable<JsSymbol> SymbolKeys => _symbolProperties.Keys;
    public IEnumerable<JsPropertyKey> OwnPropertyKeys
    {
        get
        {
            foreach (var key in _properties.Keys) yield return JsPropertyKey.String(key);
            foreach (var key in _symbolProperties.Keys) yield return JsPropertyKey.Symbol(key);
        }
    }

    /// <summary>Own keys filtered to enumerable data properties — used by
    /// <c>Object.keys</c> and friends.</summary>
    public IEnumerable<string> EnumerableKeys()
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
public sealed class JsNativeFunction : JsObject
{
    public string Name { get; }
    public Func<JsValue, JsValue[], JsValue> Body { get; }
    public bool IsConstructor { get; }

    /// <summary>Construct with a (thisValue, args) callable.</summary>
    public JsNativeFunction(string name, Func<JsValue, JsValue[], JsValue> body, bool isConstructor = true)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        IsConstructor = isConstructor;
    }

    /// <summary>Convenience overload for legacy (args-only) host functions.</summary>
    public JsNativeFunction(string name, Func<JsValue[], JsValue> body)
        : this(name, (_, a) => body(a), isConstructor: false) { }

    public override string ToString() => $"function {Name}() {{ [native code] }}";
}

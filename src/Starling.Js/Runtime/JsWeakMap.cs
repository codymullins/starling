using System.Runtime.CompilerServices;

namespace Tessera.Js.Runtime;

/// <summary>
/// §24.3 WeakMap Objects — object-keyed map with weak references so entries
/// don't keep their keys alive. Backed by .NET's
/// <see cref="ConditionalWeakTable{TKey,TValue}"/>, which gives us
/// GC-correctness for free (entries are reclaimed when the key has no other
/// strong references). Not iterable; no <c>size</c>.
/// </summary>
public sealed class JsWeakMap : JsObject
{
    private readonly ConditionalWeakTable<JsObject, BoxedValue> _table = new();

    public JsWeakMap(JsRealm realm) : base(realm.WeakMapPrototype) { }

    public bool Has(JsObject key) => _table.TryGetValue(key, out _);

    public JsValue Get(JsObject key)
        => _table.TryGetValue(key, out var box) ? box.Value : JsValue.Undefined;

    public void Set(JsObject key, JsValue value)
    {
        // ConditionalWeakTable.Add throws on duplicate keys; remove first then
        // re-add to mimic Map semantics (last write wins).
        if (_table.TryGetValue(key, out var existing))
        {
            existing.Value = value;
            return;
        }
        _table.Add(key, new BoxedValue(value));
    }

    public bool Delete(JsObject key) => _table.Remove(key);

    /// <summary>Box so the ConditionalWeakTable's value slot is a single
    /// reference we can mutate in place when a key gets re-set.</summary>
    private sealed class BoxedValue
    {
        public JsValue Value;
        public BoxedValue(JsValue value) { Value = value; }
    }
}

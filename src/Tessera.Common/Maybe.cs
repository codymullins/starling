namespace Tessera.Common;

/// <summary>
/// Optional value type. Prefer over <c>T?</c> for value types in public APIs where
/// "absent" is a meaningful state, not a null reference. See 01_ARCHITECTURE.md §C.1.
/// </summary>
public readonly struct Maybe<T>
{
    private readonly T _value;
    private readonly bool _hasValue;

    private Maybe(T value, bool hasValue)
    {
        _value = value;
        _hasValue = hasValue;
    }

    public static Maybe<T> Some(T value) => new(value, true);
    public static Maybe<T> None => default;

    public bool HasValue => _hasValue;

    public T Value => _hasValue
        ? _value
        : throw new InvalidOperationException("Maybe was None; check HasValue before reading Value.");

    public T OrElse(T fallback) => _hasValue ? _value : fallback;

    public Maybe<TOut> Map<TOut>(Func<T, TOut> f)
        => _hasValue ? Maybe<TOut>.Some(f(_value)) : Maybe<TOut>.None;

    public override string ToString() => _hasValue ? $"Some({_value})" : "None";
}

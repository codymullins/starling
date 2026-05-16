namespace Tessera.Common;

/// <summary>
/// Discriminated-union style result. Use for recoverable errors; reserve throwing
/// for invariant violations. See 01_ARCHITECTURE.md §C.1 / §G.
/// </summary>
public readonly struct Result<T, TError>
{
    private readonly T _value;
    private readonly TError _error;
    private readonly bool _isOk;

    private Result(T value, TError error, bool isOk)
    {
        _value = value;
        _error = error;
        _isOk = isOk;
    }

    public static Result<T, TError> Ok(T value) => new(value, default!, true);
    public static Result<T, TError> Err(TError error) => new(default!, error, false);

    public bool IsOk => _isOk;
    public bool IsErr => !_isOk;

    public T Value => _isOk
        ? _value
        : throw new InvalidOperationException("Result was Err; check IsOk before reading Value.");

    public TError Error => !_isOk
        ? _error
        : throw new InvalidOperationException("Result was Ok; check IsErr before reading Error.");

    public TOut Match<TOut>(Func<T, TOut> ok, Func<TError, TOut> err)
        => _isOk ? ok(_value) : err(_error);

    public override string ToString() => _isOk ? $"Ok({_value})" : $"Err({_error})";
}

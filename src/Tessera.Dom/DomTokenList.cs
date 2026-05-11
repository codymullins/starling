using System.Collections;

namespace Tessera.Dom;

public sealed class DomTokenList : IReadOnlyList<string>
{
    private readonly Func<string> _getValue;
    private readonly Action<string> _setValue;

    internal DomTokenList(Func<string> getValue, Action<string> setValue)
    {
        _getValue = getValue;
        _setValue = setValue;
    }

    public int Count => Tokens.Count;

    public string this[int index] => Tokens[index];

    public bool Contains(string token)
    {
        ValidateToken(token);
        return Tokens.Contains(token, StringComparer.Ordinal);
    }

    public void Add(string token)
    {
        ValidateToken(token);
        var tokens = Tokens;
        if (tokens.Contains(token, StringComparer.Ordinal))
            return;

        tokens.Add(token);
        _setValue(string.Join(' ', tokens));
    }

    public bool Remove(string token)
    {
        ValidateToken(token);
        var tokens = Tokens;
        if (!tokens.Remove(token))
            return false;

        _setValue(string.Join(' ', tokens));
        return true;
    }

    public IEnumerator<string> GetEnumerator() => Tokens.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private List<string> Tokens => _getValue()
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();

    private static void ValidateToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (token.Any(char.IsWhiteSpace))
            throw new ArgumentException("A DOM token cannot contain whitespace.", nameof(token));
    }
}

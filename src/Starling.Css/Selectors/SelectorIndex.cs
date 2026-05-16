using Tessera.Dom;

namespace Tessera.Css.Selectors;

public sealed class SelectorIndex<T>
{
    private readonly Dictionary<string, List<SelectorIndexEntry<T>>> _ids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SelectorIndexEntry<T>>> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SelectorIndexEntry<T>>> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SelectorIndexEntry<T>> _universal = [];
    private int _sequence;

    public void Add(SelectorList selectorList, T value)
    {
        ArgumentNullException.ThrowIfNull(selectorList);
        foreach (var selector in selectorList.Selectors)
            Add(selector, value);
    }

    public void Add(ComplexSelector selector, T value)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var pseudo = selector.TargetPseudoElement;
        var entry = new SelectorIndexEntry<T>(selector, value, _sequence++, pseudo);

        // Bucket by the last non-pseudo-element simple selector in the rightmost compound.
        var rightmost = selector.RightmostCompound.SimpleSelectors;
        var bucketSelectors = rightmost.Where(s => s is not PseudoElementSelector).ToList();

        if (bucketSelectors.OfType<IdSelector>().FirstOrDefault() is { } id)
            AddTo(_ids, id.Id, entry);
        else if (bucketSelectors.OfType<ClassSelector>().FirstOrDefault() is { } @class)
            AddTo(_classes, @class.ClassName, entry);
        else if (bucketSelectors.OfType<TypeSelector>().FirstOrDefault() is { } type)
            AddTo(_tags, type.LocalName, entry);
        else
            _universal.Add(entry);
    }

    public IReadOnlyList<SelectorIndexEntry<T>> GetCandidates(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var entries = new Dictionary<int, SelectorIndexEntry<T>>();
        if (!string.IsNullOrEmpty(element.Id) && _ids.TryGetValue(element.Id, out var idEntries))
            AddEntries(entries, idEntries);
        foreach (var className in element.ClassList)
            if (_classes.TryGetValue(className, out var classEntries))
                AddEntries(entries, classEntries);
        if (_tags.TryGetValue(element.LocalName, out var tagEntries))
            AddEntries(entries, tagEntries);
        AddEntries(entries, _universal);
        return entries.Values.OrderBy(entry => entry.Sequence).ToList();
    }

    private static void AddTo(
        Dictionary<string, List<SelectorIndexEntry<T>>> dictionary,
        string key,
        SelectorIndexEntry<T> entry)
    {
        if (!dictionary.TryGetValue(key, out var entries))
        {
            entries = [];
            dictionary[key] = entries;
        }

        entries.Add(entry);
    }

    private static void AddEntries(
        Dictionary<int, SelectorIndexEntry<T>> target,
        IEnumerable<SelectorIndexEntry<T>> entries)
    {
        foreach (var entry in entries)
            target.TryAdd(entry.Sequence, entry);
    }
}

public sealed record SelectorIndexEntry<T>(
    ComplexSelector Selector,
    T Value,
    int Sequence,
    PseudoElement? PseudoElementTarget = null);

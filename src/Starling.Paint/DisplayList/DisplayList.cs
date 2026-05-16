namespace Tessera.Paint.DisplayList;

/// <summary>An ordered, flat list of paint operations.</summary>
public sealed class DisplayList
{
    private readonly List<DisplayItem> _items = [];

    public IReadOnlyList<DisplayItem> Items => _items;

    public void Add(DisplayItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }
}

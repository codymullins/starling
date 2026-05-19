using Xunit.v3;

namespace Starling.Spec;

/// <summary>
/// Tags a test (class or method) with the CSS/web specification it covers.
/// Adds xUnit traits <c>Spec</c>, <c>SpecUrl</c>, and (optionally) <c>Section</c>
/// so the suite can be filtered (<c>dotnet test --filter "Spec=css-color-5"</c>)
/// and so the SpecGen reporter can build coverage tables.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class SpecAttribute(string id, string url, string? section = null) : Attribute, ITraitAttribute
{
    public string Id { get; } = id;
    public string Url { get; } = url;
    public string? Section { get; } = section;

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits()
    {
        var traits = new List<KeyValuePair<string, string>>(3)
        {
            new("Spec", Id),
            new("SpecUrl", Url),
        };
        if (!string.IsNullOrEmpty(Section))
        {
            traits.Add(new("Section", Section));
        }
        return traits;
    }
}

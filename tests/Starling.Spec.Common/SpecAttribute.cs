
namespace Starling.Spec;

/// <summary>
/// Tags a test (class or method) with the CSS/web specification it covers.
/// Inherits <see cref="TestCategoryBaseAttribute"/> so the categories
/// <c>Spec:{id}</c> and (optionally) <c>Section:{section}</c> are picked up by
/// the MSTest runner — filter via <c>dotnet test --filter "TestCategory~Spec:css-color-5"</c>
/// and the SpecGen reporter can build coverage tables from them.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class SpecAttribute : TestCategoryBaseAttribute
{
    public SpecAttribute(string id, string url, string? section = null)
    {
        Id = id;
        Url = url;
        Section = section;

        var categories = new List<string>(2) { $"Spec:{id}" };
        if (!string.IsNullOrEmpty(section))
        {
            categories.Add($"Section:{section}");
        }
        TestCategories = categories;
    }

    public string Id { get; }
    public string Url { get; }
    public string? Section { get; }

    public override IList<string> TestCategories { get; }
}

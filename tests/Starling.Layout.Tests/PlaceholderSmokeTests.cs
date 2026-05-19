using FluentAssertions;
namespace Starling.Layout.Tests;

[TestClass]
public class PlaceholderSmokeTests
{
    [TestMethod]
    public void Project_loads_and_xunit_runs()
    {
        // Reference the placeholder so the project reference isn't unused.
        typeof(Starling.Layout.PlaceholderNote).Should().NotBeNull();
    }
}

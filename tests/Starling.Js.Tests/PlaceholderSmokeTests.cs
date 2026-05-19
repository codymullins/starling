using FluentAssertions;
namespace Starling.Js.Tests;

[TestClass]
public class PlaceholderSmokeTests
{
    [TestMethod]
    public void Project_loads_and_xunit_runs()
    {
        // Reference the placeholder so the project reference isn't unused.
        typeof(Starling.Js.PlaceholderNote).Should().NotBeNull();
    }
}

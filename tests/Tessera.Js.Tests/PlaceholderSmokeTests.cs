using FluentAssertions;
using Xunit;

namespace Tessera.Js.Tests;

public class PlaceholderSmokeTests
{
    [Fact]
    public void Project_loads_and_xunit_runs()
    {
        // Reference the placeholder so the project reference isn't unused.
        typeof(Tessera.Js.PlaceholderNote).Should().NotBeNull();
    }
}

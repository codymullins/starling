using FluentAssertions;
using Xunit;

namespace Tessera.Bindings.Tests;

public class PlaceholderSmokeTests
{
    [Fact]
    public void Project_loads_and_xunit_runs()
    {
        // Reference the placeholder so the project reference isn't unused.
        typeof(Tessera.Bindings.PlaceholderNote).Should().NotBeNull();
    }
}

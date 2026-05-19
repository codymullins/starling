using FluentAssertions;
namespace Starling.Engine.Tests;

[TestClass]
public sealed class NavigationHistoryTests
{
    [TestMethod]
    public void Navigate_back_forward_and_reload_follow_browser_history_rules()
    {
        var history = new NavigationHistory();

        history.Navigate("https://example.test/one");
        history.Navigate("https://example.test/two");

        history.Current.Should().Be("https://example.test/two");
        history.CanGoBack.Should().BeTrue();
        history.Back().Should().Be("https://example.test/one");
        history.Reload().Should().Be("https://example.test/one");
        history.CanGoForward.Should().BeTrue();
        history.Forward().Should().Be("https://example.test/two");
    }

    [TestMethod]
    public void New_navigation_after_back_discards_forward_entries()
    {
        var history = new NavigationHistory();

        history.Navigate("https://example.test/one");
        history.Navigate("https://example.test/two");
        history.Back();
        history.Navigate("https://example.test/three");

        history.Entries.Should().Equal("https://example.test/one", "https://example.test/three");
        history.CanGoForward.Should().BeFalse();
    }
}

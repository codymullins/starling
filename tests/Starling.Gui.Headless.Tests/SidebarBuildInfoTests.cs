using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Starling.Gui.Chrome;
using Starling.Gui.Theme;

namespace Starling.Gui.Headless.Tests;

/// <summary>
/// The sidebar footer surfaces the build's runtime facts — commit SHA, JS engine,
/// and render engine — as a labelled column. This pins that they all render.
/// </summary>
public class SidebarBuildInfoTests
{
    [AvaloniaFact]
    public void Footer_renders_commit_js_and_render_in_a_column()
    {
        var sidebar = new Sidebar(
            new ThemeManager(), System.Array.Empty<TabInfo>(), activeId: null,
            onTabActivated: null,
            build: new BuildInfo("abc12345", "jint", "imagesharp-gpu"));
        var window = new Window { Content = sidebar, Width = 232, Height = 600 };
        window.Show();
        window.CaptureRenderedFrame(); // force measure/arrange so the tree materializes
        try
        {
            var texts = sidebar.GetVisualDescendants().OfType<TextBlock>()
                .Select(tb => tb.Text).ToList();

            texts.Should().Contain("commit").And.Contain("js").And.Contain("render");
            texts.Should().Contain("abc12345", "the commit value is shown");
            texts.Should().Contain("jint", "the active JS engine is shown");
            texts.Should().Contain("imagesharp-gpu", "the active render engine is shown");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Footer_renders_a_dash_for_missing_values()
    {
        var sidebar = new Sidebar(
            new ThemeManager(), System.Array.Empty<TabInfo>(), activeId: null,
            onTabActivated: null, build: null);
        var window = new Window { Content = sidebar, Width = 232, Height = 600 };
        window.Show();
        window.CaptureRenderedFrame();
        try
        {
            var texts = sidebar.GetVisualDescendants().OfType<TextBlock>()
                .Select(tb => tb.Text).ToList();
            texts.Should().Contain("—", "missing build facts fall back to a dash");
        }
        finally { window.Close(); }
    }
}

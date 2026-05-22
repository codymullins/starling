using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using AwesomeAssertions;
using Starling.Gui.Chrome;
using Starling.Gui.DevTools;
using Starling.Gui.Theme;
using Starling.Telemetry;

namespace Starling.Gui.Headless.Tests;

/// <summary>
/// Coverage for DevTools docking affordances: the tab strip exposes a
/// left/bottom/right/detach cluster, highlights the live position, and
/// re-highlights when <see cref="DevToolsPanel.SetDock"/> moves the panel.
/// </summary>
public class DevToolsDockTests
{
    private static TelemetryStream NewStream()
        => new(new InMemoryLogSink(), new InMemoryActivitySink(), new InMemoryMeterSink());

    private static IconButton DockButton(DevToolsPanel panel, string label)
        => panel.GetLogicalDescendants()
            .OfType<IconButton>()
            .Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == label);

    private static bool IsHighlighted(IconButton button, ThemeManager tm)
        => button.Background is SolidColorBrush b && b.Color == tm.Tokens.AccentBg;

    [AvaloniaFact]
    public void Strip_exposes_all_four_dock_affordances()
    {
        var panel = new DevToolsPanel(new ThemeManager(), NewStream());

        var labels = panel.GetLogicalDescendants()
            .OfType<IconButton>()
            .Select(b => Avalonia.Automation.AutomationProperties.GetName(b))
            .ToList();

        labels.Should().Contain("Dock to left");
        labels.Should().Contain("Dock to bottom");
        labels.Should().Contain("Dock to right");
        labels.Should().Contain("Undock into separate window");
    }

    [AvaloniaFact]
    public void Initial_dock_is_the_only_highlighted_affordance()
    {
        var tm = new ThemeManager();
        var panel = new DevToolsPanel(tm, NewStream(), dock: DevToolsDock.Bottom);

        IsHighlighted(DockButton(panel, "Dock to bottom"), tm).Should().BeTrue();
        IsHighlighted(DockButton(panel, "Dock to right"), tm).Should().BeFalse();
        IsHighlighted(DockButton(panel, "Dock to left"), tm).Should().BeFalse();
        IsHighlighted(DockButton(panel, "Undock into separate window"), tm).Should().BeFalse();
    }

    [AvaloniaFact]
    public void SetDock_moves_the_highlight()
    {
        var tm = new ThemeManager();
        var panel = new DevToolsPanel(tm, NewStream(), dock: DevToolsDock.Right);

        panel.SetDock(DevToolsDock.Floating);

        IsHighlighted(DockButton(panel, "Undock into separate window"), tm).Should().BeTrue();
        IsHighlighted(DockButton(panel, "Dock to right"), tm).Should().BeFalse();
    }

    [AvaloniaFact]
    public void DockButton_click_raises_DockRequested_with_its_position()
    {
        var panel = new DevToolsPanel(new ThemeManager(), NewStream(), dock: DevToolsDock.Right);
        DevToolsDock? requested = null;
        panel.DockRequested += (_, dock) => requested = dock;

        DockButton(panel, "Dock to bottom").RaiseClickForTest();

        requested.Should().Be(DevToolsDock.Bottom);
    }
}

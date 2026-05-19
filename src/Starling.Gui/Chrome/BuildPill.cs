using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Starling.Gui.Theme;

namespace Starling.Gui.Chrome;

/// <summary>
/// Sidebar-footer build pill. Avalonia port of Starling.Gui's Chrome/BuildPill.cs.
/// Status dot, milestone, then engine flags separated by middots.
/// </summary>
public static class BuildPill
{
    public enum BuildState { Clean, Dirty, Experimental }

    public static Border Make(
        ThemeManager tm, string milestone, IReadOnlyList<string> flags,
        BuildState state = BuildState.Clean)
    {
        var t = tm.Tokens;
        var dotColor = state switch
        {
            BuildState.Dirty => t.Warn,
            BuildState.Experimental => t.Err,
            _ => t.Accent,
        };
        var faintAccent = Color.FromArgb(128, t.Accent.R, t.Accent.G, t.Accent.B);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(ChromeKit.Dot(dotColor));
        row.Children.Add(ChromeKit.Mono(tm, milestone, tm.Metrics.FsXs, t.Accent, FontWeight.Bold));

        foreach (var flag in flags)
        {
            row.Children.Add(ChromeKit.Mono(tm, "·", tm.Metrics.FsXs, faintAccent));
            row.Children.Add(ChromeKit.Mono(tm, flag, tm.Metrics.FsXs, t.Accent));
        }

        return ChromeKit.Pill(tm, row);
    }
}

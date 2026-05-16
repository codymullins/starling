using Tessera.Gui.Theme;

namespace Tessera.Gui.Chrome;

/// <summary>
/// The build pill — port of <c>BuildPill</c> in <c>design/chrome.jsx</c> and
/// HANDOFF §3.5. Sits in the sidebar footer: a status dot, the milestone, then
/// engine flags separated by middots.
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

        var row = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        row.Add(ChromeKit.Dot(dotColor));
        row.Add(ChromeKit.Mono(tm, milestone, tm.Metrics.FsXs, t.Accent, FontAttributes.Bold));

        foreach (var flag in flags)
        {
            row.Add(ChromeKit.Mono(tm, "·", tm.Metrics.FsXs, t.Accent.WithAlpha(0.5f)));
            row.Add(ChromeKit.Mono(tm, flag, tm.Metrics.FsXs, t.Accent));
        }

        return ChromeKit.Pill(tm, row);
    }
}

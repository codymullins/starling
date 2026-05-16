namespace Tessera.Gui.Chrome;

/// <summary>
/// One sidebar tab row's data — mirrors the tab objects in <c>design/app.jsx</c>.
/// </summary>
/// <param name="Id">Stable id, used to mark the active tab.</param>
/// <param name="Host">Host string — drives the synthetic favicon.</param>
/// <param name="Title">Display title.</param>
/// <param name="Audio">Whether the tab is playing audio (accent dot).</param>
/// <param name="Loading">Whether the tab is loading (spinner replaces favicon).</param>
/// <param name="Url">Absolute URL to navigate to when the row is tapped, or null for non-actionable rows.</param>
public sealed record TabInfo(
    string Id, string Host, string Title, bool Audio = false, bool Loading = false, string? Url = null);

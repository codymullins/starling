namespace Starling.Gui.Theme;

/// <summary>
/// One timing segment — shared by the URL-bar mini load chart and the DevTools
/// Performance flame rows. Port of Starling.Gui's Theme/TimingBar.cs.
/// </summary>
/// <param name="T">Start time, in milliseconds from the start of the window.</param>
/// <param name="D">Duration, in milliseconds.</param>
/// <param name="Cat">Category — drives the bar fill colour.</param>
/// <param name="Label">Optional label, drawn inside wide-enough bars.</param>
public sealed record TimingBar(double T, double D, Category Cat, string? Label = null);

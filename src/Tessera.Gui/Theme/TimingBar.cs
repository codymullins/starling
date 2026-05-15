namespace Tessera.Gui.Theme;

/// <summary>
/// One timing segment — the shared data shape behind the URL-bar mini load
/// chart and the DevTools Performance flame rows (HANDOFF §3.4 / §5.2). Both
/// surfaces render the same shape, so the mini chart is a compressed view of
/// the Performance sample rather than a forked data set.
/// </summary>
/// <param name="T">Start time, in milliseconds from the start of the window.</param>
/// <param name="D">Duration, in milliseconds.</param>
/// <param name="Cat">Category — drives the bar fill colour.</param>
/// <param name="Label">Optional label, drawn inside wide-enough bars.</param>
public sealed record TimingBar(double T, double D, Category Cat, string? Label = null);

namespace Tessera.Gui.Theme;

/// <summary>
/// The category palette — the single enumeration every timing bar, log row, IPC
/// channel and tree node colours itself by (HANDOFF §2.4: "the palette is the
/// API"). New event types pick the closest existing category rather than
/// introducing a new hue.
/// </summary>
public enum Category
{
    Html,
    Css,
    Js,
    Layout,
    Paint,
    Gc,
    Net,
    Idle,
}

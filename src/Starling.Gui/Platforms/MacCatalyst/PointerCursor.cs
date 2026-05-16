using AppKit;

namespace Tessera.Gui.Platforms.MacCatalyst;

/// <summary>
/// Mac Catalyst pointer-cursor shim. Maps a CSS3-UI <c>cursor</c> keyword to
/// an <see cref="NSCursor"/> and pushes it onto the system cursor stack. Mac
/// idiom (UIDeviceFamily=6) routes Catalyst pointer events through AppKit's
/// cursor machinery, so calling <see cref="NSCursor.Set"/> from a pointer-move
/// handler is enough — every move re-affirms the cursor and the OS preserves
/// it while the pointer is stationary.
/// </summary>
/// <remarks>
/// <para>
/// The <c>cursor: none</c> case toggles <see cref="NSCursor.Hide"/> /
/// <see cref="NSCursor.Unhide"/> rather than calling <c>Set</c>; the hidden
/// state is reference-counted by AppKit, so we track our own contribution to
/// avoid leaking hides across pointer-exit transitions.
/// </para>
/// <para>
/// Cursors that AppKit doesn't ship a 1:1 NSCursor for (<c>progress</c>,
/// <c>wait</c>, <c>cell</c>, the diagonal resizes, <c>all-scroll</c>) fall
/// back to the nearest reasonable AppKit cursor — better than the arrow but
/// not pixel-equivalent to other browsers. Custom <c>url()</c> cursors are
/// not implemented.
/// </para>
/// </remarks>
internal static class PointerCursor
{
    private static bool _hidden;

    public static void Set(string cssCursor)
    {
        if (string.Equals(cssCursor, "none", StringComparison.OrdinalIgnoreCase))
        {
            if (!_hidden)
            {
                NSCursor.Hide();
                _hidden = true;
            }
            return;
        }

        if (_hidden)
        {
            NSCursor.Unhide();
            _hidden = false;
        }

        Map(cssCursor).Set();
    }

    /// <summary>
    /// Restores the default arrow cursor and unhides if we previously hid.
    /// Called when the pointer leaves the page surface so chrome / scroll-bar
    /// hit areas regain control of their own cursor.
    /// </summary>
    public static void Reset()
    {
        if (_hidden)
        {
            NSCursor.Unhide();
            _hidden = false;
        }
        NSCursor.ArrowCursor.Set();
    }

    private static NSCursor Map(string css) => css.ToLowerInvariant() switch
    {
        "default" or "auto" or "" => NSCursor.ArrowCursor,
        "pointer" => NSCursor.PointingHandCursor,
        "text" => NSCursor.IBeamCursor,
        "vertical-text" => NSCursor.IBeamCursorForVerticalLayout,
        "crosshair" or "cell" => NSCursor.CrosshairCursor,
        "grab" => NSCursor.OpenHandCursor,
        "grabbing" => NSCursor.ClosedHandCursor,
        "not-allowed" or "no-drop" => NSCursor.OperationNotAllowedCursor,
        "context-menu" or "help" => NSCursor.ContextualMenuCursor,
        "alias" or "copy" => NSCursor.DragCopyCursor,
        "move" or "all-scroll" => NSCursor.ClosedHandCursor,
        "col-resize" or "ew-resize" or "e-resize" or "w-resize"
            => NSCursor.ResizeLeftRightCursor,
        "row-resize" or "ns-resize" or "n-resize" or "s-resize"
            => NSCursor.ResizeUpDownCursor,
        // Diagonals have no dedicated AppKit cursor — pick the nearer axis.
        "nesw-resize" or "ne-resize" or "sw-resize" => NSCursor.ResizeUpDownCursor,
        "nwse-resize" or "nw-resize" or "se-resize" => NSCursor.ResizeUpDownCursor,
        "zoom-in" => NSCursor.ZoomInCursor,
        "zoom-out" => NSCursor.ZoomOutCursor,
        "progress" or "wait" => NSCursor.ArrowCursor,
        _ => NSCursor.ArrowCursor,
    };
}

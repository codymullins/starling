namespace Tessera.Layout;

/// <summary>
/// Module status note. wp:M1-08 has landed: <see cref="LayoutEngine"/> performs
/// block + inline formatting with margin collapse and word-wrap line breaking.
/// Floats, positioning (absolute/relative/fixed), flexbox, grid, and tables
/// are deferred — see browser-plan/07_LAYOUT.md for the M5+ roadmap.
/// </summary>
public static class PlaceholderNote
{
    public const string Message =
        "Tessera.Layout — Block + inline (wp:M1-08) ready. " +
        "Floats / positioning / flex / grid pending. See browser-plan/07_LAYOUT.md.";
}

namespace Starling.Css.FontLoading;

/// <summary>
/// The load status of a <see cref="FontFace"/> object.
/// CSS Font Loading 3 §3.1
/// (<see href="https://www.w3.org/TR/css-font-loading-3/#dom-fontface-status"/>).
/// </summary>
public enum FontFaceLoadStatus
{
    /// <summary>
    /// The font data has not been requested.
    /// CSS Font Loading 3 §3.1 — initial state.
    /// </summary>
    Unloaded,

    /// <summary>
    /// The font data is being fetched (or the load has been triggered but not
    /// yet completed). In this model slice no real network I/O occurs; this
    /// state is transient and only observable if callers inspect status between
    /// a call to <see cref="FontFace.Load"/> and its synchronous completion.
    /// CSS Font Loading 3 §3.1.
    /// </summary>
    Loading,

    /// <summary>
    /// The font data has been loaded successfully.
    /// CSS Font Loading 3 §3.1.
    /// </summary>
    Loaded,

    /// <summary>
    /// The font failed to load.
    /// CSS Font Loading 3 §3.1.
    /// </summary>
    Error,
}

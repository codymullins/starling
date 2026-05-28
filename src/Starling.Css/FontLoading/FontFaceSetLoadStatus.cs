namespace Starling.Css.FontLoading;

/// <summary>
/// The load status of a <see cref="FontFaceSet"/>.
/// CSS Font Loading 3 §4.2
/// (<see href="https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-status"/>).
/// </summary>
public enum FontFaceSetLoadStatus
{
    /// <summary>
    /// At least one member <see cref="FontFace"/> is in the
    /// <see cref="FontFaceLoadStatus.Loading"/> state.
    /// CSS Font Loading 3 §4.2.
    /// </summary>
    Loading,

    /// <summary>
    /// All member <see cref="FontFace"/> objects are in either the
    /// <see cref="FontFaceLoadStatus.Loaded"/> or
    /// <see cref="FontFaceLoadStatus.Error"/> state, or the set is empty.
    /// CSS Font Loading 3 §4.2.
    /// </summary>
    Loaded,
}

using Tessera.Skia.Interop;

namespace Tessera.Skia;

/// <summary>
/// Thrown when a <c>tessera_skia</c> native shim call returns a non-OK
/// <c>TsStatus</c>. Carries the failing operation name and the raw status so a
/// single catch site can triage every interop failure.
/// </summary>
#pragma warning disable RCS1194 // Implement exception constructors — this is an
// internal interop-only exception; the two purpose-built constructors below are
// the only ways it is ever raised. The generic (string, Exception) / ()
// constructors would be dead, misleading API surface.
internal sealed class SkiaInteropException : Exception
{
    internal SkiaInteropException(string operation, TsStatus status)
        : base($"tessera_skia: {operation} failed with {status}.")
    {
        Operation = operation;
        Status = status;
    }

    internal SkiaInteropException(string message)
        : base(message)
    {
        Operation = string.Empty;
        Status = TsStatus.UnknownError;
    }

    /// <summary>The native ABI function that failed, e.g. <c>ts_surface_create</c>.</summary>
    public string Operation { get; }

    /// <summary>The raw status code the shim returned.</summary>
    public TsStatus Status { get; }

    /// <summary>Throws a <see cref="SkiaInteropException"/> when <paramref name="status"/> is not OK.</summary>
    internal static void ThrowIfNotOk(TsStatus status, string operation)
    {
        if (status != TsStatus.Ok)
            throw new SkiaInteropException(operation, status);
    }
}
#pragma warning restore RCS1194

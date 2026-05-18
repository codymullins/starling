using Tessera.Common.Diagnostics;
using Tessera.Layout.Text;

namespace Tessera.Paint.Backend;

internal enum PaintBackendKind
{
    Skia,
    ImageSharp,
}

/// <summary>
/// Reads <c>TESSERA_PAINT_BACKEND</c> once and dispenses the matching paint
/// backend and text measurer so layout and raster never disagree within a
/// single render. Mirrors Tessera.Codecs.NativeImageDecoder.SelectBackend.
/// </summary>
internal static class PaintBackendSelector
{
    private const string EnvVar = "TESSERA_PAINT_BACKEND";

    private static readonly Lazy<PaintBackendKind> _selected = new(ReadEnv);

    internal static PaintBackendKind Selected => _selected.Value;

    private static PaintBackendKind ReadEnv()
    {
        var raw = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(raw))
            return PaintBackendKind.Skia;

        return raw.Trim().ToLowerInvariant() switch
        {
            "skia" => PaintBackendKind.Skia,
            "imagesharp" => PaintBackendKind.ImageSharp,
            _ => throw new InvalidOperationException(
                $"{EnvVar}='{raw}' is not a recognised paint backend. Allowed values: 'skia', 'imagesharp'."),
        };
    }

    internal static IPaintBackend Create(FontResolver fonts, FontFaceRegistry? webFonts, IDiagnostics? diag = null)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        return Selected switch
        {
            PaintBackendKind.Skia => new SkiaGraphiteBackend(fonts, webFonts, diag),
            PaintBackendKind.ImageSharp => CreateImageSharpBackend(fonts, webFonts, diag),
            _ => throw new InvalidOperationException($"Unhandled paint backend: {Selected}."),
        };
    }

    internal static ITextMeasurer CreateMeasurer(FontResolver fonts, FontFaceRegistry? webFonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        return Selected switch
        {
            PaintBackendKind.Skia => new SkiaTextMeasurer(fonts, webFonts),
            PaintBackendKind.ImageSharp => CreateImageSharpMeasurer(fonts, webFonts),
            _ => throw new InvalidOperationException($"Unhandled paint backend: {Selected}."),
        };
    }

    private static IPaintBackend CreateImageSharpBackend(FontResolver fonts, FontFaceRegistry? webFonts, IDiagnostics? diag)
    {
#if TESSERA_IMAGESHARP_DRAWING
        return new ImageSharpBackend(fonts, webFonts, diag);
#else
        throw new InvalidOperationException(
            $"{EnvVar}=imagesharp requires the assembly to be built with the MSBuild property " +
            "EnableImageSharpDrawing3=true; this binary was compiled without it.");
#endif
    }

    private static ITextMeasurer CreateImageSharpMeasurer(FontResolver fonts, FontFaceRegistry? webFonts)
    {
#if TESSERA_IMAGESHARP_DRAWING
        return new ImageSharpTextMeasurer(fonts, webFonts);
#else
        throw new InvalidOperationException(
            $"{EnvVar}=imagesharp requires the assembly to be built with the MSBuild property " +
            "EnableImageSharpDrawing3=true; this binary was compiled without it.");
#endif
    }
}

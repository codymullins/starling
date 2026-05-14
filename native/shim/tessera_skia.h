/* tessera_skia.h — C ABI surface for the Tessera Skia Graphite shim.
 *
 * SCAFFOLD ONLY (WP M3-06a / Phase 0).
 * ------------------------------------
 * This header declares the complete `extern "C"` surface that the .NET interop
 * project (`src/Tessera.Skia`, WP M3-06h) will P/Invoke into. The bodies are
 * stubbed in tessera_skia.cpp and currently return TS_NOT_IMPLEMENTED / empty.
 * The REAL implementation — wiring Dawn + Skia Graphite behind these signatures
 * — is WP M3-06g and depends on a built `libskia` (WP M3-06b) being available.
 *
 * Design rules baked into this ABI (from the master plan, Phase 2):
 *   - It is a SMALL, custom surface — exactly what the display list needs, no
 *     more. Do NOT grow it into a general Skia binding.
 *   - WebGPU / Dawn / Skia C++ types NEVER cross this boundary. Everything is an
 *     opaque `void*`-style handle (TsContext*, TsSurface*, ...). This is the key
 *     insulation against WebGPU C-API churn.
 *   - All handles are created/destroyed in pairs; .NET wraps each in a
 *     SafeHandle for deterministic cleanup.
 *   - Pixel data crossing the boundary is straight RGBA8888, tightly packed.
 *   - PNG ENCODE stays in C# initially (read_pixels -> raw RGBA -> existing
 *     managed encoder), so this ABI has no encode entry point.
 */

#ifndef TESSERA_SKIA_H
#define TESSERA_SKIA_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#  define TS_API __declspec(dllexport)
#  define TS_CALL __cdecl
#else
#  define TS_API __attribute__((visibility("default")))
#  define TS_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* --------------------------------------------------------------------------
 * Status codes — every fallible call returns one of these.
 * ------------------------------------------------------------------------ */
typedef enum TsStatus {
    TS_OK = 0,
    TS_NOT_IMPLEMENTED = 1,   /* scaffold default — replaced by WP M3-06g */
    TS_INVALID_ARGUMENT = 2,
    TS_NULL_HANDLE = 3,
    TS_DEVICE_LOST = 4,       /* Dawn device/adapter failure */
    TS_BACKEND_UNAVAILABLE = 5,
    TS_ALLOCATION_FAILED = 6,
    TS_READBACK_FAILED = 7,
    TS_SHAPING_FAILED = 8,
    TS_UNKNOWN_ERROR = 100
} TsStatus;

/* Backend selection hint. Dawn auto-selects per platform; this only forces a
 * specific backend for debugging. AUTO is the production path. */
typedef enum TsBackendHint {
    TS_BACKEND_AUTO = 0,
    TS_BACKEND_METAL = 1,    /* macOS */
    TS_BACKEND_D3D12 = 2,    /* Windows */
    TS_BACKEND_VULKAN = 3,   /* Linux */
    TS_BACKEND_GL_ANGLE = 4  /* fallback only */
} TsBackendHint;

/* --------------------------------------------------------------------------
 * Opaque handles. The concrete types live entirely inside the shim .cpp;
 * .NET only ever sees the pointer. WebGPU types stay behind TsContext.
 * ------------------------------------------------------------------------ */
typedef struct TsContext  TsContext;   /* Dawn instance/adapter/device + Graphite Context + Recorder */
typedef struct TsSurface  TsSurface;   /* a Graphite-backed render target */
typedef struct TsCanvas   TsCanvas;    /* draw target view onto a TsSurface (not owned) */
typedef struct TsTypeface TsTypeface;  /* an SkTypeface */
typedef struct TsFont     TsFont;      /* a sized SkFont built from a TsTypeface */

/* --------------------------------------------------------------------------
 * Plain-old-data structs passed by value / pointer across the boundary.
 * ------------------------------------------------------------------------ */

/* sRGBA, 8 bits per channel, components 0-255. */
typedef struct TsColor {
    uint8_t r, g, b, a;
} TsColor;

typedef struct TsRect {
    float x, y, width, height;
} TsRect;

/* One shaped glyph in a run: a glyph id plus its pen position. Produced by
 * ts_shape_text, consumed by ts_canvas_draw_text. */
typedef struct TsGlyph {
    uint32_t glyph_id;
    float    x;        /* pen x, in surface pixels */
    float    y;        /* pen y (baseline), in surface pixels */
} TsGlyph;

/* Font metrics for a sized TsFont, in pixels. */
typedef struct TsFontMetrics {
    float ascent;       /* distance above baseline (positive) */
    float descent;      /* distance below baseline (positive) */
    float leading;      /* recommended extra line gap */
    float cap_height;
    float x_height;
    float underline_position;
    float underline_thickness;
} TsFontMetrics;

/* --------------------------------------------------------------------------
 * Context / device lifecycle.
 *   Inside ts_context_create (WP M3-06g): create Dawn Instance -> Adapter ->
 *   Device, hand wgpu::Device to skgpu::graphite::ContextFactory::MakeDawn,
 *   store Context + Recorder. WebGPU handles never escape TsContext.
 * ------------------------------------------------------------------------ */
TS_API TsStatus  TS_CALL ts_context_create(TsBackendHint hint, TsContext** out_context);
TS_API void      TS_CALL ts_context_destroy(TsContext* context);
/* Human-readable backend actually selected, e.g. "Dawn/Metal". Buffer is
 * caller-owned; returns the number of bytes written (excluding NUL). */
TS_API size_t    TS_CALL ts_context_backend_name(TsContext* context, char* buffer, size_t buffer_len);

/* --------------------------------------------------------------------------
 * Surface + canvas.
 *   ts_surface_create makes an offscreen Graphite render target (headless /
 *   golden path). GUI layer-backed surfaces (ts_surface_create_from_metal_layer
 *   etc.) are added in Phase 7 and intentionally NOT scaffolded here.
 * ------------------------------------------------------------------------ */
TS_API TsStatus  TS_CALL ts_surface_create(TsContext* context, int32_t width, int32_t height, TsSurface** out_surface);
TS_API void      TS_CALL ts_surface_destroy(TsSurface* surface);
/* Borrowed canvas view onto the surface — do NOT destroy; valid until the
 * surface is destroyed. */
TS_API TsStatus  TS_CALL ts_surface_get_canvas(TsSurface* surface, TsCanvas** out_canvas);
TS_API TsStatus  TS_CALL ts_canvas_clear(TsCanvas* canvas, TsColor color);

/* --------------------------------------------------------------------------
 * The 4 DisplayItem ops. These mirror Tessera.Paint's DisplayItem kinds 1:1 —
 * the display list is the seam, so this set is fixed.
 * ------------------------------------------------------------------------ */
TS_API TsStatus  TS_CALL ts_canvas_fill_rect(TsCanvas* canvas, TsRect rect, TsColor color);
TS_API TsStatus  TS_CALL ts_canvas_stroke_rect(TsCanvas* canvas, TsRect rect, TsColor color, float stroke_width);
/* Draws a pre-shaped glyph run (output of ts_shape_text) with the given font. */
TS_API TsStatus  TS_CALL ts_canvas_draw_text(TsCanvas* canvas, TsFont* font,
                                             const TsGlyph* glyphs, size_t glyph_count,
                                             TsColor color);
/* Draws an image from tightly-packed RGBA8888 pixels, scaled into dst_rect.
 * `pixels` length must be width*height*4. */
TS_API TsStatus  TS_CALL ts_canvas_draw_image(TsCanvas* canvas,
                                              const uint8_t* pixels, int32_t width, int32_t height,
                                              TsRect dst_rect);

/* --------------------------------------------------------------------------
 * Fonts + text shaping (HarfBuzz, via Skia).
 *   ts_typeface_from_data: from embedded TTF bytes (e.g. OpenSans-Regular.ttf).
 *   ts_typeface_from_name: from the system SkFontMgr.
 * ------------------------------------------------------------------------ */
TS_API TsStatus  TS_CALL ts_typeface_from_data(const uint8_t* ttf_bytes, size_t ttf_len, TsTypeface** out_typeface);
TS_API TsStatus  TS_CALL ts_typeface_from_name(const char* family_name, TsTypeface** out_typeface);
TS_API void      TS_CALL ts_typeface_destroy(TsTypeface* typeface);

TS_API TsStatus  TS_CALL ts_font_create(TsTypeface* typeface, float size_px, TsFont** out_font);
TS_API void      TS_CALL ts_font_destroy(TsFont* font);
TS_API TsStatus  TS_CALL ts_font_metrics(TsFont* font, TsFontMetrics* out_metrics);

/* Shapes UTF-8 text into a positioned glyph run.
 *   - Caller passes a `glyphs` buffer of capacity `glyph_capacity`.
 *   - On TS_OK, `*out_glyph_count` is the number of glyphs written.
 *   - If the buffer is too small, returns TS_INVALID_ARGUMENT and sets
 *     `*out_glyph_count` to the required capacity (caller re-allocates + retries).
 */
TS_API TsStatus  TS_CALL ts_shape_text(TsFont* font,
                                       const char* utf8_text, size_t utf8_len,
                                       TsGlyph* glyphs, size_t glyph_capacity,
                                       size_t* out_glyph_count);

/* --------------------------------------------------------------------------
 * Flush + readback (golden / headless path).
 *   ts_flush_and_submit: snap the Graphite Recorder, submit to the device,
 *   wait for GPU completion.
 *   ts_read_pixels: copy the surface contents into a caller-owned RGBA8888
 *   buffer (length must be width*height*4). PNG encode stays in C#.
 * ------------------------------------------------------------------------ */
TS_API TsStatus  TS_CALL ts_flush_and_submit(TsContext* context, TsSurface* surface);
TS_API TsStatus  TS_CALL ts_read_pixels(TsSurface* surface, uint8_t* out_pixels, size_t out_pixels_len);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* TESSERA_SKIA_H */

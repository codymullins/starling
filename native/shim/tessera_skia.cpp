/* tessera_skia.cpp — stub bodies for the Tessera Skia Graphite C ABI shim.
 *
 * SCAFFOLD ONLY (WP M3-06a / Phase 0).
 * ------------------------------------
 * Every function here is a placeholder: it validates nothing beyond null
 * handles and returns TS_NOT_IMPLEMENTED (or an empty/zeroed result). The real
 * implementation is WP M3-06g (Phase 2), which:
 *   - #includes the Skia + Dawn headers staged by native/build-skia.* into
 *     runtimes/<rid>/native/include/,
 *   - defines the opaque structs (TsContext wrapping Dawn Instance/Adapter/
 *     Device + skgpu::graphite::Context + Recorder, etc.),
 *   - statically links against libskia + Dawn (see CMakeLists.txt),
 *   - implements each entry point against Skia Graphite.
 *
 * Keeping this file compilable now means src/Tessera.Skia (WP M3-06h) can be
 * scaffolded and its P/Invoke signatures validated against a real (if inert)
 * shared library before the GPU work lands.
 */

#include "tessera_skia.h"

/* --------------------------------------------------------------------------
 * Opaque handle definitions.
 * Scaffold: empty structs so the C ABI is real (non-null pointers can be
 * minted) without pulling in Skia/Dawn yet. WP M3-06g replaces these with the
 * actual Skia/Dawn-backed definitions.
 * ------------------------------------------------------------------------ */
struct TsContext  { int _scaffold; };
struct TsSurface  { int _scaffold; };
struct TsCanvas   { int _scaffold; };
struct TsTypeface { int _scaffold; };
struct TsFont     { int _scaffold; };

/* --------------------------------------------------------------------------
 * Context / device lifecycle.
 * ------------------------------------------------------------------------ */
TS_API TsStatus TS_CALL ts_context_create(TsBackendHint /*hint*/, TsContext** out_context) {
    if (out_context == nullptr) {
        return TS_INVALID_ARGUMENT;
    }
    *out_context = nullptr;
    /* WP M3-06g: create Dawn Instance -> Adapter -> Device, then
     * skgpu::graphite::ContextFactory::MakeDawn(...); store Context + Recorder. */
    return TS_NOT_IMPLEMENTED;
}

TS_API void TS_CALL ts_context_destroy(TsContext* /*context*/) {
    /* WP M3-06g: tear down Recorder, Context, Dawn device. */
}

TS_API size_t TS_CALL ts_context_backend_name(TsContext* context, char* buffer, size_t buffer_len) {
    if (context == nullptr || buffer == nullptr || buffer_len == 0) {
        return 0;
    }
    buffer[0] = '\0';
    return 0;
}

/* --------------------------------------------------------------------------
 * Surface + canvas.
 * ------------------------------------------------------------------------ */
TS_API TsStatus TS_CALL ts_surface_create(TsContext* context, int32_t width, int32_t height,
                                          TsSurface** out_surface) {
    if (context == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (out_surface == nullptr || width <= 0 || height <= 0) {
        return TS_INVALID_ARGUMENT;
    }
    *out_surface = nullptr;
    /* WP M3-06g: SkSurfaces::RenderTarget(recorder, imageInfo, ...). */
    return TS_NOT_IMPLEMENTED;
}

TS_API void TS_CALL ts_surface_destroy(TsSurface* /*surface*/) {
    /* WP M3-06g: release the Graphite surface. */
}

TS_API TsStatus TS_CALL ts_surface_get_canvas(TsSurface* surface, TsCanvas** out_canvas) {
    if (surface == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (out_canvas == nullptr) {
        return TS_INVALID_ARGUMENT;
    }
    *out_canvas = nullptr;
    /* WP M3-06g: return surface->getCanvas() (borrowed, not owned). */
    return TS_NOT_IMPLEMENTED;
}

TS_API TsStatus TS_CALL ts_canvas_clear(TsCanvas* canvas, TsColor /*color*/) {
    if (canvas == nullptr) {
        return TS_NULL_HANDLE;
    }
    /* WP M3-06g: canvas->clear(SkColorSetARGB(...)). */
    return TS_NOT_IMPLEMENTED;
}

/* --------------------------------------------------------------------------
 * The 4 DisplayItem ops.
 * ------------------------------------------------------------------------ */
TS_API TsStatus TS_CALL ts_canvas_fill_rect(TsCanvas* canvas, TsRect /*rect*/, TsColor /*color*/) {
    if (canvas == nullptr) {
        return TS_NULL_HANDLE;
    }
    /* WP M3-06g: SkPaint(style=Fill); canvas->drawRect(...). */
    return TS_NOT_IMPLEMENTED;
}

TS_API TsStatus TS_CALL ts_canvas_stroke_rect(TsCanvas* canvas, TsRect /*rect*/, TsColor /*color*/,
                                              float /*stroke_width*/) {
    if (canvas == nullptr) {
        return TS_NULL_HANDLE;
    }
    /* WP M3-06g: SkPaint(style=Stroke, strokeWidth); canvas->drawRect(...). */
    return TS_NOT_IMPLEMENTED;
}

TS_API TsStatus TS_CALL ts_canvas_draw_text(TsCanvas* canvas, TsFont* font,
                                            const TsGlyph* glyphs, size_t glyph_count,
                                            TsColor /*color*/) {
    if (canvas == nullptr || font == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (glyphs == nullptr && glyph_count != 0) {
        return TS_INVALID_ARGUMENT;
    }
    /* WP M3-06g: build an SkTextBlob from the positioned glyph run;
     * canvas->drawTextBlob(...). */
    return TS_NOT_IMPLEMENTED;
}

TS_API TsStatus TS_CALL ts_canvas_draw_image(TsCanvas* canvas,
                                             const uint8_t* pixels, int32_t width, int32_t height,
                                             TsRect /*dst_rect*/) {
    if (canvas == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (pixels == nullptr || width <= 0 || height <= 0) {
        return TS_INVALID_ARGUMENT;
    }
    /* WP M3-06g: wrap pixels in an SkImage (RGBA8888); canvas->drawImageRect(...). */
    return TS_NOT_IMPLEMENTED;
}

/* --------------------------------------------------------------------------
 * Fonts + text shaping.
 * ------------------------------------------------------------------------ */
TS_API TsStatus TS_CALL ts_typeface_from_data(const uint8_t* ttf_bytes, size_t ttf_len,
                                              TsTypeface** out_typeface) {
    if (out_typeface == nullptr) {
        return TS_INVALID_ARGUMENT;
    }
    *out_typeface = nullptr;
    if (ttf_bytes == nullptr || ttf_len == 0) {
        return TS_INVALID_ARGUMENT;
    }
    /* WP M3-06g: SkFontMgr::makeFromData(SkData::MakeWithCopy(...)). */
    return TS_NOT_IMPLEMENTED;
}

TS_API TsStatus TS_CALL ts_typeface_from_name(const char* family_name, TsTypeface** out_typeface) {
    if (out_typeface == nullptr) {
        return TS_INVALID_ARGUMENT;
    }
    *out_typeface = nullptr;
    if (family_name == nullptr) {
        return TS_INVALID_ARGUMENT;
    }
    /* WP M3-06g: SkFontMgr::matchFamilyStyle(family_name, SkFontStyle()). */
    return TS_NOT_IMPLEMENTED;
}

TS_API void TS_CALL ts_typeface_destroy(TsTypeface* /*typeface*/) {
    /* WP M3-06g: release the sk_sp<SkTypeface>. */
}

TS_API TsStatus TS_CALL ts_font_create(TsTypeface* typeface, float size_px, TsFont** out_font) {
    if (typeface == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (out_font == nullptr || size_px <= 0.0f) {
        return TS_INVALID_ARGUMENT;
    }
    *out_font = nullptr;
    /* WP M3-06g: SkFont(typeface, size_px). */
    return TS_NOT_IMPLEMENTED;
}

TS_API void TS_CALL ts_font_destroy(TsFont* /*font*/) {
    /* WP M3-06g: release the SkFont. */
}

TS_API TsStatus TS_CALL ts_font_metrics(TsFont* font, TsFontMetrics* out_metrics) {
    if (font == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (out_metrics == nullptr) {
        return TS_INVALID_ARGUMENT;
    }
    *out_metrics = TsFontMetrics{};  /* zeroed */
    /* WP M3-06g: SkFont::getMetrics(&skMetrics); translate to TsFontMetrics. */
    return TS_NOT_IMPLEMENTED;
}

TS_API TsStatus TS_CALL ts_shape_text(TsFont* font,
                                      const char* utf8_text, size_t utf8_len,
                                      TsGlyph* glyphs, size_t glyph_capacity,
                                      size_t* out_glyph_count) {
    if (font == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (out_glyph_count == nullptr || (utf8_text == nullptr && utf8_len != 0)) {
        return TS_INVALID_ARGUMENT;
    }
    (void)glyphs;
    (void)glyph_capacity;
    *out_glyph_count = 0;
    /* WP M3-06g: run the Skia HarfBuzz shaper; emit positioned TsGlyph entries.
     * If glyph_capacity is too small, set *out_glyph_count to the required
     * size and return TS_INVALID_ARGUMENT so the caller can retry. */
    return TS_NOT_IMPLEMENTED;
}

/* --------------------------------------------------------------------------
 * Flush + readback.
 * ------------------------------------------------------------------------ */
TS_API TsStatus TS_CALL ts_flush_and_submit(TsContext* context, TsSurface* surface) {
    if (context == nullptr || surface == nullptr) {
        return TS_NULL_HANDLE;
    }
    /* WP M3-06g: recorder->snap() -> context->insertRecording(...) ->
     * context->submit(skgpu::graphite::SyncToCpu::kYes). */
    return TS_NOT_IMPLEMENTED;
}

TS_API TsStatus TS_CALL ts_read_pixels(TsSurface* surface, uint8_t* out_pixels, size_t out_pixels_len) {
    if (surface == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (out_pixels == nullptr || out_pixels_len == 0) {
        return TS_INVALID_ARGUMENT;
    }
    /* WP M3-06g: surface->readPixels(SkPixmap(RGBA8888, out_pixels, ...)). */
    return TS_NOT_IMPLEMENTED;
}

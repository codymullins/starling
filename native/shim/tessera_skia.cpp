/* tessera_skia.cpp — Skia Graphite + Dawn implementation of the Tessera C ABI.
 *
 * WP M3-06g (Phase 2).
 * --------------------
 * Real implementation behind the `extern "C"` surface declared in
 * tessera_skia.h. Wires Dawn (Metal backend on macOS) into Skia Graphite:
 *
 *   - ts_context_create: dawnProcSetProcs -> dawn::native::Instance ->
 *     EnumerateAdapters (Metal) -> Adapter::CreateDevice -> wgpu::Device +
 *     Queue -> skgpu::graphite::ContextFactory::MakeDawn -> Context +
 *     Recorder. Dawn / wgpu types never escape TsContext.
 *   - ts_surface_create: SkSurfaces::RenderTarget on the Graphite Recorder.
 *   - the 4 DisplayItem ops: SkCanvas drawRect / drawGlyphs / drawImageRect.
 *   - fonts: SkTypeface from TTF data or the CoreText SkFontMgr; SkFont.
 *   - shaping: SkFont::textToGlyphs + getXPos (see note in the WP handoff —
 *     the skshaper module was NOT staged by the native build, so this is the
 *     core-Skia non-complex shaper; HarfBuzz complex shaping is a follow-up).
 *   - flush + readback: Recorder::snap -> Context::insertRecording ->
 *     Context::submit(SyncToCpu::kYes) -> SkSurface::readPixels.
 *
 * Everything Dawn/Graphite-specific is confined to this translation unit; the
 * header exposes only opaque handles + POD structs.
 */

#include "tessera_skia.h"

#include <cstring>
#include <memory>
#include <vector>

// --- Dawn native (Metal backend selection + device creation) ---------------
#include "dawn/dawn_proc.h"
#include "dawn/native/DawnNative.h"
#include "webgpu/webgpu_cpp.h"

// --- Skia core -------------------------------------------------------------
#include "include/core/SkCanvas.h"
#include "include/core/SkColor.h"
#include "include/core/SkData.h"
#include "include/core/SkFont.h"
#include "include/core/SkFontMetrics.h"
#include "include/core/SkFontMgr.h"
#include "include/core/SkFontStyle.h"
#include "include/core/SkFontTypes.h"
#include "include/core/SkImage.h"
#include "include/core/SkImageInfo.h"
#include "include/core/SkPaint.h"
#include "include/core/SkPixmap.h"
#include "include/core/SkRect.h"
#include "include/core/SkRefCnt.h"
#include "include/core/SkSamplingOptions.h"
#include "include/core/SkSurface.h"
#include "include/core/SkTypeface.h"

// --- Skia Graphite + Dawn backend ------------------------------------------
#include "include/gpu/graphite/Context.h"
#include "include/gpu/graphite/ContextOptions.h"
#include "include/gpu/graphite/GraphiteTypes.h"
#include "include/gpu/graphite/Image.h"
#include "include/gpu/graphite/Recorder.h"
#include "include/gpu/graphite/Recording.h"
#include "include/gpu/graphite/Surface.h"
#include "include/gpu/graphite/dawn/DawnBackendContext.h"

// --- CoreText font manager (macOS) -----------------------------------------
#include "include/ports/SkFontMgr_mac_ct.h"

/* --------------------------------------------------------------------------
 * Opaque handle definitions — concrete types live entirely in this TU.
 * ------------------------------------------------------------------------ */
struct TsContext {
    // Dawn objects. Order matters for destruction: Graphite Context/Recorder
    // hold refs on the wgpu::Device, so they are torn down first (members are
    // destroyed bottom-to-top).
    std::unique_ptr<dawn::native::Instance> dawnInstance;
    wgpu::Device device;
    wgpu::Queue  queue;

    std::unique_ptr<skgpu::graphite::Context>  graphite;
    std::unique_ptr<skgpu::graphite::Recorder> recorder;

    const char* backendName = "Dawn/Metal";
};

struct TsSurface {
    sk_sp<SkSurface> surface;
    int32_t width = 0;
    int32_t height = 0;
    // Borrowed from the owning TsContext — needed to upload raster pixels as
    // Graphite texture-backed images (drawImageRect on a Graphite canvas only
    // draws texture-backed SkImages, not raster ones).
    skgpu::graphite::Recorder* recorder = nullptr;
};

struct TsCanvas {
    SkCanvas* canvas = nullptr;  // borrowed from the SkSurface, not owned
    // Borrowed Graphite recorder, threaded through from the surface so
    // ts_canvas_draw_image can upload pixels as a texture.
    skgpu::graphite::Recorder* recorder = nullptr;
};

struct TsTypeface {
    sk_sp<SkTypeface> typeface;
};

struct TsFont {
    SkFont font;
};

/* --------------------------------------------------------------------------
 * Local helpers.
 * ------------------------------------------------------------------------ */
namespace {

SkColor ToSkColor(TsColor c) {
    return SkColorSetARGB(c.a, c.r, c.g, c.b);
}

SkRect ToSkRect(TsRect r) {
    return SkRect::MakeXYWH(r.x, r.y, r.width, r.height);
}

// RGBA8888, premultiplied — the renderable format for GPU surfaces. Graphite
// render targets require a premul (or opaque) alpha type.
SkImageInfo RgbaSurfaceInfo(int w, int h) {
    return SkImageInfo::Make(w, h, kRGBA_8888_SkColorType, kPremul_SkAlphaType);
}

// RGBA8888, unpremultiplied — the wire format crossing the C boundary for
// pixel uploads (draw_image) and readback (read_pixels). Skia converts
// between this and the premul surface format on the fly.
SkImageInfo RgbaWireInfo(int w, int h) {
    return SkImageInfo::Make(w, h, kRGBA_8888_SkColorType, kUnpremul_SkAlphaType);
}

}  // namespace

/* --------------------------------------------------------------------------
 * Context / device lifecycle.
 * ------------------------------------------------------------------------ */
TS_API TsStatus TS_CALL ts_context_create(TsBackendHint hint, TsContext** out_context) {
    if (out_context == nullptr) {
        return TS_INVALID_ARGUMENT;
    }
    *out_context = nullptr;

    auto ctx = std::make_unique<TsContext>();

    // Install Dawn's proc table so the wgpu:: C++ wrappers resolve to the
    // statically-linked dawn_native implementation.
    dawnProcSetProcs(&dawn::native::GetProcs());

    // Create the Dawn instance. TimedWaitAny lets us block on GPU completion.
    wgpu::InstanceDescriptor instanceDesc = {};
    static const wgpu::InstanceFeatureName kInstanceFeatures[] = {
        wgpu::InstanceFeatureName::TimedWaitAny,
    };
    instanceDesc.requiredFeatureCount = 1;
    instanceDesc.requiredFeatures = kInstanceFeatures;
    ctx->dawnInstance = std::make_unique<dawn::native::Instance>(
        reinterpret_cast<const WGPUInstanceDescriptor*>(&instanceDesc));

    // Pick a backend. macOS production path is Metal; AUTO also resolves to
    // Metal here since that is the only native backend the macOS build ships.
    wgpu::BackendType wantBackend = wgpu::BackendType::Metal;
    switch (hint) {
        case TS_BACKEND_D3D12:  wantBackend = wgpu::BackendType::D3D12;  break;
        case TS_BACKEND_VULKAN: wantBackend = wgpu::BackendType::Vulkan; break;
        case TS_BACKEND_GL_ANGLE: wantBackend = wgpu::BackendType::OpenGL; break;
        case TS_BACKEND_METAL:
        case TS_BACKEND_AUTO:
        default:                wantBackend = wgpu::BackendType::Metal;  break;
    }

    wgpu::RequestAdapterOptions adapterOptions = {};
    adapterOptions.backendType = wantBackend;
    adapterOptions.powerPreference = wgpu::PowerPreference::HighPerformance;

    std::vector<dawn::native::Adapter> adapters =
        ctx->dawnInstance->EnumerateAdapters(&adapterOptions);
    if (adapters.empty()) {
        // Fall back to any adapter the instance can see.
        adapters = ctx->dawnInstance->EnumerateAdapters();
    }
    if (adapters.empty()) {
        return TS_BACKEND_UNAVAILABLE;
    }

    // Create the device from the first matching adapter.
    WGPUDevice rawDevice = adapters.front().CreateDevice();
    if (rawDevice == nullptr) {
        return TS_DEVICE_LOST;
    }
    ctx->device = wgpu::Device::Acquire(rawDevice);
    ctx->queue = ctx->device.GetQueue();

    // Hand the Dawn device to Skia Graphite.
    skgpu::graphite::DawnBackendContext backendContext;
    backendContext.fInstance = wgpu::Instance(ctx->dawnInstance->Get());
    backendContext.fDevice = ctx->device;
    backendContext.fQueue = ctx->queue;

    skgpu::graphite::ContextOptions contextOptions;
    ctx->graphite =
        skgpu::graphite::ContextFactory::MakeDawn(backendContext, contextOptions);
    if (!ctx->graphite) {
        return TS_DEVICE_LOST;
    }

    ctx->recorder = ctx->graphite->makeRecorder();
    if (!ctx->recorder) {
        return TS_ALLOCATION_FAILED;
    }

    *out_context = ctx.release();
    return TS_OK;
}

TS_API void TS_CALL ts_context_destroy(TsContext* context) {
    if (context == nullptr) {
        return;
    }
    // Members destruct in reverse declaration order: recorder, graphite,
    // queue, device, dawnInstance — Graphite teardown before Dawn teardown.
    delete context;
}

TS_API size_t TS_CALL ts_context_backend_name(TsContext* context, char* buffer, size_t buffer_len) {
    if (context == nullptr || buffer == nullptr || buffer_len == 0) {
        return 0;
    }
    const char* name = context->backendName ? context->backendName : "";
    size_t len = std::strlen(name);
    if (len >= buffer_len) {
        len = buffer_len - 1;
    }
    std::memcpy(buffer, name, len);
    buffer[len] = '\0';
    return len;
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
    if (!context->recorder) {
        return TS_NULL_HANDLE;
    }

    SkImageInfo info = RgbaSurfaceInfo(width, height);
    sk_sp<SkSurface> surface = SkSurfaces::RenderTarget(context->recorder.get(), info);
    if (!surface) {
        return TS_ALLOCATION_FAILED;
    }

    auto handle = std::make_unique<TsSurface>();
    handle->surface = std::move(surface);
    handle->width = width;
    handle->height = height;
    handle->recorder = context->recorder.get();
    *out_surface = handle.release();
    return TS_OK;
}

TS_API void TS_CALL ts_surface_destroy(TsSurface* surface) {
    delete surface;
}

TS_API TsStatus TS_CALL ts_surface_get_canvas(TsSurface* surface, TsCanvas** out_canvas) {
    if (surface == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (out_canvas == nullptr) {
        return TS_INVALID_ARGUMENT;
    }
    *out_canvas = nullptr;
    if (!surface->surface) {
        return TS_NULL_HANDLE;
    }
    SkCanvas* canvas = surface->surface->getCanvas();
    if (canvas == nullptr) {
        return TS_UNKNOWN_ERROR;
    }
    // The TsCanvas is a thin owned wrapper around a borrowed SkCanvas* — it
    // does not own the SkCanvas. .NET treats it as a borrowed view.
    auto handle = std::make_unique<TsCanvas>();
    handle->canvas = canvas;
    handle->recorder = surface->recorder;
    *out_canvas = handle.release();
    return TS_OK;
}

TS_API TsStatus TS_CALL ts_canvas_clear(TsCanvas* canvas, TsColor color) {
    if (canvas == nullptr || canvas->canvas == nullptr) {
        return TS_NULL_HANDLE;
    }
    canvas->canvas->clear(ToSkColor(color));
    return TS_OK;
}

/* --------------------------------------------------------------------------
 * The 4 DisplayItem ops.
 * ------------------------------------------------------------------------ */
TS_API TsStatus TS_CALL ts_canvas_fill_rect(TsCanvas* canvas, TsRect rect, TsColor color) {
    if (canvas == nullptr || canvas->canvas == nullptr) {
        return TS_NULL_HANDLE;
    }
    SkPaint paint;
    paint.setColor(ToSkColor(color));
    paint.setStyle(SkPaint::kFill_Style);
    paint.setAntiAlias(true);
    canvas->canvas->drawRect(ToSkRect(rect), paint);
    return TS_OK;
}

TS_API TsStatus TS_CALL ts_canvas_stroke_rect(TsCanvas* canvas, TsRect rect, TsColor color,
                                              float stroke_width) {
    if (canvas == nullptr || canvas->canvas == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (stroke_width <= 0.0f) {
        return TS_INVALID_ARGUMENT;
    }
    SkPaint paint;
    paint.setColor(ToSkColor(color));
    paint.setStyle(SkPaint::kStroke_Style);
    paint.setStrokeWidth(stroke_width);
    paint.setAntiAlias(true);
    canvas->canvas->drawRect(ToSkRect(rect), paint);
    return TS_OK;
}

TS_API TsStatus TS_CALL ts_canvas_draw_text(TsCanvas* canvas, TsFont* font,
                                            const TsGlyph* glyphs, size_t glyph_count,
                                            TsColor color) {
    if (canvas == nullptr || canvas->canvas == nullptr || font == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (glyphs == nullptr && glyph_count != 0) {
        return TS_INVALID_ARGUMENT;
    }
    if (glyph_count == 0) {
        return TS_OK;
    }

    std::vector<SkGlyphID> ids(glyph_count);
    std::vector<SkPoint> positions(glyph_count);
    for (size_t i = 0; i < glyph_count; ++i) {
        ids[i] = static_cast<SkGlyphID>(glyphs[i].glyph_id);
        positions[i] = SkPoint::Make(glyphs[i].x, glyphs[i].y);
    }

    SkPaint paint;
    paint.setColor(ToSkColor(color));
    paint.setAntiAlias(true);

    // Positions are absolute (in surface pixels), so origin is (0,0).
    canvas->canvas->drawGlyphs(SkSpan<const SkGlyphID>(ids.data(), ids.size()),
                               SkSpan<const SkPoint>(positions.data(), positions.size()),
                               SkPoint::Make(0.0f, 0.0f), font->font, paint);
    return TS_OK;
}

TS_API TsStatus TS_CALL ts_canvas_draw_image(TsCanvas* canvas,
                                             const uint8_t* pixels, int32_t width, int32_t height,
                                             TsRect dst_rect) {
    if (canvas == nullptr || canvas->canvas == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (pixels == nullptr || width <= 0 || height <= 0) {
        return TS_INVALID_ARGUMENT;
    }

    SkImageInfo info = RgbaWireInfo(width, height);
    const size_t rowBytes = static_cast<size_t>(width) * 4;
    SkPixmap pixmap(info, pixels, rowBytes);

    // Copy the pixels into a raster SkImage so the caller's buffer need not
    // outlive this call.
    sk_sp<SkImage> rasterImage = SkImages::RasterFromPixmapCopy(pixmap);
    if (!rasterImage) {
        return TS_ALLOCATION_FAILED;
    }

    // A Graphite canvas only draws texture-backed images — drawImageRect with a
    // raster SkImage is a silent no-op. Upload the raster pixels to a GPU
    // texture via the Recorder before drawing. (SkImages::TextureFromImage,
    // include/gpu/graphite/Image.h — the current Graphite upload entry point.)
    sk_sp<SkImage> image = rasterImage;
    if (canvas->recorder != nullptr) {
        sk_sp<SkImage> textureImage =
            SkImages::TextureFromImage(canvas->recorder, rasterImage.get(), {});
        if (!textureImage) {
            return TS_ALLOCATION_FAILED;
        }
        image = std::move(textureImage);
    }

    SkRect src = SkRect::MakeWH(static_cast<float>(width), static_cast<float>(height));
    SkSamplingOptions sampling(SkFilterMode::kLinear);
    canvas->canvas->drawImageRect(image, src, ToSkRect(dst_rect), sampling, nullptr,
                                  SkCanvas::kStrict_SrcRectConstraint);
    return TS_OK;
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

    sk_sp<SkData> data = SkData::MakeWithCopy(ttf_bytes, ttf_len);
    sk_sp<SkFontMgr> mgr = SkFontMgr_New_CoreText(nullptr);
    if (!mgr) {
        return TS_BACKEND_UNAVAILABLE;
    }
    sk_sp<SkTypeface> typeface = mgr->makeFromData(std::move(data));
    if (!typeface) {
        return TS_INVALID_ARGUMENT;
    }

    auto handle = std::make_unique<TsTypeface>();
    handle->typeface = std::move(typeface);
    *out_typeface = handle.release();
    return TS_OK;
}

TS_API TsStatus TS_CALL ts_typeface_from_name(const char* family_name, TsTypeface** out_typeface) {
    if (out_typeface == nullptr) {
        return TS_INVALID_ARGUMENT;
    }
    *out_typeface = nullptr;
    if (family_name == nullptr) {
        return TS_INVALID_ARGUMENT;
    }

    sk_sp<SkFontMgr> mgr = SkFontMgr_New_CoreText(nullptr);
    if (!mgr) {
        return TS_BACKEND_UNAVAILABLE;
    }
    sk_sp<SkTypeface> typeface = mgr->matchFamilyStyle(family_name, SkFontStyle());
    if (!typeface) {
        // Fall back to the legacy resolver, which never returns null.
        typeface = mgr->legacyMakeTypeface(family_name, SkFontStyle());
    }
    if (!typeface) {
        return TS_INVALID_ARGUMENT;
    }

    auto handle = std::make_unique<TsTypeface>();
    handle->typeface = std::move(typeface);
    *out_typeface = handle.release();
    return TS_OK;
}

TS_API void TS_CALL ts_typeface_destroy(TsTypeface* typeface) {
    delete typeface;
}

TS_API TsStatus TS_CALL ts_font_create(TsTypeface* typeface, float size_px, TsFont** out_font) {
    if (typeface == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (out_font == nullptr || size_px <= 0.0f) {
        return TS_INVALID_ARGUMENT;
    }
    *out_font = nullptr;
    if (!typeface->typeface) {
        return TS_NULL_HANDLE;
    }

    auto handle = std::make_unique<TsFont>();
    handle->font = SkFont(typeface->typeface, size_px);
    handle->font.setEdging(SkFont::Edging::kAntiAlias);
    handle->font.setSubpixel(true);
    *out_font = handle.release();
    return TS_OK;
}

TS_API void TS_CALL ts_font_destroy(TsFont* font) {
    delete font;
}

TS_API TsStatus TS_CALL ts_font_metrics(TsFont* font, TsFontMetrics* out_metrics) {
    if (font == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (out_metrics == nullptr) {
        return TS_INVALID_ARGUMENT;
    }
    *out_metrics = TsFontMetrics{};

    SkFontMetrics m;
    font->font.getMetrics(&m);
    // SkFontMetrics: fAscent is negative (above baseline), fDescent positive.
    out_metrics->ascent = -m.fAscent;
    out_metrics->descent = m.fDescent;
    out_metrics->leading = m.fLeading;
    out_metrics->cap_height = m.fCapHeight;
    out_metrics->x_height = m.fXHeight;
    out_metrics->underline_position = m.fUnderlinePosition;
    out_metrics->underline_thickness = m.fUnderlineThickness;
    return TS_OK;
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
    *out_glyph_count = 0;
    if (utf8_len == 0) {
        return TS_OK;
    }

    // NOTE: the skshaper module headers/libs were not staged by the native
    // build, so this uses SkFont's built-in glyph mapping + advance-based
    // positioning rather than HarfBuzz complex shaping. This is sufficient for
    // LTR runs of simple scripts and for the golden smoke tests; full
    // HarfBuzz shaping (ligatures, marks, bidi, kerning features) is a
    // follow-up once skshaper is staged. See the WP handoff log.
    const SkFont& skFont = font->font;

    int glyphCount = skFont.countText(utf8_text, utf8_len, SkTextEncoding::kUTF8);
    if (glyphCount <= 0) {
        // Empty or undecodable text — not an error, just no glyphs.
        return TS_OK;
    }

    if (glyph_capacity < static_cast<size_t>(glyphCount) || glyphs == nullptr) {
        // Caller's buffer is too small (or absent): report required capacity.
        *out_glyph_count = static_cast<size_t>(glyphCount);
        return TS_INVALID_ARGUMENT;
    }

    std::vector<SkGlyphID> ids(static_cast<size_t>(glyphCount));
    int converted = skFont.textToGlyphs(utf8_text, utf8_len, SkTextEncoding::kUTF8,
                                        SkSpan<SkGlyphID>(ids.data(), ids.size()));
    if (converted <= 0) {
        return TS_SHAPING_FAILED;
    }

    // Advance-based pen positioning along the baseline (y = 0).
    std::vector<SkScalar> widths(static_cast<size_t>(converted));
    skFont.getWidths(SkSpan<const SkGlyphID>(ids.data(), converted),
                     SkSpan<SkScalar>(widths.data(), widths.size()));

    float penX = 0.0f;
    for (int i = 0; i < converted; ++i) {
        glyphs[i].glyph_id = ids[i];
        glyphs[i].x = penX;
        glyphs[i].y = 0.0f;
        penX += widths[i];
    }

    *out_glyph_count = static_cast<size_t>(converted);
    return TS_OK;
}

/* --------------------------------------------------------------------------
 * Flush + readback.
 * ------------------------------------------------------------------------ */
TS_API TsStatus TS_CALL ts_flush_and_submit(TsContext* context, TsSurface* surface) {
    if (context == nullptr || surface == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (!context->recorder || !context->graphite || !surface->surface) {
        return TS_NULL_HANDLE;
    }

    std::unique_ptr<skgpu::graphite::Recording> recording = context->recorder->snap();
    if (!recording) {
        return TS_UNKNOWN_ERROR;
    }

    skgpu::graphite::InsertRecordingInfo info;
    info.fRecording = recording.get();
    info.fTargetSurface = surface->surface.get();
    if (context->graphite->insertRecording(info) !=
        skgpu::graphite::InsertStatus::kSuccess) {
        return TS_UNKNOWN_ERROR;
    }

    // Block until the GPU has finished so the subsequent readback is valid.
    if (!context->graphite->submit(skgpu::graphite::SyncToCpu::kYes)) {
        return TS_DEVICE_LOST;
    }
    return TS_OK;
}

namespace {

// Callback context for the Graphite async readback.
struct AsyncReadState {
    bool done = false;
    bool ok = false;
    std::unique_ptr<const SkImage::AsyncReadResult> result;
};

void AsyncReadDone(SkImage::ReadPixelsContext rawCtx,
                   std::unique_ptr<const SkImage::AsyncReadResult> result) {
    auto* state = static_cast<AsyncReadState*>(rawCtx);
    state->result = std::move(result);
    state->ok = (state->result != nullptr);
    state->done = true;
}

}  // namespace

TS_API TsStatus TS_CALL ts_read_pixels(TsContext* context, TsSurface* surface,
                                       uint8_t* out_pixels, size_t out_pixels_len) {
    if (context == nullptr || surface == nullptr) {
        return TS_NULL_HANDLE;
    }
    if (out_pixels == nullptr || out_pixels_len == 0) {
        return TS_INVALID_ARGUMENT;
    }
    if (!surface->surface || !context->graphite) {
        return TS_NULL_HANDLE;
    }

    const size_t required = static_cast<size_t>(surface->width) *
                            static_cast<size_t>(surface->height) * 4;
    if (out_pixels_len < required) {
        return TS_INVALID_ARGUMENT;
    }

    // Graphite has no synchronous SkSurface::readPixels — drive the async
    // path and force completion with a SyncToCpu submit.
    SkImageInfo dstInfo = RgbaWireInfo(surface->width, surface->height);
    SkIRect srcRect = SkIRect::MakeWH(surface->width, surface->height);

    AsyncReadState state;
    context->graphite->asyncRescaleAndReadPixels(
        surface->surface.get(), dstInfo, srcRect,
        SkImage::RescaleGamma::kSrc, SkImage::RescaleMode::kNearest,
        &AsyncReadDone, &state);

    // The callback fires during a SyncToCpu submit.
    if (!context->graphite->submit(skgpu::graphite::SyncToCpu::kYes)) {
        return TS_DEVICE_LOST;
    }
    // Defensive: pump until the callback has run.
    int guard = 0;
    while (!state.done && guard++ < 1000) {
        context->graphite->checkAsyncWorkCompletion();
    }

    if (!state.done || !state.ok || !state.result) {
        return TS_READBACK_FAILED;
    }

    const size_t srcRowBytes = state.result->rowBytes(0);
    const auto* src = static_cast<const uint8_t*>(state.result->data(0));
    const size_t dstRowBytes = static_cast<size_t>(surface->width) * 4;
    for (int y = 0; y < surface->height; ++y) {
        std::memcpy(out_pixels + y * dstRowBytes, src + y * srcRowBytes, dstRowBytes);
    }
    return TS_OK;
}

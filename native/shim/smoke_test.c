/* smoke_test.c — tiny C harness for the Tessera Skia Graphite shim.
 *
 * WP M3-06g.
 * ----------
 * Links libtessera_skia and drives the full headless path:
 *   ts_context_create -> ts_surface_create -> ts_surface_get_canvas
 *   -> ts_canvas_clear -> ts_canvas_fill_rect
 *   -> ts_flush_and_submit -> ts_read_pixels
 * then asserts that (a) the cleared background and (b) the filled rect have
 * the expected RGBA pixel values. Exits non-zero on any failure.
 *
 * This is intentionally pure C (no Skia/Dawn headers) — it proves the C ABI
 * is callable exactly the way .NET's [LibraryImport] bindings (WP M3-06h)
 * will call it.
 */

#include "tessera_skia.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define WIDTH  64
#define HEIGHT 64

static int g_failures = 0;

static const char* status_name(TsStatus s) {
    switch (s) {
        case TS_OK:                   return "TS_OK";
        case TS_NOT_IMPLEMENTED:      return "TS_NOT_IMPLEMENTED";
        case TS_INVALID_ARGUMENT:     return "TS_INVALID_ARGUMENT";
        case TS_NULL_HANDLE:          return "TS_NULL_HANDLE";
        case TS_DEVICE_LOST:          return "TS_DEVICE_LOST";
        case TS_BACKEND_UNAVAILABLE:  return "TS_BACKEND_UNAVAILABLE";
        case TS_ALLOCATION_FAILED:    return "TS_ALLOCATION_FAILED";
        case TS_READBACK_FAILED:      return "TS_READBACK_FAILED";
        case TS_SHAPING_FAILED:       return "TS_SHAPING_FAILED";
        default:                      return "TS_UNKNOWN_ERROR";
    }
}

#define REQUIRE_OK(call)                                                     \
    do {                                                                     \
        TsStatus _s = (call);                                                \
        if (_s != TS_OK) {                                                   \
            fprintf(stderr, "FAIL: %s -> %s\n", #call, status_name(_s));      \
            return 1;                                                        \
        }                                                                    \
    } while (0)

/* Channel-tolerant pixel compare (premul/unpremul rounding => allow +/-2). */
static int pixel_is(const uint8_t* px, uint8_t r, uint8_t g, uint8_t b, uint8_t a,
                    const char* what) {
    int dr = (int)px[0] - r, dg = (int)px[1] - g;
    int db = (int)px[2] - b, da = (int)px[3] - a;
    if (dr < 0) dr = -dr; if (dg < 0) dg = -dg;
    if (db < 0) db = -db; if (da < 0) da = -da;
    if (dr > 2 || dg > 2 || db > 2 || da > 2) {
        fprintf(stderr,
                "FAIL: %s expected (%u,%u,%u,%u) got (%u,%u,%u,%u)\n",
                what, r, g, b, a, px[0], px[1], px[2], px[3]);
        g_failures++;
        return 0;
    }
    printf("  ok: %s = (%u,%u,%u,%u)\n", what, px[0], px[1], px[2], px[3]);
    return 1;
}

int main(void) {
    printf("tessera_skia smoke test: %dx%d surface\n", WIDTH, HEIGHT);

    TsContext* ctx = NULL;
    REQUIRE_OK(ts_context_create(TS_BACKEND_AUTO, &ctx));

    char backend[64];
    size_t n = ts_context_backend_name(ctx, backend, sizeof(backend));
    printf("  backend: %.*s\n", (int)n, backend);

    TsSurface* surface = NULL;
    REQUIRE_OK(ts_surface_create(ctx, WIDTH, HEIGHT, &surface));

    TsCanvas* canvas = NULL;
    REQUIRE_OK(ts_surface_get_canvas(surface, &canvas));

    /* Clear to opaque blue, then fill an opaque red rect in the interior. */
    TsColor blue = { 0, 0, 255, 255 };
    TsColor red  = { 255, 0, 0, 255 };
    REQUIRE_OK(ts_canvas_clear(canvas, blue));

    TsRect rect = { 16.0f, 16.0f, 32.0f, 32.0f };
    REQUIRE_OK(ts_canvas_fill_rect(canvas, rect, red));

    REQUIRE_OK(ts_flush_and_submit(ctx, surface));

    size_t buf_len = (size_t)WIDTH * HEIGHT * 4;
    uint8_t* pixels = (uint8_t*)malloc(buf_len);
    if (pixels == NULL) {
        fprintf(stderr, "FAIL: malloc\n");
        return 1;
    }
    REQUIRE_OK(ts_read_pixels(surface, pixels, buf_len));

    /* Corner pixel (2,2) should be the blue background. */
    const uint8_t* corner = &pixels[(2 * WIDTH + 2) * 4];
    pixel_is(corner, 0, 0, 255, 255, "background corner");

    /* Center pixel (32,32) is inside the red rect. */
    const uint8_t* center = &pixels[(32 * WIDTH + 32) * 4];
    pixel_is(center, 255, 0, 0, 255, "rect center");

    free(pixels);
    ts_surface_destroy(surface);
    ts_context_destroy(ctx);

    if (g_failures != 0) {
        fprintf(stderr, "smoke test FAILED (%d pixel mismatches)\n", g_failures);
        return 1;
    }
    printf("smoke test PASSED\n");
    return 0;
}

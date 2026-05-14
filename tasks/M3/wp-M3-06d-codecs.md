---
id: "wp:M3-06d-codecs"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-codecs"
claimed_at: "2026-05-14T14:56:38Z"
completed_at: "2026-05-14T15:30:00Z"
branch: "main"
depends_on:
  - "wp:M3-06c-decoded-image"
blocks:
  - "wp:M3-06l-ci-policy"
subsystem: "Tessera.Codecs"
plan_refs:
  - "browser-plan/01_ARCHITECTURE.md#project-layout"
  - "browser-plan/08_FONTS_PAINT.md#raster-backend"
  - "browser-plan/12_TESTING.md#interop-seam-policy-test"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06d-codecs — `Tessera.Codecs` OS-native image decoders

## Goal

Phase 8 (project half): create `src/Tessera.Codecs`, the **second vetted interop
seam** — a new project allowed `LibraryImport`, with platform dispatch via
`OperatingSystem.IsMacOS()/IsWindows()/IsLinux()` runtime guards. Implement
OS-native image decoders (macOS ImageIO, Windows WIC, Linux libjpeg/libpng/libwebp)
that all return the `Tessera.Common.Image.DecodedImage` defined in `06c`. Wire
`ImageFetcher` to call `NativeImageDecoder.Decode(bytes)`. macOS backend first.

## Inputs

- `wp:M3-06c-decoded-image` complete: `DecodedImage` exists in `Tessera.Common`
  and `ImageFetcher` already produces it (via ImageSharp).
- `OperatingSystem` runtime guards; `[GeneratedComInterface]` source generator
  for the WIC backend.

## Outputs

- `src/Tessera.Codecs/Tessera.Codecs.csproj` — new project; references
  `Tessera.Common` only; added to `Tessera.sln`. One of the two projects allowed
  `LibraryImport`.
- `src/Tessera.Codecs/NativeImageDecoder.cs` — magic-byte sniffer + platform
  dispatch entry point returning `DecodedImage`; throws `ImageDecodeException`
  on failure.
- `src/Tessera.Codecs/Mac/ImageIODecoder.cs` — `CGImageSource` via ImageIO.
- `src/Tessera.Codecs/Windows/WicDecoder.cs` — WIC via `[GeneratedComInterface]`.
- `src/Tessera.Codecs/Linux/` — `libpng16`, `libjpeg-turbo`/`libjpeg`, `libwebp`
  bound by soname; the magic-byte sniffer picks the lib.
- `src/Tessera.Engine/ImageFetcher.cs` — decode call becomes
  `NativeImageDecoder.Decode(bytes)`, catching `ImageDecodeException`.
- `tests/Tessera.Codecs.Tests/` — decodes PNG/JPEG/WebP fixtures to known pixel
  values; runs on macOS, Windows, Linux in CI.

## Acceptance

- `Tessera.Codecs` builds, references only `Tessera.Common`, is in `Tessera.sln`.
- `NativeImageDecoder.Decode(bytes)` returns a correct `DecodedImage` for
  PNG/JPEG/WebP on macOS via ImageIO (Windows/Linux backends follow; macOS
  first).
- `ImageFetcher` no longer calls ImageSharp for decode; a broken/undecodable
  image surfaces `ImageDecodeException` and is handled (alt text path, no crash).
- `tests/Tessera.Codecs.Tests` decodes PNG/JPEG/WebP fixtures to known pixel
  values, green on all three OSes in CI.
- The interop-policy lint job tolerates `LibraryImport` in `Tessera.Codecs`
  (the allowlist work itself is `06l`).

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 8,
  "Tessera.Codecs project").
- WIC COM interop is the highest-effort backend — mitigate with
  `[GeneratedComInterface]`. macOS ImageIO first to unblock the common dev path.
- Image **encode** is handed to the Skia layer (`SKSurface.Encode`), **not** to
  `Tessera.Codecs` — pixels originate in the painter.
- Linux CI runners need `libpng16-16 libjpeg-turbo8 libwebp7` installed — that
  apt step lands in `06l-ci-policy`.
- `Tessera.sln` is a merge-conflict hotspot — note the touch in the handoff log.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
- 2026-05-14T14:56Z — claimed (agent-claude-cody-codecs).
- 2026-05-14T15:30Z — complete (agent-claude-cody-codecs).
  - New project `src/Tessera.Codecs` (net10.0, classlib, refs Tessera.Common
    only, `AllowUnsafeBlocks`, `InternalsVisibleTo` the test project). Added to
    `Tessera.sln` — **note the sln touch** (merge-conflict hotspot).
  - `IImageDecoder` + `ImageDecodeException` + `ImageFormatSniffer` (magic-byte
    classifier) + `NativeImageDecoder` (public entry, OS dispatch via
    `OperatingSystem.Is*()` guards).
  - Backends: `Mac/ImageIODecoder` (ImageIO `CGImageSource` →
    premultiplied-RGBA `CGBitmapContext` → un-premultiply in place);
    `Windows/WicDecoder` (WIC via `[GeneratedComInterface]` source generator);
    `Linux/` (`LinuxImageDecoder` dispatch + `LibPngDecoder` simplified
    `png_image` API, `LibJpegDecoder` TurboJPEG `libturbojpeg.so.0`,
    `LibWebpDecoder` `libwebp.so.7`).
  - `ImageFetcher` rewired: decode is now `NativeImageDecoder.Decode(bytes)`,
    catching `ImageDecodeException` (was ImageSharp exceptions). Added the
    `Tessera.Codecs` project ref to `Tessera.Engine.csproj`. ImageSharp stays in
    Engine/Paint for PNG *encode* only.
  - New `tests/Tessera.Codecs.Tests` (added to sln): 18 tests — sniffer unit
    tests + PNG/JPEG/WebP fixture decode + failure paths. New fixtures
    `testdata/images/{dot.png,swatch.jpg,tile.webp}`.
  - **Runtime-verified live:** macOS ImageIO backend — all 18 codec tests +
    79 Engine.Tests green on this macOS host. **Compile-only here:** Windows WIC
    and Linux libpng/libjpeg/libwebp backends (no CTM flip / no R-B swap needed
    on macOS was confirmed empirically). CI must cover the Windows/Linux legs;
    Linux runners need `libpng16-16 libjpeg-turbo8 libwebp7` (apt step is 06l).
  - Full `dotnet build` + `dotnet test` green from repo root.
  - Downstream: `wp:M3-06l-ci-policy` is unblocked (interop allowlist must now
    include `Tessera.Codecs`).

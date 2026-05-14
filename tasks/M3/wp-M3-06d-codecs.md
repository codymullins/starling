---
id: "wp:M3-06d-codecs"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "blocked"
claimed_by: ""
claimed_at: ""
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

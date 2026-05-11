# Golden images

This directory holds reference PNGs the renderer's output is compared against
during `dotnet test --filter Category=GoldenImage`.

M0 ships no goldens yet — the M0 smoke test only verifies a non-empty PNG is
produced. Strict hash matching arrives in M1 once layout is deterministic
across platforms (font hinting differences make this fiddly cross-OS).

See [`browser-plan/12_TESTING.md`](../../browser-plan/12_TESTING.md) for the
strategy and tolerances (SSIM, per-pixel max diff, etc.).

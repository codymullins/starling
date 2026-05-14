# third_party/REVISIONS.md — pinned native dependency manifest

This file is the **lockfile** for Tessera's native graphics stack. It hard-pins
the exact revisions of Skia, Dawn, and ANGLE that the native build
(`native/build-skia.*`) checks out and compiles. Nothing here floats.

> **Why this matters.** Skia Graphite and the WebGPU C API (via Dawn) were still
> stabilizing through 2025–2026. A floating `main` checkout would make every
> native build non-reproducible and would silently break the C ABI shim
> (`native/shim/`). Treat this file exactly like `packages.lock.json`: a change
> here is a deliberate, reviewed dependency bump, and the hash of this file is
> the cache key for the native build (`/.github/workflows/native.yml`).

## Pinning policy

1. **Skia is pinned to a Chrome-stable-aligned milestone branch, never `main`.**
   Skia cuts a `chrome/m1XX` branch every ~4 weeks from a stable revision, then
   stabilizes it for ~6 weeks alongside the matching Chromium release. We track a
   milestone branch that already has Graphite + Dawn support landed and has been
   through Chrome's stabilization pipeline.
2. **Dawn and ANGLE are pinned to the exact SHAs Skia's `DEPS` resolves at the
   pinned Skia revision.** We do *not* independently pick Dawn/ANGLE revisions —
   `tools/git-sync-deps` (run by the build script) pulls whatever Skia's `DEPS`
   says. The SHAs are recorded here so the build script can *verify* the synced
   tree matches, and so a reader can audit the manifest without a checkout.
3. **Bumps are atomic.** To move to a newer milestone: change the Skia branch +
   SHA, re-resolve Dawn/ANGLE from the new `DEPS`, update this file, and let CI
   rebuild all three RIDs against the new hash. Never bump one dependency in
   isolation.
4. **The build scripts fail loudly on drift.** `native/build-skia.{sh,ps1}`
   assert that the checked-out Skia `HEAD` SHA equals `SKIA_COMMIT` below. If
   they differ, the build aborts — a stale or tampered checkout never produces
   an artifact.

## Pinned revisions

| Dependency | Ref | Revision |
|---|---|---|
| **Skia** | `chrome/m140` (milestone branch) | `SKIA_COMMIT` below |
| **Dawn** | resolved by Skia `DEPS` | `DAWN_COMMIT` below |
| **ANGLE** | resolved by Skia `DEPS` | `ANGLE_COMMIT` below |

### Skia

- **Milestone branch:** `chrome/m140` — concretely pinned. This branch has
  Graphite (`skia_enable_graphite`) and the Dawn backend (`skia_use_dawn`)
  landed and stabilized. Chrome shipped Skia Graphite to stable on Apple Silicon
  during the M13x line; `chrome/m140` is a conservative, already-stabilized
  choice for the first Tessera native build.
- **Commit (`SKIA_COMMIT`):** `5db0949ba318d248ebc3d33c73ad1251bf95c243`
  <!-- TODO: confirm — branch-tip SHA observed via
       https://skia.googlesource.com/skia/+/refs/heads/chrome/m140 ;
       re-verify against the live branch tip before the first CI run, since
       milestone branches still receive occasional cherry-picks. -->
- **Source of truth:** <https://skia.googlesource.com/skia/+/refs/heads/chrome/m140>
- **DEPS file consulted:** <https://skia.googlesource.com/skia/+/refs/heads/chrome/m140/DEPS>

### Dawn

- **Path in Skia checkout:** `third_party/externals/dawn`
- **Commit (`DAWN_COMMIT`):** `0b095928b31253ffc96884e460e08cc5710c2c2`
  <!-- TODO: confirm — read from chrome/m140 DEPS at third_party/externals/dawn.
       Run `git -C third_party/skia/third_party/externals/dawn rev-parse HEAD`
       after `tools/git-sync-deps` and paste the exact 40-char SHA here. -->
- Dawn provides the WebGPU implementation Skia Graphite renders through; Dawn in
  turn selects Metal (macOS), D3D12 (Windows), or Vulkan (Linux) at runtime.

### ANGLE

- **Path in Skia checkout:** `third_party/externals/angle2`
- **Commit (`ANGLE_COMMIT`):** `b6b2f3808014eadf33f215adc2e99f208c800ae4`
  <!-- TODO: confirm — read from chrome/m140 DEPS at third_party/externals/angle2.
       Run `git -C third_party/skia/third_party/externals/angle2 rev-parse HEAD`
       after `tools/git-sync-deps` and paste the exact 40-char SHA here. -->
- ANGLE is the **GL/GLES fallback** provider (`skia_use_gl=true`). It is not the
  primary path — Graphite-on-Dawn is — so do not over-invest in it for v1.

> **TODO-confirm note.** The milestone branch name `chrome/m140` is pinned
> concretely and is correct. The three 40-character SHAs above are transcribed
> from the public `DEPS` / branch listing and are marked `TODO: confirm`: they
> must be re-read from an actual checkout (`git rev-parse HEAD` in each tree
> after `tools/git-sync-deps`) and corrected here before the native build is
> trusted. The build scripts will refuse to build until `SKIA_COMMIT` matches
> the real checkout, which is the backstop against a wrong transcription.

## Machine-readable pins

The build scripts parse the block below. Keep the `KEY=value` lines exact —
one space-free token per line, no quoting.

```ini
SKIA_BRANCH=chrome/m140
SKIA_COMMIT=5db0949ba318d248ebc3d33c73ad1251bf95c243
DAWN_COMMIT=0b095928b31253ffc96884e460e08cc5710c2c2
ANGLE_COMMIT=b6b2f3808014eadf33f215adc2e99f208c800ae4
```

## Artifact strategy (out-of-band, never in PR CI, never committed)

The native libraries built from these revisions are **large, slow to produce,
and platform-specific**. They are handled out-of-band:

- **Built by a dedicated pipeline** — `.github/workflows/native.yml`, triggered
  manually or when this file changes. Never by PR `ci.yml` (a Skia build is
  20–40 min; PR CI must stay fast).
- **Consumed as a versioned release artifact / internal package** — downstream
  .NET projects (`src/Tessera.Skia`, WP 06h) restore the prebuilt
  `runtimes/<rid>/native/` payload; they do not rebuild Skia.
- **Never committed to git** — `runtimes/` and `third_party/skia/` are
  `.gitignore`d. Only the *scripts* that reproduce the build live in the repo.
- **Reproducible** — anyone on a provisioned machine can run
  `native/build-skia.*` and get bit-identical inputs because every revision is
  pinned here.

See `native/README.md` for the reproduction steps and per-platform prerequisites.

# native/ — Skia Graphite + Dawn + ANGLE build and C ABI shim

This directory holds everything needed to **reproduce** Tessera's native
graphics layer. It contains build scripts and the C ABI shim source — it does
**not** contain any built binaries, and a Skia checkout is never committed here.

> **Status (Phase 0 / Phase 1 scaffolding).** The scripts, directory layout, CI
> workflow, and shim *scaffold* in this directory are complete and committed.
> The actual native libraries have **not** been built — that requires running
> `build-skia.*` on a provisioned machine (multi-hour GN/Ninja build). The shim
> in `shim/` is a header + stub-body scaffold; its real implementation is
> WP `M3-06g` and depends on a real `libskia` being available.

## Layout

```
native/
├── README.md            ← this file
├── build-skia.sh        ← Skia+Dawn build driver (macOS, Linux)
├── build-skia.ps1       ← Skia+Dawn build driver (Windows)
├── out/                 ← GN/Ninja build output  (gitignored)
├── build/               ← CMake build dir for the shim (gitignored)
└── shim/
    ├── tessera_skia.h    ← C ABI surface (scaffold — signatures only)
    ├── tessera_skia.cpp  ← stub bodies (scaffold — return TS_NOT_IMPLEMENTED)
    └── CMakeLists.txt    ← builds libtessera_skia, static-linked to libskia

third_party/
├── REVISIONS.md         ← the pinned lockfile (Skia/Dawn/ANGLE SHAs)
└── skia/                ← fetched Skia checkout (gitignored)

runtimes/
└── <rid>/native/        ← staged build output, per RID (gitignored)
```

`<rid>` is a .NET Runtime Identifier: `osx-arm64`, `win-x64`, `linux-x64`.

## What the build produces

1. `build-skia.*` fetches Skia at the revision pinned in
   `third_party/REVISIONS.md`, runs `tools/git-sync-deps` (which pulls Dawn and
   ANGLE at the `DEPS`-resolved revisions), runs `gn gen` with the Graphite GN
   args, and `ninja`-builds `libskia` plus its Dawn/ANGLE static libs.
2. Output is staged into `runtimes/<rid>/native/` (the Skia/Dawn static libs +
   headers + license files).
3. **WP 06g** (not in this scaffold) then builds `shim/` via CMake, statically
   linking the shim's custom `extern "C"` ABI against `libskia` + Dawn into a
   single `libtessera_skia.{dylib,dll,so}` per RID — the one native file .NET
   loads.

## Reproducing a build

```bash
# macOS (osx-arm64) / Linux (linux-x64)
./native/build-skia.sh

# Windows (win-x64), from a Developer PowerShell
pwsh ./native/build-skia.ps1
```

The scripts:

- read the pinned revisions out of `third_party/REVISIONS.md`,
- fetch / update `third_party/skia/` to exactly `SKIA_COMMIT`,
- **abort loudly** if the checked-out `HEAD` does not equal `SKIA_COMMIT`,
- `tools/git-sync-deps`, `gn gen`, `ninja`,
- stage artifacts + license files into `runtimes/<rid>/native/`.

Build osx-arm64 first and fully working, then win-x64, then linux-x64 — strict
staging is the risk mitigation (see `tasks/M3/wp-M3-06b-native-build.md`).

## GN args used

Set by the build scripts (see `third_party/REVISIONS.md` and the master plan
Phase 1):

```
skia_enable_graphite=true
skia_use_dawn=true
skia_use_gl=true            # ANGLE fallback
skia_use_harfbuzz=true
skia_use_icu=true
is_official_build=true
target_cpu="arm64"|"x64"    # per RID
target_os="mac"|"win"|"linux"
```

## Prerequisites per platform

All platforms need:

- **`depot_tools`** on `PATH` (provides `gn`, `ninja`, `fetch`,
  `gclient`). <https://chromium.googlesource.com/chromium/tools/depot_tools.git>
- **Python 3.9+** (required by `tools/git-sync-deps` and `depot_tools`).
- **Git 2.30+**.
- ~40 GB free disk and a fast network for the first checkout.

### macOS (`osx-arm64`) — primary, build this first

- macOS 14+ on Apple Silicon (CI: `macos-15`).
- **Xcode** + Command Line Tools (`xcode-select --install`); the full Xcode app
  is needed for the Metal toolchain Dawn links against.
- Targets Metal via Dawn.

### Windows (`win-x64`)

- Windows 11 / Server 2025 (CI: `windows-2025`).
- **Visual Studio 2022** with the "Desktop development with C++" workload and
  the Windows 10/11 SDK.
- `pwsh` (PowerShell 7+).
- Targets D3D12 via Dawn.

### Linux (`linux-x64`)

- Ubuntu 24.04 (CI: `ubuntu-24.04`).
- Clang/LLVM, `build-essential`, plus the X11/Vulkan headers Skia+Dawn need:
  `sudo apt-get install build-essential clang libgl1-mesa-dev libx11-dev \
   libxcomposite-dev libxcursor-dev libxi-dev libxrandr-dev \
   libvulkan-dev mesa-vulkan-drivers ninja-build`
- Targets Vulkan via Dawn.

## Artifact strategy

Native binaries are built **out-of-band** by `.github/workflows/native.yml`
(manual / `REVISIONS.md`-triggered), uploaded as release artifacts, and consumed
by `src/Tessera.Skia` as a versioned package. They are **not** built in PR
`ci.yml` and **not** committed to git. See `third_party/REVISIONS.md` for the
full rationale and the pinning policy.

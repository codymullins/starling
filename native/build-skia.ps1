#!/usr/bin/env pwsh
# native/build-skia.ps1 — reproducible Skia + Graphite + Dawn build (Windows)
#
# Fetches Skia at the revision pinned in third_party/REVISIONS.md, syncs its
# DEPS (Dawn + ANGLE), runs `gn gen` with the Graphite GN args, `ninja`-builds,
# and stages the output + license files into runtimes\<rid>\native\.
#
# This script is SCAFFOLDING for WP M3-06b. It encodes the full reproducible
# recipe but has not itself been run end-to-end here (a Skia build is a
# multi-hour GN/Ninja job needing depot_tools + Visual Studio). Run it on a
# provisioned machine — see native/README.md for prerequisites.
#
# Usage:  pwsh ./native/build-skia.ps1
# Requires: depot_tools on PATH (gn, ninja), python3, git, VS 2022 C++ workload.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Log  { param([string]$Message) Write-Host "[build-skia] $Message" -ForegroundColor Cyan }
function Throw-Die  { param([string]$Message) throw "[build-skia] ERROR: $Message" }

# --- locate repo paths -------------------------------------------------------
$ScriptDir     = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot      = (Resolve-Path (Join-Path $ScriptDir '..')).Path
$RevisionsFile = Join-Path $RepoRoot 'third_party/REVISIONS.md'
$SkiaDir       = Join-Path $RepoRoot 'third_party/skia'
$OutBase       = Join-Path $RepoRoot 'native/out'

# --- detect RID / GN target --------------------------------------------------
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
switch ($arch) {
  'X64'   { $Rid = 'win-x64';   $GnTargetCpu = 'x64' }
  'Arm64' { $Rid = 'win-arm64'; $GnTargetCpu = 'arm64' }
  default { Throw-Die "unsupported Windows arch: $arch" }
}
$GnTargetOs = 'win'
Write-Log "host: Windows/$arch -> RID=$Rid (target_os=$GnTargetOs, target_cpu=$GnTargetCpu)"

# --- toolchain checks --------------------------------------------------------
foreach ($tool in 'git', 'python3', 'gn', 'ninja') {
  if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
    Throw-Die "$tool not found on PATH (add depot_tools to PATH for gn/ninja)"
  }
}

# --- parse pinned revisions from REVISIONS.md --------------------------------
if (-not (Test-Path $RevisionsFile)) {
  Throw-Die "missing $RevisionsFile (run WP M3-06a first)"
}
$revText = Get-Content -Raw $RevisionsFile

function Read-Pin {
  param([string]$Key)
  $m = [regex]::Match($revText, "(?m)^$([regex]::Escape($Key))=(.+)$")
  if (-not $m.Success) { Throw-Die "could not read $Key from $RevisionsFile" }
  return $m.Groups[1].Value.Trim()
}

$SkiaBranch  = Read-Pin 'SKIA_BRANCH'
$SkiaCommit  = Read-Pin 'SKIA_COMMIT'
$DawnCommit  = Read-Pin 'DAWN_COMMIT'
$AngleCommit = Read-Pin 'ANGLE_COMMIT'
Write-Log "pinned Skia $SkiaBranch @ $SkiaCommit"
Write-Log "pinned Dawn  @ $DawnCommit"
Write-Log "pinned ANGLE @ $AngleCommit"

# --- fetch / update Skia checkout to the pinned commit -----------------------
$SkiaRemote = 'https://skia.googlesource.com/skia.git'
if (-not (Test-Path (Join-Path $SkiaDir '.git'))) {
  Write-Log "cloning Skia into $SkiaDir ..."
  git clone $SkiaRemote $SkiaDir
}

Write-Log "checking out Skia $SkiaCommit ($SkiaBranch) ..."
git -C $SkiaDir fetch origin $SkiaBranch --tags
git -C $SkiaDir -c advice.detachedHead=false checkout $SkiaCommit

# --- HARD GUARD: checkout SHA must equal the pinned SHA ----------------------
$ActualSha = (git -C $SkiaDir rev-parse HEAD).Trim()
if ($ActualSha -ne $SkiaCommit) {
  Throw-Die "Skia checkout drift: HEAD=$ActualSha but REVISIONS.md pins $SkiaCommit. Refusing to build."
}
Write-Log 'Skia checkout verified == pinned SHA'

# --- sync Skia DEPS (pulls Dawn + ANGLE at DEPS-resolved revisions) ----------
Write-Log 'syncing Skia DEPS (Dawn, ANGLE, harfbuzz, icu, ...) ...'
python3 (Join-Path $SkiaDir 'tools/git-sync-deps')

# --- verify Dawn / ANGLE revisions match the manifest ------------------------
function Test-Dep {
  param([string]$Name, [string]$Path, [string]$Want)
  if (-not (Test-Path (Join-Path $Path '.git'))) { Throw-Die "$Name not synced at $Path" }
  $got = (git -C $Path rev-parse HEAD).Trim()
  if ($got -ne $Want) {
    Throw-Die "$Name revision drift: synced $got but REVISIONS.md pins $Want. Re-run WP M3-06a's manifest update."
  }
  Write-Log "$Name verified == pinned SHA"
}
Test-Dep -Name 'Dawn'  -Path (Join-Path $SkiaDir 'third_party/externals/dawn')   -Want $DawnCommit
Test-Dep -Name 'ANGLE' -Path (Join-Path $SkiaDir 'third_party/externals/angle2') -Want $AngleCommit

# --- gn gen ------------------------------------------------------------------
$OutDir = Join-Path $OutBase $Rid
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$GnArgs = @(
  'skia_enable_graphite=true',
  'skia_use_dawn=true',
  'skia_use_gl=true',
  'skia_use_harfbuzz=true',
  'skia_use_icu=true',
  'is_official_build=true',
  "target_cpu=`"$GnTargetCpu`"",
  "target_os=`"$GnTargetOs`""
) -join ' '

Write-Log "gn gen $OutDir"
Write-Log "  args: $GnArgs"
Push-Location $SkiaDir
try {
  gn gen $OutDir --args="$GnArgs"

  # --- ninja build -----------------------------------------------------------
  Write-Log 'ninja build (this is the long part — 20-40 min) ...'
  ninja -C $OutDir skia
}
finally {
  Pop-Location
}

# --- stage artifacts into runtimes\<rid>\native\ -----------------------------
$StageDir = Join-Path $RepoRoot "runtimes/$Rid/native"
New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
Write-Log "staging artifacts into $StageDir"

# Skia + Dawn static libraries (.lib on Windows).
Get-ChildItem -Path $OutDir -Filter '*.lib' -File |
  ForEach-Object { Copy-Item -Force $_.FullName (Join-Path $StageDir $_.Name) }

# Public headers the shim compiles against.
$IncludeDir = Join-Path $StageDir 'include'
New-Item -ItemType Directory -Force -Path $IncludeDir | Out-Null
Copy-Item -Recurse -Force (Join-Path $SkiaDir 'include') (Join-Path $IncludeDir 'skia')
$DawnInc = Join-Path $SkiaDir 'third_party/externals/dawn/include'
if (Test-Path $DawnInc) { Copy-Item -Recurse -Force $DawnInc (Join-Path $IncludeDir 'dawn') }

# License files — required for redistribution; uploaded by native.yml.
Copy-Item -Force (Join-Path $SkiaDir 'LICENSE') (Join-Path $StageDir 'LICENSE.skia')
$DawnLic  = Join-Path $SkiaDir 'third_party/externals/dawn/LICENSE'
$AngleLic = Join-Path $SkiaDir 'third_party/externals/angle2/LICENSE'
if (Test-Path $DawnLic)  { Copy-Item -Force $DawnLic  (Join-Path $StageDir 'LICENSE.dawn') }
if (Test-Path $AngleLic) { Copy-Item -Force $AngleLic (Join-Path $StageDir 'LICENSE.angle') }

Write-Log "done — Skia + Dawn artifacts staged in $StageDir"
Write-Log 'next: WP M3-06g builds native/shim/ (CMake) and static-links libtessera_skia.*'

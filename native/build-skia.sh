#!/usr/bin/env bash
# native/build-skia.sh — reproducible Skia + Graphite + Dawn build (macOS, Linux)
#
# Fetches Skia at the revision pinned in third_party/REVISIONS.md, syncs its
# DEPS (Dawn + ANGLE), runs `gn gen` with the Graphite GN args, `ninja`-builds,
# and stages the output + license files into runtimes/<rid>/native/.
#
# This script is SCAFFOLDING for WP M3-06b. It encodes the full reproducible
# recipe but has not itself been run end-to-end here (a Skia build is a
# multi-hour GN/Ninja job needing depot_tools + a platform toolchain). Run it on
# a provisioned machine — see native/README.md for prerequisites.
#
# Usage:  ./native/build-skia.sh
# Requires: depot_tools on PATH (gn, ninja), python3, git.

set -euo pipefail

# --- locate repo paths -------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
REVISIONS_FILE="${REPO_ROOT}/third_party/REVISIONS.md"
SKIA_DIR="${REPO_ROOT}/third_party/skia"
OUT_BASE="${REPO_ROOT}/native/out"

log()  { printf '\033[1;34m[build-skia]\033[0m %s\n' "$*"; }
die()  { printf '\033[1;31m[build-skia] ERROR:\033[0m %s\n' "$*" >&2; exit 1; }

# --- detect RID / GN target --------------------------------------------------
UNAME_S="$(uname -s)"
UNAME_M="$(uname -m)"
case "${UNAME_S}" in
  Darwin)
    GN_TARGET_OS="mac"
    case "${UNAME_M}" in
      arm64)  RID="osx-arm64";  GN_TARGET_CPU="arm64" ;;
      x86_64) RID="osx-x64";    GN_TARGET_CPU="x64" ;;
      *) die "unsupported macOS arch: ${UNAME_M}" ;;
    esac
    LIB_EXT="a"
    ;;
  Linux)
    GN_TARGET_OS="linux"
    case "${UNAME_M}" in
      x86_64)  RID="linux-x64";   GN_TARGET_CPU="x64" ;;
      aarch64) RID="linux-arm64"; GN_TARGET_CPU="arm64" ;;
      *) die "unsupported Linux arch: ${UNAME_M}" ;;
    esac
    LIB_EXT="a"
    ;;
  *)
    die "unsupported OS '${UNAME_S}' — use native/build-skia.ps1 on Windows"
    ;;
esac
log "host: ${UNAME_S}/${UNAME_M} -> RID=${RID} (target_os=${GN_TARGET_OS}, target_cpu=${GN_TARGET_CPU})"

# --- toolchain checks --------------------------------------------------------
# Note: gn + ninja are NOT required on PATH. Skia self-provisions both during
# `tools/git-sync-deps` (bin/gn, third_party/ninja/ninja) — we resolve and use
# those Skia-bundled binaries after the sync step below. depot_tools is only
# needed for `gclient`/`fetch`, not for the Skia build proper.
command -v git    >/dev/null 2>&1 || die "git not found on PATH"
command -v python3>/dev/null 2>&1 || die "python3 not found on PATH"

# --- parse pinned revisions from REVISIONS.md --------------------------------
[ -f "${REVISIONS_FILE}" ] || die "missing ${REVISIONS_FILE} (run WP M3-06a first)"

read_pin() {
  # extract `KEY=value` from the machine-readable block in REVISIONS.md
  local key="$1" val
  val="$(grep -E "^${key}=" "${REVISIONS_FILE}" | head -n1 | cut -d= -f2- || true)"
  [ -n "${val}" ] || die "could not read ${key} from ${REVISIONS_FILE}"
  printf '%s' "${val}"
}

SKIA_BRANCH="$(read_pin SKIA_BRANCH)"
SKIA_COMMIT="$(read_pin SKIA_COMMIT)"
DAWN_COMMIT="$(read_pin DAWN_COMMIT)"
ANGLE_COMMIT="$(read_pin ANGLE_COMMIT)"
log "pinned Skia ${SKIA_BRANCH} @ ${SKIA_COMMIT}"
log "pinned Dawn  @ ${DAWN_COMMIT}"
log "pinned ANGLE @ ${ANGLE_COMMIT}"

# --- fetch / update Skia checkout to the pinned commit -----------------------
SKIA_REMOTE="https://skia.googlesource.com/skia.git"
if [ ! -d "${SKIA_DIR}/.git" ]; then
  # The target may pre-exist as an empty placeholder (e.g. a stray `.keep` left
  # by scaffolding). `git clone` refuses a non-empty target, so clear the dir
  # IFF it holds nothing but ignorable placeholders; otherwise abort rather
  # than clobber something real.
  if [ -d "${SKIA_DIR}" ]; then
    if [ -z "$(find "${SKIA_DIR}" -mindepth 1 ! -name '.keep' -print -quit)" ]; then
      log "clearing empty placeholder dir ${SKIA_DIR}"
      rm -rf "${SKIA_DIR}"
    else
      die "${SKIA_DIR} exists, is not a Skia git checkout, and is not empty.
       Refusing to clobber it — inspect and remove it manually, then re-run."
    fi
  fi
  log "cloning Skia into ${SKIA_DIR} ..."
  git clone "${SKIA_REMOTE}" "${SKIA_DIR}"
fi

log "checking out Skia ${SKIA_COMMIT} (${SKIA_BRANCH}) ..."
git -C "${SKIA_DIR}" fetch origin "${SKIA_BRANCH}" --tags
git -C "${SKIA_DIR}" -c advice.detachedHead=false checkout "${SKIA_COMMIT}"

# --- HARD GUARD: checkout SHA must equal the pinned SHA ----------------------
ACTUAL_SHA="$(git -C "${SKIA_DIR}" rev-parse HEAD)"
if [ "${ACTUAL_SHA}" != "${SKIA_COMMIT}" ]; then
  die "Skia checkout drift: HEAD=${ACTUAL_SHA} but REVISIONS.md pins ${SKIA_COMMIT}.
       Refusing to build. Fix third_party/REVISIONS.md or the checkout."
fi
log "Skia checkout verified == pinned SHA"

# --- sync Skia DEPS (pulls Dawn + ANGLE at DEPS-resolved revisions) ----------
# GIT_SYNC_DEPS_SKIP_EMSDK=1: emsdk (Emscripten SDK) is only needed for WASM
# builds of Skia. Tessera builds native (Metal/D3D12/Vulkan via Dawn), so we
# skip it — its activate-emsdk post-step also resolves paths relative to CWD
# and fails unless run from inside the Skia checkout. The `cd` is belt-and-
# suspenders for any other CWD-relative step.
log "syncing Skia DEPS (Dawn, ANGLE, harfbuzz, icu, ...) ..."
( cd "${SKIA_DIR}" && GIT_SYNC_DEPS_SKIP_EMSDK=1 python3 tools/git-sync-deps )

# --- verify Dawn / ANGLE revisions match the manifest ------------------------
verify_dep() {
  local name="$1" path="$2" want="$3" got
  [ -d "${path}/.git" ] || die "${name} not synced at ${path}"
  got="$(git -C "${path}" rev-parse HEAD)"
  if [ "${got}" != "${want}" ]; then
    die "${name} revision drift: synced ${got} but REVISIONS.md pins ${want}.
         Skia's DEPS at ${SKIA_COMMIT} no longer resolves the pinned ${name} —
         re-run WP M3-06a's manifest update."
  fi
  log "${name} verified == pinned SHA"
}
verify_dep "Dawn"  "${SKIA_DIR}/third_party/externals/dawn"   "${DAWN_COMMIT}"
verify_dep "ANGLE" "${SKIA_DIR}/third_party/externals/angle2" "${ANGLE_COMMIT}"

# --- provision + resolve Skia's gn + ninja -----------------------------------
# git-sync-deps fetches `gn` (via a hook) but NOT `ninja` — `bin/fetch-ninja`
# must be invoked explicitly. Both scripts chdir into the Skia root themselves
# and install into the checkout (bin/gn, third_party/ninja/ninja). Both are
# idempotent ("Already up to date." on re-run).
log "fetching Skia-pinned ninja ..."
( cd "${SKIA_DIR}" && python3 bin/fetch-ninja )
if [ ! -x "${SKIA_DIR}/bin/gn" ]; then
  log "fetching Skia-pinned gn ..."
  ( cd "${SKIA_DIR}" && python3 bin/fetch-gn )
fi

GN="${SKIA_DIR}/bin/gn"
NINJA="${SKIA_DIR}/third_party/ninja/ninja"
[ -x "${GN}" ]    || die "gn not found at ${GN} after bin/fetch-gn"
[ -x "${NINJA}" ] || die "ninja not found at ${NINJA} after bin/fetch-ninja"
log "using Skia gn=${GN}"
log "using Skia ninja=${NINJA}"

# --- gn gen ------------------------------------------------------------------
OUT_DIR="${OUT_BASE}/${RID}"
mkdir -p "${OUT_DIR}"

# skia_use_cpp20=true is REQUIRED with skia_use_dawn=true: Dawn's Tint compiler
# uses C++20 (`concept`/`requires`). Skia's build defaults to C++17 and would
# fail to compile Dawn without this. (Confirmed against chrome/m140.)
GN_ARGS="\
skia_enable_graphite=true \
skia_use_dawn=true \
skia_use_cpp20=true \
skia_use_gl=true \
skia_use_harfbuzz=true \
skia_use_icu=true \
is_official_build=true \
target_cpu=\"${GN_TARGET_CPU}\" \
target_os=\"${GN_TARGET_OS}\""

log "gn gen ${OUT_DIR}"
log "  args: ${GN_ARGS}"
( cd "${SKIA_DIR}" && "${GN}" gen "${OUT_DIR}" --args="${GN_ARGS}" )

# --- ninja build -------------------------------------------------------------
log "ninja build (this is the long part — 20-40 min) ..."
( cd "${SKIA_DIR}" && "${NINJA}" -C "${OUT_DIR}" skia )

# --- stage artifacts into runtimes/<rid>/native/ -----------------------------
STAGE_DIR="${REPO_ROOT}/runtimes/${RID}/native"
mkdir -p "${STAGE_DIR}"
log "staging artifacts into ${STAGE_DIR}"

# Skia + Dawn static libraries.
find "${OUT_DIR}" -maxdepth 1 -name "*.${LIB_EXT}" -exec cp -f {} "${STAGE_DIR}/" \;

# Public headers the shim compiles against.
mkdir -p "${STAGE_DIR}/include"
cp -Rf "${SKIA_DIR}/include" "${STAGE_DIR}/include/skia"
if [ -d "${SKIA_DIR}/third_party/externals/dawn/include" ]; then
  cp -Rf "${SKIA_DIR}/third_party/externals/dawn/include" "${STAGE_DIR}/include/dawn"
fi

# License files — required for redistribution; uploaded by native.yml.
cp -f "${SKIA_DIR}/LICENSE" "${STAGE_DIR}/LICENSE.skia"
[ -f "${SKIA_DIR}/third_party/externals/dawn/LICENSE" ] \
  && cp -f "${SKIA_DIR}/third_party/externals/dawn/LICENSE" "${STAGE_DIR}/LICENSE.dawn"
[ -f "${SKIA_DIR}/third_party/externals/angle2/LICENSE" ] \
  && cp -f "${SKIA_DIR}/third_party/externals/angle2/LICENSE" "${STAGE_DIR}/LICENSE.angle"

log "done — Skia + Dawn artifacts staged in ${STAGE_DIR}"
log "next: WP M3-06g builds native/shim/ (CMake) and static-links libtessera_skia.*"

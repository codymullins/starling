#!/usr/bin/env bash
# tools/fetch-wpt.sh — fetch the web-platform-tests suite at a pinned commit into
# testdata/wpt/suite/ (gitignored). Uses a blobless partial clone + cone
# sparse-checkout so only the chosen subset's blobs are downloaded — the full
# suite is 2M+ files, so we never check it all out.
#
# The checkout lands in testdata/wpt/suite/ (NOT testdata/wpt/, which holds a
# few hand-committed encoding fixtures used by Starling.Engine.Tests).
#
# Usage:   tools/fetch-wpt.sh
# Subset:  WPT_DIRS="dom css/css-syntax url" tools/fetch-wpt.sh
# Idempotent: re-runs check out the pinned SHA and re-apply the sparse set.
set -euo pipefail

# Pinned for reproducible pass-rate measurement. Bump deliberately.
WPT_SHA="2f79718126841d68bf361edc9318123d82013ed7"
REPO="https://github.com/web-platform-tests/wpt.git"

# Directories to materialize. `resources` (testharness.js et al.) and `common`
# are required by virtually every testharness test; the rest is the conformance
# subset we measure. Override with WPT_DIRS. Keep cone-mode prefixes (dir paths).
WPT_DIRS="${WPT_DIRS:-resources common dom domparsing css/css-syntax css/selectors css/cssom encoding url html/dom selection}"

root="$(git rev-parse --show-toplevel)"
dst="$root/testdata/wpt/suite"

if [[ -f "$dst/.git/HEAD" ]] || [[ -f "$dst/.git" ]]; then
    echo "wpt checkout exists at $dst — fetching pinned SHA $WPT_SHA"
    git -C "$dst" sparse-checkout set $WPT_DIRS
    git -C "$dst" fetch --filter=blob:none --depth 1 origin "$WPT_SHA"
    git -C "$dst" checkout -q FETCH_HEAD
else
    echo "cloning wpt (blobless partial, sparse) @ $WPT_SHA → $dst"
    echo "  subset: $WPT_DIRS"
    mkdir -p "$dst"
    git -C "$dst" init -q
    git -C "$dst" remote add origin "$REPO" 2>/dev/null || true
    git -C "$dst" config core.sparseCheckout true
    git -C "$dst" sparse-checkout init --cone
    git -C "$dst" sparse-checkout set $WPT_DIRS
    git -C "$dst" fetch -q --filter=blob:none --depth 1 origin "$WPT_SHA"
    git -C "$dst" checkout -q FETCH_HEAD
fi

harness="$dst/resources/testharness.js"
if [[ ! -f "$harness" ]]; then
    echo "ERROR: $harness missing — the 'resources' dir must be in WPT_DIRS." >&2
    exit 1
fi
echo "wpt ready: $(find "$dst" -name '*.html' | wc -l | tr -d ' ') html files; resources:"
ls "$dst/resources" | grep -i testharness || true

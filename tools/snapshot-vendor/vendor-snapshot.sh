#!/usr/bin/env bash
#
# tools/snapshot-vendor/vendor-snapshot.sh — vendor a remote HTML page and
# every static subresource it references into
# testdata/snapshots/<host>/, plus a manifest.json that records each URL,
# HTTP timestamp, content-type, byte length, and SHA-256.
#
# Designed for very small, mostly-static pages (think marketing front
# pages). Walks <link href> and <img src> attributes in the root HTML;
# does not transitively follow CSS @import or JS-discovered assets — keep
# the input simple.
#
# Usage:
#   tools/snapshot-vendor/vendor-snapshot.sh https://nginx.org/
#
# Re-running overwrites files under the destination directory. The diff
# between the old manifest.json and the new one is the audit trail for a
# re-vendor.

set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <root-url>" >&2
  exit 2
fi

ROOT_URL="$1"
HOST="$(printf %s "$ROOT_URL" | sed -E 's,^https?://([^/]+).*,\1,' | sed 's/^www\.//')"
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DEST="$REPO_ROOT/testdata/snapshots/$HOST"
mkdir -p "$DEST"

echo "Vendoring $ROOT_URL -> $DEST"

# --- helpers ------------------------------------------------------------

# Resolve a (possibly relative) href against the root URL into an absolute URL.
absolutise() {
  local href="$1"
  if [[ "$href" =~ ^https?:// ]]; then
    printf '%s' "$href"
  elif [[ "$href" == //* ]]; then
    printf '%s' "${ROOT_URL%%:*}:$href"
  elif [[ "$href" == /* ]]; then
    local scheme_host
    scheme_host="$(printf %s "$ROOT_URL" | sed -E 's,^(https?://[^/]+).*,\1,')"
    printf '%s' "${scheme_host}${href}"
  else
    # Relative to the root URL's directory.
    local base
    base="$(printf %s "$ROOT_URL" | sed -E 's,[^/]*$,,')"
    printf '%s' "${base}${href}"
  fi
}

# Convert an absolute URL into a snapshot-relative path (always starts /).
relpath_of() {
  local url="$1"
  printf '%s' "$url" | sed -E 's,^https?://[^/]+,,'
}

# Fetch <url> -> <local-path>; emit one manifest record on stdout.
fetch_record() {
  local url="$1"
  local rel
  rel="$(relpath_of "$url")"
  [[ "$rel" == "/" || -z "$rel" ]] && rel="/index.html"
  local local_path="$DEST$rel"
  mkdir -p "$(dirname "$local_path")"

  local header_file body_file
  header_file="$(mktemp)"
  body_file="$local_path"

  # -L follow redirects to land at the canonical file; -A sets a stable
  # UA so re-vendoring against the same server is deterministic.
  curl --silent --show-error --location \
       -A "tessera-snapshot-vendor/1.0" \
       -D "$header_file" \
       -o "$body_file" \
       "$url"

  local ctype timestamp sha length
  ctype="$(grep -i '^content-type:' "$header_file" | tail -1 | sed -E 's/^[Cc]ontent-[Tt]ype:[[:space:]]*//; s/[\r[:space:]]*$//')"
  timestamp="$(grep -i '^date:' "$header_file" | tail -1 | sed -E 's/^[Dd]ate:[[:space:]]*//; s/[\r[:space:]]*$//')"
  sha="$(shasum -a 256 "$body_file" | awk '{print $1}')"
  length="$(wc -c < "$body_file" | tr -d ' ')"
  rm -f "$header_file"

  # JSON object on stdout (no trailing comma).
  printf '    {\n      "url": "%s",\n      "path": "%s",\n      "content_type": "%s",\n      "timestamp": "%s",\n      "sha256": "%s",\n      "bytes": %s\n    }' \
    "$url" "$rel" "$ctype" "$timestamp" "$sha" "$length"
}

# --- 1. fetch root HTML -------------------------------------------------

ROOT_REL="$(relpath_of "$ROOT_URL")"
[[ "$ROOT_REL" == "/" || -z "$ROOT_REL" ]] && ROOT_REL="/index.html"
ROOT_LOCAL="$DEST$ROOT_REL"
mkdir -p "$(dirname "$ROOT_LOCAL")"

# Fetch into a temp first so we can parse subresources before committing.
ROOT_TMP="$(mktemp)"
ROOT_HDR="$(mktemp)"
curl --silent --show-error --location \
     -A "tessera-snapshot-vendor/1.0" \
     -D "$ROOT_HDR" \
     -o "$ROOT_TMP" \
     "$ROOT_URL"

# --- 2. scrape link/href + img/src --------------------------------------

# Crude but adequate: split on whitespace-equivalents, harvest href="…"
# and src="…" attribute values, drop dupes, drop fragments / external
# anchors / data URIs.
mapfile -t REFS < <(
  tr -d '\n\r' < "$ROOT_TMP" |
    grep -oE '(href|src)="[^"#?]+"' |
    sed -E 's/^(href|src)="//; s/"$//' |
    sort -u
)

declare -a SUBRESOURCES=()
for ref in "${REFS[@]}"; do
  # Skip anchors, data: URIs and JS.
  [[ -z "$ref" ]] && continue
  [[ "$ref" == data:* ]] && continue
  [[ "$ref" == javascript:* ]] && continue
  [[ "$ref" == mailto:* ]] && continue
  [[ "$ref" == http://* || "$ref" == https://* ]] && {
    # Only follow same-origin links so we never leak third-party assets
    # into the fixture.
    [[ "$ref" != "$ROOT_URL"* && "$ref" != "${ROOT_URL%/}"* ]] && {
      case "$ref" in
        https://"$HOST"/*|http://"$HOST"/*|https://www."$HOST"/*|http://www."$HOST"/*) : ;;
        *) continue ;;
      esac
    }
  }
  # Filter to subresource extensions we actually care about.
  case "$ref" in
    *.css|*.png|*.jpg|*.jpeg|*.gif|*.webp|*.svg|*.woff|*.woff2|*.ttf|*.otf|*.ico)
      SUBRESOURCES+=("$(absolutise "$ref")") ;;
    *) continue ;;
  esac
done

# Dedup.
mapfile -t SUBRESOURCES < <(printf '%s\n' "${SUBRESOURCES[@]}" | sort -u)

# --- 3. write root + subresources, build manifest -----------------------

cp "$ROOT_TMP" "$ROOT_LOCAL"

MANIFEST="$DEST/manifest.json"
{
  printf '{\n'
  printf '  "root_url": "%s",\n' "$ROOT_URL"
  printf '  "host": "%s",\n' "$HOST"
  printf '  "captured_at": "%s",\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  printf '  "vendor_tool": "tools/snapshot-vendor/vendor-snapshot.sh",\n'
  printf '  "resources": [\n'

  first=1
  # Root entry — synthesise from $ROOT_HDR so we don't double-fetch.
  ctype="$(grep -i '^content-type:' "$ROOT_HDR" | tail -1 | sed -E 's/^[Cc]ontent-[Tt]ype:[[:space:]]*//; s/[\r[:space:]]*$//')"
  timestamp="$(grep -i '^date:' "$ROOT_HDR" | tail -1 | sed -E 's/^[Dd]ate:[[:space:]]*//; s/[\r[:space:]]*$//')"
  sha="$(shasum -a 256 "$ROOT_LOCAL" | awk '{print $1}')"
  length="$(wc -c < "$ROOT_LOCAL" | tr -d ' ')"
  printf '    {\n      "url": "%s",\n      "path": "%s",\n      "content_type": "%s",\n      "timestamp": "%s",\n      "sha256": "%s",\n      "bytes": %s\n    }' \
    "$ROOT_URL" "$ROOT_REL" "$ctype" "$timestamp" "$sha" "$length"
  first=0

  for sub in "${SUBRESOURCES[@]}"; do
    [[ $first -eq 0 ]] && printf ',\n'
    fetch_record "$sub"
    first=0
  done

  printf '\n  ]\n}\n'
} > "$MANIFEST"

rm -f "$ROOT_TMP" "$ROOT_HDR"

echo "Vendored $(jq '.resources | length' "$MANIFEST" 2>/dev/null || echo '?') resources -> $MANIFEST"

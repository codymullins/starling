#!/usr/bin/env bash
# tasks/lib/claim.sh — claim, release, or status a work-package file.
#
# Usage:
#   claim.sh <wp-id> <agent-id>             # claim
#   claim.sh release <wp-id>                # release a claim
#   claim.sh review <wp-id> [<pr-url>]      # flip to in_review
#   claim.sh complete <wp-id>               # flip to complete
#   claim.sh status <wp-id>                 # print frontmatter
#
# The script edits frontmatter atomically in a single commit. Concurrency is
# handled by git: two agents racing to claim the same package will produce a
# merge conflict on push; second to push pulls, re-evaluates, and picks a
# different package.
#
# Requirements: bash 4+, GNU sed (or BSD sed with `-E`), git.

set -euo pipefail

repo_root() {
    git rev-parse --show-toplevel
}

now_utc() {
    # ISO-8601 UTC, second precision, with trailing Z.
    date -u +"%Y-%m-%dT%H:%M:%SZ"
}

find_wp_file() {
    local wp_id="$1"
    # Accept either bare id or wp:-prefixed.
    wp_id="${wp_id#wp:}"
    local root
    root="$(repo_root)"
    # Files live at tasks/M<n>/wp-<wp_id>.md.
    local matches
    matches=$(find "$root/tasks" -type f -name "wp-${wp_id}.md" | head -n 1 || true)
    if [[ -z "$matches" ]]; then
        echo "error: no work-package file found for id wp:${wp_id}" >&2
        return 2
    fi
    echo "$matches"
}

# Replace a frontmatter scalar `key: <value>` between the first two `---` lines.
# Quotes the value to keep YAML happy. Idempotent.
set_field() {
    local file="$1" key="$2" value="$3"
    # Use a Python helper for safety: pure sed across YAML is fragile.
    python3 - "$file" "$key" "$value" <<'PY'
import sys, re, pathlib
path, key, value = sys.argv[1], sys.argv[2], sys.argv[3]
src = pathlib.Path(path).read_text()
m = re.match(r"^---\n(.*?)\n---\n", src, re.DOTALL)
if not m:
    sys.exit(f"error: no frontmatter in {path}")
fm = m.group(1)
quoted = '"' + value.replace('"', '\\"') + '"'
pattern = re.compile(rf'^{re.escape(key)}:\s*.*$', re.MULTILINE)
if pattern.search(fm):
    new_fm = pattern.sub(f'{key}: {quoted}', fm)
else:
    # Append before the closing fence.
    new_fm = fm.rstrip() + f'\n{key}: {quoted}'
new_src = f'---\n{new_fm}\n---\n' + src[m.end():]
pathlib.Path(path).write_text(new_src)
PY
}

append_handoff() {
    local file="$1" line="$2"
    python3 - "$file" "$line" <<'PY'
import sys, pathlib
path, line = sys.argv[1], sys.argv[2]
src = pathlib.Path(path).read_text()
marker = "## Handoff log"
if marker not in src:
    src = src.rstrip() + f"\n\n{marker}\n\n- {line}\n"
else:
    src = src.rstrip() + f"\n- {line}\n"
pathlib.Path(path).write_text(src)
PY
}

read_field() {
    local file="$1" key="$2"
    python3 - "$file" "$key" <<'PY'
import sys, re, pathlib
path, key = sys.argv[1], sys.argv[2]
src = pathlib.Path(path).read_text()
m = re.match(r"^---\n(.*?)\n---\n", src, re.DOTALL)
if not m:
    sys.exit(1)
fm = m.group(1)
mm = re.search(rf'^{re.escape(key)}:\s*"?(.*?)"?\s*$', fm, re.MULTILINE)
print(mm.group(1) if mm else "")
PY
}

require_clean_tree() {
    if ! git diff --quiet || ! git diff --cached --quiet; then
        echo "error: working tree has uncommitted changes. Commit or stash before claiming." >&2
        exit 3
    fi
}

cmd_claim() {
    local wp_id="$1" agent="$2"
    local file
    file="$(find_wp_file "$wp_id")"
    local current_status
    current_status="$(read_field "$file" status)"
    if [[ "$current_status" != "available" ]]; then
        echo "error: $wp_id is '$current_status', not 'available'" >&2
        exit 4
    fi

    local now
    now="$(now_utc)"
    local branch="wp-${wp_id#wp:}"

    set_field "$file" status "claimed"
    set_field "$file" claimed_by "$agent"
    set_field "$file" claimed_at "$now"
    set_field "$file" branch "$branch"
    append_handoff "$file" "$now — claimed by $agent, branch \`$branch\`"

    git add "$file"
    git commit -m "wp:${wp_id#wp:} — claim ($agent)" >/dev/null

    echo "claimed wp:${wp_id#wp:}"
    echo "  branch: $branch"
    echo "  next:   git switch -c $branch"
}

cmd_release() {
    local wp_id="$1"
    local file
    file="$(find_wp_file "$wp_id")"
    local who when
    who="$(read_field "$file" claimed_by)"
    when="$(read_field "$file" claimed_at)"
    local now
    now="$(now_utc)"

    set_field "$file" status "available"
    set_field "$file" claimed_by ""
    set_field "$file" claimed_at ""
    set_field "$file" branch ""
    append_handoff "$file" "$now — released (was $who, claimed $when)"

    git add "$file"
    git commit -m "wp:${wp_id#wp:} — release" >/dev/null
    echo "released wp:${wp_id#wp:}"
}

cmd_review() {
    local wp_id="$1" pr_url="${2:-}"
    local file
    file="$(find_wp_file "$wp_id")"
    set_field "$file" status "in_review"
    local now
    now="$(now_utc)"
    if [[ -n "$pr_url" ]]; then
        append_handoff "$file" "$now — PR opened: $pr_url"
    else
        append_handoff "$file" "$now — PR opened"
    fi
    git add "$file"
    git commit -m "wp:${wp_id#wp:} — in_review" >/dev/null
    echo "marked in_review wp:${wp_id#wp:}"
}

cmd_complete() {
    local wp_id="$1"
    local file
    file="$(find_wp_file "$wp_id")"
    local now
    now="$(now_utc)"
    set_field "$file" status "complete"
    set_field "$file" completed_at "$now"
    append_handoff "$file" "$now — merged; complete"
    git add "$file"
    git commit -m "wp:${wp_id#wp:} — complete" >/dev/null
    echo "completed wp:${wp_id#wp:}"
}

cmd_status() {
    local wp_id="$1"
    local file
    file="$(find_wp_file "$wp_id")"
    python3 - "$file" <<'PY'
import sys, re, pathlib
src = pathlib.Path(sys.argv[1]).read_text()
m = re.match(r"^---\n(.*?)\n---\n", src, re.DOTALL)
print(m.group(1) if m else "(no frontmatter)")
PY
}

main() {
    if [[ $# -lt 1 ]]; then
        echo "usage: $0 {claim|release|review|complete|status} <wp-id> [...]" >&2
        exit 1
    fi
    local action="$1"; shift || true
    case "$action" in
        claim)
            [[ $# -ge 2 ]] || { echo "usage: claim <wp-id> <agent-id>" >&2; exit 1; }
            require_clean_tree
            cmd_claim "$1" "$2"
            ;;
        release)
            [[ $# -ge 1 ]] || { echo "usage: release <wp-id>" >&2; exit 1; }
            require_clean_tree
            cmd_release "$1"
            ;;
        review)
            [[ $# -ge 1 ]] || { echo "usage: review <wp-id> [pr-url]" >&2; exit 1; }
            require_clean_tree
            cmd_review "$1" "${2:-}"
            ;;
        complete)
            [[ $# -ge 1 ]] || { echo "usage: complete <wp-id>" >&2; exit 1; }
            require_clean_tree
            cmd_complete "$1"
            ;;
        status)
            [[ $# -ge 1 ]] || { echo "usage: status <wp-id>" >&2; exit 1; }
            cmd_status "$1"
            ;;
        *)
            # Back-compat: `claim.sh <wp-id> <agent-id>` (no verb) means claim.
            if [[ -n "${1-}" && "$action" == wp:* ]]; then
                require_clean_tree
                cmd_claim "$action" "$1"
            else
                echo "unknown action: $action" >&2
                exit 1
            fi
            ;;
    esac
}

main "$@"

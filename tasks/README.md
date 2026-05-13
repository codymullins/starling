# Tasks — multi-agent coordination

This directory is the **shared work queue** for Tessera. Every implementation work-package
described in [`../browser-plan/14_AGENT_TASKS.md`](../browser-plan/14_AGENT_TASKS.md)
has a tracking file here. The file is the single source of truth for "is this
package claimed, by whom, and how far along?"

The directory is committed. Agents coordinate **through git** — no daemon, no
external service. Claiming a task is a normal commit; conflicts surface as
normal merge conflicts.

---

## Layout

```
tasks/
├── README.md         # this file
├── INDEX.md          # human-readable rollup of every package by status
├── SCHEMA.md         # the frontmatter contract for wp-*.md files
├── lib/
│   └── claim.sh      # atomic claim/release helper (optional but recommended)
└── M<n>/
    └── wp-<id>.md    # one file per work package
```

A work-package file looks like this (full schema in [`SCHEMA.md`](SCHEMA.md)):

```markdown
---
id: wp:M1-01a-tokenizer-scaffold
milestone: M1
status: available
claimed_by: ""
claimed_at: ""
branch: ""
depends_on: []
blocks: ["wp:M1-01b-tokenizer-text-states"]
subsystem: Tessera.Html
plan_refs:
  - browser-plan/04_HTML_PARSING.md#tokenizer
---

# wp:M1-01a — HTML tokenizer scaffold

## Goal
...

## Acceptance
...

## Handoff log
- 2026-05-11 — claimed by agent-claude-cody, branch wp-M1-01a
```

---

## Agent workflow

A new agent session follows the same loop every time. **No prior chat memory is
required** — the repo + plan + tasks dir contain the whole state.

### 1. Orient

```bash
# Read these three, in order:
cat AGENTS.md                       # rules of engagement
cat browser-plan/13_MILESTONES.md   # what milestone we're in
cat tasks/INDEX.md                  # what's available, what's blocked
```

### 2. Pick an unblocked task

A task is **available** to claim if:

- `status: available` (not `claimed`, `in_review`, or `complete`)
- every `depends_on` entry has `status: complete`

Prefer the **lowest-numbered milestone** with available work, then the
**highest-priority** package within it (critical-path before parallel work — see
the parallelization map in `14_AGENT_TASKS.md`).

If two tasks look equivalent: take the one fewer agents touched recently (the
handoff log shows recency).

### 3. Claim atomically

```bash
./tasks/lib/claim.sh wp:M1-01a-tokenizer-scaffold "agent-claude-cody"
```

The helper edits the frontmatter (`status: claimed`, `claimed_by`,
`claimed_at`, `branch`) in a single commit. If another agent claimed the same
task between your read and your commit, git push will fail with a non-fast-forward
error — pull, see the conflict, pick a different task.

You can do it by hand too — the helper just runs a `sed`-and-commit dance.

### 4. Work on main

All work goes on `main` — there is no per-package branch. The `branch:`
field in the task file is preserved for historical context (older packages
recorded the branch they were developed on) but new claims default to
`main`.

Commit frequently. Each commit message **must** reference the package id so
history is greppable:

```
wp:M1-01a — add Data state transitions
```

### 5. Stop early? Leave a breadcrumb.

If you have to stop before the acceptance criteria are met:

1. Update the task file's **Handoff log** with what's done, what's next, and any
   gotchas you hit.
2. Either:
   - **Keep the claim** (status stays `claimed`) if you'll resume yourself.
   - **Release the claim** (`./tasks/lib/claim.sh release wp:M1-01a-tokenizer-scaffold`)
     so another agent can pick it up. The handoff log carries the context.
3. Commit.

Stale claims (a `claimed_at` older than 72 h with no commits referencing the
package id) are considered abandoned — any agent may release and re-claim
them.

### 6. Finish

When acceptance criteria pass:

1. `dotnet build` + `dotnet test` green locally.
2. Commit your final changes on main with `wp:<id> —` in the subject.
3. Set the task file `status: complete` and add `completed_at:` (the
   `claim.sh complete` helper does this).
4. Re-scan `INDEX.md` — your completion may have unblocked downstream
   packages; if so, note them in the handoff log so the next agent knows
   to pick them up.

> The `in_review` state in `SCHEMA.md` is preserved for projects that wire
> up a remote with PR review later. Today this repo doesn't use it —
> packages move `claimed` → `complete` directly.

---

## Dependencies

`depends_on` lists package IDs that **must be `complete`** before this one can
start. The graph mirrors the dependency graph in `14_AGENT_TASKS.md`.

`blocks` is the inverse — informational, kept in sync by hand.

If you discover a hidden dependency mid-flight, edit the task file and the
handoff log. Don't suffer in silence.

---

## Why this approach

| Need | How it's handled |
|---|---|
| Many agents working in parallel | Each on a different `wp-*.md` file → no merge conflict |
| Two agents try to claim the same task | First commit wins; second sees git conflict on the frontmatter |
| Agent dies mid-task | `claimed_at` ages out; next agent re-claims; handoff log carries context |
| Cross-session continuity | The plan, the task file, and the branch are all in the repo |
| Visibility for humans | `tasks/INDEX.md` rolls up; PR titles carry package IDs |
| Avoiding "what was I doing?" | Handoff log is the contract |

Nothing here requires a server. Everything is files in git.

---

## See also

- [`AGENTS.md`](../AGENTS.md) — top-level rules of engagement (read this first)
- [`SCHEMA.md`](SCHEMA.md) — exact frontmatter fields and allowed values
- [`INDEX.md`](INDEX.md) — current status of all packages
- [`browser-plan/14_AGENT_TASKS.md`](../browser-plan/14_AGENT_TASKS.md) — the
  authoritative package catalog (this directory implements it)

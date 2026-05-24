# Work-package file schema

Every file under `tasks/M*/wp-*.md` MUST have YAML frontmatter conforming to this
schema. The format is checked by `tasks/lib/claim.sh` and (eventually) by CI.

## Frontmatter fields

```yaml
---
id: wp:M1-01a-tokenizer-scaffold       # required, must equal "wp:" + filename minus ".md"
parent: wp:M1-01-html-tokenizer        # optional, rolls sub-tasks up to their parent
milestone: M1                          # required, one of M0..M13
status: available                      # required, one of:
                                       #   available  — anyone may claim
                                       #   claimed    — someone is working on it
                                       #   in_review  — optional; only used when a remote with PR review is configured
                                       #   complete   — merged
                                       #   blocked    — dependency or external block; see notes
claimed_by: ""                         # required, agent identifier or "" when unclaimed
claimed_at: ""                         # required, ISO-8601 UTC ("2026-05-11T15:30:00Z") or ""
branch: ""                             # required; defaults to "main" — work happens on main and the field is informational. Historical packages may record a per-package branch name (e.g., wp-M1-01a) from when that convention was active.
completed_at: ""                       # optional, set when status flips to complete
reviewed_commit: ""                    # optional, for review tasks: git commit SHA/ref that was reviewed
depends_on:                            # required, list of wp ids (may be empty)
  - wp:M0-01-scaffold
blocks:                                # optional, informational reverse-deps
  - wp:M1-01b-tokenizer-text-states
subsystem: Starling.Html                # required, short module name
plan_refs:                             # required, at least one
  - browser-plan/04_HTML_PARSING.md#tokenizer
  - browser-plan/14_AGENT_TASKS.md#wpm1-01-html-tokenizer
---
```

## Body sections

Below the frontmatter, in this order:

```
# wp:<id> — short title

## Goal
One paragraph. The "definition of done" in plain English.

## Inputs
What must exist before this can start. Mirror `depends_on` but in prose; call
out specific files or types.

## Outputs
What this work package creates. File paths, type names, public APIs.

## Acceptance
Concrete check-offs. Mirror the acceptance criteria from `14_AGENT_TASKS.md`
and add test names. A future agent should be able to read this and know whether
the work is done.

## Notes
Anything the implementer learned that future readers (or the agent picking up
the next sub-task) will need. Open questions, design decisions, spec quirks.

## Handoff log
Append-only timeline. One bullet per session event. Most recent at the bottom.

- 2026-05-11T15:30Z — created (agent-claude-cody)
- 2026-05-11T16:10Z — claimed by agent-claude-cody, branch wp-M1-01a
- 2026-05-11T18:45Z — paused; data state done, tag-open next; releasing claim
```

## Status state machine

```
        ┌─────────────┐
        │  available  │◄──────────┐
        └──────┬──────┘           │
               │ claim            │ release (stale or voluntary)
               ▼                  │
        ┌─────────────┐           │
        │  claimed    │───────────┘
        └──────┬──────┘
               │ open PR
               ▼
        ┌─────────────┐
        │  in_review  │
        └──────┬──────┘
               │ merge
               ▼
        ┌─────────────┐
        │  complete   │  (terminal)
        └─────────────┘

  blocked: parallel to available; needs human intervention to unblock.
```

Only specific transitions are allowed:

| From | To | Trigger |
|---|---|---|
| `available` | `claimed` | agent runs claim |
| `claimed` | `complete` | acceptance criteria pass + commit on main |
| `claimed` | `in_review` | (optional) PR opened — only used when a remote with review is configured |
| `claimed` | `available` | agent releases or claim ages out |
| `claimed` | `blocked` | external block discovered |
| `in_review` | `complete` | PR merged |
| `in_review` | `claimed` | PR closed without merge |
| `blocked` | `available` | block resolved |
| `available` | `blocked` | human marks blocked |

In this repo today the default flow is `available → claimed → complete`;
the `in_review` state is preserved for future use.

`complete` is terminal — if work is bad, file a follow-up package, don't
re-open.

## Identifier rules

- `id` is `wp:M<milestone>-<seq>[<letter>]-<slug>`.
- `<seq>` is two digits, zero-padded.
- `<letter>` is optional, lowercase a–z; use it for sub-tasks of a parent
  package.
- `<slug>` is lower-kebab-case, ≤ 30 chars.
- Examples: `wp:M0-01-scaffold`, `wp:M1-01-html-tokenizer`,
  `wp:M1-01a-tokenizer-scaffold`.

## Agent identifier convention

`agent-<model>-<human>` — e.g., `agent-claude-cody`. If two agents run as the
same person at the same time, append a short suffix (`agent-claude-cody-2`).

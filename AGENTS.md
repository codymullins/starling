# Agents — rules of engagement

This file is the **first thing an implementation agent should read** when
opening this repo. It explains how multiple agents share work without
stepping on each other, and how a single agent can stop mid-task and have a
later session resume cleanly.

## TL;DR

```bash
# 1. Orient
cat AGENTS.md                            # this file
less browser-plan/13_MILESTONES.md       # where we are
less tasks/INDEX.md                      # what's available

# 2. Claim something unblocked
./tasks/lib/claim.sh claim wp:M1-03-dom-core "agent-claude-<your-handle>"

# 3. Read the package file and start working on main
less tasks/M1/wp-M1-03-dom-core.md

# 4. Commit often, with the wp id in the subject:
#    "wp:M1-03 — Node hierarchy + tests"

# 5. Build + test before completing
dotnet build && dotnet test

# 6. Mark complete:
./tasks/lib/claim.sh complete wp:M1-03-dom-core
```

**All work happens on `main`.** This repo doesn't use a per-package branch
workflow — agents commit directly to main. (If a remote with PR review is
ever wired up, the optional `in_review` state in `tasks/SCHEMA.md` is
available for it; today it's unused.)

If you have to stop early, **leave a handoff log entry** in the task file
and either keep the claim (you'll resume) or release it
(`./tasks/lib/claim.sh release wp:…`). Either way: commit so the next
agent sees the state.

## The contract

| You can rely on | You must do |
|---|---|
| One file per work package under `tasks/M*/wp-*.md` | Touch only your claimed package's file (plus your code changes) |
| `tasks/INDEX.md` reflects the current status | Update INDEX.md when you change a status |
| Work happens on `main`; commits carry the wp id in the subject | Prefix commit messages with `wp:<id> —` so history is greppable |
| Dependencies are explicit in `depends_on` | Don't start a package whose deps aren't `complete` |
| Stale claims age out at 72 h | Add a handoff log entry every session, even if you're "still going" |

## Repo map

```
tessera/
├── AGENTS.md                  ← you are here
├── README.md                  ← human-facing intro
├── browser-plan/              ← the design (immutable except by deliberate edit)
│   ├── 00_INDEX.md            ← start here for design context
│   ├── 13_MILESTONES.md       ← what milestone we're in
│   └── 14_AGENT_TASKS.md      ← authoritative package catalog
├── tasks/                     ← work coordination (this repo's queue)
│   ├── README.md              ← detailed workflow
│   ├── SCHEMA.md              ← frontmatter contract
│   ├── INDEX.md               ← current status of all packages
│   ├── lib/claim.sh           ← atomic claim/release helper
│   └── M<n>/wp-*.md           ← one file per work package
├── src/                       ← engine + Headless CLI + MAUI Gui (Mac Catalyst)
├── Starling.AppHost/          ← Aspire AppHost (orchestrates Gui + Headless)
├── Starling.ServiceDefaults/  ← Aspire OTel + health-check shared bootstrap
├── tests/                     ← one xUnit project per src/ module + E2E
├── bench/Starling.Bench/      ← BenchmarkDotNet
└── testdata/                  ← fixtures + golden PNGs + WPT subsets
```

## Build + test (must be green before merge)

```bash
dotnet --version            # expect 10.0.x
dotnet restore
dotnet build -c Debug
dotnet test  -c Debug
```

If `dotnet build` errors with permission-denied apphost deletions in a
sandbox or container, pass `-p:UseAppHost=false`. This is a sandbox quirk
only — CI runs without the flag.

## Interop policy — managed-first, native at vetted seams

Native interop (`[LibraryImport]`/`[DllImport]`) is confined to two
**designated interop projects**: `src/Starling.Skia` (graphics) and
`src/Starling.Codecs` (image decode). Every other engine module under
`src/Starling.{Common,Url,Net,Html,Dom,Css,Layout,Paint,Js,Bindings,Loop,Engine}/`
stays **pure managed** — no P/Invoke, no native dependencies beyond what the
.NET BCL ships. **TLS path: BouncyCastle.** `Starling.Net` uses
`BouncyCastle.Cryptography` (pure-managed, no P/Invoke) for TLS 1.3 via
`BcTlsTransport`. The `wp:M3-06e` SslStream migration was rolled back in
`939f3a5 fix ssl crash` (2026-05-14) after a macOS TLS 1.3 issue surfaced in
integration; re-attempting SslStream — or formally re-blessing BouncyCastle as
the long-term path — is a tracked open item in `wp:M3-06-native-interop-pivot`'s
handoff log. The interop-seam policy is still satisfied either way, because
BouncyCastle adds no native dependency. CI greps the engine-project allowlist
(every engine project *except* the two interop projects); the lint job fails if
you regress it. The GUI shell (`src/Starling.Gui`, .NET MAUI) and the Aspire
AppHost/ServiceDefaults projects are exempt — they link against UIKit/Cocoa
(Catalyst) and ASP.NET host plumbing respectively, which is fine because the
engine never imports from any of them. The engine projects must continue to
build and test cleanly without those heavier platforms loaded.

**The native Skia shim is a hard requirement, not optional.** Skia Graphite is
the engine's sole rasterizer — there is no managed fallback (the interim
ImageSharp fallback was removed deliberately). `libtessera_skia` is gitignored
and never committed, so a fresh checkout will fail `dotnet build` until you
build it: `./native/build-skia.sh` then the shim CMake build — see
[`native/README.md`](native/README.md). Do **not** "fix" a missing-shim build
error by reintroducing a fallback; build the shim. Currently produced for
osx-arm64 only; win-x64/linux-x64 native builds are unrun, so those CI legs are
honestly red until they exist.

## Decision hierarchy when something's ambiguous

1. **The plan** (`browser-plan/`) — if a doc gives an answer, follow it.
2. **The work-package file** — it overrides general advice for the scope.
3. **The spec** the plan cites (WHATWG, ECMA, RFC) — follow the spec literally,
   citing section numbers in code comments.
4. **Ask** — open an `<!-- OPEN QUESTION -->` block in the relevant
   `browser-plan/` doc and proceed with your best guess. Do not block on
   absent humans.

## Concurrency model

- **Two agents on different packages:** independent. They commit to main
  in any order — different files, no conflict.
- **Two agents on the same package:** the second one to commit a `claim`
  edit loses the race (git non-fast-forward on the claim commit). They
  pull, see the new state, and pick a different package.
- **Agent dies mid-task:** the `claimed_at` timestamp shows when. After
  72 h with no commits referencing the package, any agent may release
  the claim and start over (or pick up using the handoff log).
- **Cross-package edits:** if your work changes a shared file
  (`Directory.Build.props`, `Directory.Packages.props`, `Starling.sln`),
  call it out in the handoff log so a concurrent agent can rebase
  cleanly. These files are the merge-conflict hotspots.

## A worked example

Day 1 — agent-alex picks up `wp:M1-03-dom-core`:

```bash
./tasks/lib/claim.sh claim wp:M1-03-dom-core "agent-claude-alex"
# tasks/M1/wp-M1-03-dom-core.md now has status: claimed, claimed_by: agent-claude-alex
# ...writes Node.cs, NodeKind.cs, mutations on main...
git commit -am "wp:M1-03 — Node + NodeKind + first 30 mutation tests"
```

Day 2 — alex has to stop, riley picks it up:

```bash
# alex, end of session:
$EDITOR tasks/M1/wp-M1-03-dom-core.md   # adds handoff log entry
./tasks/lib/claim.sh release wp:M1-03-dom-core

# riley starts:
./tasks/lib/claim.sh claim wp:M1-03-dom-core "agent-claude-riley"
cat tasks/M1/wp-M1-03-dom-core.md       # reads the handoff log
# ...continues on main from "remaining: Element attribute mutations"...
```

Day 3 — riley finishes:

```bash
dotnet build && dotnet test             # green
git commit -am "wp:M1-03 — Element attribute mutations + tests"
./tasks/lib/claim.sh complete wp:M1-03-dom-core
# Update tasks/INDEX.md: M1-03 → 🟢 complete; M1-04, M1-06 unblock.
```

## Questions

If the design plan says X and reality says Y, edit the plan to match Y in
the same PR, and explain in the PR body. The plan is a living document —
it tracks the implementation, not the other way around.

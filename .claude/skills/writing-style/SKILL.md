---
name: writing-style
description: "Writing-style rules for any prose authored in this project — READMEs, design docs in browser-plan/, commit bodies, PR descriptions, status updates. USE WHEN about to write or edit prose (Markdown text, doc comments meant for humans, commit messages, PR bodies), or when reviewing prose for tone. Enforces: fifth-grade reading level (short sentences, common words), no semicolons unless truly required, no unclear abbreviations (spell out PSL, ALPN, HSTS, TDZ, SSIM, GA/gtag, MCP, VM, GC on first use), no verbose restating, no pointing out the obvious. Project-specific naming: never use the phrase 'hand-written' — name the Starling resource explicitly ('Starling JS engine', 'Starling DOM', 'Starling networking'). DO NOT USE FOR: code logic, identifier naming, or test assertions — only human-facing prose."
---

# Writing style

Apply these rules to every paragraph of prose authored in this project.

## Rules

1. **Fifth-grade reading level where possible.** Short sentences. Common
   words. Grammatically correct. Prefer "uses" over "leverages", "checks"
   over "verifies", "real sites" over "real-world bundles".
2. **No unclear abbreviations.** Spell out anything most readers won't
   know on first use.
   - Fine as-is in context: HTTP, TLS, DNS, URL, DOM, CSS, JSON, PR, CI,
     GUI, Test262, WPT (when context is clear), HTML, JS.
   - Spell out or rephrase: TDZ, SSIM ("similarity score"), PSL ("Public
     Suffix List"), ALPN (drop or explain), HSTS (drop or "hardening"),
     GA/gtag ("Google Analytics"), MCP ("Model Context Protocol"), VM
     ("virtual machine") on first use, GC ("garbage collector") on first
     use, BCL (".NET base class library").
3. **No semicolons unless truly necessary.** A period almost always reads
   better. Exception: dense table cells where two clauses must share one
   row.
4. **No verbose explanations.** If a sentence restates the previous one
   with different words, cut it. If a parenthetical is more than ~6
   words, ask whether it earns its keep.
5. **Don't point out the obvious.**
   - Bad: "runs on every PR" (already in the CI config).
   - Bad: "click the link to learn more" (the link does that).
   - Bad: "these tests pin behavior" right after listing the tests.
   - Bad: filler lead-ins like "These tests are author-written" that add
     no information.
6. **Never use the phrase "hand-written"** — code comments, READMEs,
   commit messages, anywhere. Name the Starling resource explicitly:
   "the Starling JS engine", "Starling DOM", "Starling networking from
   `System.Net.Sockets` up". Reads as marketing and obscures what we
   actually mean.

## Workflow when invoked

1. Before writing: scan the rules above.
2. Draft the prose.
3. Re-read each paragraph and check:
   - Would a smart fifth grader understand this?
   - Any semicolons I can replace with a period?
   - Any abbreviation a first-time reader wouldn't know?
   - Did I restate the previous sentence?
   - Did I state something the reader can already see (link, code,
     filename)?
   - Did I write "hand-written" anywhere?
4. Cut, swap, simplify. Then save.

## Editing this skill

The user may add, remove, or change any rule. Treat this file as the
source of truth — when memory or older guidance disagrees, follow what's
here. To change a rule, edit this file directly.

## Background

Codified after the user flagged the same prose for three issues in one
session:

1. The phrase "hand-written" (caught twice).
2. Filler lead-ins like "These tests are author-written".
3. Verbose, semicolon-heavy prose with unclear abbreviations across nine
   new READMEs.

A short memory pointer at
`~/.claude/projects/-Users-cody-code-starling/memory/feedback_writing_style.md`
points back here so the rules survive memory drift.

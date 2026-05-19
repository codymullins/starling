# `testdata/webref` — pinned snapshot of `w3c/webref`

Source: <https://github.com/w3c/webref>
License: MIT
Snapshot commit: `589264b15ca2172f2592c5578a857b4d0e2840c1`
Fetched: 2026-05-19

This directory contains a curated subset of the [W3C webref](https://github.com/w3c/webref)
machine-readable specification data, used by `tools/Starling.SpecGen` to:

1. Build the canonical CSS spec catalog (`tasks/SPEC_COVERAGE.md`).
2. Generate stub `[PendingFact]` tests for each property / at-rule / value
   defined by every in-scope spec.

## Layout

| Path | Source path in webref | Contents |
|---|---|---|
| `css/*.json` | `ed/css/*.json` | One file per CSSWG spec — properties, at-rules, value types, selectors, prose, anchor URLs. **123 files**. |
| `idl/*.idl`  | `ed/idl/{css-*,cssom*,web-animations*,font-*,geometry*}.idl` | Web IDL for the JS-visible CSS surface (CSSOM, CSSOM View, Web Animations, Font Loading, etc.). **41 files**. |

## Refreshing

```bash
# from a temp dir
git clone --depth 1 --filter=blob:none --sparse https://github.com/w3c/webref.git
cd webref
git sparse-checkout set ed/css ed/idl
git rev-parse HEAD              # pin this commit in the README below

# copy into the repo
cp ed/css/*.json $REPO/testdata/webref/css/
cp ed/idl/{css-*,cssom*,web-animations*,font-*,geometry*}.idl $REPO/testdata/webref/idl/
```

Update the **Snapshot commit** line above when you refresh.

## Why commit it instead of fetching at build time?

- Reproducible builds — agents cloning the repo offline get the spec data.
- Pin the surface area we test against; spec drift is reviewed via PR diff.
- 2.4 MB CSS + IDL combined; small enough to ship in-tree.

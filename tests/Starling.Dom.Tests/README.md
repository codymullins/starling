# Starling.Dom.Tests

Tests for the Starling DOM in `src/Starling.Dom` — nodes, attributes,
events, observers, and the traversal helpers.

## What it covers

`HTMLCollection`, `NodeList`, `NodeIterator`, `TreeWalker`, event dispatch,
`MutationObserver`, and attribute reflection. Targeted tests that pin one
behavior at a time.

For an overall pass rate against the Web Platform Tests `dom/` set, see
[`Starling.Wpt.Tests`](../Starling.Wpt.Tests/README.md).

## How to run

```bash
dotnet test tests/Starling.Dom.Tests
```

## What the badge means

The common DOM is in. Ranges and a few older APIs are still gaps. As of
2026-05-24, Web Platform Tests scores: `dom/lists` 77%, `dom/historical`
61%, `dom/ranges` 0.5%.

Design: [`05_DOM.md`](../../browser-plan/05_DOM.md).

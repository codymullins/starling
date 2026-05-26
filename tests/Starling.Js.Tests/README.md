# Starling.Js.Tests

Direct tests for parts of the Starling JS engine — the lexer, parser,
bytecode compiler, virtual machine, built-ins, and garbage collector.

## What it covers

Small, targeted tests that pin one behavior at a time. They catch bugs
that would otherwise show up as confusing batches of Test262 failures.
Examples: `CatchBlockLexicalTests`, `JsonStringifyArrayTests`, and the
mapped-`arguments` aliasing cases.

For overall pass rate against the spec, see
[`Starling.Js.Test262.Tests`](../Starling.Js.Test262.Tests/README.md).

## How to run

```bash
dotnet test tests/Starling.Js.Tests
```

## What the badge means

New language work lands here with regression tests first, then shows up in
the Test262 number on the next run.

Design: [`09_JS_ENGINE.md`](../../browser-plan/09_JS_ENGINE.md).

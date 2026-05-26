# Starling.Js.Test262.Tests

Runs the official JavaScript spec tests ([Test262](https://github.com/tc39/test262))
against the Starling JS engine.

## What it covers

The `language/` part of Test262 — parsing, scoping, classes, async and
await, generators, completion values. The `built-ins/` and `intl402/` parts
are not in scope yet (see [`09_JS_ENGINE.md`](../../browser-plan/09_JS_ENGINE.md)).

A second runner does the same against [Jint](https://github.com/sebastienros/jint)
so we can compare numbers.

## How to run

```bash
tools/fetch-test262.sh                       # downloads the test corpus
dotnet test tests/Starling.Js.Test262.Tests
```

If the corpus is missing the tests skip, so the build stays green.

## What the badge means

About 95% of `language/` passes today. Targets are 80% at milestone 3, 95%
at milestone 7, and 98% at milestone 11 — see
[`12_TESTING.md`](../../browser-plan/12_TESTING.md). Open work is in
[`tasks/INDEX.md`](../../tasks/INDEX.md).

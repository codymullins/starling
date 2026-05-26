# Starling.Html.Tests

Tests for the HTML parser in `src/Starling.Html`.

## What it covers

The [html5lib](https://github.com/html5lib/html5lib-tests) test files — the
same ones major browsers use. There are two kinds:

- `tokenizer/*.test` — JSON files that check tokenizer state changes.
- `tree-construction/*.dat` — text files that check the parsed tree
  against an expected serialization.

## How to run

```bash
dotnet test tests/Starling.Html.Tests
```

## What the badge means

The html5lib tokenizer passes at the target rate (100% by milestone 2).
Tree construction is at parity for the parts real sites use.

Design: [`04_HTML_PARSING.md`](../../browser-plan/04_HTML_PARSING.md).

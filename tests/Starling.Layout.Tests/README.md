# Starling.Layout.Tests

Tests for `src/Starling.Layout`. They check sizes and positions on the box
tree, not the painted pixels (those live in
[`Starling.Paint.Tests`](../Starling.Paint.Tests/README.md)).

## What it covers

Block layout, inline and line-breaking, margin collapse, Flexbox, simple
tables, and part of CSS Grid.

## How to run

```bash
dotnet test tests/Starling.Layout.Tests
```

## What the badge means

Block, inline, margin collapse, and Flexbox are solid. Tables are minimal
and Grid is in progress. Targets from
[`12_TESTING.md`](../../browser-plan/12_TESTING.md): `css/css-flexbox` goes
from 30% at milestone 3 to 90% at milestone 11, and `css/css-grid` goes
from 0% at milestone 3 to 80% at milestone 11.

Design: [`07_LAYOUT.md`](../../browser-plan/07_LAYOUT.md).

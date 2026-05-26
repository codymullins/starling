# Starling.Spec.Common

Shared infrastructure for **CSS/web spec conformance tests**.

There is no stub generation. A conformance test exists only because
someone wrote a real assertion (or a `[PendingFact]` placeholder for a
known gap). Reference this
project from any `*.Tests` project that asserts spec behavior, then put the
test wherever it naturally belongs — `Starling.Layout.Tests`,
`Starling.Paint.Tests`, `Starling.Css.Tests`, etc. The `TestCategory~Spec:<id>`
filter spans every project.

## Attributes

| Attribute | When to use |
|---|---|
| `[Spec(id, url, section?)]` | On every conformance test class/method. Adds traits `Spec`, `SpecUrl`, `Section`. |
| `[SpecFact]` | A spec test that is expected to pass today. Adds `Status=Implemented`. |
| `[PendingFact(reason, trackingWp?)]` | A spec test that documents a requirement we don't satisfy yet. Skipped by default; adds `Status=Pending`. Give it a real assertion body so promotion is just swapping the attribute to `[SpecFact]`. |

## Filtering examples

```bash
# Run only tests that are expected to pass (gate CI on this).
dotnet test --filter "Status!=Pending"

# Run only one spec's tests, including pending.
dotnet test --filter "Spec=css-color-5"

# Force-run pending tests to detect ones that now pass.
STARLING_RUN_PENDING=true dotnet test --filter "Status=Pending"
```

## Test layout convention

CSS *parsing / cascade* conformance lives in `tests/Starling.Css.Spec.Tests/`
(it only references `Starling.Css`):

```
tests/Starling.Css.Spec.Tests/
└── <SpecId>/
    ├── _spec.md          # frontmatter: id, url, version, sections covered
    ├── <Topic>Tests.cs   # one class per spec section / feature
    └── ...
```

*Behavioral* conformance (layout, paint, rendering) lives in the test project
for that layer — e.g. percentage-height resolution is in
`Starling.Layout.Tests/LayoutEngineTests.cs`, tagged
`[Spec("css-sizing-3", …)]`. Put the test where the code it exercises is
tested; the `[Spec]` trait is what ties it back to the spec, not the folder.

See `tasks/SPEC_COVERAGE.md` for the live coverage matrix and
`tools/Starling.SpecGen/README.md` for the catalog generator.

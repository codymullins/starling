# Starling.Spec.Common

Shared infrastructure for **CSS/web spec conformance tests**.

## Attributes

| Attribute | When to use |
|---|---|
| `[Spec(id, url, section?)]` | On every conformance test class/method. Adds traits `Spec`, `SpecUrl`, `Section`. |
| `[SpecFact]` | A spec test that is expected to pass today. Adds `Status=Implemented`. |
| `[PendingFact(reason, trackingWp?)]` | A spec test that documents a requirement we don't satisfy yet. Skipped by default; adds `Status=Pending`. |

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

```
tests/Starling.<Family>.Spec.Tests/
└── <SpecId>/
    ├── _spec.md          # frontmatter: id, url, version, sections covered
    ├── <Topic>Tests.cs   # one class per spec section / feature group
    └── ...
```

See `tasks/SPEC_COVERAGE.md` for the live coverage matrix and
`tools/Starling.SpecGen/README.md` for the catalog/report generator.

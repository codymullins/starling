# Starling.Css.Spec.Tests

CSS spec **conformance** tests for the parsing/cascade/selector layer
(`Starling.Css`). Layout-affecting and paint-affecting tests live in
`Starling.Layout.Spec.Tests` and `Starling.Paint.Spec.Tests`.

Each subfolder corresponds to one CSS spec, named after its short id (e.g.
`CssColor5`, `CssBackgrounds3`). The `_spec.md` file in each folder is the
canonical record an agent reads first; the `.cs` files are executable
restatements of the spec's requirements.

Run the whole suite (default — pending tests are skipped, so this should
always be green):

```bash
dotnet test tests/Starling.Css.Spec.Tests
```

Run only one spec's tests:

```bash
dotnet test tests/Starling.Css.Spec.Tests --filter "Spec=css-color-5"
```

Probe pending tests (in a non-gating CI job):

```bash
STARLING_RUN_PENDING=true dotnet test tests/Starling.Css.Spec.Tests \
  --filter "Status=Pending"
```

See `tests/Starling.Spec.Common/README.md` for attribute reference and
`tasks/SPEC_COVERAGE.md` for the project-wide coverage matrix.

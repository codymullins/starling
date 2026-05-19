# Strictly-Ready Stub Inventory

Snapshot of `tests/Starling.Css.Spec.Tests/` `[PendingFact]` stubs that can
be promoted to `[SpecFact]` **with a strict assertion right now** — meaning
the assertion will verify spec-mandated *typed semantics*, not just that the
tokenizer accepts the declaration.

Strict bar:

> A stub may be promoted if the legacy `tests/Starling.Css.Tests/` suite
> (tagged `[Spec("<id>", "<url>")]`) contains a test that exercises the
> same property / selector / at-rule **with a concrete value** and asserts
> the resulting typed `CssValue` / `Selector` / `AtRule`. Promotion means
> writing the same typed assertion in the spec-tests stub.
>
> A stub may **not** be promoted on the basis of "the parser accepts it
> without throwing." That proves CSS Syntax 3 tokenization, not the spec
> the stub belongs to.

Last regenerated: 2026-05-19. Re-run the script at the bottom of this file
after every `generate-stubs`.

## Properties (28 strictly ready)

Evidence: there exists a string `"<property>: <value>"` inside a legacy test
class tagged with the spec; the legacy test asserts a typed result.

| Spec | Stub folder | Strict-ready properties |
|---|---|---|
| css-fonts-4         | `CssFonts/`         | `font-family`, `font-style`, `font-weight` (3) |
| css-cascade-5       | `CssCascade/`       | `all` (1) |
| css-animations-1    | `CssAnimations/`    | `animation`, `animation-duration`, `animation-name`, `animation-timing-function` (4) |
| css-sizing-4        | `CssSizing4/`       | `aspect-ratio` (1) |
| css-flexbox-1       | `CssFlexbox/`       | `flex`, `flex-direction`, `flex-flow` (3) |
| css-grid-2          | `CssGrid/`          | `grid-area`, `grid-auto-flow`, `grid-column`, `grid-row`, `grid-template-columns` (5) |
| css-logical-1       | `CssLogical/`       | `block-size`, `border-inline`, `border-inline-start`, `border-start-start-radius`, `inline-size`, `inset`, `inset-inline`, `margin-block`, plus 3 more (11) |
| **Total**           |                     | **28** |

Already promoted with strict assertions (`Should().BeEquivalentTo(new {...})`
on typed values):

| Spec | Folder | Promoted |
|---|---|---|
| css-color-4 | `CssColor/PropertyTests.cs` | `color`, `opacity` (2) |

## Selectors (21 strictly ready)

Evidence: the selector string appears verbatim in a legacy
`Selector*Tests.cs` / `ModernPseudoClassTests.cs` / `PseudoElementTests.cs`
class, which asserts a typed `Selector` from `SelectorParser.Parse(...)` and
in most cases a positive match against a fixture element.

| Spec | Stub folder | Strict-ready selectors |
|---|---|---|
| selectors-4 | `Selectors/SelectorTests.cs` | `:any-link`, `:autofill`, `:defined`, `:first-child`, `:fullscreen`, `:hover`, `:in-range`, `:invalid`, `:link`, `:modal`, `:only-child`, `:optional`, `:out-of-range`, `:picture-in-picture`, `:placeholder-shown`, … (21 total) |

## At-rules (7 strictly ready)

Evidence: a legacy `[Spec]`-tagged test parses CSS containing the `@-rule`
and asserts on the resulting `AtRule` node (name, prelude, body).

| Spec | Stub folder | Strict-ready at-rules |
|---|---|---|
| css-animations-1   | `CssAnimations/AtRuleTests.cs`   | `@keyframes` (1) |
| css-fonts-4        | `CssFonts/AtRuleTests.cs`        | `@font-face` (1) |
| css-cascade-5      | `CssCascade/AtRuleTests.cs`      | `@import`, `@layer` (2) |
| mediaqueries-5     | `Mediaqueries/AtRuleTests.cs`    | `@media` (1) |
| css-conditional-5  | `CssConditional/AtRuleTests.cs`  | `@media`, `@supports` (2) |

## Grand total

| Category | Count |
|---|---:|
| Properties | 28 |
| Selectors  | 21 |
| At-rules   |  7 |
| **Strict-ready stubs** | **56** |
| Already promoted (strict) |  2 |
| Pending (cannot be strictly promoted yet) | 1127 |
| **Total spec stubs** | **1185** |

## How to promote a stub strictly

1. Open the legacy test that proves the feature (e.g.
   `tests/Starling.Css.Tests/LogicalPropertyTests.cs` for `margin-inline-start`).
2. Note the typed assertion — usually `decl.Value.Should().Be(new CssLength(...))`
   or `.Should().BeEquivalentTo(new {...})` for records with extra fields.
3. In the corresponding stub
   (`tests/Starling.Css.Spec.Tests/<Folder>/PropertyTests.cs`), replace:
   ```csharp
   [PendingFact("property '<name>' not asserted yet", trackingWp: "...")]
   public void Parses_<name>() => throw new NotImplementedException();
   ```
   with the same typed assertion, tagged `[SpecFact]`. Template:
   `tests/Starling.Css.Spec.Tests/CssColor/PropertyTests.cs`.
4. Run `dotnet test tests/Starling.Css.Spec.Tests --filter "Spec=<spec-id>"`.
5. Re-generation is safe — the stub generator never overwrites existing files.

## Why the count is small

Most of the **1185 stubs** describe properties / selectors / at-rules whose
*existence* the engine handles (parser tokenizes them) but whose *spec
semantics* aren't yet covered by a legacy test. Without a legacy test to
mirror, writing a strict assertion would require new engine work —
typically:

- A typed value parser path (e.g. for grid `<track-list>` syntax).
- Cascade/style-engine wiring (e.g. logical-property → physical-property mapping
  varying by writing-mode, requiring a fixture element).
- Selector matching against a fixture DOM (for selectors with no legacy
  `SelectorMatcherTests` case).

Those promotions belong in the per-spec implementation work packages
(`wp:spec-<id>`), not in this inventory.

## Regenerating this report

```bash
cd /Users/cody/code/tessera
python3 - <<'PY'
import re, pathlib
LEGACY = pathlib.Path("tests/Starling.Css.Tests")
SPEC = pathlib.Path("tests/Starling.Css.Spec.Tests")
spec_tag_re = re.compile(r'\[Spec\("([^"]+)"')
decl_re = re.compile(r'([a-z][a-z0-9\-]+?)\s*:\s*[^"\';\n}]+')
proved = {}
for f in sorted(LEGACY.glob("*.cs")):
    src = f.read_text()
    m = spec_tag_re.search(src)
    if not m: continue
    bag = set()
    for a,b in re.findall(r'"([^"]*)"|"""(.*?)"""', src, re.DOTALL):
        for s in (a,b):
            bag |= {p for p in decl_re.findall(s) if len(p)>=3}
    proved.setdefault(m.group(1), set()).update(bag)
LEGACY_TO_WEBREF = {
    "css-fonts-4":"CssFonts","css-cascade-5":"CssCascade","css-animations-1":"CssAnimations",
    "css-sizing-4":"CssSizing4","css-flexbox-1":"CssFlexbox","css-grid-2":"CssGrid",
    "css-logical-1":"CssLogical","css-color-4":"CssColor","css-color-5":"CssColor5",
    "css-transforms-2":"CssTransforms2","css-images-4":"CssImages4","css-values-5":"CssValues5",
}
total = 0
for sid, folder in LEGACY_TO_WEBREF.items():
    fp = SPEC/folder/"PropertyTests.cs"
    if not fp.exists(): continue
    stubs = set(re.findall(r"property '([a-z\-]+)' not asserted yet", fp.read_text()))
    overlap = sorted(stubs & proved.get(sid,set()))
    if overlap:
        total += len(overlap)
        print(f"{sid:20} {folder:18} {len(overlap):>3}: {', '.join(overlap)}")
print(f"\nSTRICT properties: {total}")
PY
```

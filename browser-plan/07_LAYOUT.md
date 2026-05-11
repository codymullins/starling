# 07 — Layout

## Scope

**In:** Box tree construction, formatting contexts (block, inline, flex, grid, table), intrinsic sizing, line layout, position resolution, containing block, fragmentation (minimal), `getBoundingClientRect`.
**Out:** Multi-col layout (deferred), CSS regions (no), `position: sticky` correctness edge cases (M5+), subgrid (M6+), table layout perfection (M5+ — basic only).

## Spec refs

- [SPEC: CSS 2.2](https://www.w3.org/TR/CSS22/visuren.html) — block + inline base model (still the most-implemented reference)
- [SPEC: CSS Display 3](https://www.w3.org/TR/css-display-3/) — `display` values
- [SPEC: CSS Box Model 3](https://www.w3.org/TR/css-box-3/)
- [SPEC: CSS Sizing 3](https://www.w3.org/TR/css-sizing-3/) — intrinsic sizing
- [SPEC: CSS Position 3](https://www.w3.org/TR/css-position-3/)
- [SPEC: CSS Flexbox 1](https://www.w3.org/TR/css-flexbox-1/) — implement literally
- [SPEC: CSS Grid 2](https://www.w3.org/TR/css-grid-2/) — implement literally
- [SPEC: CSS Inline 3](https://www.w3.org/TR/css-inline-3/) — for vertical-align, baselines
- [SPEC: CSS Text 3](https://www.w3.org/TR/css-text-3/) — line breaking
- [SPEC: CSS Logical Properties](https://www.w3.org/TR/css-logical-1/) — writing modes (deferred default to ltr)

## Project layout

```
src/Tessera.Layout/
├── Tessera.Layout.csproj
├── ILayoutEngine.cs
├── LayoutEngine.cs                     # façade
├── Box/
│   ├── Box.cs                          # tree node
│   ├── BoxKind.cs
│   ├── BlockContainer.cs
│   ├── InlineContainer.cs
│   ├── InlineBox.cs / TextBox.cs / AtomicInlineBox.cs
│   ├── FlexContainer.cs / FlexItem.cs
│   ├── GridContainer.cs / GridItem.cs
│   ├── TableBox.cs (M5+)
│   └── AbsolutelyPositioned.cs
├── Tree/
│   ├── BoxTreeBuilder.cs               # DOM + styles -> box tree
│   ├── AnonymousBoxes.cs               # generated text wrappers
│   └── ReplacedElement.cs              # img, input, etc.
├── Block/
│   ├── BlockFormattingContext.cs
│   ├── MarginCollapse.cs
│   └── Floats.cs
├── Inline/
│   ├── InlineFormattingContext.cs
│   ├── LineBreaker.cs                  # Unicode line-breaking subset (UAX #14)
│   ├── LineBuilder.cs
│   └── Baseline.cs
├── Flex/
│   └── FlexLayout.cs                   # impl of Flexbox §9 (literal)
├── Grid/
│   ├── GridLayout.cs
│   ├── TrackSizing.cs
│   └── Placement.cs
├── Sizing/
│   ├── IntrinsicSizing.cs              # min-content, max-content, fit-content
│   └── BoxSizing.cs
└── Position/
    ├── AbsoluteLayout.cs
    └── FixedLayout.cs
```

## Public API

```csharp
public interface ILayoutEngine
{
    Box LayoutDocument(Document doc, Size viewport);
}

public sealed class LayoutEngine : ILayoutEngine
{
    public LayoutEngine(IStyleEngine style, ITextShaper textShaper);
}
```

## Box tree

Built from DOM + computed styles. **Not** a 1:1 mapping with elements — `display: contents` removes a box, `display: list-item` may generate marker box, `::before`/`::after` create boxes, anonymous block boxes wrap inline runs adjacent to blocks, etc.

```csharp
public abstract class Box
{
    public Element? Element { get; init; }   // null for anonymous boxes
    public ComputedStyle Style { get; init; }
    public Box? Parent { get; internal set; }
    public List<Box> Children { get; } = new();
    public Rect Frame { get; internal set; }   // in containing-block coords
    public Edges Margin, Padding, Border;
    public bool IsAnonymous { get; init; }
    public BoxKind Kind { get; init; }
}

public enum BoxKind {
    BlockContainer, InlineContainer, InlineBox, AtomicInline, TextBox,
    FlexContainer, FlexItem, GridContainer, GridItem,
    Table, TableRowGroup, TableRow, TableCell,
    Replaced, AbsolutelyPositioned
}

public readonly record struct Rect(double X, double Y, double Width, double Height);
public readonly record struct Edges(double Top, double Right, double Bottom, double Left);
public readonly record struct Size(double Width, double Height);
```

## Box tree construction

Per [SPEC: CSS Display 3 §2](https://www.w3.org/TR/css-display-3/#box-generation).

```
For each Element from the root, recurse:
  match Style.Display:
    "none"     -> skip subtree
    "contents" -> recurse children directly, no own box
    "block"    -> BlockContainer
    "inline"   -> InlineBox
    "inline-block" -> AtomicInlineBox containing a BlockContainer
    "flex"     -> FlexContainer
    "grid"     -> GridContainer
    "table"    -> TableBox (M5+)
    "list-item" -> BlockContainer with marker pseudo-box
    "inline-flex" / "inline-grid" -> AtomicInlineBox containing the relevant
```

After children attached, **fix up**:
- If a `BlockContainer` has mixed block/inline children: wrap consecutive inline children in anonymous BlockContainers (per [SPEC: CSS 2.2 §9.2.1.1](https://www.w3.org/TR/CSS22/visuren.html#anonymous-block-level)).
- If an `InlineContainer` has block-level descendants (rare; `<p><div>x</div></p>`): per the spec, split the inline into pieces.

### `::before` and `::after`

If `Element.ComputedStyle.Get(PropertyId.Content)` is not `none`, insert a generated box at the front/back of children.

### Replaced elements

`<img>`, `<canvas>`, `<video>`, `<iframe>`, `<input type=text>`, `<textarea>`, `<select>`, `<button>`. These have intrinsic dimensions and are leaves in the box tree.

```csharp
public sealed class ReplacedElement : Box
{
    public Size IntrinsicSize { get; init; }
    public double? IntrinsicAspectRatio { get; init; }
    public IReplacedContent Content { get; init; }   // image bytes, or form-control widget
}
```

## Layout pass

Single recursive pass per `LayoutDocument(viewport)`. For each box:
1. Determine **containing block**.
2. Resolve **width** (per box kind, formatting context).
3. Lay out **children**.
4. Resolve **height** (often based on children).
5. Position **absolutely-positioned descendants** in a post-pass.

### Containing block

- Root: viewport.
- `position: static|relative`: parent's content edge.
- `position: absolute`: nearest ancestor with `position != static`.
- `position: fixed`: viewport.

## Block formatting context (BFC)

Per [SPEC: CSS 2.2 §9.4.1](https://www.w3.org/TR/CSS22/visuren.html#block-formatting).

Algorithm:
```
LayoutBlockContainer(box, containingBlock):
  Resolve width per CSS 2.2 §10.3.3 (10 cases: normal flow, in-flow, etc.)
  y = 0
  for each child in box.Children:
    LayoutBlock(child, contentArea)
    child.Frame = (computedX, y, childWidth, childHeight)
    y += childHeight   # plus margin collapse
  height = autoHeight ? y : explicit
```

### Margin collapse

Per [SPEC: CSS 2.2 §8.3.1](https://www.w3.org/TR/CSS22/box.html#collapsing-margins).
- Adjacent block-level siblings' vertical margins collapse to max.
- Parent and first/last child can collapse if no separator (border/padding).
- Negative margins: combine by `pos + neg` (largest pos, smallest neg, then sum).
- BFC roots prevent collapse across the boundary.

Implementation: a `MarginAccumulator` struct flowing through `LayoutBlockContainer`.

### Floats

`float: left|right` per CSS 2.2 §9.5. Implement basic version:
- Floats are removed from in-flow and placed against the containing-block edge.
- Subsequent in-flow content wraps around (line boxes shorten).
- `clear: left|right|both` forces the box below the relevant floats.
- A `FloatBand` data structure tracks left/right occluded vertical strips.

Float-of-float, intersecting floats, very tricky cases: best-effort in v1. Most modern sites don't depend on this.

## Inline formatting context (IFC)

Per [SPEC: CSS 2.2 §9.4.2](https://www.w3.org/TR/CSS22/visuren.html#inline-formatting).

Algorithm:
```
LayoutInlineFormattingContext(container, contentWidth):
  shape all text runs at this point (call ITextShaper)
  produce list of "inline-level items": TextRun, InlineBox, AtomicInline
  feed items to LineBreaker which produces LineBoxes
  for each line box:
    align items per text-align/vertical-align/baseline rules
    set line height = max(font line-height, item heights)
```

### Line breaking

UAX #14 Line Breaking Algorithm. We need a subset:
- ASCII space → break opportunity.
- Hyphens `-`, U+2013, U+2014 → opportunity after.
- CJK character → break before/after most chars.
- `white-space` property modifies: `nowrap` disables, `pre` preserves, etc.

`SixLabors.Fonts` provides text measurement; we own the segmentation.

### Baseline alignment

Each glyph has ascent/descent from the font. Line box height = max(ascent) + max(descent) for items on the line. `vertical-align: middle|sub|super|top|bottom|baseline|<length>` adjusts per CSS 2.2 §10.8.

### Bidi

Per UAX #9. `SixLabors.Fonts` does the shaping; we own the BiDi paragraph algorithm or rely on `Fonts`' provided reordering helper. Confirm and revisit.

## Flex layout

Per [SPEC: CSS Flexbox §9](https://www.w3.org/TR/css-flexbox-1/#layout-algorithm). Implement the algorithm **literally**, in 9 numbered steps:

1. Generate anonymous flex items.
2. Determine the available main and cross size.
3. Determine the flex base size and hypothetical main size of each item.
4. Determine the main size of the flex container.
5. Collect flex items into flex lines.
6. Resolve flexible lengths (the `flex-grow` / `flex-shrink` distribution).
7. Determine the hypothetical cross size of each item.
8. Calculate the cross size of each flex line.
9. Handle 'align-content: stretch'.
10. Determine the used cross size of each flex item.
11. Main-axis alignment (`justify-content`).
12. Cross-axis alignment (`align-items`, `align-self`).
13. Resize and align the flex container.

Common bugs to avoid:
- `min-width: auto` on flex items defaults differently from block items.
- Aspect-ratio interactions.

## Grid layout

Per [SPEC: CSS Grid 2 §12](https://www.w3.org/TR/css-grid-2/#layout-algorithm).

Phases:
1. Resolve placement (item → grid area). Auto-placement per §8.
2. Resolve track sizes (the hard part — `fr`, `min-content`, `max-content`, `minmax()`).
3. Position items within tracks; apply alignment.

Track sizing algorithm details in [SPEC: Grid §12.5](https://www.w3.org/TR/css-grid-2/#algo-track-sizing). Long. Implement step by step.

## Position layout

### Relative

`position: relative` offsets paint after normal layout; doesn't affect siblings. Easy.

### Absolute / Fixed

After main pass, collect all absolutely-positioned boxes. For each:
- Find containing block.
- Resolve `top`/`right`/`bottom`/`left` and `width`/`height` per CSS 2.2 §10.3.7 (very many cases).
- Lay out as if its own BFC.

### Sticky

`position: sticky` — element behaves like `relative` until the user scrolls past a threshold, then it sticks. Implement in M5+ since it needs scroll integration with the shell.

## Intrinsic sizing

`min-content`, `max-content`, `fit-content`, `auto`. Per CSS Sizing 3.

For text: `min-content` = longest unbreakable run, `max-content` = sum of all glyph advances.
For block: walk children recursively.
For flex/grid: cached per item, multiple passes possible.

## Public-side data

```csharp
public sealed class LayoutResult
{
    public Box Root { get; init; }
    public Dictionary<Element, Box> ElementToBox { get; init; }
    public IReadOnlyList<Box> StackingContexts { get; init; }   // for paint
}
```

### `getBoundingClientRect` (CSSOM View)

```csharp
public static Rect GetBoundingClientRect(Element e, LayoutResult r);
```

Returns the union of all box fragments of `e` in viewport coordinates.

## Performance budget

For a 1k-element page (e.g. google.com static):
- Box tree construction: ≤ 5ms.
- Full layout: ≤ 20ms cold, ≤ 8ms incremental.
- 60fps means ≤ 16ms per frame total; incremental layout target ≤ 4ms.

Hot-path rules:
- Pool `Box` instances. Layout often rebuilds the tree.
- Cache shaped runs per `(font, style, text-hash)`.
- Skip layout for unchanged subtrees (use `Document.MutationVersion` per element).

## Stacking contexts

Determined by:
- Root element.
- `position != static` and `z-index != auto`.
- `opacity != 1`.
- `transform`, `filter`, `mix-blend-mode`, `isolation: isolate`.

Stacking contexts produce a paint order list. Built bottom-up after layout.

## Acceptance Tests

- [ ] CSS 2.2 reference rendering for the test suite at https://test.csswg.org/suites/css2.1/ (subset of 50 cases) — pass.
- [ ] WPT `css/css-flexbox/**` ≥ 90%.
- [ ] WPT `css/css-grid/**` ≥ 80%.
- [ ] Margin collapse: `<div style="margin:20px"><p style="margin:30px">x</p></div>` produces 30px before the `<div>` (max collapse).
- [ ] `position: absolute` with all four offsets resolves width correctly.
- [ ] `<img>` with intrinsic 200×100 and no width/height lays out at 200×100.
- [ ] `getBoundingClientRect` matches paint coordinates within ±0.5px.
- [ ] A 1k-element document lays out in ≤ 20ms cold on the CI runner.

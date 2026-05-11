# 06 — CSS

## Scope

**In:** Tokenizer, syntax parser, selector parser+matcher, property parsing for v1 subset, cascade, inheritance, computed values, used values, media queries (subset), @rules, custom properties.
**Out:** Animations/transitions (M6+), CSS Houdini (no), `@container` (deferred), CSS Nesting (M5+), color functions beyond v1 set (later).

## Spec refs

- [SPEC: CSS Syntax Level 3](https://www.w3.org/TR/css-syntax-3/) — tokenizer + parser
- [SPEC: CSS Cascade Level 5](https://www.w3.org/TR/css-cascade-5/) — cascade + inheritance
- [SPEC: Selectors Level 4](https://www.w3.org/TR/selectors-4/) — selector matching
- [SPEC: CSS Values Level 4](https://www.w3.org/TR/css-values-4/) — units, calc()
- [SPEC: CSS Conditional Level 5](https://www.w3.org/TR/css-conditional-5/) — @media, @supports
- [SPEC: CSSOM](https://drafts.csswg.org/cssom/) — JS-visible CSS object model
- [SPEC: CSSOM View](https://drafts.csswg.org/cssom-view/) — `getBoundingClientRect`, etc.

## Project layout

```
src/Tessera.Css/
├── Tessera.Css.csproj
├── Tokenizer/
│   ├── CssTokenizer.cs
│   └── CssToken.cs
├── Parser/
│   ├── CssParser.cs
│   ├── ComponentValueParser.cs
│   ├── RuleParser.cs
│   └── DeclarationParser.cs
├── Selectors/
│   ├── SelectorParser.cs
│   ├── Selector.cs               # discriminated union
│   ├── SelectorMatcher.cs
│   └── SelectorIndex.cs          # bucketed by id / class / tag
├── Values/
│   ├── Value.cs                  # discriminated union for all CSS values
│   ├── Length.cs / Color.cs / Image.cs / Calc.cs
│   ├── UnitConverter.cs
│   └── Initial.cs                # the spec's "initial value" per property
├── Properties/
│   ├── PropertyId.cs             # one enum entry per supported property
│   ├── PropertyRegistry.cs       # parse + initial + inherits + shorthand resolver
│   ├── Shorthands.cs             # margin, padding, font, border, ...
│   └── CustomProperties.cs       # --x
├── Cascade/
│   ├── StyleEngine.cs            # façade
│   ├── ComputedStyle.cs          # the result; struct-of-arrays-ish
│   ├── Cascade.cs                # gather + sort + win
│   └── Inheritance.cs
├── Media/
│   ├── MediaQuery.cs
│   └── MediaQueryEvaluator.cs
├── UserAgent/
│   └── UaStyleSheet.cs           # the HTML5 UA defaults
└── StyleSheet.cs / Rule.cs / StyleRule.cs / MediaRule.cs / FontFaceRule.cs / ImportRule.cs
```

## Tokenizer

Per [SPEC: CSS Syntax §4](https://www.w3.org/TR/css-syntax-3/#tokenization). 22 token types.

```csharp
public enum CssTokenType : byte {
    Ident, Function, AtKeyword, Hash, String, BadString,
    Url, BadUrl, Delim, Number, Percentage, Dimension,
    Whitespace, Cdo, Cdc, Colon, Semicolon, Comma,
    LeftSquare, RightSquare, LeftParen, RightParen,
    LeftBrace, RightBrace, Eof,
}
```

Hot path: avoid `string` allocation; produce tokens as `(type, start, length)` slices into the source string. The source is kept alive in `StyleSheet.Source` for diagnostics.

## Parser

Three-layer per [SPEC: CSS Syntax §5](https://www.w3.org/TR/css-syntax-3/):

1. **Component values** — Group tokens into a tree (`{}`, `()`, `[]` brackets balanced).
2. **Declarations** and **AtRules** — `name: value;` declarations and `@name (prelude) { block }`.
3. **Rules** — Style rules with selector lists and declaration blocks.

Implement the algorithms literally:
- `consume a list of rules`
- `consume a qualified rule`
- `consume an at-rule`
- `consume a list of declarations`
- `consume a component value`
- `consume a simple block`
- `consume a function`

The result is a `StyleSheet`:

```csharp
public sealed class StyleSheet
{
    public Url BaseUrl { get; init; }
    public string Source { get; init; }
    public IReadOnlyList<Rule> Rules { get; init; }
    public Origin Origin { get; init; }   // UA | User | Author
    public bool IsImportant { get; init; }  // is this an !important sheet wholesale
}

public abstract record Rule;
public sealed record StyleRule(IReadOnlyList<Selector> Selectors,
                               IReadOnlyList<Declaration> Declarations) : Rule;
public sealed record MediaRule(MediaQuery Query, IReadOnlyList<Rule> Inner) : Rule;
public sealed record FontFaceRule(IReadOnlyList<Declaration> Declarations) : Rule;
public sealed record ImportRule(Url Url, MediaQuery? Query) : Rule;
public sealed record SupportsRule(SupportsCondition Cond, IReadOnlyList<Rule> Inner) : Rule;
public sealed record KeyframesRule(string Name, IReadOnlyList<Keyframe> Frames) : Rule;
```

## Selectors

Per [SPEC: Selectors Level 4](https://www.w3.org/TR/selectors-4/).

### Grammar

```
complex   := compound ( combinator compound )*
combinator:= ' ' | '>' | '+' | '~' | '||'
compound  := type? ( id | class | attr | pseudoclass | pseudoelement )*
```

### Selector AST

```csharp
public sealed record Selector(IReadOnlyList<ComplexSelector> Alternatives);
public sealed record ComplexSelector(IReadOnlyList<(CompoundSelector S, Combinator C)> Parts);
public sealed record CompoundSelector(IReadOnlyList<SimpleSelector> Simples);

public abstract record SimpleSelector;
public sealed record TypeSelector(string Local, string? Namespace)       : SimpleSelector;
public sealed record UniversalSelector                                    : SimpleSelector;
public sealed record IdSelector(string Id)                                : SimpleSelector;
public sealed record ClassSelector(string Class)                          : SimpleSelector;
public sealed record AttrSelector(string Name, AttrOp Op, string? Val,
                                  bool CaseInsensitive)                   : SimpleSelector;
public sealed record PseudoClassSelector(string Name, object? Arg)        : SimpleSelector;
public sealed record PseudoElementSelector(string Name)                   : SimpleSelector;
public enum Combinator { Descendant, Child, NextSibling, SubsequentSibling }
public enum AttrOp { Exists, Equals, Includes, DashMatch, Prefix, Suffix, Substring }
```

### Pseudo-classes (v1 subset)

```
:hover, :active, :focus, :focus-visible, :focus-within,
:checked, :disabled, :enabled, :required, :optional, :placeholder-shown,
:root, :empty, :target, :lang(...),
:first-child, :last-child, :only-child,
:first-of-type, :last-of-type, :only-of-type,
:nth-child(an+b), :nth-last-child(an+b), :nth-of-type(an+b), :nth-last-of-type(an+b),
:is(...), :where(...), :not(...), :has(...)        # :has is M5+
```

Pseudo-elements (v1):
```
::before, ::after, ::first-letter (M5+), ::first-line (M5+),
::placeholder, ::marker
```

### Matching

Implement right-to-left matching with index. For an element, iterate candidate rules from indexes:
- **Id index**: `Dictionary<string, List<Rule>>` for rules whose rightmost compound has an id selector.
- **Class index**: `Dictionary<string, List<Rule>>`.
- **Tag index**: `Dictionary<string, List<Rule>>`.
- **Universal bucket**: rules with universal/attr-only rightmost.

Match function:
```csharp
public bool Matches(Selector selector, Element element, MatchContext ctx);
```

For complex selectors, walk leftward through siblings/ancestors per spec.

Performance: matching one rule against one element is O(complexity). Total cost = O(rules × elements) without indexes; with indexes O(matched rules × elements) — practical websites: 5–50k rules, 1k–10k elements.

## Properties (v1 subset)

Implement these. Generate `PropertyId.cs` from this list.

### Layout-affecting

```
display, position, top/right/bottom/left, z-index,
float, clear,
width, min-width, max-width, height, min-height, max-height,
margin (+ top/right/bottom/left), padding (+ top/right/bottom/left),
box-sizing, overflow (+ x/y),
border (shorthand) — border-style, border-color, border-width, border-radius,
flex (shorthand) — flex-direction, flex-wrap, flex-grow, flex-shrink, flex-basis,
justify-content, align-items, align-self, align-content, gap, row-gap, column-gap,
grid (shorthand) — grid-template-columns, grid-template-rows, grid-template-areas,
grid-auto-columns, grid-auto-rows, grid-auto-flow,
grid-column, grid-row, grid-area, place-items, place-content, place-self,
order
```

### Visual

```
color, background (shorthand) — background-color, background-image, background-position,
background-size, background-repeat, background-attachment, background-clip,
opacity, visibility, mix-blend-mode,
border-image (deferred), box-shadow, text-shadow (M4+),
filter (basic: blur, drop-shadow only) (M5+)
```

### Text

```
font (shorthand) — font-family, font-size, font-style, font-weight,
font-variant, line-height,
letter-spacing, word-spacing, white-space, text-align, text-decoration,
text-transform, text-indent, vertical-align, direction, unicode-bidi
```

### Misc

```
cursor, pointer-events, user-select,
transition (M6+), animation (M6+),
transform (basic: translate/scale/rotate; matrix later) (M4+),
content (for ::before/::after)
```

### Custom properties

`--x` parses as a stored token stream. `var(--x, fallback)` substitutes at computed-value time. Per [SPEC: CSS Variables Level 1](https://www.w3.org/TR/css-variables-1/).

## Values

```csharp
public abstract record CssValue;
public sealed record Keyword(string Name)                    : CssValue;
public sealed record Number(double Value)                    : CssValue;
public sealed record Percentage(double Value)                : CssValue;
public sealed record Length(double Value, LengthUnit Unit)   : CssValue;
public sealed record Time(double Ms)                         : CssValue;
public sealed record Angle(double Deg)                       : CssValue;
public sealed record Resolution(double Dpi)                  : CssValue;
public sealed record Color(byte R, byte G, byte B, byte A)   : CssValue;   // sRGB
public sealed record String_(string Value)                   : CssValue;
public sealed record UrlValue(Url Url)                       : CssValue;
public sealed record FunctionValue(string Name, IReadOnlyList<CssValue> Args) : CssValue;
public sealed record CalcExpr(CalcNode Root)                 : CssValue;
public sealed record VarRef(string Name, IReadOnlyList<CssValue>? Fallback) : CssValue;

public enum LengthUnit { Px, Em, Rem, Vh, Vw, Vmin, Vmax, Pt, Pc, In, Cm, Mm, Ch, Ex, Q }
```

### Units

`px` is the canonical unit. Conversion tables:
- 1in = 96px, 1cm = 37.795px, 1mm = 3.78px, 1pt = 1.333px, 1pc = 16px.
- `em` resolves against the element's `font-size`.
- `rem` resolves against root.
- `vh`/`vw`: viewport.
- `ch`: width of `0` in the current font.
- `ex`: x-height.

### Colors

Parse `#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa`, `rgb()`, `rgba()`, `hsl()`, `hsla()`, named colors (148), `currentColor`, `transparent`.

Modern color: `color(display-p3 ...)`, `oklch()`, `oklab()` — store as `Color` after gamut-clamp to sRGB in v1. Wide-gamut paint is M7+.

### `calc()`

Parse to an expression tree of `+ - * /`. Compute at used-value time. Type checking per spec: only specific unit combinations are valid.

## Cascade

Per [SPEC: CSS Cascade Level 5](https://www.w3.org/TR/css-cascade-5/).

### Origin order (low to high)

1. UA origin.
2. UA `!important`.
3. User origin.
4. User `!important`.
5. Author origin.
6. Author `!important`.
7. Animations.
8. Transitions.

### Within an origin, order by

1. Specificity (per Selectors 4: count of IDs, classes/attrs/pseudos, types).
2. Tree order in the stylesheet.

### Computed values

`StyleEngine.Compute(Element)` returns a `ComputedStyle`.

```csharp
public sealed class ComputedStyle
{
    // Storage: a fixed-size array indexed by PropertyId
    private readonly object?[] _values;

    public T Get<T>(PropertyId p) where T : class;
    public Length GetLength(PropertyId p);
    public Color GetColor(PropertyId p);
    // ... typed accessors per property
}
```

Initial values per [SPEC: Values §6.1](https://www.w3.org/TR/css-values-4/#valdef-keyword-initial). Generate `Initial.cs` from a table.

Inheritance: for each property, look up whether it inherits (a static table). If yes and element has no own declaration, copy parent's computed value. If no, use initial.

## UA stylesheet

Bundled as a string constant at build time. Source: [WHATWG HTML §15](https://html.spec.whatwg.org/multipage/rendering.html). Generate `UaStyleSheet.cs` from the spec.

This is critical — block-level defaults for `<div>`, `<p>`, etc. come from here. Without UA styles, every element renders inline. Estimated 600 lines of CSS.

## Quirks mode

If `Document.Mode == Quirks`:
- `<body>`/`<table>` line-height defaults differ.
- Hashless hex colors in `bgcolor` etc. allowed.
- Image dimensions: `<table>` ignores `border` differently.

Implement as a flag passed into the style engine; alternate UA stylesheet selected. See [SPEC: Quirks Mode Standard](https://quirks.spec.whatwg.org/) for the full list — implement the **layout-affecting** ones only in v1; ignore the legacy table quirks except for those mentioned.

## Media queries

```csharp
public sealed record MediaQuery(
    string? MediaType,           // "screen" | "print" | "all" | null
    bool Not,
    IReadOnlyList<MediaFeature> Features);

public sealed record MediaFeature(string Name, MediaFeatureValue? Value);
```

v1 features: `width`/`min-width`/`max-width`, `height`/min/max, `aspect-ratio`, `orientation`, `resolution`, `hover`, `pointer`, `prefers-color-scheme`, `prefers-reduced-motion`.

Evaluator gets a `MediaContext` from the engine (viewport size, color scheme, etc.).

## CSSOM

[SPEC: CSSOM](https://drafts.csswg.org/cssom/). Surface:
- `Element.Style` (CSSStyleDeclaration) — read/write inline `style=""`.
- `Document.StyleSheets` — read-only list.
- `Window.GetComputedStyle(Element)`.
- `MatchMedia(query)` returning `MediaQueryList` with `addListener`.

`CSSStyleDeclaration.SetProperty('background-color', 'red')` mutates the style attribute and invalidates the cascade for that element.

## Invalidation

Maintain three buckets per `StyleEngine`:
- **Selector dependency map**: `class.foo` → set of rules. Mutations to `class` invalidate only matching rules' subtrees.
- **Attribute dependency map**: similar for `[type=submit]`.
- **Descendant invalidation**: a mutation in subtree X invalidates X's style if any rules use `>`, `+`, `~` near matched compound.

Cheap path: full re-cascade. Use this in v1, optimize later.

## Performance budget

- 5k rules × 1k elements: full cascade ≤ 20ms.
- Selector matching uses indexes (id/class/tag).
- `getComputedStyle` is O(properties) per call, no recomputation if cache valid.

## Public API summary

```csharp
public interface IStyleEngine
{
    StyleSheet Parse(string source, Url baseUrl, Origin origin);
    void AddStyleSheet(StyleSheet sheet);
    void RemoveStyleSheet(StyleSheet sheet);
    ComputedStyle Compute(Element element);
    void Invalidate(Element root);
}
```

## Acceptance Tests

- [ ] CSS Syntax Level 3 conformance: tokenizer matches reference Rust impl on a 100-fixture set.
- [ ] Selector matching: WPT `css/selectors/**` ≥ 95% (skip ones requiring features not in v1 subset).
- [ ] Cascade tests: WPT `css/css-cascade/**` ≥ 95%.
- [ ] `getComputedStyle(elem).getPropertyValue('color')` returns expected sRGB triplet for cascaded color.
- [ ] UA stylesheet renders `<p>` as block-level by default.
- [ ] `@media (max-width: 600px) { ... }` rules apply iff viewport ≤ 600px.
- [ ] Inline `style="..."` overrides author stylesheet but not `!important` author styles.
- [ ] `--x: 12px; width: var(--x)` resolves to 12px at used-value time.
- [ ] Mutating `class` invalidates downstream cascade and produces correct ComputedStyle within the same frame.

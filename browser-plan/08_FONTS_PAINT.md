# 08 — Fonts and Paint

## Scope

**In:** Display list IR, ImageSharp paint backend, font loading and shaping via SixLabors.Fonts, font fallback, image decoding (PNG/JPEG/GIF/WebP via ImageSharp), stacking context paint order, compositing.
**Out:** GPU acceleration (no managed GPU pipeline in v1), SVG paint (M5+, basic only), video (no), `<canvas>` rendering (M7+).

## Spec refs

- [SPEC: CSS Backgrounds 3](https://www.w3.org/TR/css-backgrounds-3/)
- [SPEC: CSS Color 4](https://www.w3.org/TR/css-color-4/)
- [SPEC: CSS Text Decor 3](https://www.w3.org/TR/css-text-decor-3/)
- [SPEC: CSS Transforms 1](https://www.w3.org/TR/css-transforms-1/)
- [SPEC: Compositing 1](https://www.w3.org/TR/compositing-1/) — `mix-blend-mode`, `isolation`

## Library facts (2026-05)

| Library | Version | Surface we use |
|---|---|---|
| `SixLabors.ImageSharp` | 3.1.12 | `Image<Rgba32>`, format codecs (PNG, JPEG, GIF, BMP, TGA, WebP, PBM, QOI) |
| `SixLabors.ImageSharp.Drawing` | 2.1.7 | `image.Mutate(ctx => ctx.Fill(...).DrawText(...))`, path drawing, clipping |
| `SixLabors.Fonts` | 2.1.3 | TTF/OTF/WOFF/WOFF2 loading; variable fonts; OpenType GSUB/GPOS shaping; BiDi |

All three are pure managed, MIT-licensed, no native deps.

### What SixLabors.Fonts shapes well

OpenType GSUB/GPOS, Latin/Greek/Cyrillic, BiDi (UAX #9), variable fonts. Per upstream README: "advanced text shaping for complex scripts (Indic, Myanmar, Universal Shaping Engine)".

### Gaps

- No Hebrew shaper (per upstream issue #214 — present at search time). Implement minimal RTL fallback if not landed by start of M2.
- Color fonts (COLRv1/SVG-in-OT): SixLabors supports COLRv0/v1; SVG-in-OT supported. Acceptable.
- Emoji color font: bundle Noto Color Emoji (COLRv1 build) to use without OS fallback.

## Project layout

```
src/Tessera.Paint/
├── Tessera.Paint.csproj
├── IPainter.cs
├── Painter.cs                          # façade
├── DisplayList/
│   ├── DisplayList.cs
│   ├── DisplayItem.cs                  # discriminated union
│   ├── DisplayListBuilder.cs           # walks box tree
│   └── StackingContextOrder.cs
├── Backend/
│   ├── IPaintBackend.cs
│   ├── ImageSharpBackend.cs            # the only backend in v1
│   ├── Transform2D.cs
│   └── ClipStack.cs
├── Text/
│   ├── ITextShaper.cs                  # bridges to SixLabors.Fonts
│   ├── ShapedRun.cs
│   ├── ShapedRunCache.cs
│   ├── FontResolver.cs                 # @font-face + system loader + bundled fallback
│   └── BundledFonts.cs
├── Images/
│   ├── ImageDecoder.cs                 # wraps ImageSharp formats
│   └── ImageCache.cs
└── Effects/
    ├── BoxShadow.cs
    ├── FilterChain.cs                  # blur, drop-shadow (M5+)
    └── Gradients.cs                    # linear-gradient, radial-gradient
```

## Public API

```csharp
public interface IPainter
{
    Image<Rgba32> Paint(LayoutResult layout, Size viewport);
}

public sealed class Painter : IPainter
{
    public Painter(ITextShaper shaper, IImageCache images);
}
```

## Display list

A flat list of paint operations in **paint order**. Decoupled from the box tree so we can serialize, cache, diff, or replay it.

```csharp
public abstract record DisplayItem;
public sealed record SaveLayer(double Opacity, BlendMode Blend, Rect? Bounds)             : DisplayItem;
public sealed record Restore                                                              : DisplayItem;
public sealed record PushTransform(Matrix3x2 Matrix)                                      : DisplayItem;
public sealed record PopTransform                                                         : DisplayItem;
public sealed record PushClip(ClipShape Shape)                                            : DisplayItem;
public sealed record PopClip                                                              : DisplayItem;
public sealed record FillRect(Rect Bounds, Brush Brush)                                   : DisplayItem;
public sealed record StrokeRect(Rect Bounds, Brush Brush, double Width)                   : DisplayItem;
public sealed record DrawRoundedRect(Rect Bounds, Corners Radii, Brush? Fill, Stroke? S)  : DisplayItem;
public sealed record DrawPath(Path2D Path, Brush? Fill, Stroke? S)                        : DisplayItem;
public sealed record DrawText(ShapedRun Run, Color Color, Vector2 Origin)                 : DisplayItem;
public sealed record DrawImage(IRasterImage Image, Rect SrcRect, Rect DstRect)            : DisplayItem;
public sealed record DrawBoxShadow(Rect Bounds, Color Color, Vector2 Offset, double Blur,
                                   double Spread, bool Inset)                             : DisplayItem;

public abstract record Brush;
public sealed record SolidBrush(Color C)                          : Brush;
public sealed record LinearGradientBrush(/* stops, angle, ... */) : Brush;
public sealed record RadialGradientBrush(/* ... */)               : Brush;
public sealed record ImageBrush(IRasterImage Img, TilingMode Tile, Rect Slice) : Brush;
```

### Builder

```csharp
public sealed class DisplayListBuilder
{
    public DisplayList Build(LayoutResult layout, Size viewport)
    {
        var dl = new DisplayList();
        foreach (var sc in layout.StackingContexts.OrderBy(ZIndex))
            BuildStackingContext(sc, dl);
        return dl;
    }
}
```

Within a stacking context, paint order per [SPEC: CSS 2.2 Appendix E](https://www.w3.org/TR/CSS22/zindex.html):
1. Background and borders of element forming the SC.
2. Child stacking contexts with negative z-index.
3. In-flow, non-inline-level, non-positioned descendants.
4. Non-positioned floats.
5. In-flow, inline-level, non-positioned descendants (text, inlines).
6. Child stacking contexts with z-index 0; positioned descendants with z-index 0.
7. Child stacking contexts with positive z-index.

## ImageSharp backend

`ImageSharpBackend` walks the display list and calls `image.Mutate(ctx => ...)`.

```csharp
public sealed class ImageSharpBackend : IPaintBackend
{
    public Image<Rgba32> Render(DisplayList dl, Size viewport)
    {
        var img = new Image<Rgba32>(
            (int)Math.Ceiling(viewport.Width),
            (int)Math.Ceiling(viewport.Height),
            new Rgba32(255, 255, 255, 255));

        img.Mutate(ctx =>
        {
            var state = new BackendState();
            foreach (var item in dl)
                Replay(ctx, state, item);
        });
        return img;
    }
}
```

`Replay` maps each `DisplayItem` to an ImageSharp `IImageProcessingContext` call:
- `FillRect` → `ctx.Fill(brush, new RectangleF(...))`.
- `DrawRoundedRect` → build an `IPath` via `RectangularPolygon` + corner clipping.
- `DrawText` → `ctx.DrawText(textOptions, run.Text, brush)` — but for already-shaped runs we use the low-level `DrawingOptions` + glyph path API (`Font.GetGlyphs` produces an `IGlyph` per shaped glyph; convert to `IPath`).
- `DrawImage` → `ctx.DrawImage(img, location, opacity)`.
- `PushClip` → `ctx.SetGraphicsOptions(...)` is too coarse; instead use the `ctx.Clip(...)` extension if present, else manually pre-multiply alpha within a temporary `Image<Rgba32>` and blit.

### Transform stack

ImageSharp lacks a stateful transform context. Implement a managed `Matrix3x2` stack and pre-transform geometry before passing to ImageSharp. For text, transform the glyph paths.

### SaveLayer / Restore

For `opacity != 1`, `mix-blend-mode`, and `filter`, render into a temp `Image<Rgba32>`, then blit with the requested opacity/blend.

```csharp
case SaveLayer s:
    _stack.Push(new Layer { Buffer = new Image<Rgba32>(viewportSize), Opacity = s.Opacity, Blend = s.Blend });
    break;
case Restore:
    var layer = _stack.Pop();
    ctx.DrawImage(layer.Buffer, _origin, MapBlend(layer.Blend), (float)layer.Opacity);
    layer.Buffer.Dispose();
    break;
```

### Performance note

ImageSharp is CPU-bound and slower than Skia. Acceptable trade-off for pure-managed. Hot paths:
- Pre-allocate the result `Image<Rgba32>` once per page; clear instead of recreate.
- Reuse `IPath` objects.
- Cache shaped runs.
- Consider parallel paint of independent stacking contexts.

## Text shaping

```csharp
public interface ITextShaper
{
    ShapedRun Shape(ReadOnlySpan<char> text, FontFace face, double fontSize,
                    BiDiDirection dir, FontFeatures features);
}

public sealed class ShapedRun
{
    public string SourceText { get; init; }
    public FontFace Face { get; init; }
    public double FontSize { get; init; }
    public IReadOnlyList<ShapedGlyph> Glyphs { get; init; }
    public double Advance { get; init; }   // total horizontal advance
    public double Ascent  { get; init; }
    public double Descent { get; init; }
}

public readonly record struct ShapedGlyph(
    ushort GlyphId, double XAdvance, double YAdvance,
    double XOffset, double YOffset, int Cluster);
```

### Implementation against SixLabors.Fonts

```csharp
public sealed class SixLaborsTextShaper : ITextShaper
{
    public ShapedRun Shape(/* args */)
    {
        var font = _face.ToSixLaborsFont(fontSize);
        var options = new TextOptions(font)
        {
            TextDirection   = dir == BiDiDirection.Rtl ? TextDirection.RightToLeft : TextDirection.LeftToRight,
            FeaturesEnabled = features.ToTags(),
        };
        var glyphs = new List<ShapedGlyph>();
        var enumerator = new TextLayoutEngine(font, options).Layout(text);
        // ... convert layout output to ShapedGlyph entries
        return new ShapedRun { /* ... */ };
    }
}
```

(SixLabors.Fonts' public API has moved across versions; consult `SixLabors.Fonts/src/SixLabors.Fonts/TextLayout.cs` in the pinned 2.1.3 tag for the exact entry points.)

### Caching

Cache `(fontFace, fontSize, text, features)` → `ShapedRun`. LRU, 16MB cap. Massive win on static pages.

## Font resolution

```csharp
public sealed class FontResolver
{
    public FontFace Resolve(IReadOnlyList<string> family, FontWeight w, FontStyle s);
    public void RegisterFontFace(FontFaceDescriptor desc, byte[] bytes);
}
```

### Source order

1. `@font-face`-declared faces (loaded from `url(...)` via the network stack and decoded with SixLabors.Fonts).
2. **Bundled fonts** (ship in `Tessera.Paint`):
   - Inter — sans-serif (matches Avalonia.Fonts.Inter).
   - JetBrains Mono — monospace.
   - Source Serif Pro — serif.
   - Noto Sans (subset) — Latin Extended, Cyrillic, Greek.
   - Noto Sans CJK SC — Chinese fallback.
   - Noto Sans Arabic — RTL fallback.
   - Noto Color Emoji — color emoji.
3. **System fonts** — discover via SixLabors.Fonts' `SystemFonts.Collection`. Cross-platform.
4. **Last-resort sans-serif** — bundled Inter at any weight, force-mapped.

Bundle binary fonts as `.ttf` (or `.woff2`) in `Tessera.Paint/Resources/Fonts/`. Total ~25MB; acceptable.

### Generic family map

| Family | Bundled face |
|---|---|
| `sans-serif` | Inter Variable |
| `serif` | Source Serif 4 Variable |
| `monospace` | JetBrains Mono |
| `cursive` | (fallback to serif) |
| `fantasy` | (fallback to serif) |
| `system-ui` | Inter |

### Font matching

Per [SPEC: CSS Fonts 4 §5](https://www.w3.org/TR/css-fonts-4/#font-matching-algorithm):
1. Take the first available family in the `font-family` list.
2. Within the family, choose the face whose weight is closest.
3. If a glyph is missing, fall back to the next family.

Missing-glyph fallback runs **per character cluster** during shaping. Substitute glyphs from the fallback face.

## Images

```csharp
public interface IRasterImage
{
    int Width { get; }
    int Height { get; }
    Image<Rgba32> AsImageSharp();
}
```

Decoding via `Image.Load<Rgba32>(stream)`. Formats supported by ImageSharp 3.1: PNG, JPEG, GIF (animated frames), BMP, TGA, WebP, PBM, QOI. **No SVG decoder** in ImageSharp — for SVG images:

OUT-OF-SCOPE-V1 fully; M5+ minimal SVG (path, rect, circle, fill/stroke, text) implemented in `Tessera.Paint/Svg/` and rasterized into an `Image<Rgba32>`.

Animated GIFs: render the first frame in v1. Animation in M6+.

### Image cache

`(url, decodedFormat)` → `Image<Rgba32>` LRU, 256MB cap. Drop on tab close.

## Gradients

`linear-gradient`, `radial-gradient`, `conic-gradient`. ImageSharp.Drawing supports linear and radial brushes natively (`LinearGradientBrush`, `RadialGradientBrush`). Conic: rasterize manually into a small image and tile, or implement a custom `IBrush`.

## Box shadow

Outset:
1. Allocate temp buffer of size `bounds + 2 * (blur + spread)`.
2. Fill rounded rect of `bounds + spread` in shadow color.
3. Apply Gaussian blur (`GaussianBlurProcessor` in ImageSharp). Sigma = blur/2.
4. Composite onto main image at offset, clipped to **outside** the element's rounded rect (anti-clip via even-odd fill).

Inset: similar but clip to **inside**, fill the inverse, blur.

Performance note: shadows with large blur radii are expensive. Cache by `(bounds, color, blur, spread, inset, cornerRadii)`.

## Borders

Per [SPEC: CSS Backgrounds 3 §4](https://www.w3.org/TR/css-backgrounds-3/#borders).
- `solid`, `dotted`, `dashed`, `double`, `groove`, `ridge`, `inset`, `outset`, `hidden`, `none`.
- `border-radius` rounds corners; clip background and shadow accordingly.
- Border drawing: build path for the border ring (outer rect minus inner rect with rounded corners), fill per side color/style.

## Transforms

`transform: translate(...) rotate(...) scale(...)` builds a `Matrix3x2`. Push onto stack before painting subtree; pop after.

`transform-origin` shifts the matrix accordingly.

3D transforms (`rotateX`, `perspective`) — OUT-OF-SCOPE-V1; treat as 2D ignoring Z.

## Filter and backdrop-filter

`blur(...)`, `drop-shadow(...)` → ImageSharp processors. Others (`brightness`, `contrast`, `grayscale`, `invert`, `saturate`, `sepia`, `hue-rotate`): implement as pixel ops.

`backdrop-filter`: requires snapshotting the under-content. Costly. OUT-OF-SCOPE-V1.

## Color management

Working color space: sRGB throughout v1.
- Display-P3, OKLCH, etc., are parsed and gamut-clamped into sRGB.
- Pre-multiplied alpha internally; ImageSharp expects non-premultiplied `Rgba32` for results. Convert at compositing boundaries.

## Compositing layers

Future optimization for damage tracking. v1: full repaint every frame on any invalidation. Damage rectangles in M5+.

## Acceptance Tests

- [ ] `Tessera.Headless render testdata/fonts/sample.html -o out.png` produces a PNG where text is anti-aliased and kerned (visual diff against golden).
- [ ] `border-radius` with `box-shadow` produces a recognizable rounded shadow (golden image).
- [ ] `linear-gradient(90deg, red, blue)` paints a horizontal gradient.
- [ ] PNG, JPEG, GIF, WebP `<img src>` all load and paint at intrinsic size.
- [ ] CJK text (`<p>こんにちは</p>`) renders with Noto Sans CJK without tofu glyphs.
- [ ] Emoji `<p>👋</p>` renders in color.
- [ ] RTL text (`<p dir=rtl>שלום</p>`) renders right-to-left with correct glyph order.
- [ ] Painting a 1920×1080 page with ~500 boxes completes in ≤ 50ms on the CI runner.
- [ ] No P/Invoke and no Skia references: `grep -rn 'DllImport\|LibraryImport\|Skia' src/Tessera.Paint/` is empty.

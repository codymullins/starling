# 11 — Avalonia Shell

## Scope

**In:** Hosting the engine's painted surface in an Avalonia window, URL bar, tab control, back/forward/reload, history menu, downloads UI, settings, keyboard/mouse routing into the engine, devtools panel skeleton.
**Out:** Native chrome theming per OS (use Avalonia defaults), tabs-as-windows, OS-level autofill integration.

## Stack

| Component | Version | Notes |
|---|---|---|
| Avalonia | 12.0.x | Stable since Apr 2026; targets .NET 10 directly. Recommended platform per upstream. |
| Avalonia.Desktop | 12.0.x | Windows/macOS/Linux backends |
| Avalonia.Themes.Fluent | 12.0.x | Default look |
| Avalonia.Fonts.Inter | 12.0.x | Bundles Inter |
| Avalonia.ReactiveUI | 12.0.x | Optional; we use it for binding the URL bar |

Pin to **12.0.3** (current as of plan date) via [`Directory.Packages.props`](02_PROJECT_SETUP.md#directorypackagesprops-central-package-management). Why 12 and not 11.12:

- Avalonia 12 targets .NET 10 directly and drops .NET Framework / netstandard, simplifying the build matrix.
- Rendering pipeline rewrite (deferred composition, dirty-rect tracking) reports up to ~19× FPS on complex layouts upstream. We benefit on the `EngineSurface` blit path.
- Native dispatcher implementation removes a class of timing bugs around `Dispatcher.UIThread.Post`.
- Page-based navigation primitive is useful later if we ever ship a mobile shell.

Spec source: [Avalonia 12 release blog](https://avaloniaui.net/blog/avalonia-12/), [breaking changes doc](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes).

## Migration notes (vs Avalonia 11)

These are the breaking changes that touch our code paths. Any agent writing new shell code or porting a snippet they found elsewhere should consult this list:

| Area | v11 | v12 (this plan) |
|---|---|---|
| TFMs | netstandard2.0 / net6+ | **.NET 8+ only**; we use net10.0 |
| Window decorations | `TitleBar`, `CaptionButtons`, `ChromeOverlayLayer`; `Window.ExtendClientAreaChromeHints` | Single `WindowDrawnDecorations` class; use `WindowDecorations` property with `ExtendClientAreaToDecorationsHint`. Affects custom chrome only — our default chrome is unaffected. |
| Focus events | `GotFocusEventArgs` on `GotFocus`/`LostFocus` | `FocusChangedEventArgs` carrying `OldFocusedElement`, `NewFocusedElement`, `NavigationMethod`, `KeyModifiers`. Update `InputTranslator` accordingly. |
| Clipboard | `BinaryFormatter` used on Windows | `BinaryFormatter` removed. We **must** serialize explicitly. Plan: clipboard payloads are UTF-8 strings (text/HTML); for richer types, serialize via `System.Text.Json`. |
| Binding | Data-annotations validation on by default | Off by default. We opt in with `AppBuilder.WithDataAnnotationsValidation()` in `Program.cs` because the Settings form uses `[Required]`/`[Range]` on view models. |
| NUnit | NUnit 3 supported | NUnit 4 required if you adopt NUnit. We use xUnit v3 — no impact, but flagged for cross-agent awareness. |

Other changes (e.g., styled property generators, new compositor APIs) don't affect our v1 surface.

## Project layout

```
src/Tessera.Shell/
├── Tessera.Shell.csproj
├── Program.cs
├── App.axaml / App.axaml.cs
├── Views/
│   ├── MainWindow.axaml / .axaml.cs
│   ├── BrowserView.axaml / .cs      # one per tab
│   ├── UrlBar.axaml / .cs
│   ├── TabStrip.axaml / .cs
│   ├── DownloadsFlyout.axaml / .cs
│   ├── HistoryFlyout.axaml / .cs
│   ├── SettingsWindow.axaml / .cs
│   └── DevTools/
│       ├── DevToolsWindow.axaml / .cs
│       ├── ConsolePanel.axaml / .cs
│       ├── NetworkPanel.axaml / .cs
│       └── DomInspectorPanel.axaml / .cs
├── ViewModels/
│   ├── MainWindowVM.cs
│   ├── TabVM.cs
│   ├── UrlBarVM.cs
│   ├── DownloadsVM.cs
│   └── SettingsVM.cs
├── Engine/
│   ├── EngineHost.cs                # owns the Browser instance
│   ├── EngineSurface.cs             # the painted area Control
│   ├── EngineSurfaceBitmap.cs       # Avalonia Bitmap from Image<Rgba32>
│   └── InputTranslator.cs           # Avalonia events -> Engine InputEvent
└── Resources/
    └── Icons/
```

## Process model in v1

Single process. The shell **directly hosts** the engine in-proc. Multi-process IPC (see [01_ARCHITECTURE.md#multi-process-model](01_ARCHITECTURE.md#multi-process-model)) is deferred to M9.

## Top-level layout

```
+-----------------------------------------------------------+
|  [tabs strip]                              [...] [_][_][x]|
+-----------------------------------------------------------+
|  [<][>][↻] [https://example.com         ]  [ ⬇ ] [ ⋯ ]    |
+-----------------------------------------------------------+
|                                                           |
|                    [EngineSurface]                        |
|                                                           |
|                                                           |
+-----------------------------------------------------------+
| status: Loading example.com…    | progress: 67%           |
+-----------------------------------------------------------+
```

XAML (sketch):

```xml
<Window xmlns="https://github.com/avaloniaui">
  <DockPanel>
    <local:TabStrip DockPanel.Dock="Top"/>
    <local:UrlBar  DockPanel.Dock="Top"/>
    <StatusBar     DockPanel.Dock="Bottom"/>
    <ContentControl Content="{Binding ActiveTab.BrowserView}"/>
  </DockPanel>
</Window>
```

## EngineSurface — the rendered area

This is the **only non-trivial piece** of the shell. We render the engine's `Image<Rgba32>` into an Avalonia `Bitmap` and blit it into a `Control`.

### Strategy

```csharp
public sealed class EngineSurface : Control
{
    private WriteableBitmap? _bitmap;
    private readonly EngineHost _host;

    public EngineSurface(EngineHost host)
    {
        _host = host;
        _host.Browser.FrameReady += OnFrameReady;
        Focusable = true;
    }

    private void OnFrameReady(object? sender, FrameReadyArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            EnsureBitmap(e.Image.Width, e.Image.Height);
            using var fb = _bitmap!.Lock();
            CopyImageSharpToBitmap(e.Image, fb);
            InvalidateVisual();
        });
    }

    public override void Render(DrawingContext ctx)
    {
        if (_bitmap is not null)
            ctx.DrawImage(_bitmap, new Rect(Bounds.Size));
    }
}
```

### Why `WriteableBitmap`?

It exposes a CPU-accessible pixel buffer. We can pin and `Buffer.MemoryCopy` from ImageSharp's `IMemoryGroup<Rgba32>` into the Avalonia buffer in one shot.

ImageSharp's `Image<Rgba32>` exposes pixel rows via `DangerousGetSinglePixelMemory()` when contiguous (which it is by default). Convert byte order if necessary (Avalonia expects `Bgra8888` on most platforms — we either render ImageSharp as `Bgra32` or swap on copy).

### Resize

On `SizeChanged`, the surface updates `Page.ViewportSize` on the engine, triggering relayout. We debounce resize events to avoid relayout storms (50ms timer).

### High-DPI

Avalonia provides `RenderScaling`. We multiply viewport by it and render at native pixels. Engine paint produces a 2x bitmap on Retina. CSS pixels remain unscaled internally.

## Input translation

```csharp
public sealed class InputTranslator
{
    public InputEvent? Translate(PointerPressedEventArgs e) => new MouseDownEvent {
        X = e.GetPosition(_surface).X, Y = e.GetPosition(_surface).Y,
        Button = e.GetCurrentPoint(_surface).Properties.PointerUpdateKind,
        ModifierKeys = e.KeyModifiers,
    };
    // KeyDown/KeyUp/PointerMoved/PointerReleased/PointerWheelChanged
}
```

The engine receives `InputEvent`s and dispatches to the appropriate target (element under cursor). Implementation in `Tessera.Engine`.

Hit testing: the latest `LayoutResult` is kept in the engine. Hit-test by walking the box tree top-down per stacking context order. Result: `Element` (or null).

### Focus

Avalonia owns OS focus. The engine owns DOM focus state. Translation: pointer-press or `tab` keys move focus inside the engine; `Window.Activated`/`Deactivated` informs the engine to fire `focus`/`blur` on the DOM.

### Text input

Keyboard events go to the engine. IME composition (CJK) — v1 simplification: pass `TextInput` events directly to the focused input element. Full IME support is M6+.

### Selection

`Ctrl+C`/`Ctrl+V`/`Ctrl+X` — implement by translating to clipboard ops. Avalonia provides `Application.Current.Clipboard` (cross-platform managed surface). Text selection visuals: M5+.

## URL bar

`UrlBar` is a `TextBox` + buttons. On Enter: parse via `Tessera.Url.Url.Parse`. If parse fails, prepend `https://` and retry. If still fails, fall back to a search URL (configurable; default `https://duckduckgo.com/?q=<urlencoded>`).

### Autocomplete

History-based autocomplete via fuzzy match on hostname. M4+.

## Tabs

`TabStrip` holds `TabVM` items. Each `TabVM` owns one `BrowsingContext` → one `EngineSurface`.

Switching tabs swaps the active `EngineSurface` in the `ContentControl`. Inactive tabs continue to run JS but may skip paint (saves CPU).

New-tab page: a hardcoded `about:new-tab` page generated from a small markdown source. v2: dial-style speed-dial.

## History UI

Per `BrowsingContext`, an in-memory list of entries with thumbnails (captured at navigation completion). `HistoryFlyout` displays the last N entries. Full history persisted in `~/.tessera/history.sqlite` — but we can't depend on SQLite native libs. v1: persist a JSON-lines file: `history.jsonl`. JSON serialization via `System.Text.Json` (pure managed).

## Downloads

`DownloadsFlyout` lists active+completed downloads. A download is started when:
- The user clicks a link whose target response has `Content-Disposition: attachment` or a `Content-Type` we don't handle inline.
- The user uses the `Save As` action from a context menu.

`DownloadManager` lives in `Tessera.Engine` and uses `Tessera.Net` directly. Writes to the OS Downloads folder (`Environment.GetFolderPath(SpecialFolder.UserProfile) + "/Downloads"`). Notify the shell via events.

## Settings

`SettingsWindow` with categories: Appearance, Privacy, Network. Persists to `%LOCALAPPDATA%/Tessera/settings.json`.

Minimal options for v1:
- Homepage.
- Default search engine.
- Cookies: allow / block third-party / block all.
- Clear browsing data.

## Devtools (M7+)

```
+-------------------------------------------------------+
|  Elements  Console  Network  Sources  Performance     |
+-------------------------------------------------------+
| <DOM tree>           | <Styles | Computed | Box>      |
|                      |                                |
+----------------------+--------------------------------+
| <console output>                              [ > ]   |
+-------------------------------------------------------+
```

Implementation strategy: bind to engine's introspection APIs (already exposed via the `Document`/`StyleEngine`/`Painter` types). Render an interactive tree view.

Implements **agent-discoverable** APIs out of the gate — devtools is a thin viewer over data the engine already exposes for testing.

## Program.cs (entry)

```csharp
using Avalonia;
using Avalonia.ReactiveUI;
using Tessera.Shell;

namespace Tessera.Shell;
class Program
{
    public static int Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .UseReactiveUI()
            .WithDataAnnotationsValidation()   // Avalonia 12: opt back in for Settings VM
            .LogToTrace();
}
```

## Headless mode reuses the engine without the shell

`Tessera.Headless` (in [02_PROJECT_SETUP.md](02_PROJECT_SETUP.md#headless-cli-shape)) builds the same `Browser` but no Avalonia. Saves rendered PNGs to disk. This is what test/agent flows use.

## Cross-platform notes

- **Windows**: Avalonia uses Direct2D. No deps from us.
- **macOS**: Avalonia uses Metal. Bundle an `.app` via `dotnet publish -r osx-arm64 --self-contained false`.
- **Linux**: X11 + Wayland both supported by Avalonia. Font cache may differ; SixLabors.Fonts uses `SystemFonts.Collection` cross-platform.

We don't write any platform-specific code in `Tessera.Shell`. All differences absorbed by Avalonia.

## Packaging

| OS | Format |
|---|---|
| Windows | MSIX via `dotnet publish` + `MakeAppx` (managed packaging tools) |
| macOS | `.app` bundle + `.dmg`. Avalonia provides a template; no native code. |
| Linux | AppImage + .deb. Use the managed `Avalonia.AppImage` packaging story. |

All artifacts produced by `dotnet publish -c Release -r <rid> --self-contained` plus a small wrapper script.

## Performance

The shell adds ≤ 50MB RSS over the engine.

Frame rate target: 60 fps on a 1080p viewport for "moderate" pages (≤2k DOM nodes). Hit by:
- Avoiding unnecessary `InvalidateVisual`.
- Buffer-reuse for the Avalonia bitmap.
- Damage rectangles (M5+) to update only changed areas.

## Acceptance Tests

- [ ] Launch `Tessera.Shell` on Windows, macOS, Linux. Window opens, shows about:new-tab.
- [ ] Type `https://example.com` in URL bar, press Enter, the page renders within 1s on a wired connection.
- [ ] Multiple tabs: open 5; each is independent; switching is instant.
- [ ] Back/forward buttons navigate per per-tab history.
- [ ] Window resize triggers relayout; surface re-renders within 100ms after release.
- [ ] Keyboard input into a focused `<input>` produces text on the page.
- [ ] Mouse click on a `<button>` fires `click` event in JS and runs the handler.
- [ ] Right-click → "Save Image As" downloads the right file with the right MIME type.
- [ ] Settings persist across restarts.
- [ ] No process spawns native helpers: process tree shows only `Tessera.Shell` (and one `dotnet` for hosted assembly).

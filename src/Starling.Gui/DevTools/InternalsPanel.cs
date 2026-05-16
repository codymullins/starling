using Microsoft.Maui.Controls.Shapes;
using Tessera.Gui.Chrome;
using Tessera.Gui.Theme;

namespace Tessera.Gui.DevTools;

/// <summary>
/// The Internals panel — the engine debug surface no other browser gives you.
/// Port of <c>InternalPanel</c> in <c>design/devtools.jsx</c> and HANDOFF §5.4:
/// a module-chip toolbar over a 2×2 grid of cards (Parser, JS engine, GC, IPC).
/// </summary>
public sealed class InternalsPanel : Grid
{
    private static readonly string[] Modules =
        { "parser", "js", "style", "layout", "paint", "gc", "ipc", "sandbox" };

    public InternalsPanel(ThemeManager tm)
    {
        var t = tm.Tokens;

        BackgroundColor = t.Panel;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // module-chip toolbar
        RowDefinitions.Add(new RowDefinition(GridLength.Star)); // card grid

        this.Add(BuildToolbar(tm), 0, 0);
        this.Add(BuildCards(tm), 0, 1);
    }

    private static View BuildToolbar(ThemeManager tm)
    {
        var t = tm.Tokens;

        var chips = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        for (var i = 0; i < Modules.Length; i++)
        {
            var active = i == 0;
            chips.Add(new Border
            {
                BackgroundColor = active ? t.AccentBg : Colors.Transparent,
                Stroke = active ? t.AccentLine : Colors.Transparent,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = tm.Metrics.RPill },
                Padding = new Thickness(10, 3),
                Content = new Label
                {
                    Text = Modules[i],
                    FontFamily = tm.ChromeFont,
                    FontSize = tm.Metrics.FsXs,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = active ? t.Accent : t.Text2,
                },
            });
        }

        var hint = new Label
        {
            VerticalOptions = LayoutOptions.Center,
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = "step ", TextColor = t.Muted, FontFamily = tm.MonoFont, FontSize = tm.Metrics.FsXs },
                    new Span { Text = "F10", TextColor = t.Text, FontFamily = tm.MonoFont, FontSize = tm.Metrics.FsXs },
                    new Span { Text = " · break ", TextColor = t.Muted, FontFamily = tm.MonoFont, FontSize = tm.Metrics.FsXs },
                    new Span { Text = "F9", TextColor = t.Text, FontFamily = tm.MonoFont, FontSize = tm.Metrics.FsXs },
                },
            },
        };

        var grid = new Grid
        {
            HeightRequest = 36,
            Padding = new Thickness(tm.Metrics.PadSm, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = chips,
        }, 0, 0);
        grid.Add(hint, 1, 0);

        var hairline = new BoxView { Color = t.Border, HeightRequest = 1, VerticalOptions = LayoutOptions.End };
        Grid.SetColumnSpan(hairline, 2);
        grid.Add(hairline, 0, 0);
        return grid;
    }

    private static View BuildCards(ThemeManager tm)
    {
        var grid = new Grid
        {
            Padding = new Thickness(tm.Metrics.PadSm),
            ColumnSpacing = tm.Metrics.GapSm,
            RowSpacing = tm.Metrics.GapSm,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
        };
        grid.Add(ParserCard(tm), 0, 0);
        grid.Add(JsCard(tm), 1, 0);
        grid.Add(GcCard(tm), 0, 1);
        grid.Add(IpcCard(tm), 1, 1);
        return new ScrollView { Content = grid };
    }

    // ─── Card frame ────────────────────────────────────────────────────────

    private static Border Card(ThemeManager tm, string title, string badge, Color badgeColor, View body)
    {
        var t = tm.Tokens;

        var titleLabel = ChromeKit.Mono(tm, title.ToUpperInvariant(), tm.Metrics.FsXs, t.Muted);
        titleLabel.CharacterSpacing = 0.6;
        titleLabel.VerticalOptions = LayoutOptions.Center;

        var header = new Grid
        {
            HeightRequest = 28,
            Padding = new Thickness(10, 0),
            BackgroundColor = t.Panel,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        header.Add(titleLabel, 0, 0);
        header.Add(ChromeKit.Mono(tm, badge, tm.Metrics.FsXs, badgeColor), 1, 0);
        header.Add(new BoxView { Color = t.Border, HeightRequest = 1, VerticalOptions = LayoutOptions.End }, 0, 0);
        Grid.SetColumnSpan((BoxView)header.Children[^1], 2);

        var stack = new VerticalStackLayout
        {
            Children = { header, new ContentView { Padding = 10, Content = body } },
        };

        return new Border
        {
            BackgroundColor = t.Surface,
            Stroke = t.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = tm.Metrics.RMd },
            Content = stack,
        };
    }

    // ─── Parser ────────────────────────────────────────────────────────────

    private static View ParserCard(ThemeManager tm)
    {
        var t = tm.Tokens;
        Color StateColor(ParseState s) => s switch
        {
            ParseState.Parsed => t.Ok,
            ParseState.Parsing => t.Warn,
            ParseState.Fetching => t.CatNet,
            _ => t.Faint,
        };

        var tree = new VerticalStackLayout { Spacing = 2 };
        foreach (var node in SampleData.ParserTree)
        {
            var row = new HorizontalStackLayout
            {
                Spacing = 6,
                Padding = new Thickness(node.Depth * 12, 0, 0, 0),
                VerticalOptions = LayoutOptions.Center,
            };
            row.Add(ChromeKit.Dot(StateColor(node.State)));
            row.Add(ChromeKit.Mono(tm, $"<{node.Tag}>", tm.Metrics.FsXs, t.CatHtml));
            if (node.Text is not null)
                row.Add(ChromeKit.Mono(tm, $"\"{node.Text}\"", tm.Metrics.FsXs, t.Muted));
            if (node.Resource is not null)
                row.Add(ChromeKit.Mono(tm, node.Resource, tm.Metrics.FsXs, t.CatNet));
            tree.Add(row);
        }

        return Card(tm, "parser · html5", "412 tok · 87 node · 0 err", t.Ok, tree);
    }

    // ─── JS engine ─────────────────────────────────────────────────────────

    private static View JsCard(ThemeManager tm)
    {
        var t = tm.Tokens;

        var stack = new VerticalStackLayout { Spacing = 2 };
        stack.Add(ChromeKit.Mono(tm, "CALL STACK", tm.Metrics.FsXs, t.Muted));
        for (var i = 0; i < SampleData.CallStack.Count; i++)
        {
            var f = SampleData.CallStack[i];
            var color = f.Exception ? t.Err : i == 0 ? t.Text2 : t.Text2;
            var row = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition(12),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
            };
            row.Add(ChromeKit.Mono(tm, f.IsTop ? "▸" : string.Empty, tm.Metrics.FsXs, color), 0, 0);
            row.Add(ChromeKit.Mono(tm, f.Function, tm.Metrics.FsXs, color), 1, 0);
            row.Add(ChromeKit.Mono(tm, f.Source, tm.Metrics.FsXs, t.Muted), 2, 0);
            stack.Add(row);
        }

        var heap = new VerticalStackLayout { Spacing = 2 };
        heap.Add(ChromeKit.Mono(tm, "HEAP · 16.4 MB", tm.Metrics.FsXs, t.Muted));

        var bar = new Grid { HeightRequest = 8, BackgroundColor = t.Bg };
        var col = 0;
        var used = 0.0;
        foreach (var seg in SampleData.Heap)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(seg.Fraction, GridUnitType.Star)));
            bar.Add(new BoxView { Color = t[seg.Cat] }, col++, 0);
            used += seg.Fraction;
        }
        bar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(0.0001, 1 - used), GridUnitType.Star)));
        var barWrap = new Border
        {
            StrokeThickness = 0, Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            HeightRequest = 8, Content = bar, Margin = new Thickness(0, 2, 0, 6),
        };
        heap.Add(barWrap);

        foreach (var seg in SampleData.Heap)
        {
            heap.Add(new HorizontalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    new BoxView { Color = t[seg.Cat], WidthRequest = 8, HeightRequest = 8, VerticalOptions = LayoutOptions.Center },
                    ChromeKit.Mono(tm, $"{seg.Label} {seg.Kb:0.0}", 10, t.Muted),
                },
            });
        }

        var body = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
        };
        body.Add(stack, 0, 0);
        body.Add(heap, 1, 0);

        return Card(tm, "js engine · cinder", "heap 16.4 / 64 MB", t.Text2, body);
    }

    // ─── GC ────────────────────────────────────────────────────────────────

    private static View GcCard(ThemeManager tm)
    {
        var t = tm.Tokens;

        var bars = new GraphicsView
        {
            HeightRequest = 64,
            Drawable = new GcBarsDrawable
            {
                Events = SampleData.GcEvents,
                Tokens = t,
                MonoFontFamily = tm.MonoFont,
            },
        };

        View Metric(string label, string value)
        {
            var s = new VerticalStackLayout { Spacing = 1 };
            s.Add(ChromeKit.Mono(tm, label, tm.Metrics.FsXs, t.Muted));
            s.Add(ChromeKit.Mono(tm, value, tm.Metrics.FsXs, t.Text));
            return s;
        }

        var metrics = new Grid
        {
            ColumnSpacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
        };
        metrics.Add(Metric("young gen", "4.1 / 8 MB"), 0, 0);
        metrics.Add(Metric("old gen", "12.3 / 56 MB"), 1, 0);
        metrics.Add(Metric("next gc", "~2.4s"), 2, 0);

        var body = new VerticalStackLayout { Children = { bars, metrics } };
        return Card(tm, "garbage collector", "2 major · 5 minor · 38ms total", t.Warn, body);
    }

    // ─── IPC ───────────────────────────────────────────────────────────────

    private static View IpcCard(ThemeManager tm)
    {
        var t = tm.Tokens;
        var rows = new VerticalStackLayout { Spacing = 4 };

        for (var i = 0; i < SampleData.IpcChannels.Count; i++)
        {
            var c = SampleData.IpcChannels[i];
            var row = new Grid
            {
                ColumnSpacing = 6,
                VerticalOptions = LayoutOptions.Center,
                ColumnDefinitions =
                {
                    new ColumnDefinition(90),
                    new ColumnDefinition(14),
                    new ColumnDefinition(90),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
            };
            row.Add(ChromeKit.Mono(tm, c.From, tm.Metrics.FsXs, t.Text2), 0, 0);
            row.Add(ChromeKit.Mono(tm, "→", tm.Metrics.FsXs, t.Muted), 1, 0);
            row.Add(ChromeKit.Mono(tm, c.To, tm.Metrics.FsXs, t.Text2), 2, 0);
            row.Add(new GraphicsView
            {
                HeightRequest = 12,
                VerticalOptions = LayoutOptions.Center,
                Drawable = new SparklineDrawable { Seed = i, BarColor = t[c.Cat] },
            }, 3, 0);
            row.Add(ChromeKit.Mono(tm, c.Msgs.ToString(), tm.Metrics.FsXs, t.Muted), 4, 0);
            rows.Add(row);
        }

        return Card(tm, "ipc · 4 channels", "281 msgs · 99.8% ok", t.Ok, rows);
    }
}

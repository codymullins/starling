using Microsoft.Maui.Controls.Shapes;
using Tessera.Gui.Chrome;
using Tessera.Gui.Theme;

namespace Tessera.Gui.DevTools;

/// <summary>
/// The Performance panel — the DevTools hero. Port of <c>PerformancePanel</c>
/// in <c>design/devtools.jsx</c> and HANDOFF §5.2: a record toolbar, the frame
/// strip, a 50ms ruler with Web-Vitals markers, per-thread flame rows, and the
/// selected-event detail footer.
/// </summary>
public sealed class PerformancePanel : Grid
{
    private static readonly (Category Cat, string Label)[] Legend =
    {
        (Category.Html, "HTML"), (Category.Css, "CSS"), (Category.Js, "JS"),
        (Category.Layout, "Layout"), (Category.Paint, "Paint"),
        (Category.Gc, "GC"), (Category.Net, "Net"),
    };

    public PerformancePanel(ThemeManager tm)
    {
        var t = tm.Tokens;
        var sample = SampleData.Perf;

        BackgroundColor = t.Panel;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // toolbar
        RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // frames strip
        RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // ruler
        RowDefinitions.Add(new RowDefinition(GridLength.Star)); // threads
        RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // selected-event detail

        this.Add(BuildToolbar(tm, sample), 0, 0);
        this.Add(BuildFramesStrip(tm, sample), 0, 1);
        this.Add(BuildRuler(tm, sample), 0, 2);
        this.Add(BuildThreads(tm, sample), 0, 3);
        this.Add(BuildDetail(tm), 0, 4);
    }

    private static View BuildToolbar(ThemeManager tm, PerfSample sample)
    {
        var t = tm.Tokens;

        var rec = new Border
        {
            BackgroundColor = t.Err,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = tm.Metrics.RSm },
            Padding = new Thickness(8, 0),
            HeightRequest = 22,
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    ChromeKit.Dot(Colors.White, 8),
                    new Label
                    {
                        Text = "REC", FontFamily = tm.ChromeFont, FontSize = tm.Metrics.FsXs,
                        FontAttributes = FontAttributes.Bold, TextColor = Colors.White,
                        VerticalOptions = LayoutOptions.Center,
                    },
                },
            },
        };
        SemanticProperties.SetDescription(rec, "Record");

        var stat = new Label
        {
            VerticalOptions = LayoutOptions.Center,
            FormattedText = new FormattedString
            {
                Spans =
                {
                    Mono($"{sample.TotalMs:0}", t.Text, tm), Mono("ms · ", t.Muted, tm),
                    Mono($"{sample.Frames.Count}", t.Text, tm), Mono(" frames · ", t.Muted, tm),
                    Mono($"{sample.Frames.Count(f => f.Jank)}", t.Warn, tm), Mono(" jank", t.Muted, tm),
                },
            },
        };

        var legend = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        foreach (var (cat, label) in Legend)
        {
            var chip = new Border
            {
                BackgroundColor = t.Surface,
                Stroke = t.Border,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = tm.Metrics.RSm },
                Padding = new Thickness(6, 2),
                Content = new HorizontalStackLayout
                {
                    Spacing = 4,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new BoxView { Color = t[cat], WidthRequest = 8, HeightRequest = 8, CornerRadius = 2, VerticalOptions = LayoutOptions.Center },
                        ChromeKit.Mono(tm, label, tm.Metrics.FsXs, t.Muted),
                    },
                },
            };
            legend.Add(chip);
        }

        var grid = new Grid
        {
            HeightRequest = 36,
            Padding = new Thickness(tm.Metrics.PadSm, 0),
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.Add(rec, 0, 0);
        grid.Add(stat, 1, 0);
        grid.Add(legend, 3, 0);

        var hairline = new BoxView { Color = t.Border, HeightRequest = 1, VerticalOptions = LayoutOptions.End };
        Grid.SetColumnSpan(hairline, 4);
        grid.Add(hairline, 0, 0);
        return grid;
    }

    private static Span Mono(string text, Color color, ThemeManager tm)
        => new() { Text = text, TextColor = color, FontFamily = tm.MonoFont, FontSize = tm.Metrics.FsXs };

    private static View BuildFramesStrip(ThemeManager tm, PerfSample sample)
    {
        var t = tm.Tokens;
        var view = new GraphicsView
        {
            HeightRequest = 28,
            Drawable = new FramesStripDrawable
            {
                Frames = sample.Frames,
                Total = sample.TotalMs,
                Tokens = t,
                MonoFontFamily = tm.MonoFont,
            },
        };
        var grid = new Grid();
        grid.Add(view);
        grid.Add(new BoxView { Color = t.Border, HeightRequest = 1, VerticalOptions = LayoutOptions.End });
        return grid;
    }

    private static View BuildRuler(ThemeManager tm, PerfSample sample)
    {
        var t = tm.Tokens;
        var view = new GraphicsView
        {
            HeightRequest = 18,
            Drawable = new RulerDrawable
            {
                Total = sample.TotalMs,
                Markers = sample.Markers,
                Tokens = t,
                MonoFontFamily = tm.MonoFont,
            },
        };
        var grid = new Grid();
        grid.Add(view);
        grid.Add(new BoxView { Color = t.Border, HeightRequest = 1, VerticalOptions = LayoutOptions.End });
        return grid;
    }

    private static View BuildThreads(ThemeManager tm, PerfSample sample)
    {
        var t = tm.Tokens;
        var stack = new VerticalStackLayout { Padding = new Thickness(0, 6) };

        foreach (var thread in sample.Threads)
        {
            var header = new HorizontalStackLayout
            {
                Spacing = 8,
                Padding = new Thickness(10, 4, 10, 6),
                Children =
                {
                    ChromeKit.Mono(tm, thread.Name, tm.Metrics.FsXs, t.Text2),
                    ChromeKit.Mono(tm, "thread", tm.Metrics.FsXs, t.Faint),
                },
            };
            stack.Add(header);

            var rows = new VerticalStackLayout { Padding = new Thickness(10, 0), Spacing = 2 };
            foreach (var row in thread.Rows)
            {
                rows.Add(new GraphicsView
                {
                    HeightRequest = 18,
                    Drawable = new FlameRowDrawable
                    {
                        Bars = row,
                        Total = sample.TotalMs,
                        Tokens = t,
                        ShowLabels = true,
                        CornerRadius = 2f,
                        MonoFontFamily = tm.MonoFont,
                    },
                });
            }
            stack.Add(rows);
        }

        return new ScrollView { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Default };
    }

    private static View BuildDetail(ThemeManager tm)
    {
        var t = tm.Tokens;
        var fsXs = tm.Metrics.FsXs;
        var fsMd = tm.Metrics.FsMd;

        View Col(params View[] children)
        {
            var s = new VerticalStackLayout { Spacing = 2 };
            foreach (var c in children) s.Add(c);
            return s;
        }

        var selected = Col(
            ChromeKit.Mono(tm, "SELECTED · layout flow", fsXs, t.Muted),
            new Label
            {
                FontFamily = tm.MonoFont, FontSize = fsMd,
                FormattedText = new FormattedString
                {
                    Spans =
                    {
                        new Span { Text = "BlockFlow::layout(", TextColor = t.Text, FontFamily = tm.MonoFont, FontSize = fsMd },
                        new Span { Text = "root", TextColor = t.CatLayout, FontFamily = tm.MonoFont, FontSize = fsMd },
                        new Span { Text = ")", TextColor = t.Text, FontFamily = tm.MonoFont, FontSize = fsMd },
                    },
                },
            },
            ChromeKit.Mono(tm, "libstarling/layout/flow.cc:128", fsXs, t.Muted));

        var timing = Col(
            ChromeKit.Mono(tm, "TIMING", fsXs, t.Muted),
            ChromeKit.Mono(tm, "start 220.4ms", fsXs, t.Text),
            ChromeKit.Mono(tm, "self 64.1ms · total 64.1ms", fsXs, t.Text),
            ChromeKit.Mono(tm, "forced reflow (1×) from app.js:42", fsXs, t.Warn));

        var callTree = Col(
            ChromeKit.Mono(tm, "CALL TREE", fsXs, t.Muted),
            ChromeKit.Mono(tm, "↳ LayoutEngine::run()  2.1ms", fsXs, t.Text2),
            ChromeKit.Mono(tm, "  ↳ BlockFlow::layout  62.0ms", fsXs, t.Text2),
            ChromeKit.Mono(tm, "    ↳ InlineFormat::run  41.6ms", fsXs, t.Text2));

        var grid = new Grid
        {
            HeightRequest = 88,
            Padding = new Thickness(tm.Metrics.Pad, tm.Metrics.PadSm),
            ColumnSpacing = 16,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
        };
        grid.Add(selected, 0, 0);
        grid.Add(timing, 1, 0);
        grid.Add(callTree, 2, 0);

        var hairline = new BoxView { Color = t.Border, HeightRequest = 1, VerticalOptions = LayoutOptions.Start };
        Grid.SetColumnSpan(hairline, 3);
        grid.Add(hairline, 0, 0);
        return grid;
    }
}

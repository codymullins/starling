using Microsoft.Maui.Controls.Shapes;
using Tessera.Gui.Chrome;
using Tessera.Gui.Theme;

namespace Tessera.Gui.DevTools;

/// <summary>
/// The Console panel — port of <c>ConsolePanel</c> in <c>design/devtools.jsx</c>
/// and HANDOFF §5.3. A structured log table (not a freeform stream): fixed
/// columns, level row-tinting, level filter pills, and a prompt footer.
///
/// All columns are monospace, which inherently gives the tabular-numeral
/// column alignment HANDOFF §2.5 calls for — no separate <c>tnum</c> feature
/// toggle is needed (punch-list item 6).
/// </summary>
public sealed class ConsolePanel : Grid
{
    private readonly ThemeManager _tm;
    private readonly ContentView _toolbarHost;
    private readonly VerticalStackLayout _rows;
    private LogLevel? _filter;

    public ConsolePanel(ThemeManager tm)
    {
        _tm = tm;
        var t = tm.Tokens;

        BackgroundColor = t.Panel;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // toolbar
        RowDefinitions.Add(new RowDefinition(GridLength.Star)); // rows
        RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // prompt

        _toolbarHost = new ContentView { Content = BuildToolbar() };
        this.Add(_toolbarHost, 0, 0);

        _rows = new VerticalStackLayout();
        RebuildRows();
        this.Add(new ScrollView { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Default }, 0, 1);

        this.Add(BuildPrompt(), 0, 2);
    }

    private static Color LevelColor(ThemeTokens t, LogLevel level) => level switch
    {
        LogLevel.Error => t.Err,
        LogLevel.Warn => t.Warn,
        LogLevel.Info => t.Muted,
        LogLevel.Log => t.Text2,
        _ => t.CatCss,
    };

    private View BuildToolbar()
    {
        var t = _tm.Tokens;

        var pills = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        pills.Add(FilterPill(null, "all", t.Text, SampleData.Logs.Count));
        pills.Add(FilterPill(LogLevel.Error, "error", t.Err, Count(LogLevel.Error)));
        pills.Add(FilterPill(LogLevel.Warn, "warn", t.Warn, Count(LogLevel.Warn)));
        pills.Add(FilterPill(LogLevel.Info, "info", t.Muted, Count(LogLevel.Info)));
        pills.Add(FilterPill(LogLevel.Debug, "debug", t.CatCss, Count(LogLevel.Debug)));

        var filterInput = new Border
        {
            BackgroundColor = t.Surface,
            Stroke = t.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = _tm.Metrics.RSm },
            Padding = new Thickness(8, 0),
            HeightRequest = 22,
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    ChromeKit.Mono(_tm, "filter", _tm.Metrics.FsXs, t.Muted),
                    ChromeKit.Mono(_tm, "src:", _tm.Metrics.FsXs, t.Text),
                    ChromeKit.Mono(_tm, "layout", _tm.Metrics.FsXs, t.Accent),
                },
            },
        };

        var grid = new Grid
        {
            HeightRequest = 36,
            Padding = new Thickness(_tm.Metrics.PadSm, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.Add(pills, 0, 0);
        grid.Add(filterInput, 1, 0);

        var hairline = new BoxView { Color = t.Border, HeightRequest = 1, VerticalOptions = LayoutOptions.End };
        Grid.SetColumnSpan(hairline, 2);
        grid.Add(hairline, 0, 0);
        return grid;
    }

    private View FilterPill(LogLevel? level, string label, Color dotColor, int count)
    {
        var t = _tm.Tokens;
        var active = _filter == level;

        var content = new HorizontalStackLayout
        {
            Spacing = 5,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                ChromeKit.Dot(dotColor),
                ChromeKit.Mono(_tm, label, _tm.Metrics.FsXs, t.Text2),
                ChromeKit.Mono(_tm, count.ToString(), _tm.Metrics.FsXs, t.Faint),
            },
        };

        var pill = new Border
        {
            BackgroundColor = active ? t.Surface : Colors.Transparent,
            Stroke = active ? t.Border : Colors.Transparent,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = _tm.Metrics.RPill },
            Padding = new Thickness(8, 3),
            Content = content,
        };
        SemanticProperties.SetDescription(pill, $"Filter: {label}");

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _filter = level;
            _toolbarHost.Content = BuildToolbar();
            RebuildRows();
        };
        pill.GestureRecognizers.Add(tap);
        return pill;
    }

    private static int Count(LogLevel level)
    {
        var n = 0;
        foreach (var l in SampleData.Logs) if (l.Level == level) n++;
        return n;
    }

    private void RebuildRows()
    {
        _rows.Clear();
        foreach (var entry in SampleData.Logs)
        {
            if (_filter is { } f && entry.Level != f) continue;
            _rows.Add(LogRow(entry));
        }
    }

    private View LogRow(LogEntry entry)
    {
        var t = _tm.Tokens;
        var fsSm = _tm.Metrics.FsSm;
        var fsXs = _tm.Metrics.FsXs;

        var rowBg = entry.Level switch
        {
            LogLevel.Error => t.Err.WithAlpha(0.06f),
            LogLevel.Warn => t.Warn.WithAlpha(0.05f),
            _ => Colors.Transparent,
        };

        var grid = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(12, 4),
            BackgroundColor = rowBg,
            ColumnDefinitions =
            {
                new ColumnDefinition(76),
                new ColumnDefinition(64),
                new ColumnDefinition(64),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        grid.Add(ChromeKit.Mono(_tm, entry.Time, fsSm, t.Faint), 0, 0);
        grid.Add(ChromeKit.Mono(_tm, entry.Level.ToString().ToLowerInvariant(), fsSm, LevelColor(t, entry.Level)), 1, 0);
        grid.Add(ChromeKit.Mono(_tm, entry.Source, fsXs, t[entry.Cat]), 2, 0);

        var message = new Label
        {
            Text = entry.Message,
            FontFamily = _tm.MonoFont,
            FontSize = fsSm,
            TextColor = entry.IsObject ? t.CatCss : t.Text,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        grid.Add(message, 3, 0);

        if (entry.Tag is not null)
            grid.Add(ChromeKit.Mono(_tm, entry.Tag, fsXs, t.Muted), 4, 0);

        var hairline = new BoxView { Color = t.Border, HeightRequest = 1, VerticalOptions = LayoutOptions.End };
        Grid.SetColumnSpan(hairline, 5);
        grid.Add(hairline, 0, 0);
        return grid;
    }

    private View BuildPrompt()
    {
        var t = _tm.Tokens;

        var caret = new Label
        {
            Text = "›",
            FontFamily = _tm.MonoFont,
            FontSize = _tm.Metrics.FsSm,
            TextColor = t.Accent,
            VerticalOptions = LayoutOptions.Center,
        };
        var input = new Entry
        {
            Placeholder = "evaluate · ⇡ history · ⌥⇡ session",
            FontFamily = _tm.MonoFont,
            FontSize = _tm.Metrics.FsSm,
            TextColor = t.Text,
            PlaceholderColor = t.Muted,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
        };

        var row = new Grid
        {
            ColumnSpacing = 8,
            Padding = new Thickness(12, 0),
            VerticalOptions = LayoutOptions.Fill,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        row.Add(caret, 0, 0);
        row.Add(input, 1, 0);

        var grid = new Grid { HeightRequest = 30, BackgroundColor = t.Surface };
        grid.Add(new BoxView { Color = t.Border, HeightRequest = 1, VerticalOptions = LayoutOptions.Start });
        grid.Add(row);
        return grid;
    }
}

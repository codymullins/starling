using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Starling.Gui.Avalonia.Theme;

namespace Starling.Gui.Avalonia.Chrome;

/// <summary>
/// 220px vertical tab sidebar — Avalonia port of Starling.Gui's Chrome/Sidebar.cs.
/// Top to bottom: wordmark row, command-palette stub, BOOKMARKS/PINNED/TODAY
/// tab sections, and the build-pill footer.
/// </summary>
public sealed class Sidebar : Grid
{
    public const double WidthDip = 220;

    public Sidebar(
        ThemeManager tm,
        IReadOnlyList<TabInfo> bookmarks,
        string? activeId,
        Action<TabInfo>? onTabActivated = null)
    {
        var t = tm.Tokens;

        Width = WidthDip;
        Background = new SolidColorBrush(t.Bg);
        RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto");

        var wordmark = BuildWordmark(tm);
        Children.Add(wordmark); SetRow(wordmark, 0);

        var commandStub = BuildCommandStub(tm);
        Children.Add(commandStub); SetRow(commandStub, 1);

        var sections = BuildSections(tm, bookmarks, activeId, onTabActivated);
        Children.Add(sections); SetRow(sections, 2);

        var footer = BuildFooter(tm);
        Children.Add(footer); SetRow(footer, 3);

        // Right-edge hairline spans every row.
        var edge = new Rectangle
        {
            Fill = new SolidColorBrush(t.Border),
            Width = 1,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Children.Add(edge);
        SetRow(edge, 0); SetRowSpan(edge, 4);
    }

    private static Control BuildWordmark(ThemeManager tm)
    {
        var label = ChromeKit.Mono(tm, "starling", tm.Metrics.FsMd, tm.Tokens.Text, FontWeight.Bold);
        label.VerticalAlignment = VerticalAlignment.Center;
        return new Border
        {
            Height = 38,
            Padding = new Thickness(14, 0),
            Child = label,
        };
    }

    private static Control BuildCommandStub(ThemeManager tm)
    {
        var t = tm.Tokens;
        var row = new Grid
        {
            ColumnSpacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };
        var cmd = Icons.Make(Icons.Cmd, t.Muted, 11);
        row.Children.Add(cmd); SetColumn(cmd, 0);

        var label = ChromeKit.Mono(tm, "search · jump · run", tm.Metrics.FsXs, t.Muted);
        row.Children.Add(label); SetColumn(label, 1);

        var shortcut = ChromeKit.Mono(tm, "⌘K", tm.Metrics.FsXs, t.Muted);
        row.Children.Add(shortcut); SetColumn(shortcut, 2);

        var well = new Border
        {
            Margin = new Thickness(8, 0, 8, 8),
            Height = 28,
            Padding = new Thickness(8, 0),
            Background = new SolidColorBrush(t.Surface),
            BorderBrush = new SolidColorBrush(t.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(tm.Metrics.RSm),
            Child = row,
        };
        global::Avalonia.Automation.AutomationProperties.SetName(well, "Open command palette");
        return well;
    }

    private static Control BuildSections(
        ThemeManager tm,
        IReadOnlyList<TabInfo> bookmarks,
        string? activeId,
        Action<TabInfo>? onTabActivated)
    {
        var stack = new StackPanel { Spacing = 1 };
        if (bookmarks.Count > 0)
        {
            stack.Children.Add(SectionLabel(tm, "Bookmarks"));
            foreach (var tab in bookmarks)
                stack.Children.Add(TabRow(tm, tab, tab.Id == activeId, onTabActivated));
        }

        return new ScrollViewer
        {
            Content = stack,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
    }

    private static Control SectionLabel(ThemeManager tm, string text)
    {
        var label = ChromeKit.Mono(tm, text.ToUpperInvariant(), 10, tm.Tokens.Faint);
        // CharacterSpacing analogue in Avalonia is LetterSpacing on FormattedText —
        // TextBlock approximates the wider tracking via FontStretch/FontWeight; we
        // skip it here to match design proximity without invasive font work.
        return new Border
        {
            Padding = new Thickness(10, 8, 10, 4),
            Child = label,
        };
    }

    private static Control TabRow(ThemeManager tm, TabInfo tab, bool active, Action<TabInfo>? onTabActivated)
    {
        var t = tm.Tokens;

        var grid = new Grid
        {
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions = new ColumnDefinitions("2,Auto,*,Auto"),
        };

        if (active)
        {
            var rail = new Border
            {
                Width = 2,
                Background = new SolidColorBrush(t.Accent),
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(0, 6),
            };
            grid.Children.Add(rail); SetColumn(rail, 0);
        }

        Control icon = tab.Loading ? Spinner(t) : Favicon.Make(tm, tab.Host, 12);
        grid.Children.Add(icon); SetColumn(icon, 1);

        var title = ChromeKit.Sans(tm, tab.Title, tm.Metrics.FsSm,
            active ? t.Text : t.Text2);
        title.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(title); SetColumn(title, 2);

        if (tab.Audio)
        {
            var dot = ChromeKit.Dot(t.Accent);
            grid.Children.Add(dot); SetColumn(dot, 3);
        }

        var row = new Border
        {
            Height = tm.Metrics.RowSm,
            Margin = new Thickness(6, 0),
            Padding = new Thickness(10, 0),
            Background = new SolidColorBrush(active ? t.Surface : Colors.Transparent),
            CornerRadius = new CornerRadius(tm.Metrics.RSm),
            Child = grid,
        };
        global::Avalonia.Automation.AutomationProperties.SetName(row, $"Tab: {tab.Title}");

        if (!active)
        {
            ChromeKit.AttachHover(row,
                () => row.Background = new SolidColorBrush(t.Hover),
                () => row.Background = new SolidColorBrush(Colors.Transparent));
        }

        if (onTabActivated is not null && !string.IsNullOrWhiteSpace(tab.Url))
        {
            ChromeKit.AttachClick(row, () => onTabActivated(tab));
        }
        return row;
    }

    /// <summary>Static stand-in for a loading spinner — 1.5px accent ring.</summary>
    private static Control Spinner(ThemeTokens t) => new Ellipse
    {
        Width = 12,
        Height = 12,
        Stroke = new SolidColorBrush(t.Accent),
        StrokeThickness = 1.5,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Control BuildFooter(ThemeManager tm)
    {
        var pill = BuildPill.Make(tm, "M3", new[] { "flow layout", "async loader" });
        var content = new StackPanel
        {
            Margin = new Thickness(tm.Metrics.PadSm),
            Children = { pill },
        };

        var wrap = new Grid();
        var hairline = new Rectangle
        {
            Fill = new SolidColorBrush(tm.Tokens.Border),
            Height = 1,
            VerticalAlignment = VerticalAlignment.Top,
        };
        wrap.Children.Add(hairline);
        wrap.Children.Add(content);
        return wrap;
    }
}

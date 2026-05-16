using Microsoft.Maui.Controls.Shapes;
using Tessera.Gui.Theme;

namespace Tessera.Gui.Chrome;

/// <summary>
/// The 220px vertical tab sidebar — port of <c>TabStripB</c> in
/// <c>design/chrome.jsx</c> and HANDOFF §3.1. Top to bottom: wordmark row,
/// command-palette stub, PINNED and TODAY tab sections, and the build-pill
/// footer.
/// </summary>
public sealed class Sidebar : Grid
{
    public const double Width = 220;

    public Sidebar(
        ThemeManager tm,
        IReadOnlyList<TabInfo> bookmarks,
        IReadOnlyList<TabInfo> pinned,
        IReadOnlyList<TabInfo> today,
        string activeId,
        Action<TabInfo>? onTabActivated = null)
    {
        var t = tm.Tokens;

        WidthRequest = Width;
        BackgroundColor = t.Bg;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // wordmark
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // command palette stub
        RowDefinitions.Add(new RowDefinition(GridLength.Star));   // tab sections
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // build pill footer

        this.Add(BuildWordmark(tm), 0, 0);
        this.Add(BuildCommandStub(tm), 0, 1);
        this.Add(BuildSections(tm, bookmarks, pinned, today, activeId, onTabActivated), 0, 2);
        this.Add(BuildFooter(tm), 0, 3);

        // Right-edge hairline.
        var edge = new BoxView { Color = t.Border, WidthRequest = 1, HorizontalOptions = LayoutOptions.End };
        Grid.SetRowSpan(edge, 4);
        this.Add(edge, 0, 0);
    }

    private static View BuildWordmark(ThemeManager tm)
    {
        var label = ChromeKit.Mono(tm, "starling", tm.Metrics.FsMd, tm.Tokens.Text, FontAttributes.Bold);
        label.VerticalOptions = LayoutOptions.Center;
        return new ContentView
        {
            HeightRequest = 38,
            Padding = new Thickness(14, 0),
            Content = label,
        };
    }

    private static View BuildCommandStub(ThemeManager tm)
    {
        var t = tm.Tokens;
        var row = new Grid
        {
            ColumnSpacing = 6,
            VerticalOptions = LayoutOptions.Center,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(Icons.Make(Icons.Cmd, t.Muted, 11), 0, 0);
        row.Add(ChromeKit.Mono(tm, "search · jump · run", tm.Metrics.FsXs, t.Muted), 1, 0);
        row.Add(ChromeKit.Mono(tm, "⌘K", tm.Metrics.FsXs, t.Muted), 2, 0);

        var well = new Border
        {
            Margin = new Thickness(8, 0, 8, 8),
            HeightRequest = 28,
            Padding = new Thickness(8, 0),
            BackgroundColor = t.Surface,
            Stroke = t.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = tm.Metrics.RSm },
            Content = row,
        };
        SemanticProperties.SetDescription(well, "Open command palette");
        return well;
    }

    private static View BuildSections(
        ThemeManager tm,
        IReadOnlyList<TabInfo> bookmarks,
        IReadOnlyList<TabInfo> pinned,
        IReadOnlyList<TabInfo> today,
        string activeId,
        Action<TabInfo>? onTabActivated)
    {
        var stack = new VerticalStackLayout { Spacing = 1 };
        if (bookmarks.Count > 0)
        {
            stack.Add(SectionLabel(tm, "Bookmarks"));
            foreach (var tab in bookmarks) stack.Add(TabRow(tm, tab, tab.Id == activeId, onTabActivated));
        }
        stack.Add(SectionLabel(tm, "Pinned"));
        foreach (var tab in pinned) stack.Add(TabRow(tm, tab, tab.Id == activeId, onTabActivated));
        stack.Add(SectionLabel(tm, "Today"));
        foreach (var tab in today) stack.Add(TabRow(tm, tab, tab.Id == activeId, onTabActivated));

        return new ScrollView { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
    }

    private static View SectionLabel(ThemeManager tm, string text)
    {
        var label = ChromeKit.Mono(tm, text.ToUpperInvariant(), 10, tm.Tokens.Faint);
        label.CharacterSpacing = 0.6;
        return new ContentView { Padding = new Thickness(10, 8, 10, 4), Content = label };
    }

    private static View TabRow(ThemeManager tm, TabInfo tab, bool active, Action<TabInfo>? onTabActivated)
    {
        var t = tm.Tokens;

        var grid = new Grid
        {
            ColumnSpacing = 8,
            VerticalOptions = LayoutOptions.Center,
            ColumnDefinitions =
            {
                new ColumnDefinition(2),               // accent rail
                new ColumnDefinition(GridLength.Auto), // favicon / spinner
                new ColumnDefinition(GridLength.Star), // title
                new ColumnDefinition(GridLength.Auto), // audio dot
            },
        };

        if (active)
        {
            grid.Add(new Border
            {
                WidthRequest = 2,
                BackgroundColor = t.Accent,
                Stroke = Colors.Transparent,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 1 },
                Margin = new Thickness(0, 6),
            }, 0, 0);
        }

        grid.Add(tab.Loading ? Spinner(t) : Favicon.Make(tm, tab.Host, 12), 1, 0);

        var title = ChromeKit.Sans(tm, tab.Title, tm.Metrics.FsSm,
            active ? t.Text : t.Text2);
        title.VerticalOptions = LayoutOptions.Center;
        grid.Add(title, 2, 0);

        if (tab.Audio) grid.Add(ChromeKit.Dot(t.Accent), 3, 0);

        var row = new Border
        {
            HeightRequest = tm.Metrics.RowSm,
            Margin = new Thickness(6, 0),
            Padding = new Thickness(10, 0),
            BackgroundColor = active ? t.Surface : Colors.Transparent,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = tm.Metrics.RSm },
            Content = grid,
        };
        SemanticProperties.SetDescription(row, $"Tab: {tab.Title}");

        if (!active)
            ChromeKit.AttachHover(row,
                () => row.BackgroundColor = t.Hover,
                () => row.BackgroundColor = Colors.Transparent);

        if (onTabActivated is not null && !string.IsNullOrWhiteSpace(tab.Url))
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => onTabActivated(tab);
            row.GestureRecognizers.Add(tap);
        }
        return row;
    }

    /// <summary>A static stand-in for the loading spinner — a 1.5px accent ring.</summary>
    private static View Spinner(ThemeTokens t) => new Border
    {
        WidthRequest = 12,
        HeightRequest = 12,
        BackgroundColor = Colors.Transparent,
        Stroke = t.Accent,
        StrokeThickness = 1.5,
        StrokeShape = new Ellipse(),
        VerticalOptions = LayoutOptions.Center,
    };

    private static View BuildFooter(ThemeManager tm)
    {
        var content = new VerticalStackLayout
        {
            Padding = new Thickness(tm.Metrics.PadSm),
            Children = { BuildPill.Make(tm, "M3", new[] { "flow layout", "async loader" }) },
        };
        // Top hairline.
        var wrap = new Grid();
        wrap.Add(new BoxView { Color = tm.Tokens.Border, HeightRequest = 1, VerticalOptions = LayoutOptions.Start });
        wrap.Add(content);
        return wrap;
    }
}

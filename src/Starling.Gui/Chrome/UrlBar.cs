using Microsoft.Maui.Controls.Shapes;
using Tessera.Gui.Theme;

namespace Tessera.Gui.Chrome;

/// <summary>
/// The URL bar — port of <c>UrlBar</c> in <c>design/chrome.jsx</c> and HANDOFF
/// §3.3. One rounded well: a lock glyph, the editable address, the mini load
/// chart during load, and a find affordance.
///
/// The design canvas shows the URL as static tri-colour text (muted scheme +
/// path, full-colour host). A working browser needs an editable address, so the
/// text is hosted in a monospace <see cref="Entry"/> instead — the one
/// functional concession to the visual spec.
/// </summary>
public sealed class UrlBar : Border
{
    private readonly ThemeManager _tm;
    private readonly Grid _grid;
    private View? _loadChart;

    /// <summary>The editable address field — owned by the composition root.</summary>
    public Entry Address { get; }

    public event EventHandler? FindClicked;
    public event EventHandler? LockClicked;
    public event EventHandler? LoadChartClicked;

    public UrlBar(ThemeManager tm, bool secure = true)
    {
        _tm = tm;
        var t = tm.Tokens;

        var lockIcon = Icons.Make(secure ? Icons.Lock : Icons.Shield,
            secure ? t.Ok : t.Muted, 14);
        var lockWrap = new ContentView { Content = lockIcon, VerticalOptions = LayoutOptions.Center };
        SemanticProperties.SetDescription(lockWrap, secure ? "Secure connection" : "Connection not secure");
        var lockTap = new TapGestureRecognizer();
        lockTap.Tapped += (s, e) => LockClicked?.Invoke(this, EventArgs.Empty);
        lockWrap.GestureRecognizers.Add(lockTap);

        Address = new Entry
        {
            Placeholder = "https://example.com or file:///path/to/page.html",
            FontFamily = tm.MonoFont,
            FontSize = tm.Metrics.FsSm,
            TextColor = t.Text,
            PlaceholderColor = t.Muted,
            BackgroundColor = Colors.Transparent,
            ReturnType = ReturnType.Go,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            VerticalOptions = LayoutOptions.Center,
        };

        var findRow = new HorizontalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                Icons.Make(Icons.Find, t.Muted, 12),
                ChromeKit.Mono(tm, "find", tm.Metrics.FsXs, t.Muted),
            },
        };
        var findBtn = new Border
        {
            BackgroundColor = Colors.Transparent,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            Padding = new Thickness(8, 0),
            HeightRequest = 22,
            Content = findRow,
            VerticalOptions = LayoutOptions.Center,
        };
        SemanticProperties.SetDescription(findBtn, "Find in page");
        var findTap = new TapGestureRecognizer();
        findTap.Tapped += (s, e) => FindClicked?.Invoke(this, EventArgs.Empty);
        findBtn.GestureRecognizers.Add(findTap);

        _grid = new Grid
        {
            ColumnSpacing = tm.Metrics.GapSm,
            VerticalOptions = LayoutOptions.Center,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto), // lock
                new ColumnDefinition(GridLength.Star), // address
                new ColumnDefinition(GridLength.Auto), // load chart
                new ColumnDefinition(GridLength.Auto), // find
            },
        };
        _grid.Add(lockWrap, 0, 0);
        _grid.Add(Address, 1, 0);
        _grid.Add(findBtn, 3, 0);

        BackgroundColor = t.Surface;
        Stroke = t.Border;
        StrokeThickness = 1;
        StrokeShape = new RoundRectangle { CornerRadius = tm.Metrics.RMd };
        Padding = new Thickness(10, 0, 8, 0);
        HeightRequest = tm.Metrics.Row;
        HorizontalOptions = LayoutOptions.Fill;
        Content = _grid;
    }

    /// <summary>Shows the mini load chart inside the bar (HANDOFF §3.4).</summary>
    public void ShowLoadChart(IReadOnlyList<TimingBar> phases, double totalMs)
    {
        HideLoadChart();
        _loadChart = MiniLoadChart.Make(_tm, phases, totalMs,
            onClick: (s, e) => LoadChartClicked?.Invoke(this, EventArgs.Empty));
        _grid.Add(_loadChart, 2, 0);
    }

    /// <summary>Removes the mini load chart once the document is complete.</summary>
    public void HideLoadChart()
    {
        if (_loadChart is not null)
        {
            _grid.Remove(_loadChart);
            _loadChart = null;
        }
    }
}

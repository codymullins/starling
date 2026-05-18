using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Starling.Gui.Avalonia.Theme;

namespace Starling.Gui.Avalonia.Chrome;

/// <summary>
/// The URL bar — Avalonia port of Starling.Gui's Chrome/UrlBar.cs. One rounded
/// well: lock glyph, editable address, mini load chart during load, find
/// affordance.
/// </summary>
public sealed class UrlBar : Border
{
    private readonly ThemeManager _tm;
    private readonly Grid _grid;
    private Control? _loadChart;

    /// <summary>The editable address field.</summary>
    public TextBox Address { get; }

    public event EventHandler? FindClicked;
    public event EventHandler? LockClicked;
    public event EventHandler? LoadChartClicked;
    public event EventHandler? Submitted;

    public UrlBar(ThemeManager tm, bool secure = true)
    {
        _tm = tm;
        var t = tm.Tokens;

        var lockIcon = Icons.Make(secure ? Icons.Lock : Icons.Shield,
            secure ? t.Ok : t.Muted, 14);
        var lockWrap = new ContentControl { Content = lockIcon, VerticalAlignment = VerticalAlignment.Center };
        global::Avalonia.Automation.AutomationProperties.SetName(lockWrap, secure ? "Secure connection" : "Connection not secure");
        ChromeKit.AttachClick(lockWrap, () => LockClicked?.Invoke(this, EventArgs.Empty));

        Address = new TextBox
        {
            PlaceholderText = "https://example.com or file:///path/to/page.html",
            FontFamily = new FontFamily(tm.MonoFont),
            FontSize = tm.Metrics.FsSm,
            Foreground = new SolidColorBrush(t.Text),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Address.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Submitted?.Invoke(this, EventArgs.Empty);
            }
        };

        var findRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Icons.Make(Icons.Find, t.Muted, 12),
                ChromeKit.Mono(tm, "find", tm.Metrics.FsXs, t.Muted),
            },
        };
        var findBtn = new Border
        {
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 0),
            Height = 22,
            Child = findRow,
            VerticalAlignment = VerticalAlignment.Center,
        };
        global::Avalonia.Automation.AutomationProperties.SetName(findBtn, "Find in page");
        ChromeKit.AttachClick(findBtn, () => FindClicked?.Invoke(this, EventArgs.Empty));

        _grid = new Grid
        {
            ColumnSpacing = tm.Metrics.GapSm,
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
        };
        _grid.Children.Add(lockWrap); Grid.SetColumn(lockWrap, 0);
        _grid.Children.Add(Address); Grid.SetColumn(Address, 1);
        _grid.Children.Add(findBtn); Grid.SetColumn(findBtn, 3);

        Background = new SolidColorBrush(t.Surface);
        BorderBrush = new SolidColorBrush(t.Border);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(tm.Metrics.RMd);
        Padding = new Thickness(10, 0, 8, 0);
        Height = tm.Metrics.Row;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Child = _grid;
    }

    /// <summary>Shows the mini load chart inside the bar.</summary>
    public void ShowLoadChart(IReadOnlyList<TimingBar> phases, double totalMs)
    {
        HideLoadChart();
        _loadChart = MiniLoadChart.Make(_tm, phases, totalMs,
            onClick: () => LoadChartClicked?.Invoke(this, EventArgs.Empty));
        _grid.Children.Add(_loadChart);
        Grid.SetColumn(_loadChart, 2);
    }

    /// <summary>Removes the mini load chart once the document is complete.</summary>
    public void HideLoadChart()
    {
        if (_loadChart is not null)
        {
            _grid.Children.Remove(_loadChart);
            _loadChart = null;
        }
    }
}

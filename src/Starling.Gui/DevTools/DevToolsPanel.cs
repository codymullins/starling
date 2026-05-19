using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Starling.Gui.Chrome;
using Starling.Gui.Theme;
using Starling.Telemetry;

namespace Starling.Gui.DevTools;

/// <summary>
/// Docked DevTools shell — Avalonia port of Starling.Gui's DevTools/DevToolsPanel.cs.
/// Tab strip (Performance / Console / Internals / Inspect / Network) over a
/// switching body that hosts panels wired to <see cref="TelemetryStream"/>.
/// </summary>
public sealed class DevToolsPanel : Grid, IDisposable
{
    private readonly ThemeManager _tm;
    private readonly TelemetryStream _stream;
    private readonly ContentControl _body;
    private DevToolsTab _active;
    private IDisposable? _activePanel;

    public event EventHandler? CloseRequested;

    /// <summary>The currently selected tab — used to restore selection across theme rebuilds.</summary>
    public DevToolsTab ActiveTab => _active;

    public DevToolsPanel(ThemeManager tm, TelemetryStream stream, DevToolsTab active = DevToolsTab.Performance)
    {
        _tm = tm;
        _stream = stream;
        _active = active;
        var t = tm.Tokens;

        Background = new SolidColorBrush(t.Panel);
        RowDefinitions = new RowDefinitions("Auto,*");

        var strip = BuildTabStrip();
        Children.Add(strip); SetRow(strip, 0);

        _body = new ContentControl { Content = BuildBody(_active) };
        Children.Add(_body); SetRow(_body, 1);
    }

    private Control BuildTabStrip()
    {
        var t = _tm.Tokens;

        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
        };
        tabs.Children.Add(TabButton(DevToolsTab.Performance, Icons.Spark, "Performance"));
        tabs.Children.Add(TabButton(DevToolsTab.Console, Icons.Console, "Console"));
        tabs.Children.Add(TabButton(DevToolsTab.Internals, Icons.Cpu, "Internals"));
        tabs.Children.Add(TabButton(DevToolsTab.Inspect, Icons.Inspect, "Inspect", dim: true));
        tabs.Children.Add(TabButton(DevToolsTab.Network, Icons.Layers, "Net", dim: true));

        var close = new IconButton(_tm, Icons.Close, "Close DevTools", size: 26);
        close.Clicked += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

        var grid = new Grid
        {
            Height = 34,
            Margin = new Thickness(_tm.Metrics.PadSm, 0),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        grid.Children.Add(tabs); SetColumn(tabs, 0);
        grid.Children.Add(close); SetColumn(close, 1);

        return new Border
        {
            Background = new SolidColorBrush(t.Bg),
            BorderBrush = new SolidColorBrush(t.Border),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
    }

    private Control TabButton(DevToolsTab tab, string iconData, string label, bool dim = false)
    {
        var t = _tm.Tokens;
        var on = tab == _active;

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Icons.Make(iconData, on ? t.Accent : (dim ? t.Faint : t.Text2), 12),
                ChromeKit.Sans(_tm, label, _tm.Metrics.FsSm,
                    on ? t.Text : (dim ? t.Faint : t.Text2),
                    on ? FontWeight.Bold : FontWeight.Normal),
            },
        };

        var btn = new Border
        {
            Height = 26,
            Padding = new Thickness(12, 0),
            Background = new SolidColorBrush(on ? t.Panel : Colors.Transparent),
            CornerRadius = new CornerRadius(_tm.Metrics.RSm),
            Child = content,
        };
        global::Avalonia.Automation.AutomationProperties.SetName(btn, $"DevTools panel: {label}");

        ChromeKit.AttachClick(btn, () => SetActive(tab));
        return btn;
    }

    public void SetActive(DevToolsTab tab)
    {
        if (_active == tab) return;
        _active = tab;
        // Tear down the previous panel's subscription, then rebuild strip + body.
        _activePanel?.Dispose();
        _activePanel = null;

        Children.Clear();
        var strip = BuildTabStrip();
        Children.Add(strip); SetRow(strip, 0);
        _body.Content = BuildBody(_active);
        Children.Add(_body); SetRow(_body, 1);
    }

    private Control BuildBody(DevToolsTab tab)
    {
        Control panel;
        switch (tab)
        {
            case DevToolsTab.Performance:
                var perf = new PerformancePanel(_tm, _stream);
                _activePanel = perf;
                panel = perf;
                break;
            case DevToolsTab.Console:
                var console = new ConsolePanel(_tm, _stream);
                _activePanel = console;
                panel = console;
                break;
            case DevToolsTab.Internals:
                var internals = new InternalsPanel(_tm, _stream);
                _activePanel = internals;
                panel = internals;
                break;
            default:
                panel = Placeholder(tab);
                break;
        }
        return panel;
    }

    private Control Placeholder(DevToolsTab tab) => new Border
    {
        Background = new SolidColorBrush(_tm.Tokens.Panel),
        Child = ChromeKit.Sans(_tm, $"{tab} — not in this design pass", _tm.Metrics.FsSm, _tm.Tokens.Faint),
    };

    public void Dispose()
    {
        _activePanel?.Dispose();
    }
}

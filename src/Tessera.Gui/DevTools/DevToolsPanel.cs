using Microsoft.Maui.Controls.Shapes;
using Tessera.Gui.Chrome;
using Tessera.Gui.Theme;

namespace Tessera.Gui.DevTools;

public enum DevToolsTab { Performance, Console, Internals, Inspect, Network }

/// <summary>
/// The docked DevTools shell — port of the <c>DevTools</c> component in
/// <c>design/devtools.jsx</c> and HANDOFF §5.1. A tab strip with unread-count
/// badges and dock controls, over the body of the active panel.
/// </summary>
public sealed class DevToolsPanel : Grid
{
    private readonly ThemeManager _tm;
    private readonly ContentView _body;
    private DevToolsTab _active;

    public event EventHandler? CloseRequested;

    public DevToolsPanel(ThemeManager tm, DevToolsTab active = DevToolsTab.Performance)
    {
        _tm = tm;
        _active = active;
        var t = tm.Tokens;

        BackgroundColor = t.Panel;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // tab strip
        RowDefinitions.Add(new RowDefinition(GridLength.Star)); // body

        this.Add(BuildTabStrip(), 0, 0);

        _body = new ContentView { Content = BuildBody(_active) };
        this.Add(_body, 0, 1);
    }

    private View BuildTabStrip()
    {
        var t = _tm.Tokens;

        var strip = new Grid
        {
            HeightRequest = 34,
            Padding = new Thickness(_tm.Metrics.PadSm, 0),
            BackgroundColor = t.Bg,
            ColumnSpacing = 2,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star), // tabs
                new ColumnDefinition(GridLength.Auto), // dock controls
            },
        };

        var tabs = new HorizontalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        tabs.Add(TabButton(DevToolsTab.Performance, Icons.Spark, "Performance"));
        tabs.Add(TabButton(DevToolsTab.Console, Icons.Console, "Console", count: 2));
        tabs.Add(TabButton(DevToolsTab.Internals, Icons.Cpu, "Internals"));
        tabs.Add(TabButton(DevToolsTab.Inspect, Icons.Inspect, "Inspect", dim: true));
        tabs.Add(TabButton(DevToolsTab.Network, Icons.Layers, "Net", dim: true));
        strip.Add(tabs, 0, 0);

        var dock = new HorizontalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        dock.Add(new IconButton(_tm, Icons.PanelR, "Dock right", size: 26));
        dock.Add(new IconButton(_tm, Icons.Detach, "Detach", size: 26));
        var close = new IconButton(_tm, Icons.Close, "Close DevTools", size: 26);
        close.Clicked += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        dock.Add(close);
        strip.Add(dock, 1, 0);

        // Bottom hairline.
        var hairline = new BoxView { Color = t.Border, HeightRequest = 1, VerticalOptions = LayoutOptions.End };
        Grid.SetColumnSpan(hairline, 2);
        strip.Add(hairline, 0, 0);

        return strip;
    }

    private View TabButton(DevToolsTab tab, string iconData, string label, int count = 0, bool dim = false)
    {
        var t = _tm.Tokens;
        var on = tab == _active;

        var content = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
        };
        content.Add(Icons.Make(iconData, on ? t.Accent : (dim ? t.Faint : t.Text2), 12));
        content.Add(new Label
        {
            Text = label,
            FontFamily = _tm.ChromeFont,
            FontSize = _tm.Metrics.FsSm,
            TextColor = on ? t.Text : (dim ? t.Faint : t.Text2),
            FontAttributes = on ? FontAttributes.Bold : FontAttributes.None,
            VerticalOptions = LayoutOptions.Center,
        });
        if (count > 0)
        {
            content.Add(new Border
            {
                BackgroundColor = t.Err,
                Stroke = Colors.Transparent,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 7 },
                Padding = new Thickness(5, 0),
                HeightRequest = 14,
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = count.ToString(),
                    FontFamily = _tm.MonoFont,
                    FontSize = 9,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    VerticalOptions = LayoutOptions.Center,
                },
            });
        }

        var btn = new Border
        {
            HeightRequest = 26,
            Padding = new Thickness(12, 0),
            BackgroundColor = on ? t.Panel : Colors.Transparent,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = _tm.Metrics.RSm },
            Content = content,
        };
        SemanticProperties.SetDescription(btn, $"DevTools panel: {label}");

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SetActive(tab);
        btn.GestureRecognizers.Add(tap);
        return btn;
    }

    public void SetActive(DevToolsTab tab)
    {
        if (_active == tab) return;
        _active = tab;
        // Rebuild the strip (active styling) and the body. Cheap at this scale.
        Children.Clear();
        this.Add(BuildTabStrip(), 0, 0);
        _body.Content = BuildBody(_active);
        this.Add(_body, 0, 1);
    }

    private View BuildBody(DevToolsTab tab) => tab switch
    {
        DevToolsTab.Performance => new PerformancePanel(_tm),
        DevToolsTab.Console => new ConsolePanel(_tm),
        DevToolsTab.Internals => new InternalsPanel(_tm),
        _ => Placeholder(tab),
    };

    private View Placeholder(DevToolsTab tab) => new ContentView
    {
        BackgroundColor = _tm.Tokens.Panel,
        Content = new Label
        {
            Text = $"{tab} — not in this design pass",
            FontFamily = _tm.ChromeFont,
            FontSize = _tm.Metrics.FsSm,
            TextColor = _tm.Tokens.Faint,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        },
    };
}

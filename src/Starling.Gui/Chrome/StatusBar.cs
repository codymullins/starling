using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Starling.Gui.Theme;

namespace Starling.Gui.Chrome;

/// <summary>
/// Bottom status bar — calm-modern redesign. Five segments left-to-right:
/// state dot + label, italic-mono hint, then View / Doc / Hist cells with
/// hairline separators. All data is real engine state.
/// </summary>
public sealed class StatusBar : Border
{
    private readonly ThemeManager _tm;
    private readonly Ellipse _stateDot;
    private readonly TextBlock _stateLabel;
    private readonly TextBlock _hint;
    private readonly TextBlock _viewValue;
    private readonly TextBlock _docValue;
    private readonly TextBlock _histValue;

    public StatusBar(ThemeManager tm)
    {
        _tm = tm;
        var t = tm.Tokens;

        _stateDot = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(t.Accent),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _stateLabel = new TextBlock
        {
            Text = "Ready",
            FontFamily = new FontFamily(tm.SansFont),
            FontSize = 11.5,
            FontWeight = FontWeight.Medium,
            Foreground = new SolidColorBrush(t.Text2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var stateRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _stateDot, _stateLabel },
        };
        var stateCell = new Border
        {
            Padding = new Thickness(16, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = stateRow,
        };

        _hint = new TextBlock
        {
            Text = string.Empty,
            FontFamily = new FontFamily(tm.MonoFont),
            FontSize = 11,
            Foreground = new SolidColorBrush(t.Text2),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var hintCell = new Border
        {
            Padding = new Thickness(14, 0),
            Child = _hint,
        };

        _viewValue = MonoVal(tm, t);
        _docValue = MonoVal(tm, t);
        _histValue = MonoVal(tm, t);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        grid.Children.Add(stateCell); Grid.SetColumn(stateCell, 0);
        grid.Children.Add(hintCell); Grid.SetColumn(hintCell, 1);
        grid.Children.Add(KvCell(tm, t, "View", _viewValue, leftRule: true)); Grid.SetColumn(grid.Children[^1], 2);
        grid.Children.Add(KvCell(tm, t, "Doc", _docValue, leftRule: true)); Grid.SetColumn(grid.Children[^1], 3);
        grid.Children.Add(KvCell(tm, t, "Hist", _histValue, leftRule: true)); Grid.SetColumn(grid.Children[^1], 4);

        Background = new SolidColorBrush(t.Panel);
        BorderBrush = new SolidColorBrush(t.Rule());
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(0);
        Height = 32;
        Child = grid;
    }

    private static TextBlock MonoVal(ThemeManager tm, ThemeTokens t) => new()
    {
        FontFamily = new FontFamily(tm.MonoFont),
        FontSize = 11,
        Foreground = new SolidColorBrush(t.Text2),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Control KvCell(ThemeManager tm, ThemeTokens t, string key, TextBlock value, bool leftRule)
    {
        var keyBlock = new TextBlock
        {
            Text = key,
            FontFamily = new FontFamily(tm.SansFont),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(t.Faint),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { keyBlock, value },
        };
        return new Border
        {
            BorderBrush = new SolidColorBrush(t.Rule()),
            BorderThickness = leftRule ? new Thickness(1, 0, 0, 0) : new Thickness(0),
            Padding = new Thickness(13, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = row,
        };
    }

    /// <summary>The current hint text, used to restore status across theme rebuilds.</summary>
    public string HintText => _hint.Text ?? string.Empty;

    /// <summary>Sets the middle hint text — navigation feedback or hover URL.</summary>
    public void SetHint(string text, bool isError = false)
    {
        _hint.Text = text;
        _hint.Foreground = new SolidColorBrush(isError ? _tm.Tokens.Err : _tm.Tokens.Text2);
    }

    /// <summary>Updates the state dot + label (Ready / Loading / Error).</summary>
    public void SetState(StatusState state)
    {
        var t = _tm.Tokens;
        switch (state)
        {
            case StatusState.Loading:
                _stateDot.Fill = new SolidColorBrush(t.Warn);
                _stateLabel.Text = "Loading";
                _stateLabel.Foreground = new SolidColorBrush(t.Text2);
                break;
            case StatusState.Error:
                _stateDot.Fill = new SolidColorBrush(t.Err);
                _stateLabel.Text = "Error";
                _stateLabel.Foreground = new SolidColorBrush(t.Err);
                break;
            default:
                _stateDot.Fill = new SolidColorBrush(t.Accent);
                _stateLabel.Text = "Ready";
                _stateLabel.Foreground = new SolidColorBrush(t.Text2);
                break;
        }
    }

    /// <summary>Updates the right-side info cells.</summary>
    public void SetInfo(string view, string doc, string hist)
    {
        _viewValue.Text = view;
        _docValue.Text = doc;
        _histValue.Text = hist;
    }
}

public enum StatusState { Ready, Loading, Error }

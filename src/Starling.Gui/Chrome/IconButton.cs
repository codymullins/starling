using Microsoft.Maui.Controls.Shapes;
using Tessera.Gui.Theme;

namespace Tessera.Gui.Chrome;

/// <summary>
/// A square icon button — port of <c>IconBtn</c> in <c>design/chrome.jsx</c>.
/// One <c>--row</c> on a side, a hover background, an optional accent-tinted
/// "on" state, and a dimmed disabled state that also swallows taps. Every
/// instance carries an <c>aria-label</c>-equivalent description (HANDOFF §7).
/// </summary>
public sealed class IconButton : Border
{
    private readonly bool _on;
    private bool _enabled = true;

    public event EventHandler? Clicked;

    public IconButton(ThemeManager tm, string iconData, string label,
        bool isOn = false, double? size = null)
    {
        var t = tm.Tokens;
        var box = size ?? tm.Metrics.Row;
        _on = isOn;

        WidthRequest = box;
        HeightRequest = box;
        BackgroundColor = isOn ? t.AccentBg : Colors.Transparent;
        Stroke = Colors.Transparent;
        StrokeThickness = 0;
        StrokeShape = new RoundRectangle { CornerRadius = tm.Metrics.RMd };
        Content = Icons.Make(iconData, isOn ? t.Accent : t.Text2, 16);
        SemanticProperties.SetDescription(this, label);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => { if (_enabled) Clicked?.Invoke(this, EventArgs.Empty); };
        GestureRecognizers.Add(tap);

        ChromeKit.AttachHover(this,
            () => { if (_enabled && !_on) BackgroundColor = t.Hover; },
            () => { if (!_on) BackgroundColor = Colors.Transparent; });
    }

    /// <summary>Dims the button and stops it raising <see cref="Clicked"/>.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        Opacity = enabled ? 1.0 : 0.35;
    }
}

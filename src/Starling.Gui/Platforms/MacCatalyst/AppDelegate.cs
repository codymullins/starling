using Foundation;

namespace Tessera.Gui;

[Register(nameof(AppDelegate))]
public sealed class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp()
    {
        return MauiProgram.CreateMauiApp();
    }
}

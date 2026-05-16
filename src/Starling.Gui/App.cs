using Microsoft.Extensions.DependencyInjection;

namespace Tessera.Gui;

public sealed class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_services.GetRequiredService<MainPage>())
        {
            Title = "Starling",
            MinimumWidth = 1024,
            MinimumHeight = 720,
        };
    }
}

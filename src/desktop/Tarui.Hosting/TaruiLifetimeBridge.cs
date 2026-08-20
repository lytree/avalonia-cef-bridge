using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Tarui.Hosting;

public sealed class TaruiLifetimeBridge
{
    private IClassicDesktopStyleApplicationLifetime? _lifetime;

    public bool ShutdownRequested { get; private set; }

    public void Register(IClassicDesktopStyleApplicationLifetime lifetime) => _lifetime = lifetime;

    public void RequestShutdown()
    {
        ShutdownRequested = true;
        if (_lifetime is { } lifetime)
        {
            Dispatcher.UIThread.Post(() => lifetime.Shutdown());
        }
    }
}

namespace Tarui.Ipc;

/// <summary>
/// Requests host-coordinated shutdown and relaunch. Plugins must go through this instead of
/// calling <c>Environment.Exit</c> directly so the host can stop cooperatively and release all
/// desktop resources in order.
/// </summary>
public interface IAppShutdown
{
    /// <summary>
    /// Gracefully stops the application. Equivalent to <c>IHostApplicationLifetime.StopApplication</c>;
    /// the host stops every <c>IHostedService</c> and disposes desktop resources before the process exits.
    /// </summary>
    void RequestShutdown(int exitCode = 0);

    /// <summary>
    /// Starts a fresh instance of the running executable. Returns <see langword="true"/> only when a
    /// new process was actually started, so callers can gate host shutdown on a successful relaunch.
    /// </summary>
    bool TryStartRelaunch();
}

/// <summary>An <see cref="IAppShutdown"/> that does nothing; used as a DI default that the composition root overrides.</summary>
public sealed class NoopAppShutdown : IAppShutdown
{
    public void RequestShutdown(int exitCode = 0)
    {
    }

    public bool TryStartRelaunch() => false;
}

/// <summary>How the host decides to stop when windows are closed.</summary>
public enum AppShutdownMode
{
    /// <summary>The host stops when the main window (label <c>main</c>) closes.</summary>
    OnMainWindowClose,
    /// <summary>The host stops when the last window closes.</summary>
    OnLastWindowClose,
    /// <summary>The host only stops when something explicitly requests shutdown (for example a tray exit).</summary>
    Explicit,
}

/// <summary>
/// Observes confirmed window closures and decides, from <see cref="AppShutdownMode"/>, whether the
/// host should stop. The Shell notifies this coordinator after a window is fully closed and removed.
/// </summary>
public interface IAppShutdownCoordinator
{
    AppShutdownMode Mode { get; }

    void NotifyWindowClosed(string label, int remainingWindows);
}

/// <summary>Default <see cref="IAppShutdownCoordinator"/> implementing each shutdown mode.</summary>
public sealed class AppShutdownCoordinator(IAppShutdown appShutdown, AppShutdownMode mode) : IAppShutdownCoordinator
{
    public AppShutdownMode Mode { get; } = mode;

    public void NotifyWindowClosed(string label, int remainingWindows)
    {
        switch (Mode)
        {
            case AppShutdownMode.OnMainWindowClose when string.Equals(label, "main", StringComparison.Ordinal):
                appShutdown.RequestShutdown(0);
                break;
            case AppShutdownMode.OnLastWindowClose when remainingWindows == 0:
                appShutdown.RequestShutdown(0);
                break;
        }
    }
}
using Tarui.Ipc;

namespace Tarui.Plugins.System;

public interface IProcessService
{
    void Shutdown(int code);

    void Relaunch();
}

/// <summary>
/// Delegates exit and relaunch to the host-coordinated <see cref="IAppShutdown"/>, so the process
/// terminates gracefully (stopping every host service and releasing desktop resources) instead of
/// calling <see cref="Environment.Exit"/> directly. Relaunch only stops the current process after a
/// new instance has been started successfully.
/// </summary>
public sealed class ProcessService(IAppShutdown appShutdown) : IProcessService
{
    public void Shutdown(int code) => appShutdown.RequestShutdown(code);

    public void Relaunch()
    {
        if (appShutdown.TryStartRelaunch())
        {
            appShutdown.RequestShutdown(0);
        }
    }
}
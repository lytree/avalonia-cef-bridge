namespace Tarui.Plugins.System;

public interface IProcessService
{
    void Shutdown(int code);

    void Relaunch();
}

public sealed class ProcessService : IProcessService
{
    public void Shutdown(int code) =>
        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            Environment.Exit(code);
        });

    public void Relaunch()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current process path is unavailable.");
        _ = global::System.Diagnostics.Process.Start(executable);
        Shutdown(0);
    }
}

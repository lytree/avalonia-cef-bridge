using Microsoft.Win32;
using Tarui.Contracts;
using Tarui.Plugins.Autostart;

namespace Tarui.Shell;

/// <summary>
/// Autostart registration backed by the Windows <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>
/// registry key. On any other platform every operation degrades honestly: it reports disabled and
/// does nothing instead of pretending to succeed, because cross-platform autostart managers differ
/// and the running application must never fabricate a system write it did not perform.
/// </summary>
public sealed class WindowsAutostartService : IAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaruiApp";

    public ValueTask<AutostartState> IsEnabledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(new AutostartState(Enabled: false));
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return ValueTask.FromResult(new AutostartState(Enabled: !string.IsNullOrEmpty(value)));
    }

    public ValueTask<Unit> EnableAsync(AutostartEnableOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(new Unit());
        }

        AutostartConfig.ValidateArgs(options.Args);
        var commandLine = AutostartConfig.BuildCommandLine(Environment.ProcessPath!, options.Args);

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(ValueName, commandLine);
        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(new Unit());
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        return ValueTask.FromResult(new Unit());
    }
}
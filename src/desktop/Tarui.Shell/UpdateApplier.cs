using System.ComponentModel;
using System.Diagnostics;

namespace Tarui.Shell;

/// <summary>
/// Applies a fully-downloaded and SHA-256-verified update bundle staged under a directory. An applier returns
/// <see langword="true"/> when it applied the bundle, <see langword="false"/> when the staging directory holds no
/// bundle it can apply (an unsupported combination), and throws for a genuine apply failure. The runtime never
/// manipulates the running installation directly; the concrete applier owns the actual install mechanism.
/// </summary>
public interface IUpdateApplier
{
    ValueTask<bool> ApplyAsync(string stagingPath, CancellationToken cancellationToken);
}

/// <summary>
/// Windows MSIX applier. Locates a single <c>.msix</c> bundle in the staging root and installs it via
/// <c>Add-AppxPackage</c>, the packaged distribution path produced by <c>tarui build</c> (workflow W5).
/// A real install that fails (non-zero exit) throws so it surfaces as an <c>apply-failed</c> status; a staging
/// directory without an MSIX returns <see langword="false"/>. Sideloading an unsigned package for development is
/// left to the operator (dev mode / signing), matching MSIX deployment rules.
/// </summary>
public sealed class WindowsMsixUpdateApplier : IUpdateApplier
{
    public async ValueTask<bool> ApplyAsync(string stagingPath, CancellationToken cancellationToken)
    {
        var bundle = Directory.GetFiles(stagingPath, "*.msix", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        if (bundle is null)
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"Add-AppxPackage -Path \"{bundle}\"");

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                $"PowerShell could not be started to install the package: {exception.Message}", exception);
        }

        var started = process ?? throw new InvalidOperationException("PowerShell could not be started to install the package.");
        using (started)
        {
            var output = await started.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await started.StandardError.ReadToEndAsync(cancellationToken);
            await started.WaitForExitAsync(cancellationToken);
            if (started.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Add-AppxPackage failed (exit {started.ExitCode}): {(string.IsNullOrWhiteSpace(error) ? output : error).Trim()}");
            }
        }

        return true;
    }
}

/// <summary>
/// Default applier for platforms without a concrete install strategy. It reports that nothing was applied, so a
/// caller attempting an apply on an unsupported platform observes an explicit <c>update-apply-unsupported</c>
/// rather than a silent no-op. Mirrors the project's rule against dishonest cross-platform claims.
/// </summary>
public sealed class NoOpUpdateApplier : IUpdateApplier
{
    public ValueTask<bool> ApplyAsync(string stagingPath, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}
using System.Runtime.InteropServices;

namespace Tarui.Cli;

/// <summary>
/// Locates Windows SDK command-line tools (signtool.exe, makeappx.exe, ...) on disk.
/// The MSIX packer is fully managed, but optional Authenticode signing shells out to
/// signtool.exe when a certificate is configured (design §5.5 / W5).
/// </summary>
internal static class WindowsSdkToolFinder
{
    /// <summary>
    /// Returns the full path to <paramref name="toolName"/> (e.g. "signtool.exe") if it can
    /// be located via the PATH, the Windows SDK Kit versioned directories, or the Windows Kits
    /// root; otherwise <c>null</c>.
    /// </summary>
    public static string? Find(string toolName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (var directory in CandidateDirectories(toolName))
        {
            var candidate = Path.Combine(directory, toolName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return SearchPath(toolName);
    }

    private static IEnumerable<string> CandidateDirectories(string toolName)
    {
        // "bin\<arch>\<tool>" under the localized Windows Kits versioned directories
        // (e.g. C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe).
        const string windowsKits = @"C:\Program Files (x86)\Windows Kits\10";
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => "x86",
        };

        if (Directory.Exists(windowsKits))
        {
            var bin = Path.Combine(windowsKits, "bin");
            foreach (var version in Directory.EnumerateDirectories(bin)
                         .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.Combine(version, arch);
                yield return Path.Combine(version, "x64");
            }
        }

        yield return Path.Combine(windowsKits, "bin", "x64");
    }

    private static string? SearchPath(string toolName)
    {
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        var hasExtension = Path.HasExtension(toolName);
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (hasExtension)
            {
                var candidate = Path.Combine(directory, toolName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                continue;
            }

            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, toolName + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
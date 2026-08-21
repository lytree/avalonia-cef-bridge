using System.Diagnostics;

namespace Tarui.Shell;

/// <summary>
/// Registers custom protocols on Linux by publishing a per-user <c>.desktop</c> entry under
/// <c>~/.local/share/applications</c> that declares <c>x-scheme-handler/&lt;scheme&gt;</c>, then
/// best-effort promotes it to the default handler via <c>xdg-mime default</c>. Idempotent per
/// launch, so it is safe to run whenever deep-link schemes are configured. Degrades to a no-op on
/// non-Linux platforms (their registration is a packaging/deployment or OS-specific concern).
/// </summary>
public sealed class LinuxDeepLinkRegistrar
{
    private const string RelativeApplicationsDir = ".local/share/applications";
    private const string EntryName = "tarui.net";

    public static void RegisterSchemes(IReadOnlyCollection<string> schemes)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return;
        }

        var baseDir = Path.Combine(home, RelativeApplicationsDir, EntryName);
        foreach (var scheme in schemes)
        {
            if (!DeepLinkUri.IsValidScheme(scheme))
            {
                continue;
            }

            var desktopFile = Path.Combine(baseDir, $"{scheme}.desktop");
            Directory.CreateDirectory(baseDir);
            File.WriteAllText(desktopFile, BuildDesktopEntry(scheme, executable));

            RegisterAsDefaultWithXdg(desktopFile, scheme);
        }
    }

    internal static string BuildDesktopEntry(string scheme, string executable)
        => $"""
            [Desktop Entry]
            Type=Application
            Name={EntryName}
            Exec="{executable}" %u
            MimeType=x-scheme-handler/{scheme};
            Keywords=tarui;{scheme};
            NoDisplay=true
            StartupNotify=false

            """;

    private static void RegisterAsDefaultWithXdg(string desktopFile, string scheme)
    {
        // Best-effort default-handler assignment; xdg-utils may be unavailable on minimal systems,
        // in which case the published .desktop entry alone still advertises the handler.
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-mime",
                ArgumentList = { "default", desktopFile, $"x-scheme-handler/{scheme}" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is not null)
            {
                process.WaitForExit();
            }
        }
        catch
        {
            // xdg-mime missing or failing to launch is not fatal for protocol registration.
        }
    }
}
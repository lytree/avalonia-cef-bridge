using Microsoft.Win32;

namespace Tarui.Shell;

/// <summary>
/// Registers custom protocols under <c>HKCU\Software\Classes</c> so the OS hands
/// <c>scheme://</c> links to this application's executable. Per-user registration avoids the
/// elevated privileges install-time registration requires and is idempotent, so it is safe to run
/// on each launch when deep-link schemes are configured. The shell open command quotes both the
/// executable and the <c>%1</c> URL placeholder.
/// </summary>
public sealed class WindowsDeepLinkRegistrar
{
    private static readonly string[] DefaultWindowArgs = ["%1"];

    public static void RegisterSchemes(IReadOnlyCollection<string> schemes)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return;
        }

        // Launch the primary executable by path with the URL forwarded as the first argument.
        var command = BuildOpenCommand(executable, DefaultWindowArgs);

        foreach (var scheme in schemes)
        {
            if (!DeepLinkUri.IsValidScheme(scheme))
            {
                continue;
            }

            using var protocol = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}");
            protocol.SetValue(string.Empty, $"URL:{scheme}");
            protocol.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);

            using var commandKey = protocol.CreateSubKey(@"shell\open\command");
            commandKey.SetValue(string.Empty, command);
        }
    }

    private static string BuildOpenCommand(string executable, string[] args)
        => $"\"{executable}\" {string.Join(' ', args)}";
}
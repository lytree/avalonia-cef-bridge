using Microsoft.Win32;

namespace Tarui.Shell;

/// <summary>One file association handed to the runtime registrar (portable/zip scenario).</summary>
public sealed record FileAssociationSpec(string Ext, string Name, string? Description);

/// <summary>
/// Registers file associations under <c>HKCU\Software\Classes</c> so the shell opens documents
/// with this application's executable. Per-user registration mirrors
/// <see cref="WindowsDeepLinkRegistrar"/>: it avoids elevated install-time registration and is
/// idempotent (each launch overwrites the values), which keeps the portable zip distribution
/// working without an installer. The ProgID carries a <c>DefaultIcon</c> and a
/// <c>shell\open\command</c> that quotes both the executable and the <c>%1</c> document
/// placeholder; the extension key points at the ProgID.
/// </summary>
public static class WindowsFileAssociationRegistrar
{
    public static void Register(IReadOnlyCollection<FileAssociationSpec> associations)
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

        var command = BuildShellOpenCommand(executable);
        var icon = BuildDefaultIcon(executable);

        foreach (var association in associations)
        {
            if (string.IsNullOrWhiteSpace(association.Ext) || string.IsNullOrWhiteSpace(association.Name))
            {
                continue;
            }

            var extension = NormalizeExtension(association.Ext);
            var progId = BuildProgId(association.Name, extension);

            using var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}");
            progIdKey.SetValue(string.Empty, association.Description ?? association.Name);
            using var iconKey = progIdKey.CreateSubKey("DefaultIcon");
            iconKey.SetValue(string.Empty, icon);
            using var commandKey = progIdKey.CreateSubKey(@"shell\open\command");
            commandKey.SetValue(string.Empty, command);

            using var extensionKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}");
            extensionKey.SetValue(string.Empty, progId);
        }
    }

    /// <summary>Derives the ProgID <c>tarui.&lt;sanitized-name&gt;&lt;ext&gt;</c>; characters outside letters, digits and dots become '-'.</summary>
    internal static string BuildProgId(string name, string ext)
    {
        var builder = new System.Text.StringBuilder("tarui.");
        foreach (var character in name.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '.' ? character : '-');
        }

        builder.Append(ext);
        return builder.ToString();
    }

    internal static string BuildShellOpenCommand(string executable) => $"\"{executable}\" \"%1\"";

    internal static string BuildDefaultIcon(string executable) => $"\"{executable}\",0";

    private static string NormalizeExtension(string ext)
    {
        var trimmed = ext.Trim();
        return trimmed.StartsWith('.') ? trimmed : $".{trimmed}";
    }
}

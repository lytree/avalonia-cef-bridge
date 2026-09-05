using System.Globalization;
using System.Text;
using Tarui.Contracts;
using Tarui.Plugins.Autostart;

namespace Tarui.Shell;

/// <summary>
/// macOS autostart registration backed by a <c>LaunchAgents</c> plist entry with <c>RunAtLoad</c>.
/// Uses the current application executable only. The entry directory is injectable so the file
/// lifecycle is testable with a temporary directory.
/// </summary>
public sealed class MacAutostartService : IAutostartService
{
    private readonly string _baseDirectory;
    private readonly string _executablePath;

    public MacAutostartService(string baseDirectory, string executablePath)
    {
        _baseDirectory = baseDirectory;
        _executablePath = executablePath;
    }

    private string EntryFile => Path.Combine(_baseDirectory, $"{Path.GetFileNameWithoutExtension(_executablePath)}.plist");

    internal static string DefaultBaseDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents");

    public ValueTask<AutostartState> IsEnabledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new AutostartState(File.Exists(EntryFile)));
    }

    public ValueTask<Unit> EnableAsync(AutostartEnableOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AutostartConfig.ValidateArgs(options.Args);
        Directory.CreateDirectory(_baseDirectory);
        File.WriteAllText(EntryFile, BuildPlist(options.Args), new UTF8Encoding(false));
        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(EntryFile))
        {
            File.Delete(EntryFile);
        }

        return ValueTask.FromResult(new Unit());
    }

    private string BuildPlist(string[]? args)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        builder.AppendLine("<plist version=\"1.0\">");
        builder.AppendLine("<dict>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  <key>Label</key><string>com.tarui.{Path.GetFileNameWithoutExtension(_executablePath)}</string>");
        builder.AppendLine("  <key>ProgramArguments</key>");
        builder.AppendLine("  <array>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    <string>{XmlEscape(_executablePath)}</string>");
        foreach (var arg in args ?? [])
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"    <string>{XmlEscape(arg)}</string>");
        }

        builder.AppendLine("  </array>");
        builder.AppendLine("  <key>RunAtLoad</key><true/>");
        builder.AppendLine("  <key>ProcessType</key><string>Interactive</string>");
        builder.AppendLine("</dict>");
        builder.AppendLine("</plist>");
        return builder.ToString();
    }

    private static string XmlEscape(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&apos;", StringComparison.Ordinal);
}

/// <summary>
/// Linux (freedesktop) autostart registration backed by a <c>.desktop</c> entry under
/// <c>~/.config/autostart</c>. Uses the current application executable only; the entry directory is
/// injectable for testing. A <c>Hidden=true</c> entry counts as disabled so DEs that tombstone a user-
/// disabled entry are read honestly.
/// </summary>
public sealed class LinuxAutostartService : IAutostartService
{
    private readonly string _baseDirectory;
    private readonly string _executablePath;

    public LinuxAutostartService(string baseDirectory, string executablePath)
    {
        _baseDirectory = baseDirectory;
        _executablePath = executablePath;
    }

    private string EntryFile => Path.Combine(_baseDirectory, $"{Path.GetFileNameWithoutExtension(_executablePath)}.desktop");

    public ValueTask<AutostartState> IsEnabledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(EntryFile))
        {
            return ValueTask.FromResult(new AutostartState(false));
        }

        var content = File.ReadAllText(EntryFile);
        var hidden = content.Contains("Hidden=true", StringComparison.OrdinalIgnoreCase);
        return ValueTask.FromResult(new AutostartState(!hidden));
    }

    public ValueTask<Unit> EnableAsync(AutostartEnableOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AutostartConfig.ValidateArgs(options.Args);
        Directory.CreateDirectory(_baseDirectory);
        var commandLine = AutostartConfig.BuildCommandLine(_executablePath, options.Args);
        var builder = new StringBuilder();
        builder.AppendLine("[Desktop Entry]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Type=Application");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Name={Path.GetFileNameWithoutExtension(_executablePath)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Exec={commandLine}");
        builder.AppendLine("Terminal=false");
        builder.AppendLine("X-GNOME-Autostart-enabled=true");
        builder.AppendLine("Hidden=false");
        File.WriteAllText(EntryFile, builder.ToString(), new UTF8Encoding(false));
        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(EntryFile))
        {
            File.Delete(EntryFile);
        }

        return ValueTask.FromResult(new Unit());
    }

    internal static string DefaultBaseDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "autostart");
}
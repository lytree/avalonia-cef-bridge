using Tarui.Contracts;

namespace Tarui.Plugins.System;

public interface IPathService
{
    string Resolve(string kind, string? relativePath);
}

public sealed class PathService : IPathService
{
    private const string AppName = "tarui.net";

    public string Resolve(string kind, string? relativePath)
    {
        var basePath = ResolveBase(kind)
            ?? throw new InvalidOperationException($"Unknown path kind '{kind}'.");
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new InvalidOperationException($"The path kind '{kind}' is not available on this system.");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return basePath;
        }

        var root = Path.GetFullPath(basePath);
        var combined = Path.GetFullPath(Path.Combine(root, relativePath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!combined.StartsWith(root, comparison) &&
            !string.Equals(combined.TrimEnd(Path.DirectorySeparatorChar), root, comparison))
        {
            throw new InvalidOperationException("The path escapes the requested base directory.");
        }

        return combined;
    }

    private static string? ResolveBase(string kind) => kind switch
    {
        "appData" => UnderApp(Environment.SpecialFolder.ApplicationData),
        "appLocalData" => UnderApp(Environment.SpecialFolder.LocalApplicationData),
        "appConfig" => UnderApp(Environment.SpecialFolder.LocalApplicationData, "config"),
        "appCache" => UnderApp(Environment.SpecialFolder.LocalApplicationData, "cache"),
        "appLog" => UnderApp(Environment.SpecialFolder.LocalApplicationData, "logs"),
        "temp" => Path.GetTempPath(),
        "home" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "download" => UnderHome("Downloads"),
        "document" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "video" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "fonts" => Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
        "resources" => AppContext.BaseDirectory,
        _ => null,
    };

    private static string? UnderApp(Environment.SpecialFolder folder, string? suffix = null)
    {
        var root = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        return suffix is null ? Path.Combine(root, AppName) : Path.Combine(root, AppName, suffix);
    }

    private static string? UnderHome(string suffix)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home) ? null : Path.Combine(home, suffix);
    }
}

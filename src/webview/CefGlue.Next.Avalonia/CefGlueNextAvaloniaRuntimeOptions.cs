using System.Runtime.InteropServices;
using Xilium.CefGlue;

namespace CefGlue.Next.Avalonia;

public enum CefGlueNextAvaloniaLogSeverity
{
    Default,
    Verbose,
    Info,
    Warning,
    Error,
    Fatal,
    Disable
}

public sealed class CefGlueNextAvaloniaRuntimeOptions
{
    public string? RuntimeDirectory { get; init; }

    public string? ResourcesDirectory { get; init; }

    public string? LocalesDirectory { get; init; }

    public string? CacheDirectory { get; init; }

    public string? BrowserSubprocessPath { get; init; }

    public bool WindowlessRenderingEnabled { get; init; }

    public bool NoSandbox { get; init; } = true;

    public string? LogFile { get; init; }

    public CefGlueNextAvaloniaLogSeverity LogSeverity { get; init; } = CefGlueNextAvaloniaLogSeverity.Warning;

    public IReadOnlyList<KeyValuePair<string, string>> CommandLineFlags { get; init; } =
        [new KeyValuePair<string, string>("do-not-de-elevate", string.Empty)];

    public IReadOnlyList<CefGlueNextAvaloniaSchemeOptions> Schemes { get; init; } = [];

    internal string? ResolveBrowserSubprocessPath()
    {
        if (!string.IsNullOrWhiteSpace(BrowserSubprocessPath))
        {
            return Path.GetFullPath(BrowserSubprocessPath);
        }

        return Environment.ProcessPath;
    }

    internal string ResolveCacheDirectory()
    {
        var path = CacheDirectory;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(
                Path.GetTempPath(),
                "cefglue-next-avalonia",
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Directory.CreateDirectory(path);
        return Path.GetFullPath(path);
    }

    internal static void ValidateSchemes(IReadOnlyList<CefGlueNextAvaloniaSchemeOptions> schemes)
    {
        ArgumentNullException.ThrowIfNull(schemes);

        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scheme in schemes)
        {
            ArgumentNullException.ThrowIfNull(scheme);

            if (string.IsNullOrWhiteSpace(scheme.SchemeName) ||
                !Uri.CheckSchemeName(scheme.SchemeName) ||
                scheme.SchemeName.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                scheme.SchemeName.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Invalid custom scheme name '{scheme.SchemeName}'.",
                    nameof(schemes));
            }

            if (string.IsNullOrWhiteSpace(scheme.DomainName) ||
                Uri.CheckHostName(scheme.DomainName) == UriHostNameType.Unknown ||
                scheme.DomainName.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character)) ||
                scheme.DomainName.Contains('@') ||
                scheme.DomainName.Contains(':'))
            {
                throw new ArgumentException(
                    $"Invalid custom scheme host '{scheme.DomainName}'.",
                    nameof(schemes));
            }

            if (scheme.ResourceProvider is null)
            {
                throw new ArgumentException(
                    $"A resource provider is required for '{scheme.SchemeName}://{scheme.DomainName}'.",
                    nameof(schemes));
            }

            var origin = $"{scheme.SchemeName}://{scheme.DomainName}";
            if (!origins.Add(origin))
            {
                throw new ArgumentException(
                    $"Duplicate custom scheme origin '{origin}'.",
                    nameof(schemes));
            }
        }
    }

    internal string? ResolveResourcesDirectory()
    {
        if (!string.IsNullOrWhiteSpace(ResourcesDirectory))
        {
            return Path.GetFullPath(ResourcesDirectory);
        }

        return CefRuntimeLocator.GetResourceDirPath();
    }

    internal string? ResolveLocalesDirectory(string? resourcesDirectory)
    {
        if (!string.IsNullOrWhiteSpace(LocalesDirectory))
        {
            return Path.GetFullPath(LocalesDirectory);
        }

        if (resourcesDirectory is not null)
        {
            var adjacent = Path.Combine(resourcesDirectory, "locales");
            if (Directory.Exists(adjacent))
            {
                return adjacent;
            }
        }

        return null;
    }

    internal static string GetDefaultRuntimeDirectory()
    {
        var rid = OperatingSystem.IsWindows()
            ? RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64"
            : OperatingSystem.IsMacOS()
                ? RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64"
                : RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";

        var bundled = Path.Combine(AppContext.BaseDirectory, "CEF", rid);
        return Directory.Exists(bundled) ? bundled : AppContext.BaseDirectory;
    }
}

using System.Globalization;

namespace Tarui.WebView.CefGlueNext;

public enum TaruiWebResourceMode
{
    Http,
    Scheme
}

public sealed record CefGlueNextWebAppOptions
{
    public const string DefaultSchemeName = "tarui";
    public const string DefaultDomainName = "localhost";

    private CefGlueNextWebAppOptions(
        TaruiWebResourceMode mode,
        Uri startUri,
        string? contentRoot,
        string schemeName,
        string domainName,
        bool spaFallback,
        string contentSecurityPolicy,
        long maxAssetBytes)
    {
        Mode = mode;
        StartUri = startUri;
        ContentRoot = contentRoot;
        SchemeName = schemeName;
        DomainName = domainName;
        SpaFallback = spaFallback;
        ContentSecurityPolicy = contentSecurityPolicy;
        MaxAssetBytes = maxAssetBytes;
    }

    public TaruiWebResourceMode Mode { get; }

    public Uri StartUri { get; }

    public string? ContentRoot { get; }

    public string SchemeName { get; }

    public string DomainName { get; }

    public bool SpaFallback { get; }

    public string ContentSecurityPolicy { get; }

    public long MaxAssetBytes { get; }

    public static CefGlueNextWebAppOptions FromEnvironment()
    {
        var configuredMode = Environment.GetEnvironmentVariable("TARUI_WEB_MODE");
        if (string.IsNullOrWhiteSpace(configuredMode))
        {
            configuredMode = Environment.GetEnvironmentVariable("TARUI_WEB_URL") is not null
                ? "http"
                : FindContentRoot() is not null ? "scheme" : "http";
        }

        return configuredMode.Trim().ToLowerInvariant() switch
        {
            "http" => CreateHttp(),
            "scheme" => CreateScheme(),
            _ => throw new InvalidOperationException(
                "TARUI_WEB_MODE must be either 'http' or 'scheme'.")
        };
    }

    public static CefGlueNextWebAppOptions CreateHttp(Uri? uri = null)
    {
        var startUri = uri ?? new Uri(
            Environment.GetEnvironmentVariable("TARUI_WEB_URL") ?? "http://127.0.0.1:5173",
            UriKind.Absolute);
        if (startUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("HTTP mode requires an http:// or https:// TARUI_WEB_URL.");
        }

        return new CefGlueNextWebAppOptions(
            TaruiWebResourceMode.Http,
            startUri,
            null,
            DefaultSchemeName,
            DefaultDomainName,
            false,
            string.Empty,
            0);
    }

    public static CefGlueNextWebAppOptions CreateScheme(
        string? contentRoot = null,
        string? schemeName = null,
        string? domainName = null,
        bool? spaFallback = null,
        string? contentSecurityPolicy = null,
        long? maxAssetBytes = null)
    {
        var resolvedScheme = schemeName ??
            Environment.GetEnvironmentVariable("TARUI_WEB_SCHEME") ??
            DefaultSchemeName;
        if (!Uri.CheckSchemeName(resolvedScheme))
        {
            throw new InvalidOperationException($"Invalid custom scheme name '{resolvedScheme}'.");
        }

        var resolvedDomain = domainName ??
            Environment.GetEnvironmentVariable("TARUI_WEB_HOST") ??
            DefaultDomainName;
        if (Uri.CheckHostName(resolvedDomain) == UriHostNameType.Unknown)
        {
            throw new InvalidOperationException($"Invalid custom scheme host '{resolvedDomain}'.");
        }

        var resolvedRoot = contentRoot ??
            Environment.GetEnvironmentVariable("TARUI_WEB_ROOT") ??
            FindContentRoot();
        if (string.IsNullOrWhiteSpace(resolvedRoot))
        {
            throw new DirectoryNotFoundException(
                "Scheme mode requires TARUI_WEB_ROOT or a packaged web/index.html directory.");
        }

        resolvedRoot = Path.GetFullPath(resolvedRoot);
        if (!File.Exists(Path.Combine(resolvedRoot, "index.html")))
        {
            throw new DirectoryNotFoundException(
                $"Scheme content root does not contain index.html: {resolvedRoot}");
        }

        var fallback = spaFallback ?? !string.Equals(
            Environment.GetEnvironmentVariable("TARUI_WEB_SPA_FALLBACK"),
            "false",
            StringComparison.OrdinalIgnoreCase);
        var csp = contentSecurityPolicy ??
            Environment.GetEnvironmentVariable("TARUI_WEB_CSP") ??
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; font-src 'self' data:; connect-src 'self'; " +
            "object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
        var maximumBytes = maxAssetBytes ?? ParseMaximumAssetBytes();
        var startUri = new Uri($"{resolvedScheme}://{resolvedDomain}/index.html", UriKind.Absolute);
        return new CefGlueNextWebAppOptions(
            TaruiWebResourceMode.Scheme,
            startUri,
            resolvedRoot,
            resolvedScheme,
            resolvedDomain,
            fallback,
            csp,
            maximumBytes);
    }

    private static long ParseMaximumAssetBytes()
    {
        const long defaultMaximum = 64L * 1024L * 1024L;
        var configured = Environment.GetEnvironmentVariable("TARUI_WEB_MAX_ASSET_BYTES");
        if (string.IsNullOrWhiteSpace(configured)) return defaultMaximum;
        if (!long.TryParse(
                configured,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value) || value <= 0)
        {
            throw new InvalidOperationException(
                "TARUI_WEB_MAX_ASSET_BYTES must be a positive integer.");
        }

        return value;
    }

    private static string? FindContentRoot()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "web");
        if (File.Exists(Path.Combine(packaged, "index.html"))) return packaged;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 10 && directory is not null; depth++, directory = directory.Parent)
        {
            var development = Path.Combine(directory.FullName, "web", "apps", "Tarui.Web", "dist");
            if (File.Exists(Path.Combine(development, "index.html"))) return development;
        }

        return null;
    }
}

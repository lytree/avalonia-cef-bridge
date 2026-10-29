namespace Tarui.WebView.Abstractions;

/// <summary>
/// Serves files to the page through a restricted, host-controlled asset protocol (the neutral
/// equivalent of Tauri's <c>asset://</c> origin). The host decides which directories are readable;
/// the backend only forwards requests and streams the resolved file.
/// </summary>
public interface ITaruiAssetResolver
{
    /// <summary>
    /// Whether any asset root is configured. A disabled resolver never gets a scheme registered, so
    /// the protocol simply does not exist for the application.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Resolves an asset URL to a file response. Unknown roots, paths outside the configured roots,
    /// and non-file targets must return a 403/404 response instead of throwing.
    /// </summary>
    TaruiAssetResponse Resolve(string requestUrl, string method);
}

/// <summary>
/// A backend-neutral asset response. <see cref="ContentStream"/> is handed to the browser and the
/// caller owns its lifetime; it may be null for empty bodies.
/// </summary>
public sealed record TaruiAssetResponse(
    int Status,
    string StatusText,
    string MimeType,
    string CacheControl,
    long ResponseLength,
    IReadOnlyDictionary<string, string>? Headers = null,
    Stream? ContentStream = null);

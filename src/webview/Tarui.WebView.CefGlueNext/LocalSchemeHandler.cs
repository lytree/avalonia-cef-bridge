using System.Collections.Frozen;
using System.Text;
using CefGlue.Next.Avalonia;

namespace Tarui.WebView.CefGlueNext;

internal sealed class LocalWebAssetResolver : ICefGlueNextAvaloniaResourceProvider, IDisposable
{
    private static readonly FrozenDictionary<string, string> MimeTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html",
            [".htm"] = "text/html",
            [".js"] = "application/javascript",
            [".mjs"] = "application/javascript",
            [".css"] = "text/css",
            [".json"] = "application/json",
            [".map"] = "application/json",
            [".wasm"] = "application/wasm",
            [".svg"] = "image/svg+xml",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".ico"] = "image/x-icon",
            [".woff"] = "font/woff",
            [".woff2"] = "font/woff2",
            [".ttf"] = "font/ttf",
            [".otf"] = "font/otf",
            [".txt"] = "text/plain",
            [".xml"] = "application/xml",
            [".pdf"] = "application/pdf",
            [".mp3"] = "audio/mpeg",
            [".mp4"] = "video/mp4"
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private readonly string _contentRoot;
    private readonly string _contentRootPrefix;
    private readonly StringComparison _pathComparison;
    private readonly bool _spaFallback;
    private readonly long _maxAssetBytes;
    private readonly string _contentSecurityPolicy;

    public LocalWebAssetResolver(
        string contentRoot,
        string schemeName,
        string domainName,
        bool spaFallback,
        long maxAssetBytes,
        string contentSecurityPolicy = "")
    {
        _contentRoot = Path.GetFullPath(contentRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _contentRootPrefix = _contentRoot + Path.DirectorySeparatorChar;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        SchemeName = schemeName;
        DomainName = domainName;
        _spaFallback = spaFallback;
        _maxAssetBytes = maxAssetBytes;
        _contentSecurityPolicy = contentSecurityPolicy;

        if (IsReparsePoint(_contentRoot))
        {
            throw new InvalidOperationException("Scheme content root cannot be a symbolic link or reparse point.");
        }
    }

    public string SchemeName { get; }

    public string DomainName { get; }

    CefGlueNextAvaloniaResourceResponse ICefGlueNextAvaloniaResourceProvider.Resolve(
        CefGlueNextAvaloniaResourceRequest request)
    {
        var asset = Resolve(request.Url, request.Method, request.IsMainFrameResource);
        return new CefGlueNextAvaloniaResourceResponse(
            asset.Status,
            asset.StatusText,
            asset.MimeType,
            asset.CacheControl,
            asset.ResponseLength,
            Content: [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Content-Type-Options"] = "nosniff",
                ["Cache-Control"] = asset.CacheControl,
                ["Content-Security-Policy"] = _contentSecurityPolicy
            },
            ContentStream: asset.Content);
    }

    public LocalWebAsset Resolve(
        string requestUrl,
        string method,
        bool allowSpaFallback)
    {
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, SchemeName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, DomainName, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return Error(404, "Not Found");
        }

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return Error(405, "Method Not Allowed");
        }

        var rawPath = ExtractRawPath(requestUrl);
        var relativePath = rawPath == null ? null : DecodeRelativePath(rawPath);
        if (relativePath == null) return Error(403, "Forbidden");

        var candidate = ResolveCandidate(relativePath);
        if (candidate == null) return Error(403, "Forbidden");

        if (Directory.Exists(candidate))
        {
            candidate = ResolveCandidate(Path.Combine(relativePath, "index.html"));
        }

        if (candidate == null || !File.Exists(candidate) || ContainsReparsePoint(candidate))
        {
            candidate = ResolveSpaFallback(relativePath, allowSpaFallback);
        }

        if (candidate == null || !File.Exists(candidate) || ContainsReparsePoint(candidate))
        {
            return Error(404, "Not Found");
        }

        var fileInfo = new FileInfo(candidate);
        if (fileInfo.Length > _maxAssetBytes)
        {
            return Error(413, "Content Too Large");
        }

        // Stream the file directly into CEF instead of buffering the whole body in managed memory.
        // HEAD requests receive an empty stream while still advertising the real Content-Length so
        // clients can size their caches correctly without consuming bandwidth.
        Stream content;
        if (string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            content = Stream.Null;
        }
        else
        {
            content = new FileStream(
                candidate,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
        }

        var extension = Path.GetExtension(candidate);
        var mimeType = MimeTypes.GetValueOrDefault(extension, "application/octet-stream");
        var immutable = candidate.Contains(
            $"{Path.DirectorySeparatorChar}assets{Path.DirectorySeparatorChar}",
            _pathComparison);
        return new LocalWebAsset(
            200,
            "OK",
            mimeType,
            immutable ? "public, max-age=31536000, immutable" : "no-cache",
            fileInfo.Length,
            content);
    }

    private string? ResolveSpaFallback(string relativePath, bool allowSpaFallback)
    {
        if (!_spaFallback || !allowSpaFallback || Path.HasExtension(relativePath)) return null;
        return ResolveCandidate("index.html");
    }

    private string? ResolveCandidate(string relativePath)
    {
        var candidate = Path.GetFullPath(Path.Combine(_contentRoot, relativePath));
        return candidate.StartsWith(_contentRootPrefix, _pathComparison)
            ? candidate
            : null;
    }

    private bool ContainsReparsePoint(string path)
    {
        var relative = Path.GetRelativePath(_contentRoot, path);
        var current = _contentRoot;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsReparsePoint(current)) return true;
        }

        return false;
    }

    private static string? ExtractRawPath(string requestUrl)
    {
        var schemeSeparator = requestUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0) return null;

        var pathStart = requestUrl.IndexOf('/', schemeSeparator + 3);
        if (pathStart < 0) return "/";

        var queryStart = requestUrl.IndexOfAny(['?', '#'], pathStart);
        return queryStart < 0
            ? requestUrl[pathStart..]
            : requestUrl[pathStart..queryStart];
    }

    private static string? DecodeRelativePath(string absolutePath)
    {
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(absolutePath).Replace('\\', '/');
        }
        catch (UriFormatException)
        {
            return null;
        }

        if (decoded.Contains('\0') || decoded.Any(char.IsControl)) return null;
        var segments = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(static segment =>
                segment is "." or ".." || segment.Contains(':'))) return null;
        return segments.Length == 0
            ? "index.html"
            : Path.Combine(segments);
    }

    private static bool IsReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return false;
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static LocalWebAsset Error(int status, string statusText)
    {
        // For error bodies we keep the message in a MemoryStream; they are infrequent and small.
        var content = new MemoryStream(Encoding.UTF8.GetBytes($"{status} {statusText}"));
        return new LocalWebAsset(
            status,
            statusText,
            "text/plain",
            "no-store",
            content.Length,
            content);
    }

    public void Dispose()
    {
        // Successful Resolve calls hand a fresh FileStream back to CEF; disposing here would close it
        // before CEF reads from it. The provider does not currently cache any long-lived stream so
        // there is nothing to release today; keep the IDisposable hook so future caching providers
        // can plug in without changing the contract.
    }
}

internal sealed record LocalWebAsset(
    int Status,
    string StatusText,
    string MimeType,
    string CacheControl,
    long ResponseLength,
    Stream Content) : IDisposable
{
    public void Dispose() => Content.Dispose();
}

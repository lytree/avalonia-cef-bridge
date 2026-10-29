using System.Text;
using Tarui.Ipc;
using Tarui.WebView.Abstractions;

namespace Tarui.Shell;

/// <summary>
/// Serves files under an explicit allow-list of roots through the <c>asset://localhost/</c> protocol.
/// Every request is decoded into an absolute file path and then authorized twice: it must lie under
/// one of the configured roots (lexical containment), and the path is passed through
/// <see cref="IFileAccessPolicy"/> so symbolic links and reparse points cannot escape that root.
/// Without a configured root nothing is served and the scheme is not registered at all.
/// </summary>
public sealed class FileAssetResolver : ITaruiAssetResolver
{
    private const string SchemeName = "asset";
    private const string DomainName = "localhost";

    private static readonly IReadOnlyDictionary<string, string> MimeTypes =
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
            [".mp4"] = "video/mp4",
        };

    private readonly IFileAccessPolicy? _policy;
    private readonly string[] _roots;
    private readonly long _maxAssetBytes;

    public FileAssetResolver(
        IEnumerable<string>? roots,
        long maxAssetBytes = 64L * 1024L * 1024L,
        IFileAccessPolicy? policy = null)
    {
        if (maxAssetBytes <= 0)
        {
            throw new InvalidOperationException("Asset max bytes must be a positive integer.");
        }

        _policy = policy;
        _maxAssetBytes = maxAssetBytes;
        _roots = (roots ?? [])
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(NormalizeRoot)
            .Distinct(PathComparer)
            .ToArray();
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public bool IsEnabled => _roots.Length > 0;

    public TaruiAssetResponse Resolve(string requestUrl, string method)
    {
        if (!IsEnabled)
        {
            return Error(404, "Not Found");
        }

        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return Error(405, "Method Not Allowed");
        }

        if (!TryDecodePath(requestUrl, out var path))
        {
            return Error(403, "Forbidden");
        }

        if (!TryResolveCandidate(path, out var candidate))
        {
            return Error(403, "Forbidden");
        }

        if (!File.Exists(candidate))
        {
            return Error(404, "Not Found");
        }

        var fileInfo = new FileInfo(candidate);
        if (fileInfo.Length > _maxAssetBytes)
        {
            return Error(413, "Content Too Large");
        }

        Stream content = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
            ? Stream.Null
            : new FileStream(
                candidate,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

        var mimeType = MimeTypes.GetValueOrDefault(
            Path.GetExtension(candidate),
            "application/octet-stream");
        return new TaruiAssetResponse(
            200,
            "OK",
            mimeType,
            "no-cache",
            fileInfo.Length,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Content-Type-Options"] = "nosniff",
            },
            content);
    }

    private bool TryResolveCandidate(string path, out string candidate)
    {
        candidate = string.Empty;
        if (!Path.IsPathRooted(path))
        {
            return false;
        }

        foreach (var root in _roots)
        {
            var relative = Path.GetRelativePath(root, path);
            if (!IsWithin(relative))
            {
                continue;
            }

            if (relative == ".")
            {
                // convertFileSrc addresses files, never a directory root.
                return false;
            }

            if (_policy is not null)
            {
                try
                {
                    candidate = _policy.Authorize(FileAccessKind.Read, root, relative);
                    return true;
                }
                catch (PathAccessDeniedException)
                {
                    // A link escape or illegal segment rejects this root; another root may still own it.
                    continue;
                }
            }

            candidate = Path.GetFullPath(path);
            return true;
        }

        return false;
    }

    private static bool IsWithin(string relative)
    {
        if (relative == "." || relative.Length == 0)
        {
            return true;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return !Path.IsPathRooted(relative)
            && !relative.Equals("..", comparison)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, comparison)
            && !relative.StartsWith("../", comparison);
    }

    /// <summary>
    /// Decodes <c>asset://localhost/&lt;percent-encoded absolute path&gt;</c> into the absolute path
    /// carried by the request. Anything malformed, non-local, or non-rooted is rejected.
    /// </summary>
    private static bool TryDecodePath(string requestUrl, out string path)
    {
        path = string.Empty;
        var schemeSeparator = requestUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0 ||
            !string.Equals(requestUrl[..schemeSeparator], SchemeName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var authorityStart = schemeSeparator + 3;
        var pathStart = requestUrl.IndexOf('/', authorityStart);
        if (pathStart < 0)
        {
            return false;
        }

        var authority = requestUrl[authorityStart..pathStart];
        if (!string.Equals(authority, DomainName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawPath = requestUrl[pathStart..];
        var queryStart = rawPath.IndexOfAny(['?', '#']);
        if (queryStart >= 0)
        {
            rawPath = rawPath[..queryStart];
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(rawPath.TrimStart('/'));
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (decoded.Length == 0 || decoded.Contains('\0') || decoded.Any(char.IsControl))
        {
            return false;
        }

        path = decoded;
        return true;
    }

    private static string NormalizeRoot(string root)
    {
        var trimmed = root.Trim();
        if (!Path.IsPathRooted(trimmed))
        {
            throw new InvalidOperationException(
                $"Asset root '{root}' must be an absolute directory path.");
        }

        var full = Path.GetFullPath(trimmed);
        // Keep a drive/filesystem root intact ("C:\" must not become the drive-relative "C:").
        return full.Length > (Path.GetPathRoot(full)?.Length ?? 0)
            ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : full;
    }

    private static TaruiAssetResponse Error(int status, string statusText)
    {
        var body = Encoding.UTF8.GetBytes($"{status} {statusText}");
        return new TaruiAssetResponse(
            status,
            statusText,
            "text/plain",
            "no-store",
            body.LongLength,
            ContentStream: new MemoryStream(body));
    }
}

using Avalonia;

namespace CefGlue.Next.Avalonia;

public enum CefGlueNextAvaloniaNavigationDecision
{
    Deny,
    Allow,
    External
}

public enum CefGlueNextAvaloniaDownloadDecision
{
    Deny,
    Allow
}

public interface ICefGlueNextAvaloniaResourceProvider
{
    CefGlueNextAvaloniaResourceResponse Resolve(CefGlueNextAvaloniaResourceRequest request);
}

public sealed record CefGlueNextAvaloniaResourceRequest(
    string Url,
    string Method,
    bool IsMainFrame,
    bool IsMainFrameResource);

public sealed record CefGlueNextAvaloniaResourceResponse(
    int Status,
    string StatusText,
    string MimeType,
    string CacheControl,
    long ResponseLength,
    byte[] Content,
    IReadOnlyDictionary<string, string>? Headers = null,
    // When supplied, the handler streams this Stream straight to CEF instead of materializing the
    // byte[] into a MemoryStream. Large assets (videos, models, map tiles) can therefore be served
    // from disk without doubling their resident memory; the caller owns the Stream lifetime.
    Stream? ContentStream = null);

public sealed class CefGlueNextAvaloniaSchemeOptions
{
    public required string SchemeName { get; init; }

    public string? DomainName { get; init; }

    public required ICefGlueNextAvaloniaResourceProvider ResourceProvider { get; init; }

    public bool IsStandard { get; init; } = true;

    public bool IsLocal { get; init; }

    public bool IsDisplayIsolated { get; init; }

    public bool IsSecure { get; init; } = true;

    public bool IsCorsEnabled { get; init; }

    public bool IsCspBypassing { get; init; }

    public bool IsFetchEnabled { get; init; } = true;
}

public sealed class CefGlueNextAvaloniaNavigationRequestedEventArgs : EventArgs
{
    public CefGlueNextAvaloniaNavigationRequestedEventArgs(Uri uri, bool isMainFrame, bool userGesture, bool isRedirect)
    {
        Uri = uri;
        IsMainFrame = isMainFrame;
        UserGesture = userGesture;
        IsRedirect = isRedirect;
    }

    public Uri Uri { get; }

    public bool IsMainFrame { get; }

    public bool UserGesture { get; }

    public bool IsRedirect { get; }

    public CefGlueNextAvaloniaNavigationDecision Decision { get; set; }
}

public sealed class CefGlueNextAvaloniaExternalNavigationEventArgs : EventArgs
{
    public CefGlueNextAvaloniaExternalNavigationEventArgs(Uri uri)
    {
        Uri = uri;
    }

    public Uri Uri { get; }

    public bool Handled { get; set; }
}

public sealed class CefGlueNextAvaloniaDownloadRequestedEventArgs : EventArgs
{
    public CefGlueNextAvaloniaDownloadRequestedEventArgs(Uri uri, string? suggestedFileName)
    {
        Uri = uri;
        SuggestedFileName = suggestedFileName;
    }

    public Uri Uri { get; }

    public string? SuggestedFileName { get; }

    public CefGlueNextAvaloniaDownloadDecision Decision { get; set; }

    public string? FilePath { get; set; }

    public bool ShowDialog { get; set; } = true;
}

public sealed class CefGlueNextAvaloniaFileDropEventArgs : EventArgs
{
    public CefGlueNextAvaloniaFileDropEventArgs(IReadOnlyList<string> paths, string? text, Point position)
    {
        Paths = paths;
        Text = text;
        Position = position;
    }

    public IReadOnlyList<string> Paths { get; }

    public string? Text { get; }

    public Point Position { get; }

    public bool Accepted { get; set; }
}

public sealed class CefGlueNextAvaloniaDragRegionsUpdatedEventArgs : EventArgs
{
    public CefGlueNextAvaloniaDragRegionsUpdatedEventArgs(IReadOnlyList<CefGlueNextAvaloniaDraggableRegion> regions)
    {
        Regions = regions;
    }

    public IReadOnlyList<CefGlueNextAvaloniaDraggableRegion> Regions { get; }
}

public readonly record struct CefGlueNextAvaloniaDraggableRegion(
    int X,
    int Y,
    int Width,
    int Height,
    bool IsDraggable);

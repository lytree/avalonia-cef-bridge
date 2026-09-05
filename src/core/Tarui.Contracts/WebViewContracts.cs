namespace Tarui.Contracts;

/// <summary>A web view that may be addressed independently of its host window.</summary>
public sealed record WebviewLabelOptions(string? Label = null);

/// <summary>Navigates a web view to a resolved, same-origin URL.</summary>
public sealed record WebviewNavigateOptions(string Url, string? Label = null);

/// <summary>Opens (<see cref="Open"/> = true) or closes the browser developer tools of a web view.</summary>
public sealed record WebviewDevToolsOptions(bool Open = true, string? Label = null);

/// <summary>The observable state of a web view and its host window.</summary>
public sealed record WebviewStateInfo(
    string Label,
    string WindowLabel,
    string? Url,
    string Title);

/// <summary>The labels of every live web view (one per window for now).</summary>
public sealed record WebviewLabels(string[] Labels);

/// <summary>Reserved <c>webview://file-drop-*</c> payload describing a drag over the web view surface.</summary>
public sealed record WebViewFileDropEvent(string[] Paths, string? Text, double X, double Y);

/// <summary>Reserved <c>webview://download-requested</c> payload for an authorized download.</summary>
public sealed record WebViewDownloadRequestEvent(string Url, string? SuggestedFilename);

/// <summary>Reserved <c>webview://navigation-requested</c> payload for an authorized navigation.</summary>
public sealed record WebViewNavigationRequestEvent(string Url, bool IsMainFrame);
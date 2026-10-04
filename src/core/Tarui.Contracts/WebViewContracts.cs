namespace Tarui.Contracts;

/// <summary>A web view that may be addressed independently of its host window.</summary>
public sealed record WebviewLabelOptions(string? Label = null);

/// <summary>Navigates a web view to a resolved, same-origin URL.</summary>
public sealed record WebviewNavigateOptions(string Url, string? Label = null);

/// <summary>Opens (<see cref="Open"/> = true) or closes the browser developer tools of a web view.</summary>
public sealed record WebviewDevToolsOptions(bool Open = true, string? Label = null);

/// <summary>Sets the page zoom factor for a web view (1.0 = 100%).</summary>
public sealed record WebviewSetZoomOptions(double Factor, string? Label = null);

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

/// <summary>Reserved <c>webview://render-process-gone</c> payload describing a renderer termination.</summary>
public sealed record WebViewRenderProcessGoneEvent(string Status, int ErrorCode, string? Error);

/// <summary>Runs a script in the target web view's main frame (fire-and-forget, no completion value).</summary>
public sealed record WebviewEvalOptions(string Script, string? Label = null);

/// <summary>
/// Runs a script in the target web view and delivers its completion outcome (JSON-encoded value or
/// error message) to the channel identified by <see cref="OnEvent"/>. The channel receives exactly
/// one <see cref="WebviewEvalFrame"/>.
/// </summary>
public sealed record WebviewEvalCallbackOptions(string Script, string? OnEvent = null, string? Label = null);

/// <summary>
/// Internal completion receipt posted by an evaluated script back into the shell. The <see cref="Id"/>
/// carries the caller's channel token; gating reuses the <c>plugin:webview|eval-with-callback</c>
/// permission so only windows that may start callbacks may complete them.
/// </summary>
public sealed record WebviewEvalCompleteOptions(string Id, bool Ok, string? Value = null, string? Error = null);

/// <summary>One completion frame delivered to a <c>plugin:webview|eval-with-callback</c> channel.</summary>
public sealed record WebviewEvalFrame(bool Ok, string? Value = null, string? Error = null);
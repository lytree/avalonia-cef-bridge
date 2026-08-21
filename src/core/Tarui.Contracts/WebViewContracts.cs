namespace Tarui.Contracts;

/// <summary>
/// Payload delivered to <c>window://file-drop-entered</c> and <c>window://file-dropped</c>.
/// <c>Paths</c> carries absolute file paths (deduplicated, file-only) and <c>Text</c> an optional
/// plain-text fragment from the drag payload; <c>X</c>/<c>Y</c> are control-relative logical pixels.
/// </summary>
public sealed record WebViewFileDropEvent(
    string[] Paths,
    string? Text,
    double X,
    double Y);

/// <summary>
/// Payload delivered to <c>webview://download-requested</c> when the host observes a download. A real
/// download is only started after host policy validation.
/// </summary>
public sealed record WebViewDownloadRequestEvent(
    string Url,
    string? SuggestedFilename);

/// <summary>
/// Payload delivered to <c>webview://navigation-requested</c> for main-frame navigations that reach
/// the host policy. A navigation is only started when the host policy returns
/// <see cref="WebViewRequestDecision.Allow"/>; <see cref="WebViewRequestDecision.External"/> hands it
/// to the OS default handler.
/// </summary>
public sealed record WebViewNavigationRequestEvent(
    string Url,
    bool IsMainFrame);
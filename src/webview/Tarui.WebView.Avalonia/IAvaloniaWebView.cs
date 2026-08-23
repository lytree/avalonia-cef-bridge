using Avalonia.Controls;
using Tarui.WebView.Abstractions;

namespace Tarui.WebView.Avalonia;

/// <summary>
/// Adds the Avalonia control used to present a web view to the UI-neutral web view contract.
/// </summary>
public interface ITaruiAvaloniaWebView : ITaruiWebView
{
    /// <summary>The Avalonia control that hosts the browser surface.</summary>
    Control Control { get; }
}

/// <summary>Creates Avalonia-backed web views without exposing their browser implementation.</summary>
public interface ITaruiAvaloniaWebViewFactory
{
    /// <summary>Creates an Avalonia-backed web view using the supplied options.</summary>
    ITaruiAvaloniaWebView Create(TaruiWebViewOptions options);
}

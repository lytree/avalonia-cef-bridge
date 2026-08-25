namespace Tarui.Shell;

/// <summary>
/// An explicit, compile-time extension point that contributes native Avalonia controls to a window and
/// tracks the window lifecycle so it can react to display and tear-down. Registered via
/// <c>AddWindowExtension&lt;T&gt;()</c>, an extension is instantiated for each window that matches its optional
/// label filter and is handed a <see cref="WindowExtensionContext"/> on the UI thread before the window is
/// shown. Extensions place their controls onto the regions of the composition facade, must not replace or
/// clear the web view content slot, and can opt into lifecycle cleanup by implementing
/// <see cref="System.IDisposable"/> or <see cref="System.IAsyncDisposable"/>.
/// </summary>
public interface IShellWindowExtension
{
    /// <summary>Builds and mounts the extension's controls for the window described by <paramref name="context"/>.</summary>
    void CreateView(WindowExtensionContext context);

    /// <summary>Raised once, after the owning window has been shown. Extensions may finalize display-dependent state here.</summary>
    void OnWindowLoaded(WindowExtensionContext context)
    {
    }

    /// <summary>Raised as the owning window closes. Extensions must release native resources here.</summary>
    void OnWindowClosed(WindowExtensionContext context)
    {
    }
}
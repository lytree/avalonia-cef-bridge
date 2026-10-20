using Tarui.WebView.Abstractions;

namespace Tarui.WebView.CefGlueNext;

/// <summary>
/// The CEF-backed <see cref="ITaruiWebRuntime"/>. It is the application-facing runtime identity for
/// an app that selects the CefGlue web view: it reports the engine version, the application origin
/// and the asset resource mode, and forwards lifecycle calls to the low-level
/// <see cref="CefGlueRuntimeBootstrap"/> / <see cref="CefGlueNextAvaloniaRuntime"/>.
/// <para>
/// Constructing this type does not initialize CEF. Native startup still happens on the UI thread when
/// the web view backend is first resolved (the documented lifecycle order), so resolving the runtime
/// identity from DI — for example to answer <c>plugin:webview|runtime-info</c> — is side-effect free.
/// </para>
/// </summary>
public sealed class CefGlueNextWebRuntime : ITaruiWebRuntime
{
    private readonly CefGlueNextWebAppOptions _options;
    private readonly CefWebRuntimeExtension _extension = new();

    public CefGlueNextWebRuntime(CefGlueNextWebAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        Origin = new TaruiAppOrigin(options.StartUri, options.AllowedSchemes, options.SchemeOrigin);
    }

    /// <inheritdoc />
    public string Name => "cef";

    /// <inheritdoc />
    public string WebviewVersion => CefGlueNextPortInfo.CefVersion;

    /// <inheritdoc />
    public TaruiAppOrigin Origin { get; }

    /// <inheritdoc />
    public string ResourceMode => _options.Mode == TaruiWebResourceMode.Scheme ? "scheme" : "http";

    /// <summary>
    /// Exposes the CEF-specific surface (<see cref="CefWebRuntimeExtension"/>). Any other requested
    /// extension reports <see langword="false"/> so the caller surfaces
    /// <c>RUNTIME_TYPE_MISMATCH</c> rather than silently degrading.
    /// </summary>
    public bool TryGetExtension<TExtension>(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TExtension? extension)
        where TExtension : class, ITaruiRuntimeExtension
    {
        if (_extension is TExtension typed)
        {
            extension = typed;
            return true;
        }

        extension = null;
        return false;
    }

    /// <inheritdoc />
    public ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CefGlueRuntimeBootstrap.Initialize(_options);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ShutdownAsync()
    {
        CefGlueNextAvaloniaRuntime.Shutdown();
        return ValueTask.CompletedTask;
    }
}

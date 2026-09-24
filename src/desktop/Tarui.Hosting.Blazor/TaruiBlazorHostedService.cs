using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tarui.WebView.CefGlueNext;

namespace Tarui.Hosting.Blazor;

/// <summary>
/// Creates the in-process <see cref="TaruiBlazorWebViewManager"/> when the Tarui host starts, so
/// the Blazor renderer exists before any window opens. The manager binds to a web view later —
/// when the shell's <see cref="CefGlueNextWebViewFactory"/> creates one — via the factory's
/// <c>WebViewCreated</c> event.
/// </summary>
internal sealed partial class TaruiBlazorHybridHostedService(
    IServiceProvider services,
    TaruiBlazorOptions options,
    TaruiBlazorHybridState state,
    CefGlueNextWebViewFactory webViewFactory,
    ILogger<TaruiBlazorHybridHostedService> logger) : IHostedService, IDisposable
{
    private readonly object _gate = new();
    private TaruiBlazorWebViewManager? _manager;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.RootComponent is null)
        {
            throw new InvalidOperationException(
                "TaruiBlazorOptions.RootComponent must be set before AddTaruiBlazor() is called. " +
                "Pass the Blazor root component type explicitly so the host stays reflection-free.");
        }

        if (!typeof(Microsoft.AspNetCore.Components.ComponentBase).IsAssignableFrom(options.RootComponent))
        {
            throw new InvalidOperationException(
                $"TaruiBlazorOptions.RootComponent must derive from ComponentBase, but was " +
                $"'{options.RootComponent.FullName}'.");
        }

        var contentRoot = options.ResolveContentRoot();
        if (!Directory.Exists(contentRoot) ||
            !File.Exists(Path.Combine(contentRoot, options.HostPageRelativePath)))
        {
            throw new DirectoryNotFoundException(
                $"The Blazor content root '{contentRoot}' does not contain the host page " +
                $"'{options.HostPageRelativePath}'. Point TaruiBlazorOptions.ContentRoot at the " +
                "directory published with the application (typically wwwroot).");
        }

        var fileProvider = new PhysicalFileProvider(contentRoot);
        var manager = new TaruiBlazorWebViewManager(
            services,
            state,
            options.ResolveAppBaseUri(),
            fileProvider,
            options.HostPageRelativePath);

        lock (_gate)
        {
            _manager = manager;
        }

        state.SetManager(manager);

        // Queue the root component: it renders as soon as the first page attaches through
        // blazor.webview.js. ParameterView.Empty keeps startup deterministic; the demo passes
        // no root parameters.
        var rootTask = manager.AddRootComponentAsync(
            options.RootComponent,
            options.RootComponentSelector,
            Microsoft.AspNetCore.Components.ParameterView.Empty);
        ObserveRootTask(rootTask);

        webViewFactory.WebViewCreated += OnWebViewCreated;
        var hostPageUri = options.ResolveHostPageUri();
        var selector = options.RootComponentSelector;
        LogRendererStarted(logger, hostPageUri, selector);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        webViewFactory.WebViewCreated -= OnWebViewCreated;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        TaruiBlazorWebViewManager? manager;
        lock (_gate)
        {
            manager = _manager;
            _manager = null;
        }

        manager?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void OnWebViewCreated(object? sender, CefGlueNextWebViewCreatedEventArgs args)
    {
        // Multiple windows share the single in-process renderer; the most recently created web
        // view wins, mirroring how a single-circuit Blazor host behaves.
        state.AttachWebView(args.WebView);
    }

    private async void ObserveRootTask(Task rootTask)
    {
        try
        {
            await rootTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogRootComponentFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Tarui Blazor Hybrid renderer started; components will mount at {HostPageUri} via selector {Selector}.")]
    private static partial void LogRendererStarted(ILogger logger, Uri hostPageUri, string selector);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Tarui Blazor Hybrid failed to register the root component.")]
    private static partial void LogRootComponentFailed(ILogger logger, Exception exception);
}

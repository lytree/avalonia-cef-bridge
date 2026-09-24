using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tarui.Hosting.Blazor;

/// <summary>
/// Hosts an ASP.NET Core server inside the Tarui process. Built once by <c>AddTaruiBlazor</c> and
/// started before the main window opens so the CEF WebView can navigate to its base URL.
/// </summary>
public sealed partial class TaruiBlazorServer : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly TaruiBlazorOptions _options;
    private readonly ILogger<TaruiBlazorServer> _logger;

    public TaruiBlazorServer(IHost host, TaruiBlazorOptions options, ILogger<TaruiBlazorServer> logger)
    {
        _host = host;
        _options = options;
        _logger = logger;
    }

    /// <summary>The bound address — port <c>0</c> is resolved to the actual listener port.</summary>
    public Uri? ListeningUri { get; private set; }

    /// <summary>The URL the CEF WebView should navigate to (always the root path on the bound host).</summary>
    public Uri StartUri { get; private set; } = new("http://127.0.0.1/", UriKind.Absolute);

    /// <summary>Starts the embedded server and resolves the listener URL.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _host.StartAsync(cancellationToken).ConfigureAwait(false);
        ListeningUri = ResolveListeningUri();
        StartUri = _options.ResolveStartUri is { } resolver
            ? resolver(ListeningUri.Port)
            : new UriBuilder(ListeningUri) { Path = _options.RootPath }.Uri;
        _options.OnListening?.Invoke(StartUri);
        LogListening(_logger, ListeningUri, StartUri);
    }

    /// <summary>Stops the embedded server; safe to call multiple times.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default) => _host.StopAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogStopFailed(_logger, exception);
        }

        _host.Dispose();
    }

    private Uri ResolveListeningUri()
    {
        var server = _host.Services.GetRequiredService<IServer>();
        var features = server.Features.Get<IServerAddressesFeature>();
        var address = features?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Tarui Blazor server did not report a listening address.");
        return new Uri(address);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Tarui Blazor server is listening on {ListeningUri}; window URL is {StartUri}.")]
    private static partial void LogListening(ILogger logger, Uri listeningUri, Uri startUri);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Tarui Blazor server did not stop cleanly during disposal.")]
    private static partial void LogStopFailed(ILogger logger, Exception exception);
}

/// <summary>
/// ASP.NET Core host that hosts the user's Blazor application alongside the Tarui IPC bridge.
/// Mirrors the typical <c>WebApplication.CreateBuilder</c> flow but keeps the server loopback-bound
/// so the CEF WebView (running in the same process) is the only legitimate client.
/// </summary>
internal static class TaruiBlazorHostFactory
{
    public static IHost Create(TaruiBlazorOptions options, Action<WebApplication>? configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.RootComponent is null)
        {
            throw new InvalidOperationException(
                "TaruiBlazorOptions.RootComponent must be set before AddTaruiBlazor() is called. " +
                "Pass the Blazor root component type explicitly so the host stays reflection-free.");
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(BuildListenUrls(options));
        ApplySettings(builder.WebHost, options.WebHostSettings);

        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddSingleton(new TaruiBlazorRootComponent(options.RootComponent));
        builder.Services.AddCascadingValue(sp => sp.GetRequiredService<ITaruiIpc>());

        var app = builder.Build();

        app.UseRouting();
        app.UseAntiforgery();
        app.MapRazorComponents<TaruiBlazorAppShell>()
            .AddInteractiveServerRenderMode()
            .AddAdditionalAssemblies(new[] { options.RootComponent.Assembly });

        configure?.Invoke(app);

        return app;
    }

    private static string[] BuildListenUrls(TaruiBlazorOptions options) =>
        [FormattableString.Invariant($"http://{options.Host}:{options.Port}")];

    private static void ApplySettings(ConfigureWebHostBuilder builder, IDictionary<string, string?> settings)
    {
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
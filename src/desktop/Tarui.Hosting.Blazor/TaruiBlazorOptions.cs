using Microsoft.AspNetCore.Hosting;

namespace Tarui.Hosting.Blazor;

/// <summary>
/// Options that control how the in-process ASP.NET Core server hosts a Blazor application inside a
/// Tarui desktop window. Defaults are tuned for "Blazor Hybrid inside Tarui" — server-rendered HTML
/// over loopback HTTP, no public listener, no forwarded headers.
/// </summary>
public sealed class TaruiBlazorOptions
{
    /// <summary>The TCP port the embedded Kestrel listener binds to. Use <c>0</c> for OS-assigned.</summary>
    public int Port { get; set; }

    /// <summary>The host name the embedded Kestrel listener binds to. Defaults to loopback only.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>The relative path the Blazor root component is mounted at. Defaults to <c>/</c>.</summary>
    public string RootPath { get; set; } = "/";

    /// <summary>
    /// The Blazor application root component type (a <c>ComponentBase</c>-derived class). Required.
    /// Tarui.Hosting.Blazor never reflects over assemblies to discover it; the host application must
    /// supply it explicitly so the runtime stays reflection-free.
    /// </summary>
    public Type? RootComponent { get; set; }

    /// <summary>Optional callback invoked once the listener is bound; receives the absolute URL the CEF WebView should navigate to.</summary>
    public Action<Uri>? OnListening { get; set; }

    /// <summary>Forwarded to <see cref="IWebHostBuilder.UseSetting"/> verbatim. Use to override ASP.NET Core defaults from Tarui's configuration.</summary>
    public IDictionary<string, string?> WebHostSettings { get; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When <c>true</c>, the embedded server only listens on the configured loopback port and never
    /// registers a public certificate. This is the safe default for desktop windows; turn it off only
    /// when intentionally exposing the Blazor server to the wider network.
    /// </summary>
    public bool LoopbackOnly { get; set; } = true;

    /// <summary>
    /// Resolves the absolute URL the window should navigate to. Falls back to
    /// <c>http://{Host}:{Port}{RootPath}</c>. Callers may override to inject a dev server URL when
    /// iterating on the front-end from <c>tarui dev</c>.
    /// </summary>
    public Func<int, Uri>? ResolveStartUri { get; set; }
}
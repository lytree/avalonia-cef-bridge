namespace Tarui.Hosting.Blazor;

using Tarui.WebView.CefGlueNext;

/// <summary>
/// Options that control how <see cref="TaruiBlazorServiceCollectionExtensions.AddTaruiBlazor"/>
/// hosts a Blazor Hybrid application inside a Tarui desktop window. The component tree runs
/// in-process (no HTTP server); the <c>tarui://</c> custom scheme serves the host page and static
/// web assets, and interop frames travel over the private "__taruiHybrid" web view channel.
/// </summary>
public sealed class TaruiBlazorOptions
{
    /// <summary>
    /// The Blazor application root component type (a <c>ComponentBase</c>-derived class). Required.
    /// Tarui.Hosting.Blazor never reflects over assemblies to discover it; the host application must
    /// supply it explicitly so the runtime stays reflection-free.
    /// </summary>
    public Type? RootComponent { get; set; }

    /// <summary>
    /// CSS selector inside the host page where the root component is mounted. Defaults to
    /// <c>#app</c>; the host page must contain a matching element.
    /// </summary>
    public string RootComponentSelector { get; set; } = "#app";

    /// <summary>
    /// Filesystem directory served as the application root (the Blazor host page plus any static
    /// content). Defaults to <c>wwwroot</c> next to the application executable. Must contain the
    /// host page referenced by <see cref="HostPageRelativePath"/>.
    /// </summary>
    public string? ContentRoot { get; set; }

    /// <summary>
    /// Path to the host page relative to <see cref="ContentRoot"/>. Defaults to <c>index.html</c>.
    /// </summary>
    public string HostPageRelativePath { get; set; } = "index.html";

    /// <summary>The custom scheme the application is served over. Defaults to <c>tarui</c>.</summary>
    public string SchemeName { get; set; } = CefGlueNextWebAppOptions.DefaultSchemeName;

    /// <summary>The custom scheme domain. Defaults to <c>localhost</c>.</summary>
    public string DomainName { get; set; } = CefGlueNextWebAppOptions.DefaultDomainName;

    /// <summary>
    /// When <c>true</c> (default), main-frame requests that do not match a file fall back to the
    /// host page so client-side routes can be deep-linked.
    /// </summary>
    public bool SpaFallback { get; set; } = true;

    /// <summary>
    /// Content-Security-Policy header applied to scheme responses. Defaults to the shared Tarui
    /// policy (same-origin scripts and styles, no remote frames).
    /// </summary>
    public string? ContentSecurityPolicy { get; set; }

    internal string ResolveContentRoot()
    {
        var root = ContentRoot ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
        return Path.GetFullPath(root);
    }

    internal Uri ResolveAppBaseUri() =>
        new($"{SchemeName}://{DomainName}/", UriKind.Absolute);

    internal Uri ResolveHostPageUri() =>
        new(ResolveAppBaseUri(), HostPageRelativePath);
}

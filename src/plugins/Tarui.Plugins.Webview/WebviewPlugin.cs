using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Webview;

public sealed class WebviewPlugin(IWebviewService service) : ITaruiPlugin
{
    private static readonly string[] OtherWebviewPermissions =
    [
        "plugin:webview|navigate",
        "plugin:webview|get-state",
        "plugin:webview|devtools"
    ];

    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        var handlers = new WebviewCommands(service);

        commands.Add(
            "plugin:webview|navigate",
            TaruiJsonContext.Default.WebviewNavigateOptions,
            TaruiJsonContext.Default.Unit,
            handlers.NavigateAsync,
            "plugin:webview|navigate");

        commands.Add(
            "plugin:webview|get-state",
            TaruiJsonContext.Default.WebviewLabelOptions,
            TaruiJsonContext.Default.WebviewStateInfo,
            handlers.GetStateAsync,
            "plugin:webview|get-state");

        commands.Add(
            "plugin:webview|devtools",
            TaruiJsonContext.Default.WebviewDevToolsOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetDevToolsAsync,
            "plugin:webview|devtools");

        commands.Add(
            "plugin:webview|list",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.WebviewLabels,
            handlers.ListAsync,
            "plugin:webview|list");

        // Cross-webview operations require the <permission>-other-webview variant; register them as
        // valid permission IDs so capability files may reference them and validation stays strict.
        foreach (var permission in OtherWebviewPermissions)
        {
            commands.AddPermission(WebviewPermissionGuard.OtherWebviewPermission(permission));
        }
    }

    private sealed class WebviewCommands(IWebviewService service)
    {
        [TaruiCommand("plugin:webview|navigate")]
        public ValueTask<Unit> NavigateAsync(
            WebviewNavigateOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.NavigateAsync(Resolve(options.Label, context, "plugin:webview|navigate"), options.Url, cancellationToken);

        [TaruiCommand("plugin:webview|get-state")]
        public ValueTask<WebviewStateInfo> GetStateAsync(
            WebviewLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.GetStateAsync(Resolve(options.Label, context, "plugin:webview|get-state"), cancellationToken);

        [TaruiCommand("plugin:webview|devtools")]
        public ValueTask<Unit> SetDevToolsAsync(
            WebviewDevToolsOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetDevToolsAsync(Resolve(options.Label, context, "plugin:webview|devtools"), options.Open, cancellationToken);

        [TaruiCommand("plugin:webview|list")]
        public async ValueTask<WebviewLabels> ListAsync(
            EmptyArgs options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            var labels = await service.ListAsync(cancellationToken);
            return new WebviewLabels([.. labels]);
        }

        private static string Resolve(string? requested, CommandContext context, string permission)
        {
            var label = requested ?? context.WebViewLabel;
            WebviewPermissionGuard.EnsureOwnOrOtherWebview(context, label, permission);
            return label;
        }
    }
}

public static class WebviewPluginServiceCollectionExtensions
{
    public static IServiceCollection AddWebviewPlugin(this IServiceCollection services)
        => services.AddPlugin<WebviewPlugin>();
}
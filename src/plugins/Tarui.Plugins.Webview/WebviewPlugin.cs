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
        "plugin:webview|devtools",
        "plugin:webview|eval",
        "plugin:webview|eval-with-callback",
        "plugin:webview|set-zoom",
        "plugin:webview|print"
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
            "plugin:webview|eval",
            TaruiJsonContext.Default.WebviewEvalOptions,
            TaruiJsonContext.Default.Unit,
            handlers.EvalAsync,
            "plugin:webview|eval");

        // Eval-with-callback is the callback form of eval: the shell wraps the script so its completion
        // (JSON-encoded value or error) is posted back through the caller's channel token. The internal
        // completion command shares the eval-with-callback permission id — only a window that may start
        // a callback may complete one, and evaluated code runs with the same window's authority.
        commands.Add(
            "plugin:webview|eval-with-callback",
            TaruiJsonContext.Default.WebviewEvalCallbackOptions,
            TaruiJsonContext.Default.Unit,
            handlers.EvalWithCallbackAsync,
            "plugin:webview|eval-with-callback");

        commands.Add(
            "plugin:webview|set-zoom",
            TaruiJsonContext.Default.WebviewSetZoomOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetZoomAsync,
            "plugin:webview|set-zoom");

        commands.Add(
            "plugin:webview|print",
            TaruiJsonContext.Default.WebviewLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.PrintAsync,
            "plugin:webview|print");

        commands.Add(
            "plugin:webview|eval-callback-complete",
            TaruiJsonContext.Default.WebviewEvalCompleteOptions,
            TaruiJsonContext.Default.Unit,
            WebviewCommands.EvalCallbackCompleteAsync,
            "plugin:webview|eval-with-callback");

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

        [TaruiCommand("plugin:webview|eval")]
        public ValueTask<Unit> EvalAsync(
            WebviewEvalOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.EvalAsync(Resolve(options.Label, context, "plugin:webview|eval"), options.Script, cancellationToken);

        [TaruiCommand("plugin:webview|eval-with-callback")]
        public ValueTask<Unit> EvalWithCallbackAsync(
            WebviewEvalCallbackOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.EvalWithCallbackAsync(
                Resolve(options.Label, context, "plugin:webview|eval-with-callback"),
                options.Script,
                options.OnEvent,
                cancellationToken);

        [TaruiCommand("plugin:webview|set-zoom")]
        public ValueTask<Unit> SetZoomAsync(
            WebviewSetZoomOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetZoomAsync(Resolve(options.Label, context, "plugin:webview|set-zoom"), options.Factor, cancellationToken);

        [TaruiCommand("plugin:webview|print")]
        public ValueTask<Unit> PrintAsync(
            WebviewLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.PrintAsync(Resolve(options.Label, context, "plugin:webview|print"), cancellationToken);

        [TaruiCommand("plugin:webview|eval-callback-complete")]
        public static async ValueTask<Unit> EvalCallbackCompleteAsync(
            WebviewEvalCompleteOptions options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            var channel = ChannelContext.Bind<WebviewEvalFrame>(options.Id);
            await channel.SendAsync(new WebviewEvalFrame(options.Ok, options.Value, options.Error), cancellationToken);
            return new Unit();
        }

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
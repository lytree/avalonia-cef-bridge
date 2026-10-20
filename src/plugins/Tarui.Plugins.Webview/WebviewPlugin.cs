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
        "plugin:webview|print",
        "plugin:webview|set-bounds",
        "plugin:webview|destroy",
        "plugin:webview|send-devtools-message"
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

        // The runtime identity is app-wide (name, engine version, resource mode, origin), so it is
        // neither label-scoped nor routed through the own-or-other web view guard.
        commands.Add(
            "plugin:webview|runtime-info",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.WebviewRuntimeInfo,
            handlers.GetRuntimeInfoAsync,
            "plugin:webview|runtime-info");

        // Mounting capability: create a surface inside an existing window, reposition it, or destroy it.
        commands.Add(
            "plugin:webview|create",
            TaruiJsonContext.Default.WebviewCreateOptions,
            TaruiJsonContext.Default.WebviewStateInfo,
            handlers.CreateAsync,
            "plugin:webview|create");

        commands.Add(
            "plugin:webview|set-bounds",
            TaruiJsonContext.Default.WebviewSetBoundsOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetBoundsAsync,
            "plugin:webview|set-bounds");

        commands.Add(
            "plugin:webview|destroy",
            TaruiJsonContext.Default.WebviewLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.DestroyAsync,
            "plugin:webview|destroy");

        // The DevTools protocol channel is capability-gated and off by default: sending CDP messages
        // reaches the browser's protocol surface, so only a window that opts in may drive it.
        commands.Add(
            "plugin:webview|send-devtools-message",
            TaruiJsonContext.Default.WebviewDevToolsMessageOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SendDevToolsMessageAsync,
            "plugin:webview|send-devtools-message");

        // The CDP channel stays opt-in even when a capability grants the whole webview surface, so it
        // is kept out of the materialized "plugin:webview|default" set and must be named explicitly.
        commands.ExcludeFromDefaultSet("plugin:webview|send-devtools-message");

        // Cross-webview operations require the <permission>-other-webview variant; register them as
        // valid permission IDs so capability files may reference them and validation stays strict.
        foreach (var permission in OtherWebviewPermissions)
        {
            commands.AddPermission(WebviewPermissionGuard.OtherWebviewPermission(permission));
        }

        // Attaching a surface to another window requires the -other-window variant, since the new web
        // view inherits that window's capability profile.
        commands.AddPermission(WebviewPermissionGuard.OtherWindowPermission("plugin:webview|create"));
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

        [TaruiCommand("plugin:webview|runtime-info")]
        public ValueTask<WebviewRuntimeInfo> GetRuntimeInfoAsync(
            EmptyArgs options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.GetRuntimeInfoAsync(cancellationToken);

        [TaruiCommand("plugin:webview|create")]
        public ValueTask<WebviewStateInfo> CreateAsync(
            WebviewCreateOptions options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            var targetWindow = options.WindowLabel ?? context.WindowLabel;
            WebviewPermissionGuard.EnsureCreateTarget(context, targetWindow, "plugin:webview|create");
            return service.CreateAsync(options with { WindowLabel = targetWindow }, cancellationToken);
        }

        [TaruiCommand("plugin:webview|set-bounds")]
        public ValueTask<Unit> SetBoundsAsync(
            WebviewSetBoundsOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetBoundsAsync(
                Resolve(options.Label, context, "plugin:webview|set-bounds"),
                options.Bounds,
                cancellationToken);

        [TaruiCommand("plugin:webview|destroy")]
        public ValueTask<Unit> DestroyAsync(
            WebviewLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.DestroyAsync(Resolve(options.Label, context, "plugin:webview|destroy"), cancellationToken);

        [TaruiCommand("plugin:webview|send-devtools-message")]
        public ValueTask<Unit> SendDevToolsMessageAsync(
            WebviewDevToolsMessageOptions options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(options.Message))
            {
                throw new InvalidPayloadException();
            }

            return service.SendDevToolsMessageAsync(
                Resolve(options.Label, context, "plugin:webview|send-devtools-message"),
                options.Message,
                cancellationToken);
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
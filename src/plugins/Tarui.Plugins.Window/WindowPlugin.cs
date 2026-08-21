using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Window;

public sealed class WindowPlugin(IWindowService service) : ITaruiPlugin
{
    private static readonly string[] OtherWindowCapablePermissions =
    [
        "core:window|close",
        "core:window|minimize",
        "core:window|maximize",
        "core:window|unmaximize",
        "core:window|toggle-maximize",
        "core:window|hide",
        "core:window|show",
        "core:window|focus",
        "core:window|center",
        "core:window|set-title",
        "core:window|set-size",
        "core:window|set-position",
        "core:window|set-min-size",
        "core:window|set-max-size",
        "core:window|set-always-on-top",
        "core:window|set-resizable",
        "core:window|set-decorations",
        "core:window|set-fullscreen",
        "core:window|get-state",
        "core:window|current-monitor",
        "core:window|monitors"
    ];

    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        var handlers = new WindowCommands(service);

        commands.Add(
            "core:window|create",
            TaruiJsonContext.Default.WindowOptions,
            TaruiJsonContext.Default.Unit,
            handlers.CreateAsync,
            "core:window|create",
            CanCreateWindow);

        commands.Add(
            "core:window|close",
            TaruiJsonContext.Default.CloseWindowOptions,
            TaruiJsonContext.Default.Unit,
            handlers.CloseAsync,
            "core:window|close");

        commands.Add(
            "core:window|minimize",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.MinimizeAsync,
            "core:window|minimize");

        commands.Add(
            "core:window|maximize",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.MaximizeAsync,
            "core:window|maximize");

        commands.Add(
            "core:window|unmaximize",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.UnmaximizeAsync,
            "core:window|unmaximize");

        commands.Add(
            "core:window|toggle-maximize",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.ToggleMaximizeAsync,
            "core:window|toggle-maximize");

        commands.Add(
            "core:window|hide",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.HideAsync,
            "core:window|hide");

        commands.Add(
            "core:window|show",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.ShowAsync,
            "core:window|show");

        commands.Add(
            "core:window|focus",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.FocusAsync,
            "core:window|focus");

        commands.Add(
            "core:window|center",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.CenterAsync,
            "core:window|center");

        commands.Add(
            "core:window|set-title",
            TaruiJsonContext.Default.SetTitleOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetTitleAsync,
            "core:window|set-title");

        commands.Add(
            "core:window|set-size",
            TaruiJsonContext.Default.SetSizeOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetSizeAsync,
            "core:window|set-size");

        commands.Add(
            "core:window|set-position",
            TaruiJsonContext.Default.SetPositionOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetPositionAsync,
            "core:window|set-position");

        commands.Add(
            "core:window|set-min-size",
            TaruiJsonContext.Default.SetExtentOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetMinSizeAsync,
            "core:window|set-min-size");

        commands.Add(
            "core:window|set-max-size",
            TaruiJsonContext.Default.SetExtentOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetMaxSizeAsync,
            "core:window|set-max-size");

        commands.Add(
            "core:window|set-always-on-top",
            TaruiJsonContext.Default.SetFlagOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetAlwaysOnTopAsync,
            "core:window|set-always-on-top");

        commands.Add(
            "core:window|set-resizable",
            TaruiJsonContext.Default.SetFlagOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetResizableAsync,
            "core:window|set-resizable");

        commands.Add(
            "core:window|set-decorations",
            TaruiJsonContext.Default.SetFlagOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetDecorationsAsync,
            "core:window|set-decorations");

        commands.Add(
            "core:window|set-fullscreen",
            TaruiJsonContext.Default.SetFlagOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetFullscreenAsync,
            "core:window|set-fullscreen");

        commands.Add(
            "core:window|get-state",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.WindowStateInfo,
            handlers.GetStateAsync,
            "core:window|get-state");

        commands.Add(
            "core:window|current-monitor",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.MonitorInfo,
            handlers.GetCurrentMonitorAsync,
            "core:window|current-monitor");

        commands.Add(
            "core:window|primary-monitor",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.MonitorInfo,
            handlers.GetPrimaryMonitorAsync,
            "core:window|primary-monitor");

        commands.Add(
            "core:window|monitors",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.MonitorInfoArray,
            handlers.GetMonitorsAsync,
            "core:window|monitors");

        commands.Add(
            "core:window|list",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.WindowLabels,
            handlers.ListAsync,
            "core:window|list");

        // Cross-window operations require the <permission>-other-window variant; register them as
        // valid permission IDs so capability files may reference them and validation stays strict.
        foreach (var permission in OtherWindowCapablePermissions)
        {
            commands.AddPermission(WindowPermissionGuard.OtherWindowPermission(permission));
        }
    }

    private static bool CanCreateWindow(WindowOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny)
    {
        foreach (var scope in deny)
        {
            if (ScopeMatchesProfile(options.Label, scope))
            {
                return false;
            }
        }

        if (allow.Count == 0)
        {
            return true;
        }

        foreach (var scope in allow)
        {
            if (ScopeMatchesProfile(options.Label, scope))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ScopeMatchesProfile(string label, PathScope scope) =>
        !string.IsNullOrWhiteSpace(scope.Path) &&
        string.Equals(scope.Path, label, StringComparison.Ordinal);

    private sealed class WindowCommands(IWindowService service)
    {
        [TaruiCommand("core:window|create")]
        public ValueTask<Unit> CreateAsync(
            WindowOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.CreateAsync(options, context, cancellationToken);

        [TaruiCommand("core:window|close")]
        public ValueTask<Unit> CloseAsync(
            CloseWindowOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.CloseAsync(Resolve(options.Label, context, "core:window|close"), options.Force, cancellationToken);

        [TaruiCommand("core:window|minimize")]
        public ValueTask<Unit> MinimizeAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.MinimizeAsync(Resolve(options.Label, context, "core:window|minimize"), cancellationToken);

        [TaruiCommand("core:window|maximize")]
        public ValueTask<Unit> MaximizeAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.MaximizeAsync(Resolve(options.Label, context, "core:window|maximize"), cancellationToken);

        [TaruiCommand("core:window|unmaximize")]
        public ValueTask<Unit> UnmaximizeAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.UnmaximizeAsync(Resolve(options.Label, context, "core:window|unmaximize"), cancellationToken);

        [TaruiCommand("core:window|toggle-maximize")]
        public ValueTask<Unit> ToggleMaximizeAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.ToggleMaximizeAsync(Resolve(options.Label, context, "core:window|toggle-maximize"), cancellationToken);

        [TaruiCommand("core:window|hide")]
        public ValueTask<Unit> HideAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.HideAsync(Resolve(options.Label, context, "core:window|hide"), cancellationToken);

        [TaruiCommand("core:window|show")]
        public ValueTask<Unit> ShowAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.ShowAsync(Resolve(options.Label, context, "core:window|show"), cancellationToken);

        [TaruiCommand("core:window|focus")]
        public ValueTask<Unit> FocusAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.FocusAsync(Resolve(options.Label, context, "core:window|focus"), cancellationToken);

        [TaruiCommand("core:window|center")]
        public ValueTask<Unit> CenterAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.CenterAsync(Resolve(options.Label, context, "core:window|center"), cancellationToken);

        [TaruiCommand("core:window|set-title")]
        public ValueTask<Unit> SetTitleAsync(
            SetTitleOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetTitleAsync(Resolve(options.Label, context, "core:window|set-title"), options.Title, cancellationToken);

        [TaruiCommand("core:window|set-size")]
        public ValueTask<Unit> SetSizeAsync(
            SetSizeOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetSizeAsync(Resolve(options.Label, context, "core:window|set-size"), options.Width, options.Height, cancellationToken);

        [TaruiCommand("core:window|set-position")]
        public ValueTask<Unit> SetPositionAsync(
            SetPositionOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetPositionAsync(Resolve(options.Label, context, "core:window|set-position"), options.X, options.Y, cancellationToken);

        [TaruiCommand("core:window|set-min-size")]
        public ValueTask<Unit> SetMinSizeAsync(
            SetExtentOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetMinSizeAsync(Resolve(options.Label, context, "core:window|set-min-size"), options.Width, options.Height, cancellationToken);

        [TaruiCommand("core:window|set-max-size")]
        public ValueTask<Unit> SetMaxSizeAsync(
            SetExtentOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetMaxSizeAsync(Resolve(options.Label, context, "core:window|set-max-size"), options.Width, options.Height, cancellationToken);

        [TaruiCommand("core:window|set-always-on-top")]
        public ValueTask<Unit> SetAlwaysOnTopAsync(
            SetFlagOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetAlwaysOnTopAsync(Resolve(options.Label, context, "core:window|set-always-on-top"), options.Value, cancellationToken);

        [TaruiCommand("core:window|set-resizable")]
        public ValueTask<Unit> SetResizableAsync(
            SetFlagOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetResizableAsync(Resolve(options.Label, context, "core:window|set-resizable"), options.Value, cancellationToken);

        [TaruiCommand("core:window|set-decorations")]
        public ValueTask<Unit> SetDecorationsAsync(
            SetFlagOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetDecorationsAsync(Resolve(options.Label, context, "core:window|set-decorations"), options.Value, cancellationToken);

        [TaruiCommand("core:window|set-fullscreen")]
        public ValueTask<Unit> SetFullscreenAsync(
            SetFlagOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetFullscreenAsync(Resolve(options.Label, context, "core:window|set-fullscreen"), options.Value, cancellationToken);

        [TaruiCommand("core:window|get-state")]
        public ValueTask<WindowStateInfo> GetStateAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.GetStateAsync(Resolve(options.Label, context, "core:window|get-state"), cancellationToken);

        [TaruiCommand("core:window|current-monitor")]
        public ValueTask<MonitorInfo?> GetCurrentMonitorAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.GetCurrentMonitorAsync(Resolve(options.Label, context, "core:window|current-monitor"), cancellationToken);

        [TaruiCommand("core:window|primary-monitor")]
        public ValueTask<MonitorInfo?> GetPrimaryMonitorAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.GetPrimaryMonitorAsync(cancellationToken);

        [TaruiCommand("core:window|monitors")]
        public async ValueTask<MonitorInfo[]> GetMonitorsAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            var monitors = await service.GetMonitorsAsync(Resolve(options.Label, context, "core:window|monitors"), cancellationToken);
            return [.. monitors];
        }

        [TaruiCommand("core:window|list")]
        public async ValueTask<WindowLabels> ListAsync(
            EmptyArgs options,
            CommandContext context,
            CancellationToken cancellationToken)
        {
            var labels = await service.ListAsync(cancellationToken);
            return new WindowLabels([.. labels]);
        }

        private static string Resolve(string? requested, CommandContext context, string permission)
        {
            var label = requested ?? context.WindowLabel;
            WindowPermissionGuard.EnsureOwnOrOtherWindow(context, label, permission);
            return label;
        }
    }
}

public static class WindowPluginServiceCollectionExtensions
{
    public static IServiceCollection AddWindowPlugin(this IServiceCollection services)
        => services.AddPlugin<WindowPlugin>();
}
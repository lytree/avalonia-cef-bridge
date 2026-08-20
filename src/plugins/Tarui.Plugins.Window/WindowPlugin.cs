using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Window;

public static class WindowPlugin
{
    public static void Register(
        CommandRouterBuilder commands,
        Action<string> registerPermission,
        IWindowService service)
    {
        var handlers = new WindowCommands(service);

        commands.Add(
            "core:window|create",
            TaruiJsonContext.Default.WindowOptions,
            TaruiJsonContext.Default.Unit,
            handlers.CreateAsync,
            "core:window|create");
        registerPermission("core:window|create");

        commands.Add(
            "core:window|close",
            TaruiJsonContext.Default.CloseWindowOptions,
            TaruiJsonContext.Default.Unit,
            handlers.CloseAsync,
            "core:window|close");
        registerPermission("core:window|close");

        commands.Add(
            "core:window|minimize",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.MinimizeAsync,
            "core:window|minimize");
        registerPermission("core:window|minimize");

        commands.Add(
            "core:window|maximize",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.MaximizeAsync,
            "core:window|maximize");
        registerPermission("core:window|maximize");

        commands.Add(
            "core:window|unmaximize",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.UnmaximizeAsync,
            "core:window|unmaximize");
        registerPermission("core:window|unmaximize");

        commands.Add(
            "core:window|toggle-maximize",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.ToggleMaximizeAsync,
            "core:window|toggle-maximize");
        registerPermission("core:window|toggle-maximize");

        commands.Add(
            "core:window|hide",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.HideAsync,
            "core:window|hide");
        registerPermission("core:window|hide");

        commands.Add(
            "core:window|show",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.ShowAsync,
            "core:window|show");
        registerPermission("core:window|show");

        commands.Add(
            "core:window|focus",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.FocusAsync,
            "core:window|focus");
        registerPermission("core:window|focus");

        commands.Add(
            "core:window|center",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.Unit,
            handlers.CenterAsync,
            "core:window|center");
        registerPermission("core:window|center");

        commands.Add(
            "core:window|set-title",
            TaruiJsonContext.Default.SetTitleOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetTitleAsync,
            "core:window|set-title");
        registerPermission("core:window|set-title");

        commands.Add(
            "core:window|set-size",
            TaruiJsonContext.Default.SetSizeOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetSizeAsync,
            "core:window|set-size");
        registerPermission("core:window|set-size");

        commands.Add(
            "core:window|set-position",
            TaruiJsonContext.Default.SetPositionOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetPositionAsync,
            "core:window|set-position");
        registerPermission("core:window|set-position");

        commands.Add(
            "core:window|set-min-size",
            TaruiJsonContext.Default.SetExtentOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetMinSizeAsync,
            "core:window|set-min-size");
        registerPermission("core:window|set-min-size");

        commands.Add(
            "core:window|set-max-size",
            TaruiJsonContext.Default.SetExtentOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetMaxSizeAsync,
            "core:window|set-max-size");
        registerPermission("core:window|set-max-size");

        commands.Add(
            "core:window|set-always-on-top",
            TaruiJsonContext.Default.SetFlagOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetAlwaysOnTopAsync,
            "core:window|set-always-on-top");
        registerPermission("core:window|set-always-on-top");

        commands.Add(
            "core:window|set-resizable",
            TaruiJsonContext.Default.SetFlagOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetResizableAsync,
            "core:window|set-resizable");
        registerPermission("core:window|set-resizable");

        commands.Add(
            "core:window|set-decorations",
            TaruiJsonContext.Default.SetFlagOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetDecorationsAsync,
            "core:window|set-decorations");
        registerPermission("core:window|set-decorations");

        commands.Add(
            "core:window|set-fullscreen",
            TaruiJsonContext.Default.SetFlagOptions,
            TaruiJsonContext.Default.Unit,
            handlers.SetFullscreenAsync,
            "core:window|set-fullscreen");
        registerPermission("core:window|set-fullscreen");

        commands.Add(
            "core:window|get-state",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.WindowStateInfo,
            handlers.GetStateAsync,
            "core:window|get-state");
        registerPermission("core:window|get-state");

        commands.Add(
            "core:window|current-monitor",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.MonitorInfo,
            handlers.GetCurrentMonitorAsync,
            "core:window|current-monitor");
        registerPermission("core:window|current-monitor");

        commands.Add(
            "core:window|primary-monitor",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.MonitorInfo,
            handlers.GetPrimaryMonitorAsync,
            "core:window|primary-monitor");
        registerPermission("core:window|primary-monitor");

        commands.Add(
            "core:window|monitors",
            TaruiJsonContext.Default.WindowLabelOptions,
            TaruiJsonContext.Default.MonitorInfoArray,
            handlers.GetMonitorsAsync,
            "core:window|monitors");
        registerPermission("core:window|monitors");

        commands.Add(
            "core:window|list",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.WindowLabels,
            handlers.ListAsync,
            "core:window|list");
        registerPermission("core:window|list");
    }

    private sealed class WindowCommands(IWindowService service)
    {
        [TaruiCommand("core:window|create")]
        public ValueTask<Unit> CreateAsync(
            WindowOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.CreateAsync(options, cancellationToken);

        [TaruiCommand("core:window|close")]
        public ValueTask<Unit> CloseAsync(
            CloseWindowOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.CloseAsync(options.Label ?? context.WindowLabel, options.Force, cancellationToken);

        [TaruiCommand("core:window|minimize")]
        public ValueTask<Unit> MinimizeAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.MinimizeAsync(options.Label ?? context.WindowLabel, cancellationToken);

        [TaruiCommand("core:window|maximize")]
        public ValueTask<Unit> MaximizeAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.MaximizeAsync(options.Label ?? context.WindowLabel, cancellationToken);

        [TaruiCommand("core:window|unmaximize")]
        public ValueTask<Unit> UnmaximizeAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.UnmaximizeAsync(options.Label ?? context.WindowLabel, cancellationToken);

        [TaruiCommand("core:window|toggle-maximize")]
        public ValueTask<Unit> ToggleMaximizeAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.ToggleMaximizeAsync(options.Label ?? context.WindowLabel, cancellationToken);

        [TaruiCommand("core:window|hide")]
        public ValueTask<Unit> HideAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.HideAsync(options.Label ?? context.WindowLabel, cancellationToken);

        [TaruiCommand("core:window|show")]
        public ValueTask<Unit> ShowAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.ShowAsync(options.Label ?? context.WindowLabel, cancellationToken);

        [TaruiCommand("core:window|focus")]
        public ValueTask<Unit> FocusAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.FocusAsync(options.Label ?? context.WindowLabel, cancellationToken);

        [TaruiCommand("core:window|center")]
        public ValueTask<Unit> CenterAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.CenterAsync(options.Label ?? context.WindowLabel, cancellationToken);

        [TaruiCommand("core:window|set-title")]
        public ValueTask<Unit> SetTitleAsync(
            SetTitleOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetTitleAsync(options.Label ?? context.WindowLabel, options.Title, cancellationToken);

        [TaruiCommand("core:window|set-size")]
        public ValueTask<Unit> SetSizeAsync(
            SetSizeOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetSizeAsync(options.Label ?? context.WindowLabel, options.Width, options.Height, cancellationToken);

        [TaruiCommand("core:window|set-position")]
        public ValueTask<Unit> SetPositionAsync(
            SetPositionOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetPositionAsync(options.Label ?? context.WindowLabel, options.X, options.Y, cancellationToken);

        [TaruiCommand("core:window|set-min-size")]
        public ValueTask<Unit> SetMinSizeAsync(
            SetExtentOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetMinSizeAsync(options.Label ?? context.WindowLabel, options.Width, options.Height, cancellationToken);

        [TaruiCommand("core:window|set-max-size")]
        public ValueTask<Unit> SetMaxSizeAsync(
            SetExtentOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetMaxSizeAsync(options.Label ?? context.WindowLabel, options.Width, options.Height, cancellationToken);

        [TaruiCommand("core:window|set-always-on-top")]
        public ValueTask<Unit> SetAlwaysOnTopAsync(
            SetFlagOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetAlwaysOnTopAsync(options.Label ?? context.WindowLabel, options.Value, cancellationToken);

        [TaruiCommand("core:window|set-resizable")]
        public ValueTask<Unit> SetResizableAsync(
            SetFlagOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetResizableAsync(options.Label ?? context.WindowLabel, options.Value, cancellationToken);

        [TaruiCommand("core:window|set-decorations")]
        public ValueTask<Unit> SetDecorationsAsync(
            SetFlagOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetDecorationsAsync(options.Label ?? context.WindowLabel, options.Value, cancellationToken);

        [TaruiCommand("core:window|set-fullscreen")]
        public ValueTask<Unit> SetFullscreenAsync(
            SetFlagOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.SetFullscreenAsync(options.Label ?? context.WindowLabel, options.Value, cancellationToken);

        [TaruiCommand("core:window|get-state")]
        public ValueTask<WindowStateInfo> GetStateAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.GetStateAsync(options.Label ?? context.WindowLabel, cancellationToken);

        [TaruiCommand("core:window|current-monitor")]
        public ValueTask<MonitorInfo?> GetCurrentMonitorAsync(
            WindowLabelOptions options,
            CommandContext context,
            CancellationToken cancellationToken) =>
            service.GetCurrentMonitorAsync(options.Label ?? context.WindowLabel, cancellationToken);

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
            var monitors = await service.GetMonitorsAsync(options.Label ?? context.WindowLabel, cancellationToken);
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
    }
}

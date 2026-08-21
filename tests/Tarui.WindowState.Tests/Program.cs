using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.WindowState;

namespace Tarui.WindowState.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            WindowStatePluginRegistersAllCommands();
            await SaveDispatchForwardsCallerLabelAsync();
            await ForeignLabelRequiresOtherWindowPermissionAsync();
            await RestoreDispatchReturnsAppliedAsync();
            await SaveDispatchDeniedWithoutPermissionAsync();
            FitKeepsVisibleSnapshotUnchanged();
            FitRepositionsDisconnectedSnapshotToPrimary();
            FitPassesThroughWhenNoMonitors();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }

        Console.WriteLine("Tarui.WindowState self-tests passed.");
        return 0;
    }

    private static void WindowStatePluginRegistersAllCommands()
    {
        var builder = new CommandRouterBuilder();
        new WindowStatePlugin(new RecordingWindowStateService()).ConfigureCommands(builder);
        var router = builder.Build();

        var expected = new[]
        {
            "plugin:window-state|save",
            "plugin:window-state|restore",
            "plugin:window-state|clear",
        };

        foreach (var command in expected)
        {
            Assert(router.Commands.Contains(command), $"The window-state plugin must register command '{command}'.");
        }

        // The plugin registers each permission once plus the -other-window guard variants.
        var guardVariants = expected.Length;
        Assert(router.RegisteredPermissions.Count == expected.Length + guardVariants,
            "The plugin must register its permissions plus the -other-window guard variants.");
        foreach (var permission in expected)
        {
            Assert(router.RegisteredPermissions.Contains(permission + "-other-window"),
                $"The plugin must register '{permission}-other-window'.");
        }
    }

    private static async Task SaveDispatchForwardsCallerLabelAsync()
    {
        var service = new RecordingWindowStateService();
        var router = BuildRouter(service);

        // No label -> the calling window's own state is saved.
        var context = new CommandContext("editor", "editor", new CapabilitySet(["plugin:window-state|save"], [], []));
        var request = new InvokeRequest(1, "s1", "plugin:window-state|save",
            Element(new WindowStateSaveOptions()), "editor", "editor");

        var response = await router.InvokeAsync(request, context);
        Assert(response.Success, $"Save must succeed for the caller's own window. {response.Error?.Code}");
        Assert(Equals(service.Saved, "editor"), "Save must target the caller's own window label.");
    }

    private static async Task ForeignLabelRequiresOtherWindowPermissionAsync()
    {
        var service = new RecordingWindowStateService();
        var router = BuildRouter(service);

        var context = new CommandContext("main", "main", new CapabilitySet(["plugin:window-state|clear"], [], []));
        var request = new InvokeRequest(1, "c1", "plugin:window-state|clear",
            Element(new WindowStateClearOptions("editor")), "main", "main");

        var response = await router.InvokeAsync(request, context);
        Assert(!response.Success, "Saving another window without the -other-window permission must be denied.");
        Assert(response.Error?.Code == "PERMISSION_DENIED", "A foreign-label denial must surface as PERMISSION_DENIED.");

        // Granting the -other-window variant authorizes the operation.
        var allowed = new CommandContext("main", "main",
            new CapabilitySet(["plugin:window-state|clear", "plugin:window-state|clear-other-window"], [], []));
        var allowedResponse = await router.InvokeAsync(request with { Id = "c2" }, allowed);
        Assert(allowedResponse.Success, "The -other-window variant must authorize operating on another window.");
        Assert(Equals(service.Cleared, "editor"), "Clear must target the requested foreign label.");
    }

    private static async Task RestoreDispatchReturnsAppliedAsync()
    {
        var service = new RecordingWindowStateService { RestoreResult = new WindowStateRestoreResult(true) };
        var router = BuildRouter(service);

        var context = new CommandContext("main", "main", new CapabilitySet(["plugin:window-state|restore"], [], []));
        var request = new InvokeRequest(1, "r1", "plugin:window-state|restore",
            Element(new WindowStateRestoreOptions()), "main", "main");

        var response = await router.InvokeAsync(request, context);
        Assert(response.Success, $"Restore must succeed. {response.Error?.Code}");
        var result = response.Payload?.Deserialize(TaruiJsonContext.Default.WindowStateRestoreResult);
        Assert(result is { Applied: true }, "Restore must forward the service's applied flag.");
    }

    private static async Task SaveDispatchDeniedWithoutPermissionAsync()
    {
        var builder = new CommandRouterBuilder();
        new WindowStatePlugin(new RecordingWindowStateService()).ConfigureCommands(builder);
        var router = builder.Build();

        var context = new CommandContext("main", "main", new CapabilitySet([], [], []));
        var request = new InvokeRequest(1, "e1", "plugin:window-state|save",
            Element(new WindowStateSaveOptions()), "main", "main");

        var response = await router.InvokeAsync(request, context);
        Assert(!response.Success, "Save without its permission must be denied.");
    }

    private static void FitKeepsVisibleSnapshotUnchanged()
    {
        var monitor = PrimaryMonitor();
        var visible = new WindowStateSnapshot("main", 100, 100, 800, 600);
        var fitted = WindowStateFit.ClampToMonitors(visible, [monitor]);
        Assert(ReferenceEquals(fitted, visible) || (fitted.X == 100 && fitted.Y == 100),
            "A snapshot that overlaps a connected monitor must pass through unchanged.");
    }

    private static void FitRepositionsDisconnectedSnapshotToPrimary()
    {
        var primary = PrimaryMonitor();
        var offScreen = new WindowStateSnapshot("main", 10_000, 10_000, 800, 600);
        var fitted = WindowStateFit.ClampToMonitors(offScreen, [primary]);
        Assert(fitted.X == primary.WorkAreaPosition.X && fitted.Y == primary.WorkAreaPosition.Y,
            "A snapshot left on a disconnected monitor must be repositioned to the primary work area.");
        Assert(fitted.Width == 800 && fitted.Height == 600,
            "Fitting must preserve the snapshot size, only relocating it.");
    }

    private static void FitPassesThroughWhenNoMonitors()
    {
        var snapshot = new WindowStateSnapshot("main", 5, 6, 800, 600);
        var fitted = WindowStateFit.ClampToMonitors(snapshot, []);
        Assert(fitted.X == 5 && fitted.Y == 6, "With no monitors there is nothing to validate against.");
    }

    private static CommandRouter BuildRouter(IWindowStateService service)
    {
        var builder = new CommandRouterBuilder();
        new WindowStatePlugin(service).ConfigureCommands(builder);
        return builder.Build();
    }

    private static MonitorInfo PrimaryMonitor() =>
        new(
            "Primary",
            new LogicalPosition(0, 0),
            new LogicalSize(1920, 1080),
            new LogicalPosition(0, 0),
            new LogicalSize(1920, 1080),
            1.0,
            IsPrimary: true,
            IsCurrent: true);

    private static JsonElement Element<T>(T value) =>
        JsonSerializer.SerializeToElement(value, JsonInfoFor<T>());

    private static System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> JsonInfoFor<T>() => typeof(T) switch
    {
        _ when typeof(T) == typeof(WindowStateSaveOptions) => (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)(object)TaruiJsonContext.Default.WindowStateSaveOptions,
        _ when typeof(T) == typeof(WindowStateRestoreOptions) => (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)(object)TaruiJsonContext.Default.WindowStateRestoreOptions,
        _ when typeof(T) == typeof(WindowStateClearOptions) => (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)(object)TaruiJsonContext.Default.WindowStateClearOptions,
        _ => throw new InvalidOperationException($"No JsonTypeInfo configured for '{typeof(T).Name}'."),
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RecordingWindowStateService : IWindowStateService
    {
        public string? Saved { get; private set; }
        public string? Cleared { get; private set; }
        public WindowStateRestoreResult RestoreResult { get; set; } = new(false);

        public ValueTask<Unit> SaveAsync(string windowLabel, CancellationToken cancellationToken)
        {
            Saved = windowLabel;
            return ValueTask.FromResult(new Unit());
        }

        public ValueTask<WindowStateRestoreResult> RestoreAsync(string windowLabel, CancellationToken cancellationToken) =>
            ValueTask.FromResult(RestoreResult);

        public ValueTask<Unit> ClearAsync(string windowLabel, CancellationToken cancellationToken)
        {
            Cleared = windowLabel;
            return ValueTask.FromResult(new Unit());
        }
    }
}
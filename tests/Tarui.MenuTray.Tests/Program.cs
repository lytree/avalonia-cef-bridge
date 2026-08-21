using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Menu;
using Tarui.Plugins.Tray;
using Tarui.Shell;

namespace Tarui.MenuTray.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            MenuPluginRegistersAllCommands();
            TrayPluginRegistersAllCommands();
            await MenuDispatchForwardsOwnerAndGatesPermissionAsync();
            await TrayDispatchForwardsOwnerAndGatesPermissionAsync();
            MenuItemIdsMustBeUniqueAcrossTheTree();
            MenuBuilderRejectsDuplicateNestedIds();
            TrayIconPathResolvesRootedAndKnownBaseSpecs();
            TrayIconPathRejectsUnknownOrRelativeSpecs();
            ClickEventDtosRoundTripThroughJsonContext();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }

        Console.WriteLine("Tarui.MenuTray self-tests passed.");
        return 0;
    }

    private static void MenuPluginRegistersAllCommands()
    {
        var builder = new CommandRouterBuilder();
        new MenuPlugin(new RecordingMenuService()).ConfigureCommands(builder);
        var router = builder.Build();

        var expected = new[]
        {
            "plugin:menu|set-window-menu",
            "plugin:menu|update-item",
            "plugin:menu|remove-window-menu",
        };

        foreach (var command in expected)
        {
            Assert(router.Commands.Contains(command), $"The menu plugin must register command '{command}'.");
        }

        Assert(router.RegisteredPermissions.Count == expected.Length,
            "Every menu permission must be registered exactly once with no extras.");
    }

    private static void TrayPluginRegistersAllCommands()
    {
        var builder = new CommandRouterBuilder();
        new TrayPlugin(new RecordingTrayService()).ConfigureCommands(builder);
        var router = builder.Build();

        var expected = new[]
        {
            "plugin:tray|create",
            "plugin:tray|set-menu",
            "plugin:tray|set-icon",
            "plugin:tray|set-tooltip",
            "plugin:tray|set-visible",
            "plugin:tray|remove",
        };

        foreach (var command in expected)
        {
            Assert(router.Commands.Contains(command), $"The tray plugin must register command '{command}'.");
        }

        Assert(router.RegisteredPermissions.Count == expected.Length,
            "Every tray permission must be registered exactly once with no extras.");
    }

    private static async Task MenuDispatchForwardsOwnerAndGatesPermissionAsync()
    {
        var service = new RecordingMenuService();
        await InvokeAndAssertOwnerAsync(
            service,
            "plugin:menu|set-window-menu",
            "plugin:menu|set-window-menu",
            Element(new SetWindowMenuOptions([new MenuItemDefinition("open", Text: "Open")])));

        await AssertPermissionGatedAsync(
            "plugin:menu|update-item",
            Element(new MenuUpdateItemOptions("open", Text: "Rename")),
            "plugin:menu|set-window-menu",
            "plugin:menu|update-item");
    }

    private static async Task TrayDispatchForwardsOwnerAndGatesPermissionAsync()
    {
        var service = new RecordingTrayService();
        await InvokeAndAssertOwnerAsync(
            service,
            "plugin:tray|create",
            "plugin:tray|create",
            Element(new TrayCreateOptions("app-tray", Tooltip: "tarui")));

        await AssertPermissionGatedAsync(
            "plugin:tray|set-menu",
            Element(new TraySetMenuOptions("app-tray", [new MenuItemDefinition("quit", Text: "Quit")])),
            "plugin:tray|create",
            "plugin:tray|set-menu");
    }

    private static async Task InvokeAndAssertOwnerAsync<TService>(
        TService service,
        string command,
        string permission,
        JsonElement payload)
        where TService : class
    {
        var builder = new CommandRouterBuilder();
        Configure(service, builder);
        var router = builder.Build();

        var context = new CommandContext("editor", "editor", new CapabilitySet([permission], [], []));
        var request = new InvokeRequest(1, "m1", command, payload, "editor", "editor");

        var response = await router.InvokeAsync(request, context);
        Assert(response.Success, $"'{command}' must succeed when its permission is granted. {response.Error?.Code}");

        var owners = OwnersOf(service);
        Assert(owners.Contains("editor"), $"'{command}' must forward the calling window label as the owner.");
    }

    private static async Task AssertPermissionGatedAsync(
        string command,
        JsonElement payload,
        string grantedPermission,
        string requiredPermission)
    {
        var builder = new CommandRouterBuilder();
        Configure(new RecordingMenuService(), builder);
        Configure(new RecordingTrayService(), builder);

        var router = builder.Build();
        var context = new CommandContext("main", "main", new CapabilitySet([grantedPermission], [], []));
        var request = new InvokeRequest(1, "g1", command, payload, "main", "main");

        var response = await router.InvokeAsync(request, context);
        Assert(!response.Success, $"'{command}' must be denied without '{requiredPermission}'.");
        Assert(response.Error?.Code == "PERMISSION_DENIED", $"'{command}' must fail with PERMISSION_DENIED.");
    }

    private static void Configure<TService>(TService service, CommandRouterBuilder builder)
    {
        switch (service)
        {
            case IMenuService menu:
                new MenuPlugin(menu).ConfigureCommands(builder);
                break;
            case ITrayService tray:
                new TrayPlugin(tray).ConfigureCommands(builder);
                break;
        }
    }

    private static List<string> OwnersOf<TService>(TService service) =>
        service switch
        {
            RecordingMenuService menu => menu.Owners,
            RecordingTrayService tray => tray.Owners,
            _ => [],
        };

    private static void MenuItemIdsMustBeUniqueAcrossTheTree()
    {
        NativeMenuBuilder.ValidateUniqueIds(
            [new MenuItemDefinition("a", Text: "A"), new MenuItemDefinition("b", Text: "B")]);

        var duplicate = false;
        try
        {
            NativeMenuBuilder.ValidateUniqueIds(
                [new MenuItemDefinition("a", Text: "A"), new MenuItemDefinition("a", Text: "A2")]);
        }
        catch (InvalidOperationException)
        {
            duplicate = true;
        }

        Assert(duplicate, "Duplicate top-level ids must be rejected.");
    }

    private static void MenuBuilderRejectsDuplicateNestedIds()
    {
        var duplicate = false;
        try
        {
            NativeMenuBuilder.ValidateUniqueIds(
            [
                new MenuItemDefinition("file", Text: "File", Items:
                [
                    new MenuItemDefinition("open", Text: "Open"),
                    new MenuItemDefinition("open", Text: "Open again"),
                ]),
            ]);
        }
        catch (InvalidOperationException)
        {
            duplicate = true;
        }

        Assert(duplicate, "Duplicate ids nested in a submenu must be rejected.");
    }

    private static void TrayIconPathResolvesRootedAndKnownBaseSpecs()
    {
        var rooted = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "icon.ico"));
        Assert(string.Equals(TrayIconPath.Resolve(rooted), rooted, StringComparison.OrdinalIgnoreCase),
            "A rooted path must pass through unchanged.");

        var temp = TrayIconPath.Resolve("temp:icon.ico");
        Assert(string.Equals(Path.GetDirectoryName(temp), Path.GetTempPath().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase),
            "The temp base must resolve under the system temp directory.");

        var resources = TrayIconPath.Resolve("resources:icon.ico");
        Assert(string.Equals(Path.GetDirectoryName(resources), AppContext.BaseDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase),
            "The resources base must resolve under the app base directory.");
    }

    private static void TrayIconPathRejectsUnknownOrRelativeSpecs()
    {
        Assert(Throws(() => TrayIconPath.Resolve("bogus:icon.ico")),
            "An unknown base prefix must be rejected.");
        Assert(Throws(() => TrayIconPath.Resolve("icon.ico")),
            "A relative spec without a base prefix must be rejected.");
    }

    private static void ClickEventDtosRoundTripThroughJsonContext()
    {
        var clicked = new MenuItemClicked("open", "Open", true);
        var roundTripped = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(clicked, TaruiJsonContext.Default.MenuItemClicked),
            TaruiJsonContext.Default.MenuItemClicked);
        Assert(roundTripped is { Id: "open", Text: "Open", Checked: true },
            "The menu click DTO must round-trip through the JSON context.");

        var trayClicked = new TrayClicked("app-tray", "Left");
        var trayRoundTripped = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(trayClicked, TaruiJsonContext.Default.TrayClicked),
            TaruiJsonContext.Default.TrayClicked);
        Assert(trayRoundTripped is { Id: "app-tray", Button: "Left" },
            "The tray click DTO must round-trip through the JSON context.");
    }

    private static JsonElement Element<T>(T value) =>
        JsonSerializer.SerializeToElement(value, (JsonTypeInfo<T>)JsonTypeInfoFor(typeof(T)));

    private static object JsonTypeInfoFor(Type type) => type switch
    {
        _ when type == typeof(SetWindowMenuOptions) => TaruiJsonContext.Default.SetWindowMenuOptions,
        _ when type == typeof(MenuUpdateItemOptions) => TaruiJsonContext.Default.MenuUpdateItemOptions,
        _ when type == typeof(TrayCreateOptions) => TaruiJsonContext.Default.TrayCreateOptions,
        _ when type == typeof(TraySetMenuOptions) => TaruiJsonContext.Default.TraySetMenuOptions,
        _ => throw new InvalidOperationException($"No JsonTypeInfo configured for '{type.Name}'."),
    };

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RecordingMenuService : IMenuService
    {
        public List<string> Owners { get; } = [];

        public ValueTask<Unit> SetWindowMenuAsync(string ownerWindow, SetWindowMenuOptions options, CancellationToken cancellationToken)
        {
            Owners.Add(ownerWindow);
            return ValueTask.FromResult(new Unit());
        }

        public ValueTask<Unit> UpdateItemAsync(string ownerWindow, MenuUpdateItemOptions options, CancellationToken cancellationToken)
        {
            Owners.Add(ownerWindow);
            return ValueTask.FromResult(new Unit());
        }

        public ValueTask<Unit> RemoveWindowMenuAsync(string ownerWindow, CancellationToken cancellationToken)
        {
            Owners.Add(ownerWindow);
            return ValueTask.FromResult(new Unit());
        }
    }

    private sealed class RecordingTrayService : ITrayService
    {
        public List<string> Owners { get; } = [];

        public ValueTask<Unit> CreateAsync(string ownerWindow, TrayCreateOptions options, CancellationToken cancellationToken)
        {
            Owners.Add(ownerWindow);
            return ValueTask.FromResult(new Unit());
        }

        public ValueTask<Unit> SetMenuAsync(string ownerWindow, TraySetMenuOptions options, CancellationToken cancellationToken)
        {
            Owners.Add(ownerWindow);
            return ValueTask.FromResult(new Unit());
        }

        public ValueTask<Unit> SetIconAsync(string ownerWindow, TraySetIconOptions options, CancellationToken cancellationToken)
        {
            Owners.Add(ownerWindow);
            return ValueTask.FromResult(new Unit());
        }

        public ValueTask<Unit> SetTooltipAsync(string ownerWindow, TraySetTooltipOptions options, CancellationToken cancellationToken)
        {
            Owners.Add(ownerWindow);
            return ValueTask.FromResult(new Unit());
        }

        public ValueTask<Unit> SetVisibleAsync(string ownerWindow, TraySetVisibleOptions options, CancellationToken cancellationToken)
        {
            Owners.Add(ownerWindow);
            return ValueTask.FromResult(new Unit());
        }

        public ValueTask<Unit> RemoveAsync(string ownerWindow, TrayRemoveOptions options, CancellationToken cancellationToken)
        {
            Owners.Add(ownerWindow);
            return ValueTask.FromResult(new Unit());
        }
    }
}
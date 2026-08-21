using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.GlobalShortcut;

namespace Tarui.GlobalShortcut.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            GlobalShortcutPluginRegistersAllCommands();
            GlobalShortcutDispatchForwardsAndGatesAsync().GetAwaiter().GetResult();
            AcceleratorSpecNormalizesAndValidates();
            AcceleratorSpecEnforcesScopes();
            GlobalShortcutDtosRoundTripThroughJsonContext();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }

        Console.WriteLine("Tarui.GlobalShortcut self-tests passed.");
        return 0;
    }

    private static void GlobalShortcutPluginRegistersAllCommands()
    {
        var builder = new CommandRouterBuilder();
        new GlobalShortcutPlugin(new RecordingGlobalShortcutService()).ConfigureCommands(builder);
        var router = builder.Build();

        var expected = new[]
        {
            "plugin:global-shortcut|register",
            "plugin:global-shortcut|unregister",
            "plugin:global-shortcut|unregister-all",
            "plugin:global-shortcut|is-registered",
        };

        foreach (var command in expected)
        {
            Assert(router.Commands.Contains(command), $"The global-shortcut plugin must register command '{command}'.");
        }

        Assert(router.RegisteredPermissions.Count == expected.Length,
            "Every global-shortcut permission must be registered exactly once with no extras.");
    }

    private static async Task GlobalShortcutDispatchForwardsAndGatesAsync()
    {
        var service = new RecordingGlobalShortcutService();
        var builder = new CommandRouterBuilder();
        new GlobalShortcutPlugin(service).ConfigureCommands(builder);
        var router = builder.Build();

        var allowed = new CapabilitySet(
            ["plugin:global-shortcut|register"],
            [],
            new KeyValuePair<string, PermissionScope>[]
            {
                new("plugin:global-shortcut|register", new PermissionScope([new PathScope(Path: "Ctrl+Shift+A")], []))
            });

        var registered = await router.InvokeAsync(
            new InvokeRequest(1, "g1", "plugin:global-shortcut|register", Element(new GlobalShortcutOptions("ctrl+shift+a")), "main", "main"),
            new CommandContext("main", "main", allowed));
        Assert(registered.Success, $"register must succeed when the accelerator is within scope. {registered.Error?.Code}");

        var outOfScope = await router.InvokeAsync(
            new InvokeRequest(1, "g2", "plugin:global-shortcut|register", Element(new GlobalShortcutOptions("Ctrl+Alt+B")), "main", "main"),
            new CommandContext("main", "main", allowed));
        Assert(!outOfScope.Success && outOfScope.Error?.Code == "SCOPE_DENIED",
            "register must be denied when the accelerator is outside the allow scope.");

        var denied = await router.InvokeAsync(
            new InvokeRequest(1, "g3", "plugin:global-shortcut|unregister", Element(new GlobalShortcutOptions("Ctrl+Shift+A")), "main", "main"),
            new CommandContext("main", "main", new CapabilitySet(["plugin:global-shortcut|register"], [], [])));
        Assert(!denied.Success && denied.Error?.Code == "PERMISSION_DENIED",
            "unregister must be denied without its own permission.");
    }

    private static void AcceleratorSpecNormalizesAndValidates()
    {
        var spec = AcceleratorSpec.Parse("ctrl+shift+a");
        Assert(spec.Normalized == "Control+Shift+A", "Modifiers and key must be normalized to canonical order and case.");
        Assert(spec.Control && spec.Shift && spec.Alt is false && spec.Meta is false,
            "Modifier flags must be set correctly.");
        Assert(spec.Key == "A", "The key must be normalized to upper case.");

        var fn = AcceleratorSpec.Parse("alt-F7");
        Assert(fn.Normalized == "Alt+F7", "A function key must be preserved.");

        var meta = AcceleratorSpec.Parse("Super+Shift+P");
        Assert(meta.Meta && meta.Normalized == "Shift+Meta+P", "The meta modifier must be recognized.");

        Assert(Throws(() => AcceleratorSpec.Parse("")), "A blank accelerator must be rejected.");
        Assert(Throws(() => AcceleratorSpec.Parse("a")), "An accelerator with no modifier must be rejected.");
        Assert(Throws(() => AcceleratorSpec.Parse("not-a-key")), "An unknown key must be rejected.");
        Assert(Throws(() => AcceleratorSpec.Parse("Ctrl+Shift+B+Alt")), "A duplicate/last bare token must be rejected.");
    }

    private static void AcceleratorSpecEnforcesScopes()
    {
        var spec = AcceleratorSpec.Parse("Ctrl+Shift+A");
        Assert(spec.Matches([new PathScope(Path: "Ctrl+Shift+A")]), "An exact match must be allowed.");
        Assert(spec.Matches([new PathScope(Path: "Ctrl+*")]), "A wildcard suffix must be allowed.");
        Assert(spec.Matches([new PathScope(Path: "*")]), "A bare wildcard must match everything.");
        Assert(spec.Matches([new PathScope(Path: "Ctrl+Shift+?")]) is false, "A mismatched pattern must not match.");
    }

    private static void GlobalShortcutDtosRoundTripThroughJsonContext()
    {
        var options = new GlobalShortcutOptions("Ctrl+Shift+A");
        var roundTripped = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(options, TaruiJsonContext.Default.GlobalShortcutOptions),
            TaruiJsonContext.Default.GlobalShortcutOptions);
        Assert(roundTripped is { Accelerator: "Ctrl+Shift+A" }, "The options must round-trip through the JSON context.");

        var triggered = new GlobalShortcutTriggered("Ctrl+Shift+A");
        var triggeredRoundTrip = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(triggered, TaruiJsonContext.Default.GlobalShortcutTriggered),
            TaruiJsonContext.Default.GlobalShortcutTriggered);
        Assert(triggeredRoundTrip is { Accelerator: "Ctrl+Shift+A" }, "The triggered payload must round-trip through the JSON context.");
    }

    private static JsonElement Element(GlobalShortcutOptions value) =>
        JsonSerializer.SerializeToElement(value, TaruiJsonContext.Default.GlobalShortcutOptions);

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidPayloadException)
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

    private sealed class RecordingGlobalShortcutService : IGlobalShortcutService
    {
        public ValueTask<GlobalShortcutState> RegisterAsync(GlobalShortcutOptions options, CancellationToken cancellationToken)
            => ValueTask.FromResult(new GlobalShortcutState(true));

        public ValueTask<Unit> UnregisterAsync(GlobalShortcutOptions options, CancellationToken cancellationToken)
            => ValueTask.FromResult(new Unit());

        public ValueTask<Unit> UnregisterAllAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(new Unit());

        public ValueTask<GlobalShortcutState> IsRegisteredAsync(GlobalShortcutOptions options, CancellationToken cancellationToken)
            => ValueTask.FromResult(new GlobalShortcutState(false));
    }
}
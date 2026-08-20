using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Ipc.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        await DeniesCommandsOutsideCapability();
        await DispatchesRegisteredCommandWithoutDynamicBinding();
        ResolvesPluginSingletonThroughServiceProvider();
        DeduplicatesRegisteredPermissions();
        ExposesRouterRegisteredPermissions();
        Console.WriteLine("Tarui.Ipc self-tests passed.");
        return 0;
    }

    private static async Task DeniesCommandsOutsideCapability()
    {
        var builder = new CommandRouterBuilder();
        builder.Add(
            "test:echo",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            static (_, _, _) => ValueTask.FromResult(new Unit()),
            "test:echo");

        var response = await builder.Build().InvokeAsync(
            Request("1"),
            new CommandContext("main", "main", new CapabilitySet([])));

        Assert(!response.Success, "A command outside the capability must fail.");
        Assert(response.Error?.Code == "PERMISSION_DENIED", "The error must be PERMISSION_DENIED.");
    }

    private static async Task DispatchesRegisteredCommandWithoutDynamicBinding()
    {
        var builder = new CommandRouterBuilder();
        builder.Add(
            "test:echo",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            static (_, _, _) => ValueTask.FromResult(new Unit()),
            "test:echo");

        var dispatcher = new IpcDispatcher(builder.Build());
        var json = JsonSerializer.Serialize(Request("2"), TaruiJsonContext.Default.InvokeRequest);
        var response = await dispatcher.DispatchJsonAsync(
            json,
            new CommandContext("main", "main", new CapabilitySet(["test:echo"])));
        var parsed = JsonSerializer.Deserialize(response, TaruiJsonContext.Default.InvokeResponse);

        Assert(parsed is not null, "The dispatcher must return a response.");
        Assert(parsed!.Success, "An allowed command must succeed.");
    }

    private static void ResolvesPluginSingletonThroughServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddPlugin<TestPlugin>();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetServices<ITaruiPlugin>().ToArray();
        var second = provider.GetServices<ITaruiPlugin>().ToArray();

        Assert(first.Length == 1, "AddPlugin must register exactly one plugin instance.");
        Assert(first[0] is TestPlugin, "The resolved plugin must be the registered implementation.");
        Assert(ReferenceEquals(first[0], second[0]), "Repeated resolutions must return the same singleton.");
    }

    private static void DeduplicatesRegisteredPermissions()
    {
        var builder = new CommandRouterBuilder();
        new TestPlugin().ConfigureCommands(builder);

        var permissions = builder.RegisteredPermissions;

        Assert(permissions.Count == 2, "Duplicate permissions must be deduplicated.");
        Assert(permissions.Contains("test:plugin|read"), "The shared permission must be registered.");
        Assert(permissions.Contains("test:plugin|write"), "The distinct permission must be registered.");
    }

    private static void ExposesRouterRegisteredPermissions()
    {
        var builder = new CommandRouterBuilder();
        new TestPlugin().ConfigureCommands(builder);
        var router = builder.Build();

        var expected = builder.RegisteredPermissions
            .OrderBy(static permission => permission, StringComparer.Ordinal)
            .ToArray();
        var actual = router.RegisteredPermissions
            .OrderBy(static permission => permission, StringComparer.Ordinal)
            .ToArray();

        Assert(expected.Length == 2, "The builder must expose the deduplicated permissions.");
        Assert(expected.SequenceEqual(actual), "The router must expose the builder's registered permissions.");
    }

    private static InvokeRequest Request(string id) => new(
        1,
        id,
        "test:echo",
        JsonSerializer.SerializeToElement(new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs));

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TestPlugin : ITaruiPlugin
    {
        public TestPlugin()
        {
        }

        public void ConfigureCommands(CommandRouterBuilder commands)
        {
            commands.Add(
                "test:plugin|read-value",
                TaruiJsonContext.Default.EmptyArgs,
                TaruiJsonContext.Default.Unit,
                static (_, _, _) => ValueTask.FromResult(new Unit()),
                "test:plugin|read");
            commands.Add(
                "test:plugin|read-cache",
                TaruiJsonContext.Default.EmptyArgs,
                TaruiJsonContext.Default.Unit,
                static (_, _, _) => ValueTask.FromResult(new Unit()),
                "test:plugin|read");
            commands.Add(
                "test:plugin|write",
                TaruiJsonContext.Default.EmptyArgs,
                TaruiJsonContext.Default.Unit,
                static (_, _, _) => ValueTask.FromResult(new Unit()),
                "test:plugin|write");
        }
    }
}

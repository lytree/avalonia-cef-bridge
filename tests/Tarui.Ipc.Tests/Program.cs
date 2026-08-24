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
        await HandlerExceptionsDoNotCorruptDispatcherAsync();
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

    private static async Task HandlerExceptionsDoNotCorruptDispatcherAsync()
    {
        // A handler that throws must surface as a Web-facing failure on the response and leave
        // the dispatcher usable for subsequent invocations. Without this guard the bridge would
        // leak an unhandled rejection to the web layer and pin the dispatcher.
        var builder = new CommandRouterBuilder();
        builder.Add(
            "test:explode",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            static (_, _, _) => throw new InvalidOperationException("simulated-handler-failure"),
            "test:explode");
        builder.Add(
            "test:echo",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            static (_, _, _) => ValueTask.FromResult(new Unit()),
            "test:echo");

        var dispatcher = new IpcDispatcher(builder.Build());
        var explodingJson = JsonSerializer.Serialize(Request("explode-1"), TaruiJsonContext.Default.InvokeRequest);
        var exploding = JsonSerializer.Deserialize(
            await dispatcher.DispatchJsonAsync(
                explodingJson,
                new CommandContext("main", "main", new CapabilitySet(["test:explode"]))),
            TaruiJsonContext.Default.InvokeResponse);

        Assert(exploding is not null, "The dispatcher must produce a response envelope after a handler throws.");
        Assert(!exploding!.Success, "A handler exception must surface as a non-success response.");
        Assert(exploding.Error is not null, "The error envelope must be populated.");
        Assert(!exploding.Error!.Code.Contains("internal", StringComparison.OrdinalIgnoreCase),
            "Handler exceptions must not leak implementation details to the web layer.");

        // The dispatcher must remain usable for the next call after a handler throws.
        var echoJson = JsonSerializer.Serialize(Request("echo-1"), TaruiJsonContext.Default.InvokeRequest);
        var echo = JsonSerializer.Deserialize(
            await dispatcher.DispatchJsonAsync(
                echoJson,
                new CommandContext("main", "main", new CapabilitySet(["test:echo"]))),
            TaruiJsonContext.Default.InvokeResponse);
        Assert(echo is not null && echo.Success, "The dispatcher must continue to serve subsequent calls after a handler throws.");
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

using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Ipc.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        await DeniesCommandsOutsideCapability();
        await DispatchesRegisteredCommandWithoutDynamicBinding();
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
}

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Autostart;

namespace Tarui.Autostart.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            AutostartPluginRegistersAllCommands();
            AutostartDispatchForwardsAndGatesAsync().GetAwaiter().GetResult();
            AutostartArgsAreValidatedAndQuoted();
            AutostartCommandLineBuildsFromProcessPathAndArgs();
            AutostartDtosRoundTripThroughJsonContext();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }

        Console.WriteLine("Tarui.Autostart self-tests passed.");
        return 0;
    }

    private static void AutostartPluginRegistersAllCommands()
    {
        var builder = new CommandRouterBuilder();
        new AutostartPlugin(new RecordingAutostartService()).ConfigureCommands(builder);
        var router = builder.Build();

        var expected = new[]
        {
            "plugin:autostart|is-enabled",
            "plugin:autostart|enable",
            "plugin:autostart|disable",
        };

        foreach (var command in expected)
        {
            Assert(router.Commands.Contains(command), $"The autostart plugin must register command '{command}'.");
        }

        Assert(router.RegisteredPermissions.Count == expected.Length,
            "Every autostart permission must be registered exactly once with no extras.");
    }

    private static async Task AutostartDispatchForwardsAndGatesAsync()
    {
        var service = new RecordingAutostartService();
        var builder = new CommandRouterBuilder();
        new AutostartPlugin(service).ConfigureCommands(builder);
        var router = builder.Build();

        var enabled = await router.InvokeAsync(
            new InvokeRequest(1, "a1", "plugin:autostart|enable", Element(new AutostartEnableOptions(["--minimized"])), "main", "main"),
            new CommandContext("main", "main", new CapabilitySet(["plugin:autostart|enable"], [], [])));
        Assert(enabled.Success, $"enable must succeed when granted the enable permission. {enabled.Error?.Code}");

        var denied = await router.InvokeAsync(
            new InvokeRequest(1, "a2", "plugin:autostart|disable", Element(new EmptyArgs()), "main", "main"),
            new CommandContext("main", "main", new CapabilitySet(["plugin:autostart|enable"], [], [])));
        Assert(!denied.Success && denied.Error?.Code == "PERMISSION_DENIED",
            "disable must be denied without the disable permission.");

        var invalid = await router.InvokeAsync(
            new InvokeRequest(1, "a3", "plugin:autostart|enable", Element(new AutostartEnableOptions([new string('x', AutostartConfig.MaxSingleArgLength + 1)])), "main", "main"),
            new CommandContext("main", "main", new CapabilitySet(["plugin:autostart|enable"], [], [])));
        Assert(!invalid.Success && invalid.Error?.Code == "INVALID_ARGUMENTS",
            "An oversized pre-configured argument must be rejected.");
    }

    private static void AutostartArgsAreValidatedAndQuoted()
    {
        AutostartConfig.ValidateArgs(null);
        AutostartConfig.ValidateArgs([]);
        AutostartConfig.ValidateArgs(["--whoa", "simple"]);

        Assert(Throws(() => AutostartConfig.ValidateArgs([new string('a', AutostartConfig.MaxSingleArgLength + 1)])),
            "A single oversized argument must be rejected.");
        Assert(Throws(() => AutostartConfig.ValidateArgs(Enumerable.Repeat("a", AutostartConfig.MaxArgs + 1).ToArray())),
            "Too many arguments must be rejected.");

        var quoted = AutostartConfig.BuildCommandLine("C:\\Program Files\\app.exe", ["--flag"]);
        Assert(quoted.StartsWith("\"C:\\Program Files\\app.exe\"", StringComparison.Ordinal), "A path containing spaces must be quoted.");
        Assert(quoted.EndsWith("--flag", StringComparison.Ordinal), "A simple argument must stay unquoted.");
    }

    private static void AutostartCommandLineBuildsFromProcessPathAndArgs()
    {
        var command = AutostartConfig.BuildCommandLine("/usr/bin/app", ["--quiet", "path with space"]);
        Assert(command.StartsWith("/usr/bin/app ", StringComparison.Ordinal), "A path without spaces must not be quoted.");
        Assert(command.Contains("\"path with space\""), "An argument containing a space must be quoted.");
    }

    private static void AutostartDtosRoundTripThroughJsonContext()
    {
        var state = new AutostartState(true);
        var roundTrippedState = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(state, TaruiJsonContext.Default.AutostartState),
            TaruiJsonContext.Default.AutostartState);
        Assert(roundTrippedState is { Enabled: true }, "The autostart state must round-trip through the JSON context.");

        var options = new AutostartEnableOptions(["--x"]);
        var roundTrippedOptions = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(options, TaruiJsonContext.Default.AutostartEnableOptions),
            TaruiJsonContext.Default.AutostartEnableOptions);
        Assert(roundTrippedOptions is { Args: ["--x"] }, "The enable options must round-trip through the JSON context.");
    }

    private static JsonElement Element<T>(T value) =>
        JsonSerializer.SerializeToElement(value, (JsonTypeInfo<T>)JsonTypeInfoFor(typeof(T)));

    private static object JsonTypeInfoFor(Type type) => type switch
    {
        _ when type == typeof(AutostartEnableOptions) => TaruiJsonContext.Default.AutostartEnableOptions,
        _ when type == typeof(EmptyArgs) => TaruiJsonContext.Default.EmptyArgs,
        _ => throw new InvalidOperationException($"No JsonTypeInfo configured for '{type.Name}'."),
    };

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

    private sealed class RecordingAutostartService : IAutostartService
    {
        public ValueTask<AutostartState> IsEnabledAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(new AutostartState(false));

        public ValueTask<Unit> EnableAsync(AutostartEnableOptions options, CancellationToken cancellationToken)
            => ValueTask.FromResult(new Unit());

        public ValueTask<Unit> DisableAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(new Unit());
    }
}
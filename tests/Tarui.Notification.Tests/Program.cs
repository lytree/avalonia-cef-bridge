using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Notification;

namespace Tarui.Notification.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            NotificationPluginRegistersAllCommands();
            NotificationDispatchForwardsAndGatesAsync().GetAwaiter().GetResult();
            NotificationValidatorRejectsBlankOrOversizedPayloads();
            NotificationEventDtosRoundTripThroughJsonContext();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }

        Console.WriteLine("Tarui.Notification self-tests passed.");
        return 0;
    }

    private static void NotificationPluginRegistersAllCommands()
    {
        var builder = new CommandRouterBuilder();
        new NotificationPlugin(new RecordingNotificationService()).ConfigureCommands(builder);
        var router = builder.Build();

        var expected = new[]
        {
            "plugin:notification|permission-state",
            "plugin:notification|request-permission",
            "plugin:notification|show",
            "plugin:notification|cancel",
        };

        foreach (var command in expected)
        {
            Assert(router.Commands.Contains(command), $"The notification plugin must register command '{command}'.");
        }

        Assert(router.RegisteredPermissions.Count == expected.Length,
            "Every notification permission must be registered exactly once with no extras.");
    }

    private static async Task NotificationDispatchForwardsAndGatesAsync()
    {
        var service = new RecordingNotificationService();
        var builder = new CommandRouterBuilder();
        new NotificationPlugin(service).ConfigureCommands(builder);
        var router = builder.Build();

        var show = await router.InvokeAsync(
            new InvokeRequest(1, "n1", "plugin:notification|show", Element(new NotificationOptions("n", Title: "T", Body: "B")), "main", "main"),
            new CommandContext("main", "main", new CapabilitySet(["plugin:notification|show"], [], [])));
        Assert(show.Success, $"show must succeed when granted the show permission. {show.Error?.Code}");

        var denied = await router.InvokeAsync(
            new InvokeRequest(1, "n2", "plugin:notification|cancel", Element(new NotificationCancelOptions("n")), "main", "main"),
            new CommandContext("main", "main", new CapabilitySet(["plugin:notification|show"], [], [])));
        Assert(!denied.Success && denied.Error?.Code == "PERMISSION_DENIED",
            "cancel must be denied without the cancel permission.");
    }

    private static void NotificationValidatorRejectsBlankOrOversizedPayloads()
    {
        NotificationValidator.Validate(new NotificationOptions("ok", Title: "T", Body: "B"));

        Assert(Throws(() => NotificationValidator.Validate(new NotificationOptions("id", Title: " ", Body: "B"))),
            "A blank title must be rejected.");
        Assert(Throws(() => NotificationValidator.Validate(new NotificationOptions("id", Title: "T", Body: ""))),
            "A missing body must be rejected.");
        Assert(Throws(() => NotificationValidator.Validate(
                new NotificationOptions("id", new string('t', NotificationValidator.MaxTitleLength + 1), Body: "B"))),
            "An oversized title must be rejected.");
        Assert(Throws(() => NotificationValidator.Validate(
                new NotificationOptions("id", Title: "T", new string('b', NotificationValidator.MaxBodyLength + 1)))),
            "An oversized body must be rejected.");
        Assert(Throws(() => NotificationValidator.Validate(
                new NotificationOptions(new string('i', NotificationValidator.MaxIdLength + 1), Title: "T", Body: "B"))),
            "An oversized id must be rejected.");
    }

    private static void NotificationEventDtosRoundTripThroughJsonContext()
    {
        var activated = new NotificationEvent("n", "T", "B", "click");
        var roundTripped = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(activated, TaruiJsonContext.Default.NotificationEvent),
            TaruiJsonContext.Default.NotificationEvent);
        Assert(roundTripped is { Id: "n", Title: "T", Body: "B", Action: "click" },
            "The activated/dismissed payload must round-trip through the JSON context.");
    }

    private static JsonElement Element<T>(T value) =>
        JsonSerializer.SerializeToElement(value, (JsonTypeInfo<T>)JsonTypeInfoFor(typeof(T)));

    private static object JsonTypeInfoFor(Type type) => type switch
    {
        _ when type == typeof(NotificationOptions) => TaruiJsonContext.Default.NotificationOptions,
        _ when type == typeof(NotificationCancelOptions) => TaruiJsonContext.Default.NotificationCancelOptions,
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

    private sealed class RecordingNotificationService : INotificationService
    {
        public ValueTask<NotificationPermissionStateResult> GetPermissionStateAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(new NotificationPermissionStateResult(NotificationPermissionState.Granted));

        public ValueTask<NotificationPermissionStateResult> RequestPermissionAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(new NotificationPermissionStateResult(NotificationPermissionState.Granted));

        public ValueTask<Unit> ShowAsync(NotificationOptions options, CancellationToken cancellationToken)
            => ValueTask.FromResult(new Unit());

        public ValueTask<Unit> CancelAsync(NotificationCancelOptions options, CancellationToken cancellationToken)
            => ValueTask.FromResult(new Unit());
    }
}
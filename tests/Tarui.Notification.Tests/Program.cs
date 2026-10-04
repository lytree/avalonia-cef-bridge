using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Notification;
using Tarui.Shell.Toast;

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
            NotificationValidatorRejectsOversizedActionLists();
            NotificationEventDtosRoundTripThroughJsonContext();
            NotificationActionsRoundTripThroughJsonContext();
            ToastContentBuilderProducesEscapedToastXml();
            ToastServiceDegradesToFallbackWithoutAnEngine().GetAwaiter().GetResult();
            ToastServiceKeepsDedupSemanticsAcrossFallbacks().GetAwaiter().GetResult();
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

    private static void NotificationValidatorRejectsOversizedActionLists()
    {
        NotificationValidator.Validate(new NotificationOptions(
            "id", Title: "T", Body: "B", Actions: [new NotificationAction("a", "Approve")]));

        var tooManyActions = Enumerable.Range(0, NotificationValidator.MaxActionsPerNotification + 1)
            .Select(index => new NotificationAction($"a{index}", $"Label {index}"))
            .ToArray();
        Assert(Throws(() => NotificationValidator.Validate(
                new NotificationOptions("id", Title: "T", Body: "B", Actions: tooManyActions))),
            "More than the maximum number of actions must be rejected.");

        Assert(Throws(() => NotificationValidator.Validate(
                new NotificationOptions("id", Title: "T", Body: "B", Actions: [new NotificationAction(" ", "Label")]))),
            "A blank action id must be rejected.");
        Assert(Throws(() => NotificationValidator.Validate(
                new NotificationOptions("id", Title: "T", Body: "B", Actions: [new NotificationAction("a", new string('l', NotificationValidator.MaxActionTextLength + 1))]))),
            "An oversized action label must be rejected.");
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

    private static void NotificationActionsRoundTripThroughJsonContext()
    {
        var options = new NotificationOptions(
            "n", Title: "T", Body: "B", Actions: [new NotificationAction("ok", "Accept"), new NotificationAction("no", "Decline")]);
        var roundTripped = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(options, TaruiJsonContext.Default.NotificationOptions),
            TaruiJsonContext.Default.NotificationOptions);
        Assert(roundTripped is { Actions: [_, _] },
            "Notification actions must round-trip through the JSON context.");
        Assert(roundTripped!.Actions![0] is { Id: "ok", Label: "Accept" },
            "The first action must round-trip with its id and label.");
    }

    private static void ToastContentBuilderProducesEscapedToastXml()
    {
        var xml = ToastContentBuilder.Build(new NotificationOptions(
            "n",
            Title: "T & <Title>",
            Body: "Line \"1\" <2>",
            Icon: "appData/icon.png",
            Actions: [new NotificationAction("ok", "Accept & run")]));

        Assert(xml.StartsWith("<toast><visual><binding template=\"ToastGeneric\">", StringComparison.Ordinal),
            "The toast XML must open with the ToastGeneric binding.");
        Assert(xml.Contains("<text>T &amp; &lt;Title&gt;</text>", StringComparison.Ordinal),
            "The title must be XML-escaped.");
        Assert(xml.Contains("<text>Line &quot;1&quot; &lt;2&gt;</text>", StringComparison.Ordinal),
            "The body must be XML-escaped.");
        Assert(xml.Contains("<image placement=\"appLogoOverride\" src=\"appData/icon.png\"/>", StringComparison.Ordinal),
            "The icon must be rendered as an appLogoOverride image.");
        Assert(xml.Contains("<audio silent=\"true\"/>", StringComparison.Ordinal),
            "A silent notification must render the silent audio element.");

        var audible = ToastContentBuilder.Build(new NotificationOptions("n", Title: "T", Body: "B", Sound: true));
        Assert(!audible.Contains("<audio", StringComparison.Ordinal),
            "An audible notification must not render a silent audio element.");

        Assert(xml.Contains("<action content=\"Accept &amp; run\" arguments=\"ok\" activationType=\"foreground\"/>", StringComparison.Ordinal),
            "Actions must render as foreground buttons carrying the action id in arguments.");

        var plain = ToastContentBuilder.Build(new NotificationOptions("n", Title: "T", Body: "B"));
        Assert(!plain.Contains("<actions", StringComparison.Ordinal) && !plain.Contains("<image", StringComparison.Ordinal),
            "A notification without icon or actions must not render those elements.");
    }

    private static async Task ToastServiceDegradesToFallbackWithoutAnEngine()
    {
        var fallback = new RecordingFallbackService();
        var sink = new RecordingSink();
        using var service = new WindowsToastNotificationService(
            new ToastConfiguration("test.aumid", "Test", IconUri: null), sink, fallback);

        var permission = await service.GetPermissionStateAsync(CancellationToken.None);
        Assert(permission.Permission == NotificationPermissionState.Granted,
            "The toast service must report the granted permission state.");

        await service.ShowAsync(new NotificationOptions("n1", Title: "T", Body: "B"), CancellationToken.None);
        Assert(fallback.Shown.Count == 1 && fallback.Shown[0].Id == "n1",
            "Without an engine the show must be delegated to the fallback backend.");
        Assert(sink.Events.Count == 0, "Fallback-shown notifications must not emit toast lifecycle events.");

        await service.CancelAsync(new NotificationCancelOptions("n1"), CancellationToken.None);
        Assert(fallback.Cancelled.Count == 1 && fallback.Cancelled[0] == "n1",
            "Cancelling a fallback-shown notification must be delegated to the fallback backend.");
    }

    private static async Task ToastServiceKeepsDedupSemanticsAcrossFallbacks()
    {
        var fallback = new RecordingFallbackService();
        using var service = new WindowsToastNotificationService(
            new ToastConfiguration("test.aumid", "Test", IconUri: null), new RecordingSink(), fallback);

        await service.ShowAsync(new NotificationOptions("dup", Title: "T", Body: "B"), CancellationToken.None);
        var duplicateMessage = await ThrowsMessageAsync(async () => await service
            .ShowAsync(new NotificationOptions("dup", Title: "T", Body: "B"), CancellationToken.None));
        Assert(duplicateMessage is not null,
            "Showing the same id twice must throw even on the fallback path.");
        Assert(duplicateMessage!.Contains("already showing", StringComparison.Ordinal),
            "The duplicate-show error must describe the id collision.");

        var cancelMessage = await ThrowsMessageAsync(
            async () => await service.CancelAsync(new NotificationCancelOptions("missing"), CancellationToken.None));
        Assert(cancelMessage is not null, "Cancelling an unknown id must throw.");
        Assert(cancelMessage!.Contains("missing", StringComparison.Ordinal),
            "The unknown-cancel error must name the id.");
        Assert(fallback.Cancelled.Count == 0,
            "An unknown cancel must never reach the fallback backend.");
    }

    private static async Task<string?> ThrowsMessageAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
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

    private sealed class RecordingFallbackService : INotificationService
    {
        public List<NotificationOptions> Shown { get; } = [];
        public List<string> Cancelled { get; } = [];

        public ValueTask<NotificationPermissionStateResult> GetPermissionStateAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(new NotificationPermissionStateResult(NotificationPermissionState.Granted));

        public ValueTask<NotificationPermissionStateResult> RequestPermissionAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(new NotificationPermissionStateResult(NotificationPermissionState.Granted));

        public ValueTask<Unit> ShowAsync(NotificationOptions options, CancellationToken cancellationToken)
        {
            Shown.Add(options);
            return ValueTask.FromResult(new Unit());
        }

        public ValueTask<Unit> CancelAsync(NotificationCancelOptions options, CancellationToken cancellationToken)
        {
            Cancelled.Add(options.Id);
            return ValueTask.FromResult(new Unit());
        }
    }

    private sealed class RecordingSink : INotificationEventSink
    {
        public List<NotificationEvent> Events { get; } = [];

        public void DispatchActivated(NotificationEvent notificationEvent) => Events.Add(notificationEvent);

        public void DispatchDismissed(NotificationEvent notificationEvent) => Events.Add(notificationEvent);
    }
}
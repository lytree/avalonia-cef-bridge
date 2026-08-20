using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Shell.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        await RegistryTracksWindowEntries();
        await RouterDeliversEventsToTargetWindows();
        await RouterBroadcastsToAllWindows();
        await RouterNotifiesHubSubscribers();
        await RouterRoutesByTargetWindowPresence();
        await CapabilityLoaderMergesWindowPermissions();
        await CapabilityLoaderHandlesMissingDirectory();
        Console.WriteLine("Tarui.Shell self-tests passed.");
        return 0;
    }

    private static async Task RegistryTracksWindowEntries()
    {
        var registry = new WindowRegistry();
        var mainSink = new FakeSink();
        var editorSink = new FakeSink();
        var context = new CommandContext("main", "main", new CapabilitySet([]));

        registry.Add("main", CreateEntry(mainSink, context));
        registry.Add("editor", CreateEntry(editorSink, context));
        Assert(
            registry.Labels.OrderBy(static label => label, StringComparer.Ordinal).SequenceEqual(["editor", "main"]),
            "The registry must expose all window labels.");

        Assert(registry.Get("main").Sink == mainSink, "Get must return the stored entry.");
        Assert(registry.TryGet("editor", out var editor) && editor.Sink == editorSink, "TryGet must find windows.");
        Assert(!registry.TryGet("missing", out _), "TryGet must not find unknown windows.");

        Assert(
            registry.TryGetSink("editor", out var sink) && sink == editorSink,
            "TryGetSink must resolve the window sink.");

        var duplicate = false;
        try
        {
            registry.Add("main", CreateEntry(mainSink, context));
        }
        catch (InvalidOperationException)
        {
            duplicate = true;
        }

        Assert(duplicate, "Adding a duplicate label must fail.");

        Assert(registry.Remove("editor"), "Remove must delete the window.");
        Assert(!registry.TryGet("editor", out _), "Removed windows must be gone.");

        var missing = false;
        try
        {
            _ = registry.Get("editor");
        }
        catch (KeyNotFoundException)
        {
            missing = true;
        }

        Assert(missing, "Getting an unknown label must fail.");
    }

    private static async Task RouterDeliversEventsToTargetWindows()
    {
        var registry = new FakeSinkRegistry();
        var main = registry.Add("main");
        var editor = registry.Add("editor");
        var router = new EventRouter(registry, new EventHub());
        var payload = JsonSerializer.SerializeToElement(new WindowFocusChanged(true), TaruiJsonContext.Default.WindowFocusChanged);

        await router.EmitToWindowAsync("editor", "window://focus-changed", payload);

        Assert(main.Events.Count == 0, "A targeted emit must not reach other windows.");
        Assert(
            editor.Events.Count == 1 && editor.Events[0].Event == "window://focus-changed",
            "A targeted emit must reach the target window.");
        Assert(
            editor.Events[0].Payload.GetProperty("focused").GetBoolean(),
            "The event payload must round-trip.");
    }

    private static async Task RouterBroadcastsToAllWindows()
    {
        var registry = new FakeSinkRegistry();
        var main = registry.Add("main");
        var editor = registry.Add("editor");
        var router = new EventRouter(registry, new EventHub());
        var payload = JsonSerializer.SerializeToElement(
            new WindowLabelOptions("editor"),
            TaruiJsonContext.Default.WindowLabelOptions);

        await router.EmitToAllAsync("window://destroyed", payload);

        Assert(main.Events.Count == 1, "A broadcast must reach every window.");
        Assert(editor.Events.Count == 1, "A broadcast must reach every window.");
        Assert(
            main.Events[0].Event == "window://destroyed" && editor.Events[0].Event == "window://destroyed",
            "The broadcast event name must round-trip.");
    }

    private static async Task RouterNotifiesHubSubscribers()
    {
        var registry = new FakeSinkRegistry();
        var router = new EventRouter(registry, new EventHub());
        var received = new List<string>();
        using var subscription = router.Subscribe<JsonElement>(
            "shell://theme-changed",
            payload => received.Add(payload.GetProperty("theme").GetString() ?? string.Empty));

        var payload = JsonSerializer.SerializeToElement(new ThemeChanged("dark"), TaruiJsonContext.Default.ThemeChanged);
        await router.EmitToWindowAsync("main", "shell://theme-changed", payload);

        Assert(received.SequenceEqual(["dark"]), "Hub subscribers must observe emitted events.");

        received.Clear();
        subscription.Dispose();
        await router.EmitToWindowAsync("main", "shell://theme-changed", payload);
        Assert(received.Count == 0, "Disposed subscriptions must stop receiving events.");
    }

    private static async Task RouterRoutesByTargetWindowPresence()
    {
        var registry = new FakeSinkRegistry();
        var main = registry.Add("main");
        var editor = registry.Add("editor");
        var router = new EventRouter(registry, new EventHub());
        var payload = JsonSerializer.SerializeToElement(new Unit(), TaruiJsonContext.Default.Unit);

        await router.EmitAsync("a://event", payload, "editor");
        Assert(
            editor.Events.Count == 1 && main.Events.Count == 0,
            "A named target must deliver to that window only.");

        await router.EmitAsync("a://event", payload, null);
        Assert(
            editor.Events.Count == 2 && main.Events.Count == 1,
            "A null target must broadcast to all windows.");

        await router.EmitAsync("a://event", payload, "missing");
        Assert(
            editor.Events.Count == 2 && main.Events.Count == 1,
            "An unknown target must be a no-op for sinks.");
    }

    private static async Task CapabilityLoaderMergesWindowPermissions()
    {
        using var directory = new TempDirectory();
        directory.Write(
            "main.json",
            """
            {
              "identifier": "main",
              "windows": ["main"],
              "permissions": ["core:app|get-info"]
            }
            """);
        directory.Write(
            "shared.json",
            """
            {
              "identifier": "shared",
              "windows": ["main", "editor"],
              "permissions": ["core:window|list"]
            }
            """);
        directory.Write(
            "editor.json",
            """
            {
              "identifier": "editor",
              "windows": ["editor"],
              "permissions": ["plugin:dialog|open"]
            }
            """);
        directory.Write(
            "ignored.json",
            """
            {
              "identifier": "ignored",
              "permissions": ["core:process|exit"]
            }
            """);

        var capabilities = CapabilityLoader.Load(directory.Path);
        Assert(capabilities.Count == 2, "Only windows referenced by capability files must appear.");

        var main = capabilities["main"];
        Assert(main.Allows("core:app|get-info"), "The main window must inherit its own permissions.");
        Assert(main.Allows("core:window|list"), "The main window must inherit shared permissions.");
        Assert(!main.Allows("plugin:dialog|open"), "The main window must not gain editor permissions.");

        var editor = capabilities["editor"];
        Assert(editor.Allows("core:window|list"), "The editor window must inherit shared permissions.");
        Assert(editor.Allows("plugin:dialog|open"), "The editor window must inherit its own permissions.");
        Assert(!editor.Allows("core:app|get-info"), "The editor window must not gain main permissions.");
    }

    private static async Task CapabilityLoaderHandlesMissingDirectory()
    {
        var capabilities = CapabilityLoader.Load(Path.Combine(Path.GetTempPath(), $"tarui-missing-{Guid.NewGuid():N}"));
        Assert(capabilities.Count == 0, "A missing capability directory must yield no capabilities.");
    }

    private static WindowRegistry.Entry CreateEntry(FakeSink sink, CommandContext context) =>
        new(null!, sink, context);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeSink : IEventSink
    {
        public List<(string Event, JsonElement Payload)> Events { get; } = [];

        public ValueTask SendEventAsync(
            string eventName,
            JsonElement payload,
            CancellationToken cancellationToken = default)
        {
            Events.Add((eventName, payload));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSinkRegistry : IWindowSinkRegistry
    {
        private readonly Dictionary<string, FakeSink> _sinks = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Labels => _sinks.Keys.ToArray();

        public FakeSink Add(string label)
        {
            var sink = new FakeSink();
            _sinks[label] = sink;
            return sink;
        }

        public bool TryGetSink(string label, out IEventSink sink)
        {
            if (_sinks.TryGetValue(label, out var fake))
            {
                sink = fake;
                return true;
            }

            sink = null!;
            return false;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() => Path = Directory.CreateTempSubdirectory("tarui-shell-tests-").FullName;

        public string Path { get; }

        public void Write(string fileName, string content) =>
            File.WriteAllText(System.IO.Path.Combine(Path, fileName), content);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup for temporary test data.
            }
        }
    }
}

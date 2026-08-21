using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Shell;

namespace Tarui.Capabilities.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        CapabilitySetAllowsAndDeniesPermissions();
        CapabilitySetTracksEventsAndScopes();
        DenyRulesWinOverAllowRules();
        LoaderParsesLegacyFlatPermissions();
        LoaderParsesStructuredManifestWithScopesAndEvents();
        LoaderMergesPermissionsAcrossManifests();
        LoaderRejectsUnknownRootField();
        LoaderRejectsDuplicatePermissionIdentifiers();
        LoaderRejectsInvalidPlatform();
        LoaderRejectsMissingWindows();
        LoaderRejectsInvalidScope();
        await RouterEnforcesScopedPermissions();
        await RouterDeniesScopedPermission();
        EventNamesRestrictWebEmitsToUserNamespace();
        await EventRouterGatesReservedEventDelivery();
        PathPolicyRejectsRootedDeviceAndIllegalPaths();
        PathPolicyAllowsWithinBase();
        PathPolicyRejectsSymlinkEscape();
        PathPolicyEnforcesSizeLimits();
        await PathPolicyWritesAtomically();
        Console.WriteLine("Tarui.Capabilities self-tests passed.");
        return 0;
    }

    private static void EventNamesRestrictWebEmitsToUserNamespace()
    {
        Assert(EventNames.IsUserEvent("user://custom"), "A user:// event must be detected as a Web event.");
        Assert(!EventNames.IsUserEvent("window://focus-changed"), "A native event is not a Web event.");
        Assert(!EventNames.IsUserEvent("plain-event"), "A bare event name is not a Web event.");

        Assert(EventNames.IsReserved("window://focus-changed"), "window:// must be reserved.");
        Assert(EventNames.IsReserved("app://second-instance"), "app:// must be reserved.");
        Assert(EventNames.IsReserved("shell://theme-changed"), "shell:// must be reserved.");
        Assert(EventNames.IsReserved("fs://file-watched"), "fs:// must be reserved.");
        Assert(EventNames.IsReserved("user://custom") is false, "user:// must not be reserved.");
        Assert(EventNames.IsReserved("plain-event") is false, "A bare event name is not reserved.");

        EventNames.ValidateWebEmit("user://custom");
        AssertThrows<EventNamespaceDeniedException>(
            () => EventNames.ValidateWebEmit("window://focus-changed"),
            "Web must not forge a window:// event.");
        AssertThrows<EventNamespaceDeniedException>(
            () => EventNames.ValidateWebEmit("app://second-instance"),
            "Web must not forge an app:// event.");
        AssertThrows<EventNamespaceDeniedException>(
            () => EventNames.ValidateWebEmit("plain-event"),
            "Web must not emit outside the user:// namespace.");
    }

    private static async Task EventRouterGatesReservedEventDelivery()
    {
        var registry = new CapabilityRegistry();
        var authorized = registry.Add("authorized", new CapabilitySet([], ["window://focus-changed"], []));
        var unauthorized = registry.Add("unauthorized", new CapabilitySet([], [], []));
        var userSink = registry.Add("user-sink", new CapabilitySet([], [], []));
        var router = new EventRouter(registry, new EventHub());

        var focusPayload = JsonSerializer.SerializeToElement(
            new WindowFocusChanged(true),
            TaruiJsonContext.Default.WindowFocusChanged);

        await router.EmitToAllAsync("window://focus-changed", focusPayload);
        Assert(
            authorized.Events.Count == 1,
            "A window that declared receive authorization must get the reserved event.");
        Assert(
            unauthorized.Events.Count == 0,
            "A window without receive authorization must be skipped.");
        Assert(
            userSink.Events.Count == 0,
            "A window without receive authorization must be skipped in a broadcast.");

        var userPayload = JsonSerializer.SerializeToElement(new Unit(), TaruiJsonContext.Default.Unit);
        await router.EmitToAllAsync("user://mine", userPayload);
        Assert(
            unauthorized.Events.Count == 1 && unauthorized.Events[0].Event == "user://mine",
            "A user:// event carries no native data and must reach every window.");
    }

    private static void PathPolicyRejectsRootedDeviceAndIllegalPaths()
    {
        var policy = new FileAccessPolicy();
        using var dir = new TempDirectory();
        var baseDir = dir.Path;

        Assert(DenialReason(() => policy.Authorize(FileAccessKind.Read, baseDir, "C:\\x")) == PathDenialReason.Rooted,
            "An absolute drive path must be rejected as rooted.");
        Assert(DenialReason(() => policy.Authorize(FileAccessKind.Read, baseDir, "\\escape")) == PathDenialReason.Rooted,
            "A rooted-relative path must be rejected as rooted.");
        Assert(DenialReason(() => policy.Authorize(FileAccessKind.Read, baseDir, "/escape")) == PathDenialReason.Rooted,
            "A forward-slash rooted path must be rejected as rooted.");
        Assert(DenialReason(() => policy.Authorize(FileAccessKind.Read, baseDir, "\\\\?\\C:\\Windows")) == PathDenialReason.DeviceOrUnc,
            "A device path must be rejected.");
        Assert(DenialReason(() => policy.Authorize(FileAccessKind.Read, baseDir, "\\\\server\\share")) == PathDenialReason.DeviceOrUnc,
            "A UNC share must be rejected.");
        Assert(DenialReason(() => policy.Authorize(FileAccessKind.Read, baseDir, "a\u0001b")) == PathDenialReason.ControlCharacter,
            "A control character must be rejected.");
        Assert(DenialReason(() => policy.Authorize(FileAccessKind.Read, baseDir, "..\\secret")) == PathDenialReason.IllegalSegment,
            "A parent traversal segment must be rejected.");
        Assert(DenialReason(() => policy.Authorize(FileAccessKind.Read, baseDir, "file.txt:zone")) == PathDenialReason.IllegalSegment,
            "An alternate data stream segment must be rejected.");
        Assert(DenialReason(() => policy.Authorize(FileAccessKind.Read, baseDir, "a//b")) == PathDenialReason.IllegalSegment,
            "An empty segment must be rejected.");
    }

    private static void PathPolicyAllowsWithinBase()
    {
        var policy = new FileAccessPolicy();
        using var dir = new TempDirectory();
        var baseDir = dir.Path;
        Directory.CreateDirectory(System.IO.Path.Combine(baseDir, "docs", "reports"));

        var allowed = policy.Authorize(FileAccessKind.Read, baseDir, "docs/reports");
        Assert(
            IsWithin(allowed, baseDir),
            "A nested path inside the base directory must be authorized.");
        Assert(
            allowed.TrimEnd(System.IO.Path.DirectorySeparatorChar).EndsWith(
                System.IO.Path.Combine("docs", "reports").TrimEnd(System.IO.Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase),
            "The authorized path must resolve to the requested directory.");
    }

    private static void PathPolicyRejectsSymlinkEscape()
    {
        var policy = new FileAccessPolicy();
        using var dir = new TempDirectory();
        var baseDir = System.IO.Path.Combine(dir.Path, "base");
        var outside = System.IO.Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(outside);

        var linkPath = System.IO.Path.Combine(baseDir, "link");
        try
        {
            Directory.CreateSymbolicLink(linkPath, outside);
        }
        catch (Exception)
        {
            // Creating a symbolic link requires developer mode or elevation on Windows;
            // when the environment cannot produce one, this assertion is skipped rather
            // than failing the suite. The other path checks still run.
            return;
        }

        Assert(
            DenialReason(() => policy.Authorize(FileAccessKind.Read, baseDir, "link/secret.txt")) == PathDenialReason.LinkEscape,
            "A symbolic link escaping the base directory must be rejected.");
    }

    private static void PathPolicyEnforcesSizeLimits()
    {
        var options = new FileAccessPolicyOptions(MaxReadBytes: 100, MaxWriteBytes: 50, MaxTotalBytes: 120);
        var policy = new FileAccessPolicy(options);

        Assert(policy.IsWithinOperationLimit(FileAccessKind.Read, 100), "A read at the per-operation limit must be accepted.");
        Assert(!policy.IsWithinOperationLimit(FileAccessKind.Read, 101), "A read over the per-operation limit must be rejected.");
        Assert(!policy.IsWithinOperationLimit(FileAccessKind.Write, 51), "A write over the per-operation limit must be rejected.");

        Assert(policy.TryReserveTotalBytes(80), "Reserving within the cumulative budget must succeed.");
        Assert(!policy.TryReserveTotalBytes(60), "Reserving over the cumulative budget must fail.");
        policy.ReleaseTotalBytes(80);
        Assert(policy.TryReserveTotalBytes(60), "Releasing reserved bytes must restore the budget.");
    }

    private static async Task PathPolicyWritesAtomically()
    {
        var options = new FileAccessPolicyOptions(MaxWriteBytes: 64);
        var policy = new FileAccessPolicy(options);
        using var dir = new TempDirectory();
        var targetPath = System.IO.Path.Combine(dir.Path, "out.txt");

        await policy.WriteAllBytesAtomicAsync(targetPath, "hello"u8.ToArray());
        Assert(
            File.ReadAllText(targetPath) == "hello",
            "An atomic write must produce the intended file content.");

        Assert(
            DenialReason(() => policy.WriteAllBytesAtomicAsync(targetPath, new byte[65]).GetAwaiter().GetResult())
                == PathDenialReason.SizeLimit,
            "An over-limit atomic write must be rejected before touching the file.");

        await policy.WriteAllBytesAtomicAsync(targetPath, "world"u8.ToArray());
        Assert(
            File.ReadAllText(targetPath) == "world",
            "An atomic replace must overwrite the previous content durably.");
    }

    private static PathDenialReason DenialReason(Action action)
    {
        try
        {
            action();
        }
        catch (PathAccessDeniedException exception)
        {
            return exception.Reason;
        }

        throw new InvalidOperationException("Expected a PathAccessDeniedException.");
    }

    private static bool IsWithin(string fullPath, string baseFull)
    {
        var relative = System.IO.Path.GetRelativePath(baseFull, fullPath);
        return relative.Length == 0
            || relative == "."
            || (!System.IO.Path.IsPathRooted(relative)
                && !relative.Equals("..", StringComparison.Ordinal)
                && !relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private sealed class CapabilityRegistry : IWindowSinkRegistry
    {
        private readonly Dictionary<string, FakeSink> _sinks = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CapabilitySet> _capabilities = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Labels => _sinks.Keys.ToArray();

        public FakeSink Add(string label, CapabilitySet capabilities)
        {
            var sink = new FakeSink();
            _sinks[label] = sink;
            _capabilities[label] = capabilities;
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

        public bool TryGetCapabilities(string label, out CapabilitySet capabilities)
        {
            if (_capabilities.TryGetValue(label, out var set))
            {
                capabilities = set;
                return true;
            }

            capabilities = null!;
            return false;
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

    private static void CapabilitySetAllowsAndDeniesPermissions()
    {
        var set = new CapabilitySet(["core:app|get-info"]);
        Assert(set.Allows("core:app|get-info"), "A granted permission must be allowed.");
        Assert(!set.Allows("plugin:fs|read-text-file"), "An unlisted permission must be denied.");
        Assert(new CapabilitySet(["*"]).Allows("anything"), "The wildcard must allow every permission.");
    }

    private static void CapabilitySetTracksEventsAndScopes()
    {
        var set = new CapabilitySet(
            ["plugin:fs|read-text-file"],
            ["window://moved"],
            new KeyValuePair<string, PermissionScope>[]
            {
                new("plugin:fs|read-text-file", new PermissionScope([new PathScope("appData", "**/*.json")], []))
            });

        Assert(set.AllowsEvent("window://moved"), "A granted event must be receivable.");
        Assert(!set.AllowsEvent("menu://item-clicked"), "An unlisted event must be rejected.");
        Assert(set.TryGetScope("plugin:fs|read-text-file", out var scope), "A scoped permission must expose its scope.");
        Assert(scope.Allow.Count == 1 && scope.Deny.Count == 0, "The scope must preserve allow/deny lists.");
        Assert(!set.TryGetScope("core:app|get-info", out _), "An unscoped permission must have no scope.");
    }

    private static void DenyRulesWinOverAllowRules()
    {
        var scope = new PermissionScope(
            [new PathScope("appData", "documents/keep.json")],
            [new PathScope("appData", "documents/secret.json")]);

        Assert(scope.AllowsPath(new PathScope("appData", "documents/keep.json")), "Allow list membership must be detected.");
        Assert(scope.Denies(new PathScope("appData", "documents/secret.json")), "Deny list membership must be detected.");
        Assert(!scope.Denies(new PathScope("appData", "documents/keep.json")), "An allowed entry must not be denied.");
        Assert(scope.Allow.Count == 1 && scope.Deny.Count == 1, "The scope must preserve distinct lists.");
    }

    private static void LoaderParsesLegacyFlatPermissions()
    {
        using var directory = new TempDirectory();
        directory.Write(
            "main.json",
            """
            {
              "identifier": "main",
              "windows": ["main"],
              "permissions": ["core:process|exit"]
            }
            """);

        var capabilities = CapabilityLoader.Load(directory.Path);
        Assert(capabilities["main"].Allows("core:process|exit"), "A legacy flat permission must still load.");
        Assert(capabilities["main"].Events.Count == 0, "A legacy manifest must declare no events.");
    }

    private static void LoaderParsesStructuredManifestWithScopesAndEvents()
    {
        using var directory = new TempDirectory();
        directory.Write(
            "main.json",
            """
            {
              "identifier": "main",
              "windows": ["main"],
              "platforms": ["windows", "macos", "linux"],
              "events": ["app://second-instance"],
              "permissions": [
                "core:app|get-info",
                {
                  "identifier": "plugin:fs|read-text-file",
                  "allow": [{ "base": "appData", "path": "documents/**" }],
                  "deny": [{ "base": "appData", "path": "documents/private/**" }]
                }
              ]
            }
            """);

        var capabilities = CapabilityLoader.Load(directory.Path);
        var main = capabilities["main"];
        Assert(main.Allows("core:app|get-info"), "A plain permission must be granted.");
        Assert(main.AllowsEvent("app://second-instance"), "A declared event must be receivable.");
        Assert(main.TryGetScope("plugin:fs|read-text-file", out var scope), "A structured permission must carry a scope.");
        Assert(scope.Allow.Count == 1 && scope.Deny.Count == 1, "Allow and deny lists must be preserved.");
    }

    private static void LoaderMergesPermissionsAcrossManifests()
    {
        using var directory = new TempDirectory();
        directory.Write(
            "base.json",
            """
            { "identifier": "base", "windows": ["main"], "permissions": ["core:window|list"] }
            """);
        directory.Write(
            "fs.json",
            """
            { "identifier": "fs", "windows": ["main"], "events": ["window://moved"], "permissions": ["plugin:fs|read-text-file"] }
            """);

        var capabilities = CapabilityLoader.Load(directory.Path);
        var main = capabilities["main"];
        Assert(main.Allows("core:window|list") && main.Allows("plugin:fs|read-text-file"),
            "Permissions from multiple manifests must aggregate per window.");
        Assert(main.AllowsEvent("window://moved"), "Events from multiple manifests must aggregate.");
    }

    private static void LoaderRejectsUnknownRootField()
    {
        AssertThrows<InvalidDataException>(
            () => WriteAndLoad("""
                { "identifier": "main", "windows": ["main"], "permissions": [], "bogus": true }
                """),
            "An unknown root field must fail startup.");
    }

    private static void LoaderRejectsDuplicatePermissionIdentifiers()
    {
        AssertThrows<InvalidDataException>(
            () => WriteAndLoad("""
                {
                  "identifier": "main",
                  "windows": ["main"],
                  "permissions": ["plugin:fs|read-text-file", { "identifier": "plugin:fs|read-text-file", "allow": [] }]
                }
                """),
            "Duplicate permission identifiers must fail startup.");
    }

    private static void LoaderRejectsInvalidPlatform()
    {
        AssertThrows<InvalidDataException>(
            () => WriteAndLoad("""
                { "identifier": "main", "windows": ["main"], "platforms": ["windows", "beos"], "permissions": [] }
                """),
            "An illegal platform must fail startup.");
    }

    private static void LoaderRejectsMissingWindows()
    {
        AssertThrows<InvalidDataException>(
            () => WriteAndLoad("""
                { "identifier": "main", "permissions": [] }
                """),
            "A manifest without 'windows' must fail startup.");
    }

    private static void LoaderRejectsInvalidScope()
    {
        AssertThrows<InvalidDataException>(
            () => WriteAndLoad("""
                {
                  "identifier": "main",
                  "windows": ["main"],
                  "permissions": [{ "identifier": "plugin:fs|read-text-file", "allow": [{}] }]
                }
                """),
            "A scope entry without 'base' or 'path' must fail startup.");
    }

    private static async Task RouterEnforcesScopedPermissions()
    {
        var builder = new CommandRouterBuilder();
        builder.Add(
            "test:scoped|read",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            static (_, _, _) => ValueTask.FromResult(new Unit()),
            "plugin:fs|read-text-file",
            static (_, allow, deny) =>
                !deny.Contains(new PathScope("appData", "documents/private/x.json"))
                && allow.Contains(new PathScope("appData", "documents/**")));
        var router = builder.Build();

        var context = new CommandContext(
            "main",
            "main",
            new CapabilitySet(
                ["plugin:fs|read-text-file"],
                [],
                new KeyValuePair<string, PermissionScope>[]
                {
                    new("plugin:fs|read-text-file", new PermissionScope(
                        [new PathScope("appData", "documents/**")],
                        [new PathScope("appData", "documents/keep.json")]))
                }));

        var allowed = await Invoke(router, context);
        Assert(allowed.Success, "A scoped permission matching allow must be authorized.");
    }

    private static async Task RouterDeniesScopedPermission()
    {
        var builder = new CommandRouterBuilder();
        builder.Add(
            "test:scoped|read",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            static (_, _, _) => ValueTask.FromResult(new Unit()),
            "plugin:fs|read-text-file",
            static (_, allow, deny) =>
                !deny.Contains(new PathScope("appData", "documents/secret.json"))
                && allow.Contains(new PathScope("appData", "documents/**")));
        var router = builder.Build();

        var context = new CommandContext(
            "main",
            "main",
            new CapabilitySet(
                ["plugin:fs|read-text-file"],
                [],
                new KeyValuePair<string, PermissionScope>[]
                {
                    new("plugin:fs|read-text-file", new PermissionScope(
                        [new PathScope("appData", "documents/**")],
                        [new PathScope("appData", "documents/secret.json")]))
                }));

        var denied = await Invoke(router, context);
        Assert(!denied.Success && denied.Error!.Code == "SCOPE_DENIED",
            "A deny-rule match must map to the SCOPE_DENIED error code.");
    }

    private static async Task<InvokeResponse> Invoke(CommandRouter router, CommandContext context) =>
        await router.InvokeAsync(
            new InvokeRequest(
                1,
                Guid.NewGuid().ToString("N"),
                "test:scoped|read",
                JsonSerializer.SerializeToElement(new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs),
                context.WindowLabel,
                context.WebViewLabel),
            context);

    private static IReadOnlyDictionary<string, CapabilitySet> WriteAndLoad(string manifestJson)
    {
        using var directory = new TempDirectory();
        directory.Write("cap.json", manifestJson);
        return CapabilityLoader.Load(directory.Path);
    }

    private static void AssertThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() => Path = Directory.CreateTempSubdirectory("tarui-capabilities-tests-").FullName;

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
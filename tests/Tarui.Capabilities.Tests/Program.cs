using System.Text;
using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Shell;

namespace Tarui.Capabilities.Tests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
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
        LoaderRejectsManifestWithoutAnyTarget();
        LoaderParsesWebviewSelectorGrants();
        LoaderAcceptsWebviewOnlyManifest();
        CapabilitySetMergeUnionsGrantsWithoutDroppingDenies();
        ErrorCodesMatchFrozenWireContract();
        WebviewSurfaceResolverMergesWindowAndSelectedGrants();
        LoaderRejectsInvalidScope();
        CatalogMaterializesDefaultAndGuardSets();
        CatalogHonorsCuratedAndExcludedSets();
        CatalogExpandsNestedSetsAndRejectsCycles();
        LoaderExpandsPermissionSetEntries();
        LoaderKeepsScopedGrantAlongsideSetMember();
        LoaderRejectsScopeOnPermissionSet();
        DemoCapabilitiesResolveAgainstRegisteredPlugins();
        CapabilitySurfaceSnapshotMatchesContract(args.Contains("--update", StringComparer.Ordinal));
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
        var router = new EventRouter(registry, new WebviewRegistry(), new EventHub());

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

    /// <summary>
    /// The error codes the bridge returns on the wire are frozen contract, not implementation detail:
    /// the guest <c>invoke</c> branches on them. This pins the constants to their literal values so a
    /// rename cannot slip through as a refactor.
    /// </summary>
    private static void ErrorCodesMatchFrozenWireContract()
    {
        Assert(
            TaruiErrorCodes.CommandNotFound == "COMMAND_NOT_FOUND" &&
            TaruiErrorCodes.PermissionDenied == "PERMISSION_DENIED" &&
            TaruiErrorCodes.InvalidMessage == "INVALID_MESSAGE" &&
            TaruiErrorCodes.BridgeError == "BRIDGE_ERROR",
            "Wire error codes are frozen contract; changing one is a reviewed contract change.");
    }

    private static void LoaderRejectsManifestWithoutAnyTarget()
    {
        AssertThrows<InvalidDataException>(
            () => WriteAndLoadManifests("""
                { "identifier": "nowhere", "permissions": [] }
                """),
            "A manifest declaring neither 'windows' nor 'webviews' must fail startup.");
    }

    private static void LoaderParsesWebviewSelectorGrants()
    {
        var manifests = WriteAndLoadManifests("""
            {
              "identifier": "side-tools",
              "windows": ["main"],
              "webviews": ["side"],
              "permissions": ["plugin:fs|read-text-file"]
            }
            """);

        Assert(
            manifests.Windows["main"].Allows("plugin:fs|read-text-file"),
            "A windows target keeps granting to the window profile.");
        Assert(
            manifests.Webviews["side"].Allows("plugin:fs|read-text-file"),
            "A webviews target grants to the matching web view label.");
        Assert(
            !manifests.Webviews.ContainsKey("main"),
            "A webviews grant must not leak to unrelated surface labels.");
    }

    private static void LoaderAcceptsWebviewOnlyManifest()
    {
        var manifests = WriteAndLoadManifests("""
            {
              "identifier": "surface-only",
              "webviews": ["side"],
              "permissions": ["plugin:fs|read-text-file"],
              "events": ["fs://watch"]
            }
            """);

        Assert(
            manifests.Windows.Count == 0,
            "A webview-only manifest must not fabricate a window profile.");
        var side = manifests.Webviews["side"];
        Assert(
            side.Allows("plugin:fs|read-text-file") && side.AllowsEvent("fs://watch"),
            "A webview-only manifest grants its permissions and events to the named surface.");
    }

    private static void CapabilitySetMergeUnionsGrantsWithoutDroppingDenies()
    {
        var window = new CapabilitySet(
            ["plugin:fs|read-text-file"],
            ["window://focus-changed"],
            [new KeyValuePair<string, PermissionScope>(
                "plugin:fs|read-text-file",
                new PermissionScope([new PathScope("/data", "*.txt")], [new PathScope("/data", "secret*")]))]);
        var selected = new CapabilitySet(
            ["plugin:http|fetch", "plugin:fs|read-text-file"],
            ["fs://watch"],
            [new KeyValuePair<string, PermissionScope>(
                "plugin:fs|read-text-file",
                new PermissionScope([new PathScope("/data", "*.log")], [new PathScope("/data", "logs/secret*")]))]);

        var merged = window.Merge(selected);

        Assert(
            merged.Allows("plugin:fs|read-text-file") &&
            merged.Allows("plugin:http|fetch") &&
            merged.AllowsEvent("window://focus-changed") &&
            merged.AllowsEvent("fs://watch"),
            "A merge unions permissions and events from both sides.");
        Assert(
            merged.TryGetScope("plugin:fs|read-text-file", out var scope) &&
            scope.Allow.Contains(new PathScope("/data", "*.txt")) &&
            scope.Allow.Contains(new PathScope("/data", "*.log")) &&
            scope.Deny.Contains(new PathScope("/data", "secret*")) &&
            scope.Deny.Contains(new PathScope("/data", "logs/secret*")),
            "A merge keeps every allow and deny rule, so denies cannot be merged away.");
    }

    private static void WebviewSurfaceResolverMergesWindowAndSelectedGrants()
    {
        var windowProfile = new CapabilitySet(["plugin:fs|read-text-file"]);
        var selected = new CapabilitySet(["plugin:http|fetch"]);
        var provider = new StaticProvider(
            new Dictionary<string, CapabilitySet> { ["main"] = windowProfile },
            new Dictionary<string, CapabilitySet> { ["side"] = selected });
        var resolver = new WindowCapabilityResolver(provider);

        var side = resolver.ResolveWebviewSurface("side", windowProfile);
        Assert(
            side.Allows("plugin:fs|read-text-file") && side.Allows("plugin:http|fetch"),
            "A selected surface holds its window profile plus the webviews grant.");

        var main = resolver.ResolveWebviewSurface("main", windowProfile);
        Assert(
            main.Allows("plugin:fs|read-text-file") && !main.Allows("plugin:http|fetch"),
            "An unselected surface keeps exactly the window profile.");

        var unknown = resolver.ResolveWebviewSurface("other", windowProfile);
        Assert(
            unknown.Allows("plugin:fs|read-text-file") && !unknown.Allows("plugin:http|fetch"),
            "A surface with no webviews grant keeps exactly the window profile.");
    }

    private sealed class StaticProvider(
        IReadOnlyDictionary<string, CapabilitySet> windows,
        IReadOnlyDictionary<string, CapabilitySet> webviews) : ICapabilityProvider
    {
        public IReadOnlyDictionary<string, CapabilitySet> Capabilities => windows;

        public IReadOnlyDictionary<string, CapabilitySet> WebviewCapabilities => webviews;
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

    private static void CatalogMaterializesDefaultAndGuardSets()
    {
        var builder = new CommandRouterBuilder();
        builder.AddPermission("plugin:demo|alpha");
        builder.AddPermission("plugin:demo|beta");
        builder.AddPermission("plugin:demo|beta-other-window");
        builder.AddPermission("plugin:demo|read-other-webview");
        builder.AddPermission("app|greet");

        var catalog = new PermissionSetCatalog(builder.BuildPermissionSets());

        Assert(
            Expand(catalog, "plugin:demo|default") == "plugin:demo|alpha,plugin:demo|beta",
            "A namespace default set must hold every non-guard permission of that namespace.");
        Assert(
            Expand(catalog, "plugin:demo|other-window") == "plugin:demo|beta-other-window",
            "Cross-window guards must land in their own set, never in 'default'.");
        Assert(
            Expand(catalog, "plugin:demo|other-webview") == "plugin:demo|read-other-webview",
            "Cross-webview guards must land in their own set.");
        Assert(
            Expand(catalog, "app|default") == "app|greet",
            "Application commands without a plugin prefix must populate the implicit app set.");
    }

    private static void CatalogHonorsCuratedAndExcludedSets()
    {
        var curated = new CommandRouterBuilder();
        curated.AddPermission("plugin:demo|alpha");
        curated.AddPermission("plugin:demo|beta");
        curated.DefinePermissionSet("plugin:demo|default", ["plugin:demo|alpha"], "read-only surface");
        var curatedCatalog = new PermissionSetCatalog(curated.BuildPermissionSets());
        Assert(
            Expand(curatedCatalog, "plugin:demo|default") == "plugin:demo|alpha",
            "A curated set must replace the materialized set of the same identifier.");

        var excluded = new CommandRouterBuilder();
        excluded.AddPermission("plugin:demo|alpha");
        excluded.AddPermission("plugin:demo|beta");
        excluded.ExcludeFromDefaultSet("plugin:demo|beta");
        var excludedCatalog = new PermissionSetCatalog(excluded.BuildPermissionSets());
        Assert(
            Expand(excludedCatalog, "plugin:demo|default") == "plugin:demo|alpha",
            "An excluded permission must stay out of the namespace default set.");
    }

    private static void CatalogExpandsNestedSetsAndRejectsCycles()
    {
        var catalog = new PermissionSetCatalog(
        [
            new PermissionSetDefinition("plugin:demo|read", ["plugin:demo|alpha"]),
            new PermissionSetDefinition("plugin:demo|all", ["plugin:demo|read", "plugin:demo|beta"]),
        ]);

        Assert(
            Expand(catalog, "plugin:demo|all") == "plugin:demo|alpha,plugin:demo|beta",
            "A set member that names another set must expand recursively.");

        var cyclic = new PermissionSetCatalog(
        [
            new PermissionSetDefinition("plugin:demo|x", ["plugin:demo|y"]),
            new PermissionSetDefinition("plugin:demo|y", ["plugin:demo|x"]),
        ]);

        AssertThrows<InvalidOperationException>(
            () => cyclic.Expand("plugin:demo|x"),
            "A set cycle must be rejected instead of silently truncated.");

        AssertThrows<InvalidOperationException>(
            () => catalog.Expand("plugin:demo|unknown"),
            "Expanding an unknown set identifier must fail loudly.");
    }

    private static void LoaderExpandsPermissionSetEntries()
    {
        var catalog = new PermissionSetCatalog(
        [
            new PermissionSetDefinition(
                "plugin:demo|default",
                ["plugin:demo|alpha", "plugin:demo|beta"]),
        ]);

        using var directory = new TempDirectory();
        directory.Write(
            "cap.json",
            """
            { "identifier": "main", "windows": ["main"], "permissions": ["plugin:demo|default"] }
            """);

        var main = CapabilityLoader.Load(directory.Path, catalog)["main"];
        Assert(
            main.Allows("plugin:demo|alpha") && main.Allows("plugin:demo|beta"),
            "A declared set must expand into every member permission.");
        Assert(
            !main.Allows("plugin:demo|default"),
            "The set identifier itself must not survive expansion as a permission id.");
    }

    private static void LoaderKeepsScopedGrantAlongsideSetMember()
    {
        var catalog = new PermissionSetCatalog(
        [
            new PermissionSetDefinition(
                "core:window|default",
                ["core:window|close", "core:window|create"]),
        ]);

        using var directory = new TempDirectory();
        directory.Write(
            "cap.json",
            """
            {
              "identifier": "main",
              "windows": ["main"],
              "permissions": [
                "core:window|default",
                { "identifier": "core:window|create", "allow": [{ "path": "editor" }] }
              ]
            }
            """);

        var main = CapabilityLoader.Load(directory.Path, catalog)["main"];
        Assert(main.Allows("core:window|close"), "An unscoped set member must stay granted.");
        Assert(
            main.TryGetScope("core:window|create", out var scope) && scope.Allow.Count == 1,
            "A set member listed again with a scope must keep that scope: the set never clears it.");
    }

    private static void LoaderRejectsScopeOnPermissionSet()
    {
        var catalog = new PermissionSetCatalog(
        [
            new PermissionSetDefinition("plugin:fs|default", ["plugin:fs|read-text-file"]),
        ]);

        using var directory = new TempDirectory();
        directory.Write(
            "cap.json",
            """
            {
              "identifier": "main",
              "windows": ["main"],
              "permissions": [
                { "identifier": "plugin:fs|default", "allow": [{ "base": "appData" }] }
              ]
            }
            """);

        AssertThrows<InvalidDataException>(
            () => CapabilityLoader.Load(directory.Path, catalog),
            "A permission set must not carry allow/deny scopes; scopes belong to individual permissions.");
    }

    private static void DemoCapabilitiesResolveAgainstRegisteredPlugins()
    {
        var builder = new CommandRouterBuilder();
        RegisterDemoPlugins(builder);

        // Demo.Desktop registers the Channel streaming command in the core:channel namespace.
        builder.AddPermission("core:channel|stream-echo");

        var catalog = new PermissionSetCatalog(builder.BuildPermissionSets());
        var capabilities = CapabilityLoader.Load(
            Path.Combine(FindRepositoryRoot(), "examples", "demo", "capabilities"),
            catalog);

        Assert(
            capabilities.ContainsKey("main") && capabilities.ContainsKey("editor"),
            "Both demo capability profiles must load.");

        var unregistered = capabilities
            .SelectMany(static pair => pair.Value.Permissions)
            .Where(permission => !builder.RegisteredPermissions.Contains(permission))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert(
            unregistered.Length == 0,
            "The demo manifests must expand into permissions the plugins actually register; unregistered: " +
            string.Join(", ", unregistered));

        var main = capabilities["main"];

        // Scoped grants must survive the set rewrite: they cannot be expressed by a set.
        string[] stillGranted =
        [
            "plugin:fs|read-text-file",
            "plugin:fs|write-text-file",
            "plugin:fs|remove",
            "plugin:global-shortcut|register",
            "plugin:global-shortcut|unregister-all",
            "plugin:store|set",
            "plugin:sql|allow-load",
            "plugin:http|fetch",
            "plugin:shell|spawn",
            "plugin:websocket|connect",
            "core:window|create",
        ];
        foreach (var permission in stillGranted)
        {
            Assert(main.Allows(permission), $"The demo main window must still grant '{permission}'.");
        }

        // The opt-in surfaces must stay closed even though the demo now grants whole namespaces.
        Assert(
            !main.Allows("plugin:webview|send-devtools-message"),
            "plugin:webview|default must not grant the CDP channel.");
        Assert(
            !main.Allows("plugin:webview|eval-other-webview"),
            "Cross-webview evaluation must stay opt-in.");
        Assert(
            !main.Allows("plugin:window-state|save-other-window"),
            "A namespace default set must never include its cross-window guards.");

        // The guard set expands to exactly the cross-window permissions the window plugin registers.
        // It must cover them without inventing permissions the plugin never registered: an earlier build
        // listed deny-close-other-window here, which the plugin no longer registers (close-other-window
        // supersedes it), so asserting on that name only proved the fixture had drifted.
        Assert(
            main.Allows("core:window|close-other-window") && main.Allows("core:window|monitors-other-window"),
            "The cross-window guard set must cover the plugin's cross-window permissions.");
        Assert(
            !main.Allows("core:window|deny-close-other-window"),
            "The guard set must not carry cross-window permissions the window plugin does not register.");

        var editor = capabilities["editor"];
        Assert(
            editor.Allows("plugin:tray|remove") && editor.Allows("plugin:notification|cancel"),
            "The editor profile must grant the tray and notification namespaces through sets.");
        Assert(
            !editor.Allows("plugin:menu|show-context-menu"),
            "The editor profile keeps an explicit, narrower menu grant than plugin:menu|default.");
        Assert(
            !editor.Allows("core:window|set-theme"),
            "The editor profile must not gain the whole window surface from a set it never names.");
    }

    /// <summary>
    /// Freezes the authorization surface — every registered permission, every reserved native event
    /// prefix and the demo manifests' expansion — into a checked-in snapshot. Later refactor phases
    /// (moving services between Shell and plugins, resolving capabilities per web view) must not change
    /// it implicitly: any diff here is a contract change that needs an explicit review, not a rebuild.
    /// </summary>
    private static void CapabilitySurfaceSnapshotMatchesContract(bool update)
    {
        var builder = new CommandRouterBuilder();
        RegisterDemoPlugins(builder);
        builder.AddPermission("core:channel|stream-echo");

        var catalog = new PermissionSetCatalog(builder.BuildPermissionSets());
        var capabilities = CapabilityLoader.Load(
            Path.Combine(FindRepositoryRoot(), "examples", "demo", "capabilities"),
            catalog);
        var expected = BuildCapabilitySurfaceSnapshot(builder.RegisteredPermissions, capabilities);

        var path = Path.Combine(
            FindRepositoryRoot(), "tests", "Tarui.Capabilities.Tests", "contracts", "capability-surface.json");
        if (update)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine($"Updated {path}");
            return;
        }

        Assert(
            File.Exists(path),
            $"The capability surface snapshot is missing at '{path}'. "
                + "Run `dotnet run --project tests/Tarui.Capabilities.Tests -- --update`.");

        var actual = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert(
            string.Equals(actual, expected, StringComparison.Ordinal),
            $"The capability surface snapshot at '{path}' is stale. A diff here changes the frozen permission "
                + "or event surface: review it as a contract change before regenerating with --update.");
    }

    private static string BuildCapabilitySurfaceSnapshot(
        IEnumerable<string> registeredPermissions,
        IEnumerable<KeyValuePair<string, CapabilitySet>> capabilities)
    {
        var builder = new StringBuilder();
        builder.Append("{\n");
        AppendJsonArray(builder, "reservedEventPrefixes", "  ", EventNames.ReservedPrefixes.Order(StringComparer.Ordinal));
        builder.Append(",\n");
        AppendJsonArray(builder, "errorCodes", "  ",
        [
            TaruiErrorCodes.CommandNotFound,
            TaruiErrorCodes.PermissionDenied,
            TaruiErrorCodes.InvalidMessage,
            TaruiErrorCodes.BridgeError,
        ]);
        builder.Append(",\n");
        AppendJsonArray(builder, "registeredPermissions", "  ", registeredPermissions.Order(StringComparer.Ordinal));
        builder.Append(",\n");
        builder.Append("  \"demoCapabilities\": {\n");

        var windows = capabilities.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < windows.Length; index++)
        {
            var (label, capability) = windows[index];
            builder.Append("    \"").Append(label).Append("\": {\n");
            AppendJsonArray(builder, "events", "      ", capability.Events.Order(StringComparer.Ordinal));
            builder.Append(",\n");
            AppendJsonArray(builder, "permissions", "      ", capability.Permissions.Order(StringComparer.Ordinal));
            builder.Append('\n');
            builder.Append(index == windows.Length - 1 ? "    }\n" : "    },\n");
        }

        builder.Append("  }\n}\n");
        return builder.ToString();
    }

    private static void AppendJsonArray(StringBuilder builder, string name, string indent, IEnumerable<string> values)
    {
        var items = values.ToArray();
        builder.Append(indent).Append('"').Append(name).Append("\": [\n");
        for (var index = 0; index < items.Length; index++)
        {
            builder.Append(indent).Append("  \"").Append(items[index].Replace("\\", "\\\\", StringComparison.Ordinal))
                .Append('"');
            builder.Append(index == items.Length - 1 ? "\n" : ",\n");
        }

        builder.Append(indent).Append(']');
    }

    /// <summary>
    /// Replays the demo composition root well enough to materialize the permission-set catalog.
    /// Registering commands only wires handlers, so the injected services are never touched; passing
    /// <c>null!</c> keeps this fixture check independent of a running host.
    /// </summary>
    private static void RegisterDemoPlugins(CommandRouterBuilder builder)
    {
        ITaruiPlugin[] plugins =
        [
            new global::Tarui.Plugins.Core.CorePlugin(null!),
            new global::Tarui.Plugins.System.SystemPlugin(null!, null!, null!, null!, null!),
            new global::Tarui.Plugins.Window.WindowPlugin(null!),
            new global::Tarui.Plugins.Webview.WebviewPlugin(null!),
            new global::Tarui.Plugins.Dialog.DialogPlugin(null!),
            new global::Tarui.Plugins.Menu.MenuPlugin(null!),
            new global::Tarui.Plugins.Tray.TrayPlugin(null!),
            new global::Tarui.Plugins.Notification.NotificationPlugin(null!),
            new global::Tarui.Plugins.Autostart.AutostartPlugin(null!),
            new global::Tarui.Plugins.GlobalShortcut.GlobalShortcutPlugin(null!),
            new global::Tarui.Plugins.WindowState.WindowStatePlugin(null!),
            new global::Tarui.Plugins.Store.StorePlugin(null!),
            new global::Tarui.Plugins.Log.LogPlugin(null!),
            new global::Tarui.Plugins.DeepLink.DeepLinkPlugin(null!),
            new global::Tarui.Plugins.Updater.UpdaterPlugin(null!),
            new global::Tarui.Plugins.Events.EventPlugin(null!),
            new global::Tarui.Plugins.FileSystem.FileSystemPlugin(null!),
            new global::Tarui.Plugins.Http.HttpPlugin(null!),
            new global::Tarui.Plugins.Cookie.CookiePlugin(null!),
            new global::Tarui.Plugins.Sql.SqlPlugin(null!),
            new global::Tarui.Plugins.Websocket.WebsocketPlugin(null!),
            new global::Tarui.Plugins.PersistedScope.PersistedScopePlugin(null!),
            new global::Tarui.Plugins.Shell.ShellPlugin(null!),
        ];

        foreach (var plugin in plugins)
        {
            plugin.ConfigureCommands(builder);
        }
    }

    private static string Expand(PermissionSetCatalog catalog, string identifier) =>
        string.Join(",", catalog.Expand(identifier));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tarui.net.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (tarui.net.slnx).");
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

    private static CapabilityManifests WriteAndLoadManifests(string manifestJson)
    {
        using var directory = new TempDirectory();
        directory.Write("cap.json", manifestJson);
        return CapabilityLoader.LoadManifests(directory.Path, PermissionSetCatalog.Empty);
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
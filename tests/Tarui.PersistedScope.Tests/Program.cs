using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.PersistedScope;

namespace Tarui.PersistedScope.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            PluginRegistersAllCommands();
            await DeniesCommandsOutsideCapabilityAsync();
            await OverlayExtendsDeclaredScopeOnlyAsync();
            await ServicePersistsAllowDenyResetAsync();
            await RejectsDisallowedPermissionsAsync();
            ContractsRoundTripJson();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }

        Console.WriteLine("Tarui.PersistedScope self-tests passed.");
        return 0;
    }

    private static void PluginRegistersAllCommands()
    {
        var builder = new CommandRouterBuilder();
        new PersistedScopePlugin(new RecordingScopeService()).ConfigureCommands(builder);
        var router = builder.Build();

        var expected = new[]
        {
            PersistedScopePlugin.AllowCommand,
            PersistedScopePlugin.DenyCommand,
            PersistedScopePlugin.ResetCommand,
        };

        foreach (var command in expected)
        {
            Assert(router.Commands.Contains(command), $"The persisted-scope plugin must register command '{command}'.");
        }

        Assert(router.RegisteredPermissions.Count == expected.Length,
            "Every persisted-scope permission must be registered exactly once with no extras.");
    }

    private static async Task DeniesCommandsOutsideCapabilityAsync()
    {
        using var root = CreateTempRoot();
        var service = new PersistedScopeService(new TestPolicy(root.Dir));
        var builder = new CommandRouterBuilder();
        new PersistedScopePlugin(service).ConfigureCommands(builder);
        var router = builder.Build();

        var response = await router.InvokeAsync(
            new InvokeRequest(
                1,
                "ps1",
                PersistedScopePlugin.AllowCommand,
                Element(new PersistedScopeOptions("plugin:fs|stat", [new PathScope("temp", "a.txt")])),
                "main",
                "main"),
            new CommandContext("main", "main", new CapabilitySet(["plugin:fs|stat"], [], [])));

        Assert(!response.Success && response.Error?.Code == "PERMISSION_DENIED",
            $"A window without the persisted-scope permission must be rejected. Got: {response.Error?.Code}");
    }

    private static async Task OverlayExtendsDeclaredScopeOnlyAsync()
    {
        var builder = new CommandRouterBuilder();
        builder.Add(
            "probe:read",
            TaruiJsonContext.Default.FsPathOptions,
            TaruiJsonContext.Default.Unit,
            (_, _, _) => ValueTask.FromResult(new Unit()),
            "probe:read",
            (options, allow, deny) => FileScopeMatcher.MatchesScope(allow, deny, options.Base, options.Path));
        builder.Add(
            "probe:unscoped",
            TaruiJsonContext.Default.FsPathOptions,
            TaruiJsonContext.Default.Unit,
            (_, _, _) => ValueTask.FromResult(new Unit()),
            "probe:unscoped");
        var router = builder.Build();

        var capability = new CapabilitySet(
            ["probe:read", "probe:unscoped"],
            [],
            [new KeyValuePair<string, PermissionScope>(
                "probe:read",
                new PermissionScope([new PathScope("temp", "a.txt")], []))]);

        var overlay = new FakeOverlay(new Dictionary<string, (IReadOnlyList<PathScope> Allow, IReadOnlyList<PathScope> Deny)>(
            StringComparer.Ordinal)
        {
            ["probe:read"] = ([new PathScope("temp", "b.txt")], [new PathScope("temp", "secret.txt")]),
            ["probe:unscoped"] = ([], [new PathScope("temp")]),
        });
        var context = new CommandContext("main", "main", capability, overlay);

        var declared = await router.InvokeAsync(Invoke("probe:read", "temp", "a.txt"), context);
        Assert(declared.Success, "A path allowed by the declared capability scope must keep passing with the overlay present.");

        var extended = await router.InvokeAsync(Invoke("probe:read", "temp", "b.txt"), context);
        Assert(extended.Success, "A runtime allow entry must extend a declared capability scope.");

        var denied = await router.InvokeAsync(Invoke("probe:read", "temp", "secret.txt"), context);
        Assert(!denied.Success && denied.Error?.Code == "SCOPE_DENIED",
            $"A runtime deny entry must deny a declared allow scope. Got: {denied.Error?.Code}");

        var unscoped = await router.InvokeAsync(Invoke("probe:unscoped", "temp", "a.txt"), context);
        Assert(unscoped.Success,
            "The overlay must not introduce checks for a permission whose capability never declared a scope.");

        var withoutOverlay = await router.InvokeAsync(
            Invoke("probe:read", "temp", "b.txt"),
            new CommandContext("main", "main", capability));
        Assert(!withoutOverlay.Success && withoutOverlay.Error?.Code == "SCOPE_DENIED",
            "Without the overlay the runtime-extended path must stay denied, proving the extension came from the overlay.");
    }

    private static async Task ServicePersistsAllowDenyResetAsync()
    {
        using var root = CreateTempRoot();
        var first = new PersistedScopeService(new TestPolicy(root.Dir));

        await first.DenyAsync(new PersistedScopeOptions(
            "plugin:fs|read-text-file", [new PathScope("appData", "notes/secret.txt")]), default);
        await first.AllowAsync(new PersistedScopeOptions(
            "plugin:fs|read-text-file", [new PathScope("appData", "notes/**")]), default);
        Assert(first.TryGetOverlay("plugin:fs|read-text-file", out var liveAllow, out var liveDeny),
            "An entry granted at runtime must be visible through the overlay immediately.");
        Assert(liveAllow.Count == 1 && liveDeny.Count == 1,
            "Allow and deny entries must live side by side until a grant replaces the opposite ruling.");

        // Allowing a denied path lifts the deny so the allow takes effect.
        await first.AllowAsync(new PersistedScopeOptions(
            "plugin:fs|read-text-file", [new PathScope("appData", "notes/secret.txt")]), default);
        Assert(first.TryGetOverlay("plugin:fs|read-text-file", out liveAllow, out liveDeny),
            "The overlay entry must survive further grants.");
        Assert(liveAllow.Count == 2 && liveDeny.Count == 0,
            $"Allowing a denied path must remove the deny entry. allow={liveAllow.Count} deny={liveDeny.Count}");

        // A fresh service must reload the persisted file from disk.
        Assert(File.Exists(Path.Combine(root.Dir, "scope.json")), "A grant must persist the scope file.");
        var second = new PersistedScopeService(new TestPolicy(root.Dir));
        Assert(second.TryGetOverlay("plugin:fs|read-text-file", out var reloadedAllow, out var reloadedDeny),
            "A new service instance must reload the persisted scope file.");
        Assert(reloadedAllow.Contains(new PathScope("appData", "notes/**")) &&
               reloadedAllow.Contains(new PathScope("appData", "notes/secret.txt")) && reloadedDeny.Count == 0,
            "The reloaded overlay must match the persisted allow/deny entries.");

        // Entries stay isolated per permission.
        await second.DenyAsync(new PersistedScopeOptions(
            "plugin:http|fetch", [new PathScope(null, "https://evil.example/**")]), default);
        Assert(second.TryGetOverlay("plugin:http|fetch", out _, out var httpDeny) && httpDeny.Count == 1,
            "A deny entry on another permission must land in its own overlay entry.");

        // Resetting one permission keeps the others.
        await second.ResetAsync(new PersistedScopeResetOptions("plugin:fs|read-text-file"), default);
        Assert(!second.TryGetOverlay("plugin:fs|read-text-file", out _, out _),
            "Resetting a permission must remove its overlay entry.");
        Assert(second.TryGetOverlay("plugin:http|fetch", out _, out _),
            "Resetting one permission must not clear other permissions.");

        // A global reset clears everything.
        await second.ResetAsync(new PersistedScopeResetOptions(null), default);
        Assert(!second.TryGetOverlay("plugin:http|fetch", out _, out _),
            "A global reset must clear every permission's overlay entry.");

        var third = new PersistedScopeService(new TestPolicy(root.Dir));
        Assert(!third.TryGetOverlay("plugin:fs|read-text-file", out _, out _),
            "After a global reset a fresh instance must load an empty overlay.");
    }

    private static async Task RejectsDisallowedPermissionsAsync()
    {
        using var root = CreateTempRoot();
        var service = new PersistedScopeService(new TestPolicy(root.Dir));

        Assert(await ThrowsAsync<InvalidOperationException>(() => service.AllowAsync(
                new PersistedScopeOptions("plugin:shell|spawn", [new PathScope("temp", "evil.exe")]), default)),
            "scope-allow must reject permissions outside the fs/http allowlist.");
        Assert(await ThrowsAsync<InvalidOperationException>(() => service.DenyAsync(
                new PersistedScopeOptions("core:clipboard|read-text", [new PathScope("temp", "x")]), default)),
            "scope-deny must reject permissions outside the fs/http allowlist.");
        Assert(await ThrowsAsync<InvalidOperationException>(() => service.ResetAsync(
                new PersistedScopeResetOptions("plugin:shell|spawn"), default)),
            "scope-reset must reject permissions outside the fs/http allowlist.");
        Assert(!File.Exists(Path.Combine(root.Dir, "scope.json")),
            "A rejected grant must not create or touch the scope file.");

        // The allowlisted surface still works after the rejections.
        await service.AllowAsync(new PersistedScopeOptions(
            "plugin:fs|stat", [new PathScope("temp", "ok.txt")]), default);
        Assert(service.TryGetOverlay("plugin:fs|stat", out var allow, out _) && allow.Count == 1,
            "An allowlisted permission must keep working after rejected grants.");
    }

    private static void ContractsRoundTripJson()
    {
        var options = new PersistedScopeOptions("plugin:fs|stat", [new PathScope("temp", "a.txt"), new PathScope(Base: null, Path: null)]);
        var roundTripped = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(options, TaruiJsonContext.Default.PersistedScopeOptions),
            TaruiJsonContext.Default.PersistedScopeOptions);
        Assert(roundTripped is { Permission: "plugin:fs|stat" } && roundTripped.Paths.Count == 2 &&
               roundTripped.Paths[0] == new PathScope("temp", "a.txt"),
            "PersistedScopeOptions must round-trip through the JSON context.");

        var reset = new PersistedScopeResetOptions("plugin:http|fetch");
        var resetRoundTripped = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(reset, TaruiJsonContext.Default.PersistedScopeResetOptions),
            TaruiJsonContext.Default.PersistedScopeResetOptions);
        Assert(resetRoundTripped is { Permission: "plugin:http|fetch" },
            "PersistedScopeResetOptions must round-trip a specific permission.");

        var globalReset = new PersistedScopeResetOptions(null);
        var globalRoundTripped = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(globalReset, TaruiJsonContext.Default.PersistedScopeResetOptions),
            TaruiJsonContext.Default.PersistedScopeResetOptions);
        Assert(globalRoundTripped is { Permission: null },
            "PersistedScopeResetOptions must preserve a null (global reset) permission.");

        var changed = new PersistedScopeChangedEvent("plugin:http|upload", "deny");
        var changedRoundTripped = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(changed, TaruiJsonContext.Default.PersistedScopeChangedEvent),
            TaruiJsonContext.Default.PersistedScopeChangedEvent);
        Assert(changedRoundTripped is { Permission: "plugin:http|upload", Action: "deny" },
            "PersistedScopeChangedEvent must round-trip through the JSON context.");
    }

    private static InvokeRequest Invoke(string command, string baseName, string path, string id = "p") =>
        new(1, id, command, Element(new FsPathOptions(baseName, path)), "main", "main");

    private static JsonElement Element<T>(T value) =>
        JsonSerializer.SerializeToElement(value, (JsonTypeInfo<T>)TaruiJsonContext.Default.GetTypeInfo(typeof(T))!);

    private static async Task<bool> ThrowsAsync<TException>(Func<ValueTask<Unit>> action)
        where TException : Exception
    {
        try
        {
            await action();
            return false;
        }
        catch (TException)
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

    private static TestRoot CreateTempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"tarui-persisted-scope-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return new TestRoot(dir);
    }

    private sealed class TestRoot : IDisposable
    {
        public TestRoot(string dir) => Dir = dir;

        public string Dir { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Dir, recursive: true);
            }
            catch
            {
                // Best effort cleanup on anti-virus / file-watcher holds.
            }
        }
    }

    /// <summary>Wraps the real FileAccessPolicy so the appData base maps to the temporary directory.</summary>
    private sealed class TestPolicy : IFileAccessPolicy
    {
        private readonly FileAccessPolicy _inner = new();

        public TestPolicy(string appDataDir) => _appDataDir = appDataDir;

        private readonly string _appDataDir;

        public bool TryGetBaseDirectory(string baseName, out string directoryPath, out bool isReadOnly)
        {
            if (string.Equals(baseName, "appData", StringComparison.Ordinal))
            {
                directoryPath = _appDataDir;
                isReadOnly = false;
                return true;
            }

            return _inner.TryGetBaseDirectory(baseName, out directoryPath, out isReadOnly);
        }

        public string? ResolveBase(string baseName) =>
            string.Equals(baseName, "appData", StringComparison.Ordinal)
                ? _appDataDir
                : _inner.ResolveBase(baseName);

        public string Authorize(FileAccessKind kind, string baseDirectory, string requestPath) =>
            _inner.Authorize(kind, baseDirectory, requestPath);

        public bool IsWithinOperationLimit(FileAccessKind kind, long byteCount) =>
            _inner.IsWithinOperationLimit(kind, byteCount);

        public bool TryReserveTotalBytes(long byteCount) => _inner.TryReserveTotalBytes(byteCount);

        public void ReleaseTotalBytes(long byteCount) => _inner.ReleaseTotalBytes(byteCount);

        public Task WriteAllBytesAtomicAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default) =>
            _inner.WriteAllBytesAtomicAsync(targetPath, content, cancellationToken);
    }

    private sealed class RecordingScopeService : IPersistedScopeService
    {
        public ValueTask<Unit> AllowAsync(PersistedScopeOptions options, CancellationToken ct) =>
            ValueTask.FromResult(new Unit());

        public ValueTask<Unit> DenyAsync(PersistedScopeOptions options, CancellationToken ct) =>
            ValueTask.FromResult(new Unit());

        public ValueTask<Unit> ResetAsync(PersistedScopeResetOptions options, CancellationToken ct) =>
            ValueTask.FromResult(new Unit());
    }

    private sealed class FakeOverlay(
        IReadOnlyDictionary<string, (IReadOnlyList<PathScope> Allow, IReadOnlyList<PathScope> Deny)> entries)
        : IRuntimeScopeOverlay
    {
        public bool TryGetOverlay(string permission, out IReadOnlyList<PathScope> allow, out IReadOnlyList<PathScope> deny)
        {
            if (entries.TryGetValue(permission, out var entry))
            {
                allow = entry.Allow;
                deny = entry.Deny;
                return true;
            }

            allow = [];
            deny = [];
            return false;
        }
    }
}

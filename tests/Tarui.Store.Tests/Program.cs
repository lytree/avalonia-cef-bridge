using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Store;

namespace Tarui.Store.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            await SetAndGetRoundTripAsync();
            await NullValueRemovesKeyAsync();
            await HasReportsPresenceAsync();
            await DeleteRemovesKeyAsync();
            await ClearEmptiesStoreAsync();
            await KeysListsOnlyPresentKeysAsync();
            await MissingFileReadsAsEmptyAsync();
            await PersistedFileReloadsOnNewServiceAsync();
            await ResourcesBaseRejectsWritesAsync();
            ScopeAuthorizerRespectsAllowDenyAndWildcards();
            PluginRegistersAllSixCommands();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }

        Console.WriteLine("Tarui.Store self-tests passed.");
        return 0;
    }

    private static async Task SetAndGetRoundTripAsync()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root);
        var options = new StoreKeyOptions(Key: "greeting", Base: root.BaseName, Path: "settings.json");

        await service.SetAsync(new StoreSetOptions("greeting", "hello tarui", Base: root.BaseName, Path: "settings.json"), default);
        var result = await service.GetAsync(options, default);

        Assert(result.Value == "hello tarui", "Set then Get must round-trip the exact value.");
        Assert(File.Exists(Path.Combine(root.Dir, "settings.json")), "Setting a key must persist the store file.");
    }

    private static async Task NullValueRemovesKeyAsync()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root);

        await service.SetAsync(new StoreSetOptions("k", "v", Base: root.BaseName), default);
        await service.SetAsync(new StoreSetOptions("k", null, Base: root.BaseName), default);

        var result = await service.GetAsync(new StoreKeyOptions("k", Base: root.BaseName), default);
        Assert(result.Value is null, "A null value must erase the key (Tauri erase semantics).");
        Assert(!(await service.HasAsync(new StoreKeyOptions("k", Base: root.BaseName), default)).Has,
            "Erasing a key must make Has report false.");
    }

    private static async Task HasReportsPresenceAsync()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root);

        await service.SetAsync(new StoreSetOptions("present", "1", Base: root.BaseName), default);
        var present = await service.HasAsync(new StoreKeyOptions("present", Base: root.BaseName), default);
        var missing = await service.HasAsync(new StoreKeyOptions("absent", Base: root.BaseName), default);

        Assert(present.Has, "Has must be true for a stored key.");
        Assert(!missing.Has, "Has must be false for a key that was never stored.");
    }

    private static async Task DeleteRemovesKeyAsync()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root);
        var options = new StoreKeyOptions("doomed", Base: root.BaseName);

        await service.SetAsync(new StoreSetOptions("doomed", "x", Base: root.BaseName), default);
        await service.DeleteAsync(options, default);

        var result = await service.GetAsync(options, default);
        Assert(result.Value is null, "Delete must remove the key from the store.");
    }

    private static async Task ClearEmptiesStoreAsync()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root);

        await service.SetAsync(new StoreSetOptions("a", "1", Base: root.BaseName), default);
        await service.SetAsync(new StoreSetOptions("b", "2", Base: root.BaseName), default);
        await service.ClearAsync(new StoreFileOptions(Base: root.BaseName), default);

        var keys = await service.KeysAsync(new StoreFileOptions(Base: root.BaseName), default);
        Assert(keys.Keys.Length == 0, "Clear must remove every key from the store.");
    }

    private static async Task KeysListsOnlyPresentKeysAsync()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root);

        await service.SetAsync(new StoreSetOptions("alpha", "1", Base: root.BaseName), default);
        await service.SetAsync(new StoreSetOptions("beta", "2", Base: root.BaseName), default);

        var keys = await service.KeysAsync(new StoreFileOptions(Base: root.BaseName), default);
        var sorted = keys.Keys.OrderBy(static k => k, StringComparer.Ordinal).ToArray();
        Assert(sorted.Length == 2, "Keys must report the exact number of stored keys.");
        Assert(sorted[0] == "alpha" && sorted[1] == "beta", "Keys must list the stored keys.");
    }

    private static async Task MissingFileReadsAsEmptyAsync()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root);

        var result = await service.GetAsync(new StoreKeyOptions("any", Base: root.BaseName, Path: "none.json"), default);
        Assert(result.Value is null, "Reading a missing store file must be safe and return null.");
    }

    private static async Task PersistedFileReloadsOnNewServiceAsync()
    {
        using var root = CreateTempRoot();
        var first = CreateService(root);
        await first.SetAsync(new StoreSetOptions("durable", "yes", Base: root.BaseName), default);

        // A fresh service backed by the same directory must reload the persisted file.
        var second = CreateService(root);
        var result = await second.GetAsync(new StoreKeyOptions("durable", Base: root.BaseName), default);
        Assert(result.Value == "yes", "A new service instance must reload the persisted store file.");
    }

    private static async Task ResourcesBaseRejectsWritesAsync()
    {
        using var root = CreateTempRoot();
        var service = CreateService(root, resourcesDir: root.Dir);

        PathDenialReason? captured = null;
        try
        {
            await service.SetAsync(new StoreSetOptions("k", "v", Base: "resources"), default);
        }
        catch (PathAccessDeniedException exception)
        {
            captured = exception.Reason;
        }

        Assert(captured is not null, "Writes to the read-only resources base must be rejected.");
    }

    private static void ScopeAuthorizerRespectsAllowDenyAndWildcards()
    {
        var allow = new PathScope[]
        {
            new(Base: "appData", Path: "settings.json"),
            new(Base: "appConfig", Path: "**/*.json"),
            new(Base: "temp"),
        };
        var deny = new PathScope[] { new(Base: "appConfig", Path: "secrets/*") };

        Assert(Authorizer.AllowsRead("appData", "settings.json", allow, deny), "An exact appData path must be allowed.");
        Assert(Authorizer.AllowsRead("appConfig", "settings/feature.json", allow, deny), "**/*.json must match nested json files.");
        Assert(Authorizer.AllowsRead("temp", "anything.dat", allow, deny), "A missing pattern path must allow any path under the base.");
        Assert(!Authorizer.AllowsRead("appConfig", "secrets/token.json", allow, deny), "Deny must win over allow.");
        Assert(!Authorizer.AllowsRead("home", "settings.json", allow, deny), "Bases not listed in allow must be denied.");
        Assert(!Authorizer.AllowsRead("appConfig", "notes.txt", allow, deny), "Patterns without wildcards still require the extension match.");
    }

    private static void PluginRegistersAllSixCommands()
    {
        var builder = new CommandRouterBuilder();
        var service = new RecordingStoreService();
        new StorePlugin(service).ConfigureCommands(builder);
        var router = builder.Build();

        var expected = new[]
        {
            "plugin:store|get",
            "plugin:store|set",
            "plugin:store|has",
            "plugin:store|delete",
            "plugin:store|clear",
            "plugin:store|keys",
        };

        foreach (var command in expected)
        {
            Assert(router.Commands.Contains(command), $"The plugin must register command '{command}'.");
        }

        Assert(router.RegisteredPermissions.Count == expected.Length,
            "Every store permission must be registered exactly once with no extras.");
    }

    private static JsonStoreService CreateService(TestRoot root, string? resourcesDir = null)
    {
        var policy = new FileAccessPolicy();
        return new JsonStoreService(new ScopedPolicy(policy, root, resourcesDir));
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
        var dir = Path.Combine(Path.GetTempPath(), $"tarui-store-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return new TestRoot(dir);
    }

    /// <summary>Static facade so the authorizer assertions read naturally.</summary>
    private static class Authorizer
    {
        public static bool AllowsRead(string baseName, string path, PathScope[] allow, PathScope[] deny) =>
            StoreScopeAuthorizer.AllowsStore(new StoreKeyOptions("ignored", Base: baseName, Path: path), allow, deny);
    }

    private sealed class TestRoot : IDisposable
    {
        public TestRoot(string dir)
        {
            Dir = dir;
            BaseName = $"__store_test_{Guid.NewGuid():N}";
        }

        public string Dir { get; }
        public string BaseName { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Dir, recursive: true);
            }
            catch
            {
                // Best effort cleanup on Windows anti-virus holds.
            }
        }
    }

    /// <summary>Wraps the real FileAccessPolicy so the test base name maps to the temporary directory.</summary>
    private sealed class ScopedPolicy : IFileAccessPolicy
    {
        private readonly FileAccessPolicy _inner;
        private readonly TestRoot _root;
        private readonly string? _resourcesDir;

        public ScopedPolicy(FileAccessPolicy inner, TestRoot root, string? resourcesDir = null)
        {
            _inner = inner;
            _root = root;
            _resourcesDir = resourcesDir;
        }

        public bool TryGetBaseDirectory(string baseName, out string directoryPath, out bool isReadOnly)
        {
            if (string.Equals(baseName, _root.BaseName, StringComparison.Ordinal))
            {
                directoryPath = _root.Dir;
                isReadOnly = false;
                return true;
            }

            if (string.Equals(baseName, "resources", StringComparison.Ordinal) && _resourcesDir is not null)
            {
                directoryPath = _resourcesDir;
                isReadOnly = true;
                return true;
            }

            return _inner.TryGetBaseDirectory(baseName, out directoryPath, out isReadOnly);
        }

        public string Authorize(FileAccessKind kind, string baseDirectory, string requestPath) => _inner.Authorize(kind, baseDirectory, requestPath);

        public bool IsWithinOperationLimit(FileAccessKind kind, long byteCount) => _inner.IsWithinOperationLimit(kind, byteCount);

        public bool TryReserveTotalBytes(long byteCount) => _inner.TryReserveTotalBytes(byteCount);

        public void ReleaseTotalBytes(long byteCount) => _inner.ReleaseTotalBytes(byteCount);

        public Task WriteAllBytesAtomicAsync(string targetPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default) =>
            _inner.WriteAllBytesAtomicAsync(targetPath, content, cancellationToken);
    }

    private sealed class RecordingStoreService : IStoreService
    {
        public List<string> Calls { get; } = [];

        private void Record(string name) => Calls.Add(name);

        public ValueTask<StoreGetResult> GetAsync(StoreKeyOptions options, CancellationToken cancellationToken) { Record("get"); return ValueTask.FromResult(new StoreGetResult(null)); }
        public ValueTask<Unit> SetAsync(StoreSetOptions options, CancellationToken cancellationToken) { Record("set"); return ValueTask.FromResult(new Unit()); }
        public ValueTask<StoreHasResult> HasAsync(StoreKeyOptions options, CancellationToken cancellationToken) { Record("has"); return ValueTask.FromResult(new StoreHasResult(false)); }
        public ValueTask<Unit> DeleteAsync(StoreKeyOptions options, CancellationToken cancellationToken) { Record("delete"); return ValueTask.FromResult(new Unit()); }
        public ValueTask<Unit> ClearAsync(StoreFileOptions options, CancellationToken cancellationToken) { Record("clear"); return ValueTask.FromResult(new Unit()); }
        public ValueTask<StoreKeysResult> KeysAsync(StoreFileOptions options, CancellationToken cancellationToken) { Record("keys"); return ValueTask.FromResult(new StoreKeysResult([])); }
    }
}
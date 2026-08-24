using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Store;

/// <summary>
/// Lightweight JSON configuration persistence. The backing file lives under an authorized base
/// directory and every path flows through <c>IFileAccessPolicy</c>; writes are durable via the
/// atomic temporary-file replacement exposed by the policy.
/// </summary>
public interface IStoreService
{
    ValueTask<StoreGetResult> GetAsync(StoreKeyOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> SetAsync(StoreSetOptions options, CancellationToken cancellationToken);

    ValueTask<StoreHasResult> HasAsync(StoreKeyOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> DeleteAsync(StoreKeyOptions options, CancellationToken cancellationToken);

    ValueTask<Unit> ClearAsync(StoreFileOptions options, CancellationToken cancellationToken);

    ValueTask<StoreKeysResult> KeysAsync(StoreFileOptions options, CancellationToken cancellationToken);
}

public sealed class StorePlugin(IStoreService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            "plugin:store|get",
            TaruiJsonContext.Default.StoreKeyOptions,
            TaruiJsonContext.Default.StoreGetResult,
            (options, _, ct) => service.GetAsync(options, ct),
            "plugin:store|get",
            StoreScopeAuthorizer.AllowsStore);

        commands.Add(
            "plugin:store|set",
            TaruiJsonContext.Default.StoreSetOptions,
            TaruiJsonContext.Default.Unit,
            (options, _, ct) => service.SetAsync(options, ct),
            "plugin:store|set",
            StoreScopeAuthorizer.AllowsStoreWrite);

        commands.Add(
            "plugin:store|has",
            TaruiJsonContext.Default.StoreKeyOptions,
            TaruiJsonContext.Default.StoreHasResult,
            (options, _, ct) => service.HasAsync(options, ct),
            "plugin:store|has",
            StoreScopeAuthorizer.AllowsStore);

        commands.Add(
            "plugin:store|delete",
            TaruiJsonContext.Default.StoreKeyOptions,
            TaruiJsonContext.Default.Unit,
            (options, _, ct) => service.DeleteAsync(options, ct),
            "plugin:store|delete",
            StoreScopeAuthorizer.AllowsStoreWrite);

        commands.Add(
            "plugin:store|clear",
            TaruiJsonContext.Default.StoreFileOptions,
            TaruiJsonContext.Default.Unit,
            (options, _, ct) => service.ClearAsync(options, ct),
            "plugin:store|clear",
            StoreScopeAuthorizer.AllowsStoreWrite);

        commands.Add(
            "plugin:store|keys",
            TaruiJsonContext.Default.StoreFileOptions,
            TaruiJsonContext.Default.StoreKeysResult,
            (options, _, ct) => service.KeysAsync(options, ct),
            "plugin:store|keys",
            StoreScopeAuthorizer.AllowsStore);
    }
}

/// <summary>
/// Store commands authorize the backing (Base, Path) against the caller capability's allow/deny
/// <c>PathScope</c> lists via the shared <see cref="FileScopeMatcher"/>. Deny wins over allow; an
/// empty allow list means "any store file not explicitly denied". Write-family commands
/// additionally reject the <c>resources</c> base.
/// </summary>
public static class StoreScopeAuthorizer
{
    public static bool AllowsStore(StoreKeyOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        FileScopeMatcher.MatchesScope(allow, deny, options.Base, options.Path);

    public static bool AllowsStore(StoreFileOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        FileScopeMatcher.MatchesScope(allow, deny, options.Base, options.Path);

    public static bool AllowsStoreWrite(StoreSetOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        !IsReadOnlyBase(options.Base) && FileScopeMatcher.MatchesScope(allow, deny, options.Base, options.Path);

    public static bool AllowsStoreWrite(StoreKeyOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        !IsReadOnlyBase(options.Base) && FileScopeMatcher.MatchesScope(allow, deny, options.Base, options.Path);

    public static bool AllowsStoreWrite(StoreFileOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        !IsReadOnlyBase(options.Base) && FileScopeMatcher.MatchesScope(allow, deny, options.Base, options.Path);

    private static bool IsReadOnlyBase(string? baseName) =>
        string.Equals(baseName, "resources", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Source-generated JSON metadata for the store backing file, which maps verbatim keys to values.
/// Dictionary keys are never renamed, so user keys round-trip exactly.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Dictionary<string, string?>))]
public partial class StoreFileJsonContext : JsonSerializerContext;

public static class StorePluginServiceCollectionExtensions
{
    public static IServiceCollection AddStorePlugin(this IServiceCollection services) => services
        .AddFileAccessPolicy()
        .AddSingleton<IStoreService, JsonStoreService>()
        .AddPlugin<StorePlugin>();
}

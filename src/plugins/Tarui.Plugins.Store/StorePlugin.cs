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
/// <c>PathScope</c> lists. deny wins over allow; an empty allow list means "any store file not
/// explicitly denied". Write-family commands additionally reject the <c>resources</c> base.
/// </summary>
public static class StoreScopeAuthorizer
{
    public static bool AllowsStore(StoreKeyOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        Matches(allow, deny, options.Base, options.Path);

    public static bool AllowsStore(StoreFileOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        Matches(allow, deny, options.Base, options.Path);

    public static bool AllowsStoreWrite(StoreSetOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        !IsReadOnlyBase(options.Base) && Matches(allow, deny, options.Base, options.Path);

    public static bool AllowsStoreWrite(StoreKeyOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        !IsReadOnlyBase(options.Base) && Matches(allow, deny, options.Base, options.Path);

    public static bool AllowsStoreWrite(StoreFileOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        !IsReadOnlyBase(options.Base) && Matches(allow, deny, options.Base, options.Path);

    private static bool Matches(IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny, string baseName, string? requestPath)
    {
        foreach (var scope in deny)
        {
            if (MatchesOne(scope, baseName, requestPath))
            {
                return false;
            }
        }

        if (allow.Count == 0)
        {
            return true;
        }

        foreach (var scope in allow)
        {
            if (MatchesOne(scope, baseName, requestPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesOne(PathScope scope, string baseName, string? requestPath)
    {
        if (!string.IsNullOrEmpty(scope.Base) &&
            !string.Equals(scope.Base, baseName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = requestPath ?? string.Empty;
        if (string.IsNullOrEmpty(scope.Path))
        {
            return true;
        }

        return FsGlobMatch(scope.Path, relative);
    }

    private static bool FsGlobMatch(string pattern, string candidate)
    {
        var patternSegments = pattern.Replace('\\', '/').Split('/', StringSplitOptions.None);
        var candidateSegments = candidate.Replace('\\', '/').Split('/', StringSplitOptions.None);
        return MatchSegments(patternSegments.AsSpan(), candidateSegments.AsSpan());
    }

    private static bool MatchSegments(ReadOnlySpan<string> pattern, ReadOnlySpan<string> candidate)
    {
        while (pattern.Length > 0)
        {
            if (pattern[0] == "**")
            {
                var remaining = pattern[1..];
                for (var start = 0; start <= candidate.Length; start++)
                {
                    if (MatchSegments(remaining, candidate[start..]))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (candidate.Length == 0)
            {
                return false;
            }

            if (!MatchSegment(pattern[0], candidate[0]))
            {
                return false;
            }

            pattern = pattern[1..];
            candidate = candidate[1..];
        }

        return candidate.Length == 0;
    }

    private static bool MatchSegment(string patternSegment, string candidateSegment)
    {
        if (patternSegment == "*")
        {
            return candidateSegment.Length > 0;
        }

        var star = patternSegment.IndexOf('*');
        if (star < 0)
        {
            return string.Equals(patternSegment, candidateSegment, StringComparison.Ordinal);
        }

        if (patternSegment[(star + 1)..].Contains('*'))
        {
            return string.Equals(patternSegment, candidateSegment, StringComparison.Ordinal);
        }

        var prefix = patternSegment[..star];
        var suffix = patternSegment[(star + 1)..];
        return candidateSegment.StartsWith(prefix, StringComparison.Ordinal) &&
               candidateSegment.EndsWith(suffix, StringComparison.Ordinal) &&
               candidateSegment.Length >= prefix.Length + suffix.Length;
    }

    private static bool IsReadOnlyBase(string baseName) =>
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
using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Shell;

public static class CapabilityLoader
{
    private static readonly HashSet<string> KnownRootFields = new(
        ["$schema", "identifier", "description", "windows", "webviews", "platforms", "permissions", "events"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> KnownScopeFields = new(
        ["base", "path"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> KnownPlatforms = new(
        ["windows", "macos", "linux", "ios", "android"],
        StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, CapabilitySet> Load(string directory) =>
        Load(directory, PermissionSetCatalog.Empty);

    /// <summary>
    /// Loads every capability manifest in <paramref name="directory"/>. Permission-set identifiers
    /// (for example <c>core:window|default</c>) are expanded against <paramref name="catalog"/> into
    /// the concrete permission ids the router registered, so a manifest never has to enumerate the
    /// commands of a namespace by hand.
    /// </summary>
    public static IReadOnlyDictionary<string, CapabilitySet> Load(string directory, IPermissionSetCatalog catalog) =>
        LoadManifests(directory, catalog).Windows;

    /// <summary>
    /// Loads every capability manifest and keeps the two grant surfaces apart: grants declared through
    /// <c>windows</c> are keyed by window label, grants declared through <c>webviews</c> are keyed by
    /// web view label. A manifest may declare both; at least one is mandatory.
    /// </summary>
    public static CapabilityManifests LoadManifests(string directory, IPermissionSetCatalog catalog)
    {
        var windowBuckets = new Dictionary<string, WindowBucket>(StringComparer.Ordinal);
        var webviewBuckets = new Dictionary<string, WebviewBucket>(StringComparer.Ordinal);
        if (!Directory.Exists(directory))
        {
            return ToManifests(windowBuckets, webviewBuckets);
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(file),
                new JsonDocumentOptions { AllowTrailingCommas = true });
            ApplyManifest(file, document.RootElement, windowBuckets, webviewBuckets, catalog);
        }

        return ToManifests(windowBuckets, webviewBuckets);
    }

    private static void ApplyManifest(
        string file,
        JsonElement root,
        Dictionary<string, WindowBucket> windowBuckets,
        Dictionary<string, WebviewBucket> webviewBuckets,
        IPermissionSetCatalog catalog)
    {
        if (root.ValueKind is not JsonValueKind.Object)
        {
            throw new InvalidDataException($"[{file}] Capability manifest root must be a JSON object.");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!KnownRootFields.Contains(property.Name))
            {
                throw new InvalidDataException($"[{file}] Unknown capability field '{property.Name}'.");
            }
        }

        var identifier = requiredString(root, "identifier", file);
        var windows = ReadStringArray(root, "windows", file, required: false);
        var webviews = ReadStringArray(root, "webviews", file, required: false);
        var events = ReadStringArray(root, "events", file, required: false);
        ValidatePlatforms(root, file);

        if (windows.Length == 0 && webviews.Length == 0)
        {
            throw new InvalidDataException(
                $"[{file}] Capability '{identifier}' must declare at least one 'windows' or 'webviews' target.");
        }

        var seenPermissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in ReadPermissions(root, file))
        {
            if (!seenPermissions.Add(permission.Identifier))
            {
                throw new InvalidDataException(
                    $"[{file}] Duplicate permission identifier '{permission.Identifier}' in capability '{identifier}'.");
            }

            var (members, scope) = ResolveGrant(permission, catalog, file);
            foreach (var label in windows)
            {
                var bucket = windowBuckets.GetOrAddValue(label, static () => new WindowBucket());
                ApplyGrant(bucket, members, scope, events);
            }

            foreach (var label in webviews)
            {
                var bucket = webviewBuckets.GetOrAddValue(label, static () => new WebviewBucket());
                ApplyGrant(bucket, members, scope, events);
            }
        }
    }

    private static void ApplyGrant(SurfaceBucket bucket, string[] members, PermissionScope? scope, string[] events)
    {
        foreach (var member in members)
        {
            bucket.Permissions.Add(member);
        }

        if (scope is not null)
        {
            bucket.Scopes[members[0]] = scope;
        }

        foreach (var eventName in events)
        {
            bucket.Events.Add(eventName);
        }
    }

    /// <summary>
    /// Resolves one manifest entry into the permission ids it grants plus the scope that applies to
    /// them. A permission-set entry expands into its members and is scope-free by construction: a
    /// scope belongs to a single permission, so authoring one on a set would have to be broadcast to
    /// members that may not accept the same <c>base</c>, which is exactly the ambiguity the strict
    /// JSON shape avoids. Scoped grants therefore stay explicit.
    /// </summary>
    private static (string[] Members, PermissionScope? Scope) ResolveGrant(
        CapabilityGrant grant,
        IPermissionSetCatalog catalog,
        string file)
    {
        if (!catalog.TryGet(grant.Identifier, out _))
        {
            var scope = new PermissionScope(
                NormalizeScopes(grant.Allow, file, grant.Identifier, isDeny: false),
                NormalizeScopes(grant.Deny, file, grant.Identifier, isDeny: true));
            return ([grant.Identifier], scope.Allow.Count > 0 || scope.Deny.Count > 0 ? scope : null);
        }

        if ((grant.Allow?.Length ?? 0) > 0 || (grant.Deny?.Length ?? 0) > 0)
        {
            throw new InvalidDataException(
                $"[{file}] Permission set '{grant.Identifier}' cannot carry 'allow'/'deny' scopes; " +
                "declare the scope on the individual permissions instead.");
        }

        try
        {
            return ([.. catalog.Expand(grant.Identifier)], null);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException($"[{file}] {exception.Message}");
        }
    }

    private static void ValidatePlatforms(JsonElement root, string file)
    {
        if (!root.TryGetProperty("platforms", out var platforms) || platforms.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        foreach (var value in platforms.EnumerateArray())
        {
            var platform = value.GetString();
            if (string.IsNullOrWhiteSpace(platform) || !KnownPlatforms.Contains(platform))
            {
                throw new InvalidDataException($"[{file}] Invalid or empty platform '{platform}'.");
            }
        }
    }

    private static CapabilityGrant[] ReadPermissions(JsonElement root, string file)
    {
        if (!root.TryGetProperty("permissions", out var permissions))
        {
            return [];
        }

        if (permissions.ValueKind is not JsonValueKind.Array)
        {
            throw new InvalidDataException($"[{file}] 'permissions' must be an array.");
        }

        var result = new List<CapabilityGrant>();
        foreach (var element in permissions.EnumerateArray())
        {
            result.Add(element.ValueKind switch
            {
                JsonValueKind.String => new CapabilityGrant(element.GetString()!),
                JsonValueKind.Object => ReadStructuredPermission(element, file),
                _ => throw new InvalidDataException($"[{file}] A permission must be a string or an object with an 'identifier'.")
            });
        }

        return [.. result];
    }

    private static CapabilityGrant ReadStructuredPermission(JsonElement element, string file)
    {
        var identifier = requiredString(element, "identifier", file);

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name is not ("identifier" or "allow" or "deny"))
            {
                throw new InvalidDataException($"[{file}] Unknown permission field '{property.Name}'.");
            }
        }

        var allow = ReadScopeList(element, "allow", file);
        var deny = ReadScopeList(element, "deny", file);
        return new CapabilityGrant(identifier, allow, deny);
    }

    private static PathScope[] ReadScopeList(JsonElement element, string name, string file)
    {
        if (!element.TryGetProperty(name, out var list) || list.ValueKind is JsonValueKind.Null)
        {
            return [];
        }

        if (list.ValueKind is not JsonValueKind.Array)
        {
            throw new InvalidDataException($"[{file}] '{name}' must be an array of scope objects.");
        }

        var result = new List<PathScope>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind is not JsonValueKind.Object)
            {
                throw new InvalidDataException($"[{file}] A scope entry in '{name}' must be an object with 'base' and/or 'path'.");
            }

            foreach (var property in item.EnumerateObject())
            {
                if (!KnownScopeFields.Contains(property.Name))
                {
                    throw new InvalidDataException($"[{file}] Unknown scope field '{property.Name}' in '{name}'.");
                }
            }
            var basePath = item.TryGetProperty("base", out var baseValue) ? baseValue.GetString() : null;
            var scopePath = item.TryGetProperty("path", out var pathValue) ? pathValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(basePath) && string.IsNullOrWhiteSpace(scopePath))
            {
                throw new InvalidDataException($"[{file}] A scope entry in '{name}' must set 'base' or 'path'.");
            }
            result.Add(new PathScope(basePath, scopePath));
        }

        return [.. result];
    }

    private static PathScope[] NormalizeScopes(PathScope[]? scopes, string file, string identifier, bool isDeny)
    {
        if (scopes is null || scopes.Length == 0)
        {
            return [];
        }

        // A deny without a base/path already failed parsing; this is a defensive guard.
        foreach (var scope in scopes)
        {
            if (string.IsNullOrWhiteSpace(scope.Base) && string.IsNullOrWhiteSpace(scope.Path))
            {
                throw new InvalidDataException(
                    $"[{file}] Capability '{identifier}' has an empty {(isDeny ? "deny" : "allow")} scope.");
            }
        }
        return scopes;
    }

    private static string[] ReadStringArray(JsonElement root, string name, string file, bool required)
    {
        if (!root.TryGetProperty(name, out var element))
        {
            if (required)
            {
                throw new InvalidDataException($"[{file}] Missing required field '{name}'.");
            }
            return [];
        }

        if (element.ValueKind is not JsonValueKind.Array)
        {
            throw new InvalidDataException($"[{file}] '{name}' must be a JSON array.");
        }

        var values = element.EnumerateArray()
            .Select(static item => item.ValueKind is JsonValueKind.String ? item.GetString() : null)
            .Where(static value => value is not null && !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();

        if (required && values.Length == 0)
        {
            throw new InvalidDataException($"[{file}] '{name}' must contain at least one value.");
        }
        return values;
    }

    private static string requiredString(JsonElement root, string name, string file)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind is not JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new InvalidDataException($"[{file}] Missing or empty required string '{name}'.");
        }
        return element.GetString()!;
    }

    private static CapabilityManifests ToManifests(
        Dictionary<string, WindowBucket> windowBuckets,
        Dictionary<string, WebviewBucket> webviewBuckets) =>
        new(
            windowBuckets.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToCapabilitySet(),
                StringComparer.Ordinal),
            webviewBuckets.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ToCapabilitySet(),
                StringComparer.Ordinal));

    private abstract class SurfaceBucket
    {
        public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Events { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, PermissionScope> Scopes { get; } = new(StringComparer.Ordinal);

        public CapabilitySet ToCapabilitySet() => new(Permissions, Events, Scopes);
    }

    private sealed class WindowBucket : SurfaceBucket
    {
    }

    private sealed class WebviewBucket : SurfaceBucket
    {
    }
}

/// <summary>
/// The two grant surfaces loaded from a capability directory: grants keyed by window label and
/// grants keyed by web view label. A web view surface resolves to the union of its host window's
/// set and every <c>webviews</c>-selected grant whose label matches exactly.
/// </summary>
public sealed record CapabilityManifests(
    IReadOnlyDictionary<string, CapabilitySet> Windows,
    IReadOnlyDictionary<string, CapabilitySet> Webviews);

internal static class DictionaryExtensions
{
    public static TValue GetOrAddValue<TKey, TValue>(
        this Dictionary<TKey, TValue> dictionary,
        TKey key,
        Func<TValue> createValue)
        where TKey : notnull
        where TValue : notnull
    {
        if (!dictionary.TryGetValue(key, out var value))
        {
            value = createValue();
            dictionary.Add(key, value);
        }

        return value;
    }
}
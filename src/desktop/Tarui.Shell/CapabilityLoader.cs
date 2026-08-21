using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Shell;

public static class CapabilityLoader
{
    private static readonly HashSet<string> KnownRootFields = new(
        ["$schema", "identifier", "description", "windows", "platforms", "permissions", "events"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> KnownScopeFields = new(
        ["base", "path"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> KnownPlatforms = new(
        ["windows", "macos", "linux", "ios", "android"],
        StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, CapabilitySet> Load(string directory)
    {
        var buckets = new Dictionary<string, WindowBucket>(StringComparer.Ordinal);
        if (!Directory.Exists(directory))
        {
            return ToCapabilityMap(buckets);
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(file),
                new JsonDocumentOptions { AllowTrailingCommas = true });
            ApplyManifest(file, document.RootElement, buckets);
        }

        return ToCapabilityMap(buckets);
    }

    private static void ApplyManifest(string file, JsonElement root, Dictionary<string, WindowBucket> buckets)
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
        var windows = ReadStringArray(root, "windows", file, required: true);
        var events = ReadStringArray(root, "events", file, required: false);
        ValidatePlatforms(root, file);

        var seenPermissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in ReadPermissions(root, file))
        {
            if (!seenPermissions.Add(permission.Identifier))
            {
                throw new InvalidDataException(
                    $"[{file}] Duplicate permission identifier '{permission.Identifier}' in capability '{identifier}'.");
            }

            var scope = new PermissionScope(
                NormalizeScopes(permission.Allow, file, identifier, isDeny: false),
                NormalizeScopes(permission.Deny, file, identifier, isDeny: true));

            foreach (var label in windows)
            {
                var bucket = buckets.GetOrAddValue(label, static () => new WindowBucket());
                bucket.Permissions.Add(permission.Identifier);
                if (scope.Allow.Count > 0 || scope.Deny.Count > 0)
                {
                    bucket.Scopes[permission.Identifier] = scope;
                }

                foreach (var eventName in events)
                {
                    bucket.Events.Add(eventName);
                }
            }
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

    private static Dictionary<string, CapabilitySet> ToCapabilityMap(Dictionary<string, WindowBucket> buckets) =>
        buckets.ToDictionary(
            static pair => pair.Key,
            static pair => new CapabilitySet(pair.Value.Permissions, pair.Value.Events, pair.Value.Scopes),
            StringComparer.Ordinal);

    private sealed class WindowBucket
    {
        public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Events { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, PermissionScope> Scopes { get; } = new(StringComparer.Ordinal);
    }
}

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
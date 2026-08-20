using System.Text.Json;
using Tarui.Ipc;

namespace Tarui.Shell;

public static class CapabilityLoader
{
    public static IReadOnlyDictionary<string, CapabilitySet> Load(string directory)
    {
        var permissionsByWindow = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (!Directory.Exists(directory))
        {
            return ToCapabilityMap(permissionsByWindow);
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty("windows", out var windowsElement))
            {
                continue;
            }

            var permissions = ReadPermissions(root);
            foreach (var windowElement in windowsElement.EnumerateArray())
            {
                var label = windowElement.GetString();
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                var bucket = permissionsByWindow.GetOrAddValue(label, static () => []);
                foreach (var permission in permissions)
                {
                    bucket.Add(permission);
                }
            }
        }

        return ToCapabilityMap(permissionsByWindow);
    }

    private static string[] ReadPermissions(JsonElement root)
    {
        if (!root.TryGetProperty("permissions", out var permissionsElement) ||
            permissionsElement.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return [.. permissionsElement.EnumerateArray()
            .Select(static item => item.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)];
    }

    private static Dictionary<string, CapabilitySet> ToCapabilityMap(
        Dictionary<string, HashSet<string>> permissionsByWindow) =>
        permissionsByWindow.ToDictionary(
            static pair => pair.Key,
            static pair => new CapabilitySet(pair.Value),
            StringComparer.Ordinal);
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

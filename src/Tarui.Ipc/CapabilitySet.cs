using System.Collections.Frozen;

namespace Tarui.Ipc;

public sealed class CapabilitySet(IEnumerable<string> permissions)
{
    private readonly FrozenSet<string> _permissions = permissions.ToFrozenSet(StringComparer.Ordinal);

    public bool Allows(string permission) =>
        _permissions.Contains(permission) || _permissions.Contains("*");

    public IReadOnlyCollection<string> Permissions => _permissions;
}

public static class ExampleCapabilities
{
    public static CapabilitySet Main { get; } = new(
    [
        "core:app|get-info",
        "core:window|minimize",
        "plugin:dialog|open"
    ]);
}

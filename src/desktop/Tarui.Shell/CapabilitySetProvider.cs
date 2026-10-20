using Tarui.Ipc;

namespace Tarui.Shell;

public interface ICapabilityProvider
{
    IReadOnlyDictionary<string, CapabilitySet> Capabilities { get; }
}

/// <summary>
/// Loads and caches the capability manifests for the current window set. The permission-set catalog
/// is supplied by the shell so manifest entries such as <c>core:window|default</c> expand into the
/// permissions the registered plugins actually expose; without a catalog only literal permission ids
/// resolve.
/// </summary>
public sealed class CapabilitySetProvider : ICapabilityProvider
{
    private readonly IPermissionSetCatalog _permissionSets;
    private readonly string? _directory;
    private readonly Lazy<IReadOnlyDictionary<string, CapabilitySet>> _capabilities;

    public CapabilitySetProvider(string? directory = null)
        : this(PermissionSetCatalog.Empty, directory)
    {
    }

    public CapabilitySetProvider(IPermissionSetCatalog permissionSets, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(permissionSets);
        _permissionSets = permissionSets;
        _directory = directory;
        _capabilities = new Lazy<IReadOnlyDictionary<string, CapabilitySet>>(
            () => CapabilityLoader.Load(
                _directory ?? Path.Combine(AppContext.BaseDirectory, "capabilities"),
                _permissionSets));
    }

    public IReadOnlyDictionary<string, CapabilitySet> Capabilities => _capabilities.Value;
}

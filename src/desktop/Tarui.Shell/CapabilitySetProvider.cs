using Tarui.Ipc;

namespace Tarui.Shell;

public interface ICapabilityProvider
{
    IReadOnlyDictionary<string, CapabilitySet> Capabilities { get; }

    /// <summary>
    /// Grants declared through the <c>webviews</c> selector, keyed by web view label (exact match).
    /// Providers that only know window profiles report an empty map; a web view surface then simply
    /// inherits its host window's capability set.
    /// </summary>
    IReadOnlyDictionary<string, CapabilitySet> WebviewCapabilities => EmptyWebviewCapabilities;

    public static readonly IReadOnlyDictionary<string, CapabilitySet> EmptyWebviewCapabilities =
        new Dictionary<string, CapabilitySet>();
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
    private readonly Lazy<CapabilityManifests> _manifests;

    public CapabilitySetProvider(string? directory = null)
        : this(PermissionSetCatalog.Empty, directory)
    {
    }

    public CapabilitySetProvider(IPermissionSetCatalog permissionSets, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(permissionSets);
        _permissionSets = permissionSets;
        _directory = directory;
        _manifests = new Lazy<CapabilityManifests>(
            () => CapabilityLoader.LoadManifests(
                _directory ?? Path.Combine(AppContext.BaseDirectory, "capabilities"),
                _permissionSets));
    }

    public IReadOnlyDictionary<string, CapabilitySet> Capabilities => _manifests.Value.Windows;

    public IReadOnlyDictionary<string, CapabilitySet> WebviewCapabilities => _manifests.Value.Webviews;
}

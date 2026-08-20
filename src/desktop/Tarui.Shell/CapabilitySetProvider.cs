using Tarui.Ipc;

namespace Tarui.Shell;

public interface ICapabilityProvider
{
    IReadOnlyDictionary<string, CapabilitySet> Capabilities { get; }
}

public sealed class CapabilitySetProvider(string? directory = null) : ICapabilityProvider
{
    private readonly Lazy<IReadOnlyDictionary<string, CapabilitySet>> _capabilities = new(
        () => CapabilityLoader.Load(directory ?? Path.Combine(AppContext.BaseDirectory, "capabilities")));

    public IReadOnlyDictionary<string, CapabilitySet> Capabilities => _capabilities.Value;
}

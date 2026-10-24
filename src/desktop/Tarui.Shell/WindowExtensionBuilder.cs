using Microsoft.Extensions.DependencyInjection;

namespace Tarui.Shell;

/// <summary>
/// Collects window extension registrations during shell composition. Mirrors <c>CommandRouterBuilder</c> in
/// spirit: it is the plugin-style entry point through which a registrar contributes native window extensions
/// without reflection or scanning — membership is fully explicit at composition time.
/// </summary>
public sealed class WindowExtensionBuilder
{
    private readonly List<WindowExtensionRegistration> _registrations = [];
    private readonly List<Type> _requiredServices = [];

    /// <summary>The registrations gathered so far, in declaration order.</summary>
    public IReadOnlyList<WindowExtensionRegistration> Registrations => _registrations;

    /// <summary>
    /// The extension types declared without a factory. Each one must also be registered in the container,
    /// because factory-free registrations resolve through it instead of constructing implicitly.
    /// </summary>
    public IReadOnlyList<Type> RequiredServices => _requiredServices;

    /// <summary>Registers a native window extension that applies to every window.</summary>
    public WindowExtensionBuilder Add<T>(Func<IServiceProvider, T>? factory = null)
        where T : class, IShellWindowExtension =>
        Add(null, factory);

    /// <summary>Registers a native window extension scoped to the named windows only.</summary>
    public WindowExtensionBuilder Add<T>(string[]? labels, Func<IServiceProvider, T>? factory = null)
        where T : class, IShellWindowExtension
    {
        if (factory is null)
        {
            _requiredServices.Add(typeof(T));
        }

        _registrations.Add(TaruiShellServiceCollectionExtensions.CreateExtensionRegistration(labels, factory));
        return this;
    }

    /// <summary>
    /// Fails composition when a factory-free extension type was never registered, turning a late
    /// per-window construction failure into one actionable startup error. The type is only existence
    /// checked, never instantiated here.
    /// </summary>
    internal void EnsureRequiredServicesAreRegistered(IServiceProvider provider)
    {
        if (provider.GetService<IServiceProviderIsService>() is not { } isService)
        {
            // A provider that cannot answer "is this a service?" still gets the per-window error.
            return;
        }

        var missing = _requiredServices
            .Where(type => !isService.IsService(type))
            .Select(static type => type.FullName ?? type.Name)
            .ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Window extensions registered without a factory must also be registered as services. " +
            $"Missing registrations: {string.Join(", ", missing)}. Call services.AddTransient<T>() for each, " +
            "or pass a factory to WindowExtensionBuilder.Add<T>().");
    }
}

/// <summary>
/// Opt-in plugin contract for declaring native window extensions from a composition-root registrar. A
/// registrar is registered via <c>AddWindowExtensionRegistrar&lt;T&gt;()</c> and its <c>Configure</c> is invoked
/// once, on the UI-thread context during shell assembly, so a package can contribute several windows/control
/// groups as a unit — the same shape a plugin uses to register commands.
/// </summary>
public interface IWindowExtensionRegistrar
{
    void Configure(WindowExtensionBuilder extensions);
}

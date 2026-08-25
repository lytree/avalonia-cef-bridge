namespace Tarui.Shell;

/// <summary>
/// Collects window extension registrations during shell composition. Mirrors <c>CommandRouterBuilder</c> in
/// spirit: it is the plugin-style entry point through which a registrar contributes native window extensions
/// without reflection or scanning — membership is fully explicit at composition time.
/// </summary>
public sealed class WindowExtensionBuilder
{
    private readonly List<WindowExtensionRegistration> _registrations = [];

    /// <summary>The registrations gathered so far, in declaration order.</summary>
    public IReadOnlyList<WindowExtensionRegistration> Registrations => _registrations;

    /// <summary>Registers a native window extension that applies to every window.</summary>
    public WindowExtensionBuilder Add<T>(Func<IServiceProvider, T>? factory = null)
        where T : class, IShellWindowExtension =>
        Add(null, factory);

    /// <summary>Registers a native window extension scoped to the named windows only.</summary>
    public WindowExtensionBuilder Add<T>(string[]? labels, Func<IServiceProvider, T>? factory = null)
        where T : class, IShellWindowExtension
    {
        _registrations.Add(TaruiShellServiceCollectionExtensions.CreateExtensionRegistration(labels, factory));
        return this;
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
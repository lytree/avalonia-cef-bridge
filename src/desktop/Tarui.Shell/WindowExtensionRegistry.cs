namespace Tarui.Shell;

/// <summary>
/// A compile-time explicit registration of an <see cref="IShellWindowExtension"/> contribution. The optional
/// <c>Labels</c> filter scopes the contribution to named windows; a null filter applies to every window.
/// </summary>
public sealed record WindowExtensionRegistration(
    Func<IServiceProvider, IShellWindowExtension> Create,
    string[]? Labels)
{
    internal bool AppliesTo(string label) =>
        Labels is null || Labels.Contains(label, StringComparer.Ordinal);
}

/// <summary>
/// Resolves the explicitly registered window extensions for a given window. No reflection or scanning is
/// involved — membership comes solely from registrations made through <c>AddWindowExtension&lt;T&gt;()</c>.
/// Instances are created per window on the UI thread during assembly.
/// </summary>
public sealed class WindowExtensionRegistry(IEnumerable<WindowExtensionRegistration> registrations)
{
    private readonly WindowExtensionRegistration[] _registrations = registrations.ToArray();

    /// <summary>Creates a new instance of each extension registered for the given window label.</summary>
    public IEnumerable<IShellWindowExtension> CreateFor(string label, IServiceProvider services)
    {
        foreach (var registration in _registrations)
        {
            if (registration.AppliesTo(label))
            {
                yield return registration.Create(services);
            }
        }
    }
}
using Tarui.Shell;

namespace Demo;

/// <summary>
/// Demonstrates plugin-style window extension composition: a registrar declares the window's native
/// controls as a unit through <c>AddWindowExtensionRegistrar&lt;TDemoWindowExtensions&gt;()</c>, mirroring
/// how plugins register their commands. Extensions declared here are merged into the shell's
/// <see cref="WindowExtensionRegistry"/> alongside any direct <c>AddWindowExtension&lt;T&gt;()</c> calls.
/// </summary>
public sealed class DemoWindowExtensions : IWindowExtensionRegistrar
{
    public void Configure(WindowExtensionBuilder extensions) =>
        extensions.Add<DemoWindowChromeExtension>()
                  .Add<DemoSidebarExtension>();
}
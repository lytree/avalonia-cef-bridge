using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Shell;

public interface IMainWindowLauncher
{
    Window LaunchMainWindow();
}

public sealed class MainWindowLauncher(
    WindowRegistry registry,
    WebviewAttacher attacher,
    EventRouter eventRouter,
    ICapabilityProvider capabilities,
    ISingleInstanceCoordinator singleInstance,
    WindowOptions mainWindowOptions,
    ILogger<MainWindowLauncher>? logger = null) : IMainWindowLauncher
{
    public Window LaunchMainWindow()
    {
        if (!capabilities.Capabilities.ContainsKey(mainWindowOptions.Label))
        {
            throw new InvalidOperationException(
                "No capability file grants permissions to the '{mainWindowOptions.Label}' window. Add capabilities/{mainWindowOptions.Label}.json.");
        }

        var entry = attacher.Attach(mainWindowOptions);
        registry.Add(mainWindowOptions.Label, entry);

        // Any second-instance activations that arrived before the main window was registered are now
        // safe to deliver as app://second-instance events.
        singleInstance.Flush();

        if (Application.Current is { } application)
        {
            application.ActualThemeVariantChanged += (_, _) =>
                FireAndForget.Run(
                    eventRouter.EmitToAllAsync(
                        "shell://theme-changed",
                        JsonSerializer.SerializeToElement(
                            new ThemeChanged(ThemeNames.From(application.ActualThemeVariant)),
                            TaruiJsonContext.Default.ThemeChanged)),
                    logger);
        }

        return entry.Window;
    }
}

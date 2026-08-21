using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Shell;

public interface IMainWindowLauncher
{
    Window LaunchMainWindow();
}

public sealed class MainWindowLauncher(
    WindowRegistry registry,
    ShellWindowFactory factory,
    EventRouter eventRouter,
    ICapabilityProvider capabilities,
    ISingleInstanceCoordinator singleInstance,
    WindowOptions mainWindowOptions) : IMainWindowLauncher
{
    public Window LaunchMainWindow()
    {
        if (!capabilities.Capabilities.ContainsKey("main"))
        {
            throw new InvalidOperationException(
                "No capability file grants permissions to the 'main' window. Add capabilities/main.json.");
        }

        var entry = factory.CreateEntry(mainWindowOptions);
        registry.Add("main", entry);

        // Any second-instance activations that arrived before the main window was registered are now
        // safe to deliver as app://second-instance events.
        singleInstance.Flush();

        if (Application.Current is { } application)
        {
            application.ActualThemeVariantChanged += (_, _) =>
                FireAndForget(eventRouter.EmitToAllAsync(
                    "shell://theme-changed",
                    JsonSerializer.SerializeToElement(
                        new ThemeChanged(ThemeNames.From(application.ActualThemeVariant)),
                        TaruiJsonContext.Default.ThemeChanged)));
        }

        return entry.Window;
    }

    private static async void FireAndForget(ValueTask task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Window events are best-effort notifications.
        }
    }
}

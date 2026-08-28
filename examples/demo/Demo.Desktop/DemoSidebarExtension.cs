using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Tarui.Ipc;
using Tarui.Shell;

namespace Demo;

/// <summary>
/// Demonstrates the Phase 1 window extension contract: a native control injected into the dock region
/// of every window via <c>AddWindowExtensionRegistrar&lt;TDemoWindowExtensions&gt;</c>. The web view content
/// slot on the left fills the remaining space.
///
/// The extension also exercises the Phase 2 context: clicking the native button emits an event back to
/// the window's web view through <see cref="WindowExtensionContext.EmitAsync"/>, which the frontend
/// subscribes to and renders in its event log.
/// </summary>
public sealed class DemoSidebarExtension : IShellWindowExtension
{
    private int _clicks;
    private TextBlock? _count;

    public void CreateView(WindowExtensionContext context)
    {
        _clicks = 0;
        _count = new TextBlock
        {
            Text = "Native clicks: 0",
            Foreground = Brushes.White,
            Margin = new Thickness(12, 0, 12, 8),
        };

        var title = new TextBlock
        {
            Text = "Native Sidebar",
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(12),
        };

        var button = new Button
        {
            Content = "Emit to webview",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(12, 8),
        };
        // The shared FireAndForget helper logs delivery failures instead of silently swallowing
        // them, matching the bridge-error reporting contract used by the shell itself.
        button.Click += (_, _) => FireAndForget.Run(NotifyAsync(context));

        var sidebar = new StackPanel
        {
            Width = 200,
            Spacing = 0,
            Orientation = Orientation.Vertical,
            Background = new SolidColorBrush(Color.Parse("#202124")),
            Children = { title, _count, button },
        };

        context.Composition.Dock(sidebar, Dock.Right);
    }

    private async ValueTask NotifyAsync(WindowExtensionContext context)
    {
        _clicks++;
        _count!.Text = $"Native clicks: {_clicks}";
        var payload = JsonSerializer.SerializeToElement(new { clicks = _clicks });
        await context.EmitAsync("user://demo/native-sidebar", payload);
    }
}

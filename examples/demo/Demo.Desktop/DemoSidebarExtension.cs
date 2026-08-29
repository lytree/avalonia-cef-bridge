using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Styling;
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
    // 明/暗配色，与 DemoWindowChromeExtension 的标题栏保持一致的观感。
    private static readonly Color LightBackground = Color.Parse("#F3F3F3");
    private static readonly Color LightForeground = Color.Parse("#3D3D3D");
    private static readonly Color DarkBackground = Color.Parse("#202124");
    private static readonly Color DarkForeground = Color.Parse("#E6FFFFFF");

    private int _clicks;
    private StackPanel? _sidebar;
    private TextBlock? _title;
    private TextBlock? _count;

    public void CreateView(WindowExtensionContext context)
    {
        _clicks = 0;
        _count = new TextBlock
        {
            Text = "Native clicks: 0",
            Margin = new Thickness(12, 0, 12, 8),
        };

        _title = new TextBlock
        {
            Text = "Native Sidebar",
            FontWeight = FontWeight.Bold,
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

        _sidebar = new StackPanel
        {
            Width = 200,
            Spacing = 0,
            Orientation = Orientation.Vertical,
            Children = { _title, _count, button },
        };

        // 初始应用当前主题，并监听应用级请求主题变化（与 DemoWindowChromeExtension 一致）。
        ApplyTheme(Application.Current?.ActualThemeVariant);
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += (_, _) => ApplyTheme(app.ActualThemeVariant);
        }

        context.Composition.Dock(_sidebar, Dock.Right);
    }

    private void ApplyTheme(ThemeVariant? variant)
    {
        var dark = variant == ThemeVariant.Dark;
        var background = new SolidColorBrush(dark ? DarkBackground : LightBackground);
        var foreground = new SolidColorBrush(dark ? DarkForeground : LightForeground);
        if (_sidebar is not null) _sidebar.Background = background;
        if (_title is not null) _title.Foreground = foreground;
        if (_count is not null) _count.Foreground = foreground;
    }

    private async ValueTask NotifyAsync(WindowExtensionContext context)
    {
        _clicks++;
        _count!.Text = $"Native clicks: {_clicks}";
        var payload = JsonSerializer.SerializeToElement(new { clicks = _clicks });
        await context.EmitAsync("user://demo/native-sidebar", payload);
    }
}

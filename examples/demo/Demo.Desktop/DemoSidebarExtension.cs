using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Tarui.Shell;

namespace Demo;

/// <summary>
/// Demonstrates the Phase 1 window extension contract: a native control injected into the dock region
/// of every window via <c>AddWindowExtension&lt;TDemoSidebarExtension&gt;</c>. The web view content slot
/// on the left fills the remaining space.
/// </summary>
public sealed class DemoSidebarExtension : IShellWindowExtension
{
    public void CreateView(WindowExtensionContext context)
    {
        var sidebar = new StackPanel
        {
            Width = 200,
            Spacing = 8,
            Orientation = Orientation.Vertical,
            Background = new SolidColorBrush(Color.Parse("#202124")),
        };

        sidebar.Children.Add(new TextBlock
        {
            Text = "Native Sidebar",
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(12),
        });

        context.Composition.Dock(sidebar, Dock.Right);
    }
}
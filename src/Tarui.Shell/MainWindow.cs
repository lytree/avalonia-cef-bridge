using Avalonia.Controls;
using Avalonia.Markup.Declarative;
using Avalonia.Media;

namespace Tarui.Shell;

public sealed class MainWindow : Window
{
    public MainWindow(WebViewHost webViewHost)
    {
        Title = "tarui.net";
        Width = 1280;
        Height = 820;
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var grid = new Grid().Rows("Auto,*");
        var titleBar = new Border()
            .Background(new SolidColorBrush(Color.Parse("#10161D")))
            .Padding(18, 12)
            .Child(new TextBlock()
                .Text("tarui.net")
                .Foreground(new SolidColorBrush(Color.Parse("#E8EDF2")))
                .FontSize(16)
                .FontWeight(FontWeight.SemiBold));
        Grid.SetRow(webViewHost, 1);
        grid.Children.Add(titleBar);
        grid.Children.Add(webViewHost);
        Content = grid;
    }
}

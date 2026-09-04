using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Tarui.Contracts;

namespace Tarui.Shell;

/// <summary>
/// A native modal message box window replicating the AvaloniaTemplate/Ursa message box layout: an
/// optional status icon, a wrapping message and a right-aligned button row whose combination
/// (OK / OKCancel / YesNo / YesNoCancel) determines the returned <see cref="MessageBoxResult"/>.
/// </summary>
public sealed class MessageBoxWindow : Window
{
    private const double IconSize = 32;

    /// <summary>
    /// Creates a message box window. Button labels default to "OK"/"Cancel"/"Yes"/"No" and can be
    /// overridden per button, which is how the confirmation dialog localizes its OK/Cancel pair.
    /// </summary>
    public MessageBoxWindow(
        MessageBoxOptions options,
        string? okLabel = null,
        string? cancelLabel = null,
        string? yesLabel = null,
        string? noLabel = null)
    {
        Title = string.IsNullOrWhiteSpace(options.Title) ? "Message" : options.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 280;
        MaxWidth = 460;

        var hasIcon = !string.Equals(options.Icon, MessageBoxIconNames.None, StringComparison.Ordinal);
        var message = new TextBlock
        {
            Text = options.Content,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 340,
            Margin = new Thickness(0, 0, 0, 20),
        };

        var grid = new Grid { Margin = new Thickness(24) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        if (hasIcon)
        {
            var icon = new PathIcon
            {
                Data = GetIconGeometry(options.Icon),
                Foreground = new SolidColorBrush(GetIconColor(options.Icon)),
                Width = IconSize,
                Height = IconSize,
                Margin = new Thickness(0, 0, 16, 20),
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetRow(icon, 0);
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);
            Grid.SetColumn(message, 1);
        }
        else
        {
            Grid.SetColumnSpan(message, 2);
        }

        Grid.SetRow(message, 0);
        grid.Children.Add(message);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetRow(buttonPanel, 1);
        Grid.SetColumnSpan(buttonPanel, 2);
        grid.Children.Add(buttonPanel);

        foreach (var button in BuildButtons(options.Button, okLabel, cancelLabel, yesLabel, noLabel))
        {
            buttonPanel.Children.Add(button);
        }

        Content = grid;
    }

    /// <summary>Builds the button row for a button combination, in display order (left to right).</summary>
    private IEnumerable<Button> BuildButtons(
        string combination,
        string? okLabel,
        string? cancelLabel,
        string? yesLabel,
        string? noLabel) => combination switch
    {
        MessageBoxButtonNames.OkCancel => [CreateButton(cancelLabel ?? "Cancel", MessageBoxResultNames.Cancel, isCancel: true), CreateButton(okLabel ?? "OK", MessageBoxResultNames.Ok, isDefault: true)],
        MessageBoxButtonNames.YesNo => [CreateButton(noLabel ?? "No", MessageBoxResultNames.No), CreateButton(yesLabel ?? "Yes", MessageBoxResultNames.Yes, isDefault: true)],
        MessageBoxButtonNames.YesNoCancel => [CreateButton(cancelLabel ?? "Cancel", MessageBoxResultNames.Cancel, isCancel: true), CreateButton(noLabel ?? "No", MessageBoxResultNames.No), CreateButton(yesLabel ?? "Yes", MessageBoxResultNames.Yes, isDefault: true)],
        _ => [CreateButton(okLabel ?? "OK", MessageBoxResultNames.Ok, isDefault: true)],
    };

    private Button CreateButton(string text, string result, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 84,
            Padding = new Thickness(16, 6),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        button.Click += (_, _) => Close(new MessageBoxResult(result));
        return button;
    }

    private static StreamGeometry? GetIconGeometry(string icon) => icon switch
    {
        MessageBoxIconNames.Info => StreamGeometry.Parse("M12 1.75a10.25 10.25 0 1 0 0 20.5 10.25 10.25 0 0 0 0-20.5ZM3.25 12a8.75 8.75 0 1 1 17.5 0 8.75 8.75 0 0 1-17.5 0ZM12 8.75a1.25 1.25 0 1 0 0-2.5 1.25 1.25 0 0 0 0 2.5ZM10.75 11c0-.41.34-.75.75-.75h.5c.41 0 .75.34.75.75v3.25h.75a.75.75 0 0 1 0 1.5h-2a.75.75 0 0 1 0-1.5h.25V11.75h-.25a.75.75 0 0 1-.75-.75Z"),
        MessageBoxIconNames.Warning => StreamGeometry.Parse("M10.91 2.87a1.25 1.25 0 0 1 2.18 0l8.77 16.48c.47.88-.1 1.95-1.09 1.95H3.23c-1 0-1.56-1.07-1.09-1.95l8.77-16.48ZM12 4.13 3.53 19.8h16.94L12 4.13Zm.75 5.62v4.5a.75.75 0 0 1-1.5 0v-4.5a.75.75 0 0 1 1.5 0Zm-.75 7.5a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z"),
        MessageBoxIconNames.Error => StreamGeometry.Parse("M12 2c5.52 0 10 4.48 10 10s-4.48 10-10 10S2 17.52 2 12 6.48 2 12 2Zm0 1.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17Zm-.75 10.75v-5.5a.75.75 0 0 1 1.5 0v5.5a.75.75 0 0 1-1.5 0Zm.75 2a1 1 0 1 0 0 2 1 1 0 0 0 0-2Z"),
        MessageBoxIconNames.Question => StreamGeometry.Parse("M12 2c5.52 0 10 4.48 10 10s-4.48 10-10 10S2 17.52 2 12 6.48 2 12 2Zm0 1.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17Zm.13 12.87a1 1 0 1 0 0 2 1 1 0 0 0 0-2ZM12 6.5c-1.24 0-2.25 1-2.25 2.25a.75.75 0 0 1-1.5 0A3.75 3.75 0 1 1 15.75 9c0 1.35-.62 2.32-1.46 3-.37.29-.77.53-1.14.74l-.15.08v.93a.75.75 0 0 1-1.5 0v-1.25c0-.41.34-.75.75-.75.3 0 .6-.14.9-.36.53-.41.85-.9.85-1.39A2.25 2.25 0 0 0 12 6.5Z"),
        MessageBoxIconNames.Success => StreamGeometry.Parse("M12 2c5.52 0 10 4.48 10 10s-4.48 10-10 10S2 17.52 2 12 6.48 2 12 2Zm0 1.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17Zm4.53 5.47a.75.75 0 0 1 .07.98l-.07.08-6 6a.75.75 0 0 1-.98.07l-.08-.07-2.5-2.5a.75.75 0 0 1 .98-1.13l.08.07L10 14.44l5.47-5.47a.75.75 0 0 1 1.06 0Z"),
        _ => null,
    };

    private static Color GetIconColor(string icon) => icon switch
    {
        MessageBoxIconNames.Info => Color.Parse("#005FB8"),
        MessageBoxIconNames.Warning => Color.Parse("#9D5D00"),
        MessageBoxIconNames.Error => Color.Parse("#C42B1C"),
        MessageBoxIconNames.Question => Color.Parse("#005FB8"),
        MessageBoxIconNames.Success => Color.Parse("#107C10"),
        _ => Colors.Black,
    };
}

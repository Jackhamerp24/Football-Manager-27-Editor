using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fm27Editor.App;

/// <summary>
/// A plain message the player can read: one sentence about what is missing or what will happen, and an OK button.
/// Used for things the player can fix on the page (a destination not chosen yet), so the text stays free of jargon.
/// </summary>
internal sealed class NoticeDialog : Window
{
    NoticeDialog(string heading, string message)
    {
        Title = heading;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var ok = new Button { Content = "OK", Width = 90, Classes = { "accent" }, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = heading, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = UiKit.TextPrimary },
                new TextBlock { Text = message, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Foreground = UiKit.TextSecondary },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok } },
            },
        };
    }

    public static async Task Show(Window owner, string heading, string message) =>
        await new NoticeDialog(heading, message).ShowDialog<object?>(owner);
}

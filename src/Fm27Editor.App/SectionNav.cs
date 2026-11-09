using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fm27Editor.App;

/// <summary>
/// Left section list shared by every Advanced Search scope, so Player, Coach and Club look the same. One button per section;
/// the selected section is shaded, the others are plain text.
/// </summary>
internal sealed class SectionNav : Grid
{
    public const double NavWidth = 190;

    public SectionNav(IReadOnlyList<(string Title, Control Content)> pages)
    {
        ColumnDefinitions = new ColumnDefinitions($"{NavWidth},*");
        ColumnSpacing = 10;

        var list = new StackPanel { Spacing = 2, Margin = new Thickness(6, 8, 0, 0) };
        var body = new ContentControl();
        var buttons = new List<(Button Button, TextBlock Text)>();

        void Select(int index)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                bool active = i == index;
                buttons[i].Button.Background = active ? UiKit.AccentSoft : Brushes.Transparent;
                buttons[i].Text.Foreground = active ? UiKit.TextPrimary : UiKit.TextSecondary;
                buttons[i].Text.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
            }
            if (index >= 0 && index < pages.Count) body.Content = pages[index].Content;
        }

        for (int i = 0; i < pages.Count; i++)
        {
            int index = i;
            var text = new TextBlock { Text = pages[i].Title, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
            var button = new Button
            {
                Content = text,
                Width = NavWidth - 6,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(12, 7),
                MinHeight = 32,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(3),
            };
            button.Click += (_, _) => Select(index);
            buttons.Add((button, text));
            list.Children.Add(button);
        }

        Grid.SetColumn(list, 0);
        Grid.SetColumn(body, 1);
        Children.Add(list);
        Children.Add(body);
        Select(0);
    }
}

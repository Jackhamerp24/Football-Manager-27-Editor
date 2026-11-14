using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Fm27Editor.App;

internal static class CopyName
{
    public static Button CreateButton(string name)
    {
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M8,3 H18 V15 H16 V5 H8 Z M5,7 H14 V20 H5 Z M7,9 V18 H12 V9 Z"),
            Fill = UiKit.TextSecondary, Width = 14, Height = 14, Stretch = Stretch.Uniform,
        };
        var button = new Button
        {
            Content = icon, Width = 26, Height = 26, MinHeight = 26, Padding = new Avalonia.Thickness(5),
            Margin = new Avalonia.Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, AppLanguage.Get("Label_Copy_name"));
        ToolTip.SetTip(button, AppLanguage.Get("Label_Copy_name"));
        int feedbackVersion = 0;
        button.Click += async (_, _) =>
        {
            int version = ++feedbackVersion;
            try
            {
                var clipboard = TopLevel.GetTopLevel(button)?.Clipboard ?? throw new InvalidOperationException("Clipboard unavailable");
                await clipboard.SetTextAsync(name);
                button.Content = new TextBlock { Text = "✓", FontWeight = FontWeight.Bold, Foreground = UiKit.Brush("#4ADE80"), FontSize = 15 };
                ToolTip.SetTip(button, AppLanguage.Get("Label_Name_copied"));
                AutomationProperties.SetName(button, AppLanguage.Get("Label_Name_copied"));
            }
            catch (Exception)
            {
                button.Content = new TextBlock { Text = "!", FontWeight = FontWeight.Bold, Foreground = UiKit.Brush("#F87171"), FontSize = 15 };
                ToolTip.SetTip(button, AppLanguage.Get("CopyName_Failed"));
                AutomationProperties.SetName(button, AppLanguage.Get("CopyName_Failed"));
            }
            await Task.Delay(2000);
            if (version != feedbackVersion) return;
            button.Content = icon;
            ToolTip.SetTip(button, AppLanguage.Get("Label_Copy_name"));
            AutomationProperties.SetName(button, AppLanguage.Get("Label_Copy_name"));
        };
        return button;
    }

    public static void Attach<T>(DataGrid grid, Func<T, string> name, Action<string> status) where T : class
    {
        async Task CopyAsync()
        {
            if (grid.SelectedItem is not T row || TopLevel.GetTopLevel(grid)?.Clipboard is not { } clipboard) return;
            try
            {
                await clipboard.SetTextAsync(name(row));
                status(AppLanguage.Get("Label_Name_copied"));
            }
            catch (Exception ex) { status($"Could not copy name: {ex.Message}"); }
        }

        var item = new MenuItem { Header = AppLanguage.Get("Label_Copy_name") };
        item.Click += async (_, _) => await CopyAsync();
        grid.ContextMenu = new ContextMenu { Items = { item } };
        grid.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(grid).Properties.IsRightButtonPressed && e.Source is Avalonia.Visual source &&
                source.FindAncestorOfType<DataGridRow>(includeSelf: true)?.DataContext is T row)
                grid.SelectedItem = row;
        }, RoutingStrategies.Tunnel);
        grid.AddHandler(InputElement.KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control && grid.SelectedItem is T)
            {
                e.Handled = true;
                await CopyAsync();
            }
        }, RoutingStrategies.Tunnel);
    }
}

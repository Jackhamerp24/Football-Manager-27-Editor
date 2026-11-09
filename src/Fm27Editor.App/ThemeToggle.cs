using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    const string MoonIcon = "M20,14.5 A8.5,8.5 0 1,1 9.5,4 A7,7 0 0,0 20,14.5 Z";
    const string SunIcon = "M12,8 A4,4 0 1,1 11.99,8 Z M12,2 L12,5 M12,19 L12,22 M2,12 L5,12 M19,12 L22,12 M4.9,4.9 L7,7 M17,17 L19.1,19.1 M4.9,19.1 L7,17 M17,7 L19.1,4.9";

    /// <summary>The sun / moon button: one click switches between the dark and the light theme and everything repaints in place.</summary>
    Button BuildThemeToggle()
    {
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Stroke = Brush("#DCE2EE"), StrokeThickness = 1.7, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform, Width = 16, Height = 16,
        };
        var button = new Button
        {
            Content = icon, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(0),
            MinWidth = 34, Width = 34, MinHeight = 28, Height = 28,
            Background = Brush("#181C29"), BorderBrush = Brush("#272D42"), BorderThickness = new Thickness(1), Foreground = Brush("#DCE2EE"),
        };
        void Refresh()
        {
            // The icon shows where a click goes: the sun while dark, the moon while light.
            icon.Data = Geometry.Parse(AppTheme.Mode == ThemeMode.Dark ? SunIcon : MoonIcon);
            ToolTip.SetTip(button, AppTheme.Mode == ThemeMode.Dark ? "Light theme" : "Dark theme");
        }
        Refresh();
        AppTheme.Changed += Refresh;
        button.Click += (_, _) => AppTheme.Toggle();
        return button;
    }
}

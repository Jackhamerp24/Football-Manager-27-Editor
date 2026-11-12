using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Fm27Editor.App;

/// <summary>
/// The search-and-pick box FMRTE shows for "Change Owner", "Change Stadium Location" and the like: type part of a name, pick a line,
/// OK. <paramref name="search"/> turns the typed text into at most a few hundred lines.
/// </summary>
internal static class PickerDialog
{
    public static async Task<T?> Show<T>(Window owner, string title, Func<string, IReadOnlyList<(string Text, T Value)>> search, string watermark = "Type the name to search") where T : class
    {
        T? result = null;
        var input = new TextBox { Watermark = watermark, Margin = new Thickness(0, 0, 0, 8) };
        var list = new ListBox { MinHeight = 260, SelectionMode = SelectionMode.Single };
        var ok = new Button { Content = "OK", IsEnabled = false, Classes = { "accent" }, Width = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", Width = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        IReadOnlyList<(string Text, T Value)> shown = [];
        void Refresh()
        {
            shown = string.IsNullOrWhiteSpace(input.Text) ? [] : search(input.Text.Trim());
            list.ItemsSource = shown.Select(x => x.Text).ToArray();
            ok.IsEnabled = false;
        }
        input.TextChanged += (_, _) => Refresh();
        list.SelectionChanged += (_, _) => ok.IsEnabled = list.SelectedIndex >= 0;
        var window = new Window
        {
            Title = title, Width = 480, Height = 460, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new DockPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    Dock(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { cancel, ok } }, Avalonia.Controls.Dock.Bottom),
                    Dock(input, Avalonia.Controls.Dock.Top),
                    list,
                },
            },
        };
        // The search box takes the keyboard as soon as the picker opens, so typing needs no extra click.
        window.Opened += (_, _) => input.Focus();
        void Accept()
        {
            if (list.SelectedIndex < 0 || list.SelectedIndex >= shown.Count) return;
            result = shown[list.SelectedIndex].Value;
            window.Close();
        }
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => window.Close();
        list.DoubleTapped += (_, e) => { if (UiKit.OnRow(e)) Accept(); };
        await window.ShowDialog(owner);
        return result;
    }

    static T Dock<T>(T control, Dock side) where T : Control { DockPanel.SetDock(control, side); return control; }
}

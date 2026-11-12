using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fm27Editor.App;

/// <summary>
/// FMRTE's "Unsaved changes" question: the page has edits that were not saved, so leaving it asks Save, Discard or
/// Cancel (Cancel keeps the tab open). Returns 0 = save, 1 = discard, 2 = cancel.
/// </summary>
internal sealed class UnsavedDialog : Window
{
    public bool CanSave { get; init; } = true;

    public UnsavedDialog(bool canSave = true)
    {
        Title = "Unsaved changes";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = UiKit.Brush("#141020");

        var text = new TextBlock
        {
            Text = "This page has changes that are not saved yet. Save them, discard them, or stay on the tab.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = UiKit.TextPrimary,
            Margin = new Thickness(0, 0, 0, 16),
        };

        Button Make(string label, int value)
        {
            var button = new Button { Content = label, MinWidth = 96, FontSize = 12.5, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => Close(value);
            return button;
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { Make("Save", 0), Make("Discard", 1), Make("Cancel", 2) },
        };
        var accent = Make("Save", 0);
        accent.IsVisible = canSave;
        accent.Classes.Add("accent");
        row.Children[0] = accent;

        Content = new StackPanel { Margin = new Thickness(18), Children = { text, row } };
    }

    /// <summary>Save is only offered for the tab that is showing; a background tab can only be discarded or kept.</summary>
    public static async Task<int> Ask(Window owner, bool canSave = true) { var dialog = new UnsavedDialog { CanSave = canSave }; return await dialog.ShowDialog<int?>(owner) ?? 2; }
}


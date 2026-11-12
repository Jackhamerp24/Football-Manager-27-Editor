using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fm27Editor.App;

/// <summary>
/// "How to use" for the Wonderkid Academy, written for players. It follows what the Hook Manager does: one level active at
/// a time, adding a wider entry drops the narrower ones it covers, Every regen clears the lists, Clear on the last entry
/// turns it off, settings apply at once, and it lasts until the game exits.
/// </summary>
internal static class WonderkidAcademyHelp
{
    static Window? open;

    public static void Show(Window owner)
    {
        if (open is { } w) { w.Activate(); return; }

        TextBlock Para(string text) => new()
        {
            Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = UiKit.Brush("#CBD5E1"), LineHeight = 19,
        };

        Control Rows(params (string Term, string Text)[] rows)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*") };
            for (int i = 0; i < rows.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var term = new TextBlock
                {
                    Text = rows[i].Term, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = UiKit.Brush("#F1F5F9"),
                    Margin = new Thickness(8, 5, 8, 5), TextWrapping = TextWrapping.Wrap,
                };
                var text = Para(rows[i].Text);
                text.Margin = new Thickness(0, 5, 8, 5);
                var band = new Border { Background = i % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent };
                Grid.SetRow(band, i); Grid.SetColumnSpan(band, 2);
                Grid.SetRow(term, i);
                Grid.SetRow(text, i); Grid.SetColumn(text, 1);
                grid.Children.Add(band); grid.Children.Add(term); grid.Children.Add(text);
            }
            return grid;
        }

        Control Steps(params string[] steps) => Rows(steps.Select((s, i) => ($"{i + 1}.", s)).ToArray());

        Control Section(string title, params Control[] body)
        {
            var stack = new StackPanel { Spacing = 6, Margin = new Thickness(10, 8) };
            foreach (var b in body) stack.Children.Add(b);
            return UiKit.Card(title, stack);
        }

        var content = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        content.Children.Add(Section("What it does",
            Para("Wonderkid Academy changes the young players the game creates from now on, such as youth intakes. " +
                 "Each new youngster gets a random potential (PA) inside the range you choose. Players who already exist are never changed.")));
        content.Children.Add(Section("Settings",
            Rows(("PA … to …", "The range the new potential is picked from. It is never below the youngster's current ability and never above 200."),
                 ("Wonderkid %", "The chance, 0 to 100, that a youngster also starts with a higher current ability, part of the way to his new potential. 0 changes only the potential.")),
            Para("Changing these numbers updates the active setting straight away.")));
        content.Children.Add(Section("Levels",
            Para("\"Applies to\" decides which youngsters are affected:"),
            Rows(("Every regen", "Every new youngster in the game."),
                 ("Continents", "Youngsters of clubs from the continents you add."),
                 ("Nations", "Youngsters of clubs from the nations you add."),
                 ("Clubs", "Youngsters of the clubs you add.")),
            Para("Only one level works at a time. The level you last added to is the active one and replaces the previous level; " +
                 "the Active line shows which. Each level takes up to 32 entries."),
            Para("A wider choice removes the narrower ones it already covers: adding a nation removes its clubs, adding a continent " +
                 "removes its nations and their clubs, and choosing Every regen empties all the lists.")));
        content.Children.Add(Section("How to use",
            Steps("Set the PA range and Wonderkid %.",
                  "Choose a level in Applies to.",
                  "Click the box, pick a club, nation or continent, and press Add. For Every regen, just press Add.",
                  "Check the Active line, then keep playing: the next youth intakes use it.")));
        content.Children.Add(Section("Turning it off",
            Rows(("Clear", "Removes that entry. Clearing the last entry, or Every regen, turns Wonderkid Academy off."),
                 ("Undo", "Takes back your last change (up to 20)."),
                 ("Closing", "It keeps working after you close the editor, until you exit the game. It is not kept in your save: set it up again after restarting the game."))));

        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 16, 12), Padding = new Thickness(16, 0), Height = 28, MinHeight = 28 };
        var root = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

        var window = new Window
        {
            Title = "Wonderkid Academy: How to use", Width = 620, Height = 680, MinWidth = 480, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = root,
        };
        close.Click += (_, _) => window.Close();
        window.Closed += (_, _) => open = null;
        open = window;
        window.Show(owner);
    }
}

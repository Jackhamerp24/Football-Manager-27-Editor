using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    IReadOnlyList<LanguageEntry>? languageCatalogue;

    Control LanguagesPanel(PlayerRecord player) => LanguagesPanel(backend.Session.Owner(player), RenderEditor);

    /// <summary>FMRTE's Languages tab for a player or a staff member (same list on the person).</summary>
    Control LanguagesPanel(PersonOwner player, Action rerender)
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        var languages = backend.Session.ReadLanguages(player);
        var fields = new List<(PlayerLanguage Language, TextBox Value)>();
        var save = new Button { IsVisible = false, Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        bool Pending() => fields.Any(f => f.Value.Text != f.Language.Value.ToString(Inv));

        // Add and Remove write at once, like FMRTE's +/−; they refuse while value edits are pending since the list moves.
        void Change(Action change, string done)
        {
            if (Pending()) { status.Text = "Save the language edits first"; return; }
            try { backend.Edits.Execute("Person", player.Name, "Languages", change); status.Text = done; rerender(); }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12, RowSpacing = 4, Margin = new Thickness(12, 8) };
        foreach (var language in languages)
        {
            var value = new TextBox { Text = language.Value.ToString(Inv), Width = 84, HorizontalContentAlignment = HorizontalAlignment.Right };
            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) => Change(() => backend.Session.RemoveLanguage(player, languages, language), $"Removed {language.Name}. Continue in FM to see updates.");
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = language.Name, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(name, row); Grid.SetRow(value, row); Grid.SetColumn(value, 1); Grid.SetRow(remove, row); Grid.SetColumn(remove, 2);
            grid.Children.Add(name); grid.Children.Add(value); grid.Children.Add(remove);
            fields.Add((language, value));
            value.TextChanged += (_, _) => save.IsEnabled = Pending();
        }

        if (languageCatalogue is not { Count: > 0 }) languageCatalogue = backend.Session.LanguageCatalogue(Players());
        var addLanguage = new ComboBox { Width = 220, PlaceholderText = "Add language", ItemsSource = languageCatalogue.Where(c => languages.All(l => l.LanguageObject != c.Address)).Select(c => c.Name).ToArray() };
        var addValue = new TextBox { Width = 84, Text = "10", HorizontalContentAlignment = HorizontalAlignment.Right };
        var add = new Button { Content = "Add", IsEnabled = false };
        addLanguage.SelectionChanged += (_, _) => add.IsEnabled = addLanguage.SelectedItem is not null;
        add.Click += (_, _) =>
        {
            if (languageCatalogue.FirstOrDefault(c => c.Name == addLanguage.SelectedItem as string) is not { } entry) return;
            if (!int.TryParse(addValue.Text, out var value) || value is < 1 or > 10) { status.Text = "Languages: enter a value 1–10"; return; }
            Change(() => backend.Session.AddLanguage(player, languages, entry, value),
                $"Added {entry.Name}. Its record lives outside the game's allocator; keep a backup of the save. Continue in FM to see updates.");
        };

        var list = new StackPanel();
        if (fields.Count > 0) list.Children.Add(grid);
        list.Children.Add(new Border
        {
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, fields.Count > 0 ? 1 : 0, 0, 0),
            Padding = new Thickness(12, 8),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { addLanguage, addValue, add } },
        });
        panel.Children.Add(UiKit.Card("Languages", list));
        save.Click += (_, _) =>
        {
            var changes = new List<(PlayerLanguage Language, int Value)>();
            foreach (var f in fields)
            {
                if (!int.TryParse(f.Value.Text, out var value) || value is < 1 or > 10) { status.Text = "Languages: enter a value 1–10"; return; }
                if (value != f.Language.Value) changes.Add((f.Language, value));
            }
            try
            {
                var session = backend.Session;
                backend.Edits.Execute("Player", player.Name, "Page save", () => { var writer = new LanguageWriter(session.Memory, session.Profile, session.Game.ModuleBase); foreach (var c in changes) writer.SetValue(player, c.Language, c.Value); });
                status.Text = "Saved languages. Continue in FM to see updates.";
                rerender();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        if (fields.Count > 0) panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save } });
        return panel;
    }
}

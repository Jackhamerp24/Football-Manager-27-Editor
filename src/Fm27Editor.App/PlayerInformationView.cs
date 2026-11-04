using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    IReadOnlyList<NationRecord>? nationCatalogue;
    IReadOnlyList<CityRecord>? cityCatalogue;

    /// <summary>FMRTE 27's Information tab fields that are not in the strip above the tabs; one Save writes the changed ones.</summary>
    Control InformationPanel(PlayerRecord player)
    {
        var panel = new StackPanel { Spacing = 8 };
        var session = backend.Session;
        var fields = session.PlayerFields;
        var values = fields.ReadAll(player);
        var statuses = session.ReadStatuses(player);
        var cities = session.Cities;
        ulong cityBefore = cities.CityPointer(player);
        var city = cities.Read(cityBefore);
        var today = session.ReadGameDate();
        nationCatalogue ??= session.Nations.LoadAll().Where(n => n.DisplayName.Length > 0).OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        var nations = nationCatalogue;

        var save = new Button { Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 24, RowSpacing = 4, Margin = new Thickness(12, 8) };
        var checks = new List<Func<bool>>();
        void Refresh() => save.IsEnabled = discard.IsEnabled = checks.Any(changed => changed());
        void Row(string label, Control value)
        {
            int r = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            value.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(text, r); Grid.SetRow(value, r); Grid.SetColumn(value, 1);
            grid.Children.Add(text); grid.Children.Add(value);
        }

        // A dropdown over one of FMRTE's lists; an unknown stored code stays selectable as "Code n" so Save keeps it.
        Func<int?> Choice(string label, PlayerField field, IReadOnlyList<(int Code, string Name)> list)
        {
            if (!values.TryGetValue(field, out long raw)) return () => null;
            int current = (int)raw;
            var items = list.Any(x => x.Code == current) ? list : [.. list, (current, $"Code {current}")];
            var box = new ComboBox { Width = 300, ItemsSource = items.Select(x => x.Name).ToArray(), SelectedIndex = items.ToList().FindIndex(x => x.Code == current) };
            box.SelectionChanged += (_, _) => Refresh();
            Row(label, box);
            int? Code() => box.SelectedIndex >= 0 ? items[box.SelectedIndex].Code : null;
            checks.Add(() => Code() != current);
            return Code;
        }

        Func<bool?> Flag(string label, bool? current)
        {
            if (current is not { } before) return () => null;
            var box = new CheckBox { IsChecked = before };
            box.IsCheckedChanged += (_, _) => Refresh();
            Row(label, box);
            checks.Add(() => box.IsChecked != before);
            return () => box.IsChecked;
        }

        // Nationality
        var nationBox = new ComboBox { Width = 300, ItemsSource = nations.Select(n => n.DisplayName).ToArray() };
        int nationIndex = nations.ToList().FindIndex(n => n.Address == player.Nationality?.Address);
        nationBox.SelectedIndex = nationIndex;
        nationBox.SelectionChanged += (_, _) => Refresh();
        checks.Add(() => nationBox.SelectedIndex != nationIndex);
        Row("Nationality", nationBox);
        var nationalityInfo = Choice("Nationality Info", PlayerField.NationalityInfo, InformationLabels.NationalityInfos);

        // City Of Birth: type to search the game's cities, then pick one.
        var nationNames = nations.ToDictionary(n => n.Address, n => n.DisplayName);
        string CityLabel(CityRecord c) => nationNames.TryGetValue(c.Nation, out var n) ? $"{c.Name} ({n})" : c.Name;
        var citySearch = new TextBox { Width = 160, Text = city?.Name ?? "" };
        var cityPick = new ComboBox { Width = 300 };
        if (city is not null) { cityPick.ItemsSource = new[] { new RelationTarget(CityLabel(city), city.Address) }; cityPick.SelectedIndex = 0; }
        citySearch.TextChanged += (_, _) =>
        {
            string q = (citySearch.Text ?? "").Trim();
            if (q.Length < 2 || q == city?.Name) return;
            cityCatalogue ??= cities.LoadAll();
            cityPick.ItemsSource = cityCatalogue.Where(c => Matches(c.Name, q)).Take(60).Select(c => new RelationTarget(CityLabel(c), c.Address)).ToArray();
            cityPick.SelectedIndex = -1;
        };
        cityPick.SelectionChanged += (_, _) => Refresh();
        checks.Add(() => cityPick.SelectedItem is RelationTarget t && t.Address != cityBefore);
        if (session.Profile.Cities is not null)
            Row("City Of Birth", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { citySearch, cityPick } });

        var bodyType = Choice("Body Type", PlayerField.BodyType, InformationLabels.BodyTypes);
        var hairColour = Choice("Hair Colour", PlayerField.HairColour, InformationLabels.HairColours);
        var hairLength = Choice("Hair Length", PlayerField.HairLength, InformationLabels.HairLengths);
        var skinColour = Choice("Skin Colour", PlayerField.SkinColour, InformationLabels.SkinColours);
        var ethnicity = Choice("Ethnicity", PlayerField.Ethnicity, InformationLabels.Ethnicities);

        // Days in Country: the game stores the arrival date; FMRTE shows game date minus it.
        TextBox? days = null;
        long arrivedRaw = 0;
        int? daysBefore = null;
        if (today is { } now && values.TryGetValue(PlayerField.ArrivedInCountry, out arrivedRaw) && arrivedRaw != 0)
        {
            var arrived = new DateOnly((int)(arrivedRaw / 10000), (int)(arrivedRaw / 100 % 100), (int)(arrivedRaw % 100));
            daysBefore = now.DayNumber - arrived.DayNumber;
            days = new TextBox { Width = 110, Text = daysBefore.Value.ToString(Inv), HorizontalContentAlignment = HorizontalAlignment.Right };
            days.TextChanged += (_, _) => Refresh();
            checks.Add(() => days.Text != daysBefore.Value.ToString(Inv));
            Row("Days in Country", days);
        }

        var nonEu = Flag("Treated As Non-EU In Italy", values.TryGetValue(PlayerField.TreatedAsNonEuInItaly, out long ne) ? ne == 1 : null);
        var retired = Flag("Retired From Int. Foot.", statuses.Holder != 0 ? statuses.InternationalRetirement is not null : null);
        var retiring = Flag("Retiring After Spell At Current Club", values.TryGetValue(PlayerField.RetiringAfterSpellAtCurrentClub, out long rt) ? rt == 1 : null);

        var retirements = new PlannedRetirements(session.Memory, session.Profile, session.ModuleBase);
        if (retirements.IsSupported)
        {
            string retirementDate;
            try
            {
                var plans = retirements.ReadAll();
                uint person = retirements.PersonId(player);
                retirementDate = plans.TryGetValue(person, out var plan) ? plan.Date?.ToString("yyyy-MM-dd") ?? "Unknown" : "None";
            }
            catch (Exception ex) when (ex is StaleObjectException or Fm27Editor.Core.Memory.MemoryAccessException)
            {
                retirementDate = "Unavailable";
            }
            Row("Planned Retirement", new TextBlock { Text = retirementDate, VerticalAlignment = VerticalAlignment.Center });
        }

        panel.Children.Add(new Border { Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1), Child = grid });
        save.Click += (_, _) =>
        {
            int? newDays = null;
            if (days is not null && days.Text != daysBefore!.Value.ToString(Inv))
            {
                if (!int.TryParse(days.Text, out var d) || d < 0 || d > 36500) { status.Text = "Days in Country: enter 0–36500"; return; }
                newDays = d;
            }
            var writes = new List<Action>();
            void Field(PlayerField field, long? value)
            {
                if (value is { } v && values.TryGetValue(field, out long before) && v != before) writes.Add(() => fields.Set(player, field, before, v));
            }
            Field(PlayerField.NationalityInfo, nationalityInfo());
            Field(PlayerField.BodyType, bodyType());
            Field(PlayerField.HairColour, hairColour());
            Field(PlayerField.HairLength, hairLength());
            Field(PlayerField.SkinColour, skinColour());
            Field(PlayerField.Ethnicity, ethnicity());
            Field(PlayerField.TreatedAsNonEuInItaly, nonEu() is { } n ? (n ? 1 : 0) : null);
            Field(PlayerField.RetiringAfterSpellAtCurrentClub, retiring() is { } r ? (r ? 1 : 0) : null);
            if (newDays is { } nd && today is { } now2)
            {
                var date = now2.AddDays(-nd);
                Field(PlayerField.ArrivedInCountry, date.Year * 10000 + date.Month * 100 + date.Day);
            }
            if (nationBox.SelectedIndex >= 0 && nationBox.SelectedIndex != nationIndex && player.Nationality is { } oldNation)
            {
                var nation = nations[nationBox.SelectedIndex];
                writes.Add(() => session.Origin.SetNationality(player, oldNation.Address, nation));
            }
            if (cityPick.SelectedItem is RelationTarget t && t.Address != cityBefore && cities.Read(t.Address) is { } newCity)
                writes.Add(() => session.Origin.SetCity(player, cityBefore, newCity));
            if (retired() is { } ret && ret != (statuses.InternationalRetirement is not null))
                writes.Add(ret ? () => session.AddInternationalRetirement(player, statuses)
                               : () => new RelationshipWriter(session.Memory, session.Profile, session.Game.ModuleBase).Remove(player, statuses.InternationalRetirement!));
            try
            {
                backend.Edits.Execute(() => { foreach (var w in writes) w(); });
                status.Text = "Saved information. Continue in FM to see updates.";
                // The nationality lives on the player record; reload it so the next write checks against the new nation.
                if (nationBox.SelectedIndex != nationIndex && backend.Players.Reload(player.Address) is { } fresh) { fresh.GameDate = backend.GameDate; selected = fresh; ReplaceRow(fresh); }
                RenderEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        discard.Click += (_, _) => RenderEditor();
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
        return panel;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    IReadOnlyList<NationRecord>? nationCatalogue;
    IReadOnlyList<CityRecord>? cityCatalogue;
    readonly Dictionary<ulong, bool> goalkeeperColumn = [];

    /// <summary>FMRTE's Information page: personal details, then Technical, Mental, Physical and Hidden columns on one screen.</summary>
    Control InformationSection(PlayerRecord player)
    {
        var personal = InformationPanel(player);
        personal.Width = 295;
        personal.HorizontalAlignment = HorizontalAlignment.Left;

        var host = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,1*,1*,1*"),
            ColumnSpacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 10),
        };

        var column0 = new StackPanel
        {
            Spacing = 8,
            Width = 295,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { personal, MiniScoutingCard(player) },
        };
        Grid.SetColumn(column0, 0);
        host.Children.Add(column0);

        // FMRTE keeps the stat columns equal width and right-aligns the numbers.
        Control Column(string group, Control? header = null)
        {
            var control = GroupControl(group, player, header);
            if (control is Border border)
            {
                border.HorizontalAlignment = HorizontalAlignment.Stretch;
                border.Margin = new Thickness(0);
            }
            return control;
        }

        var groups = new[] { "Technical", "Mental", "Physical" };
        for (int i = 0; i < groups.Length; i++)
        {
            var column = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (groups[i] == "Technical")
            {
                var picker = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                var technical = new Button { Content = "Tec.", Padding = new Thickness(7, 0), MinHeight = 20, Height = 20, FontSize = 12 };
                var goalkeeping = new Button { Content = "GK", Padding = new Thickness(7, 0), MinHeight = 20, Height = 20, FontSize = 12 };
                var selectedColumn = new ContentControl { HorizontalAlignment = HorizontalAlignment.Stretch };
                void Select(bool keeper)
                {
                    goalkeeperColumn[player.Address] = keeper;
                    technical.Classes.Set("accent", !keeper);
                    goalkeeping.Classes.Set("accent", keeper);
                    if (picker.Parent is Panel oldParent) oldParent.Children.Remove(picker);
                    selectedColumn.Content = Column(keeper ? "Goalkeeping" : "Technical", picker);
                }
                technical.Click += (_, _) => Select(false);
                goalkeeping.Click += (_, _) => Select(true);
                picker.Children.Add(technical); picker.Children.Add(goalkeeping);
                column.Children.Add(selectedColumn);
                Select(goalkeeperColumn.GetValueOrDefault(player.Address, player.Position(PlayerPosition.Goalkeeper) >= 15));
            }
            else column.Children.Add(Column(groups[i]));
            if (groups[i] == "Mental")
            {
                var personality = PersonalityClassifier.Classify(player);
                var persBadge = new TextBlock
                {
                    Text = personality.Name,
                    FontSize = 11.5,
                    FontWeight = personality.IsNegative ? FontWeight.Bold : FontWeight.SemiBold,
                    Foreground = Brush(personality.IsNegative ? "#F87171" : "#94A3B8"),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                if (personality.IsNegative)
                    ToolTip.SetTip(persBadge, "Negative personality (regen/newgen classification)");
                column.Children.Add(AttributeCard(
                    "Personality",
                    PlayerFields.All.Where(f => f.Group == "Personality").Select(f => FieldControl(f, player)),
                    rightHeader: persBadge));
            }
            if (groups[i] == "Physical") column.Children.Add(Column("Hidden"));
            Grid.SetColumn(column, i + 1);
            host.Children.Add(column);
        }
        return host;
    }

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

        var rowsStack = new StackPanel { Spacing = 0 };
        Grid.SetIsSharedSizeScope(rowsStack, true);
        int rowIdx = 0;
        var checks = new List<Func<bool>>();
        void Refresh() => UpdateScalarDraftState(player);
        void Row(string label, Control value)
        {
            var text = new TextBlock
            {
                Text = label,
                FontSize = 13,
                Foreground = UiKit.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, 5, 0),
            };
            Control inner;
            if (value is CheckBox or NumericUpDown || label is "Planned Retirement" or "Days in Country")
            {
                if (value is NumericUpDown || label == "Days in Country")
                {
                    value.Width = 96;
                    value.HorizontalAlignment = HorizontalAlignment.Right;
                    value.MinHeight = 24;
                }
                var flagRow = new DockPanel { LastChildFill = true, MinHeight = 24 };
                DockPanel.SetDock(value, Dock.Right);
                flagRow.Children.Add(value);
                flagRow.Children.Add(text);
                inner = flagRow;
            }
            else
            {
                value.HorizontalAlignment = HorizontalAlignment.Stretch;
                value.MinHeight = 24;
                var line = new Grid
                {
                    MinHeight = 24,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "InfoLbl" },
                        new ColumnDefinition(GridLength.Star),
                    },
                };
                Grid.SetColumn(text, 0);
                Grid.SetColumn(value, 1);
                line.Children.Add(text);
                line.Children.Add(value);
                inner = line;
            }
            rowsStack.Children.Add(new Border
            {
                Background = rowIdx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                Padding = new Thickness(5, 2, 5, 2),
                Child = inner,
            });
        }

        // A dropdown over one of FMRTE's lists; an unknown stored code stays selectable as "Code n" so Save keeps it.
        Func<int?> Choice(string label, PlayerField field, IReadOnlyList<(int Code, string Name)> list)
        {
            if (!values.TryGetValue(field, out long raw)) return () => null;
            int current = (int)raw;
            var items = list.Any(x => x.Code == current) ? list : [.. list, (current, $"Code {current}")];
            var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = items.Select(x => x.Name).ToArray(), SelectedIndex = items.ToList().FindIndex(x => x.Code == current) };
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

        foreach (var id in new[] { "Born", "Height", "HomeReputation", "CurrentReputation", "WorldReputation" })
        {
            var field = PlayerFields.All.Single(f => f.Id == id);
            var label = id switch { "HomeReputation" => "Home Reputation", "CurrentReputation" => "Current Reputation", "WorldReputation" => "World Reputation", _ => field.Label };
            Row(label, FieldControl(field, player, labelOverride: ""));
        }

        foreach (var descriptor in SquadScalarDefinitions.Descriptors.Where(d => d.Field is PlayerField.Morale or PlayerField.AskingPrice or PlayerField.Caps or PlayerField.InternationalGoals or PlayerField.SubCaps or PlayerField.SubGoals))
        {
            if (!values.TryGetValue(descriptor.Field, out long original)) continue;
            long current = playerScalarDrafts.GetValueOrDefault(player.Address)?.TryGetValue(descriptor.Field, out var staged) == true ? staged.Value : original;
            var input = new NumericUpDown
            {
                Minimum = descriptor.Min,
                Maximum = descriptor.Max,
                Value = current,
                FormatString = descriptor.Field == PlayerField.AskingPrice ? "N0" : "0",
                ShowButtonSpinner = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Right,
            };
            input.ValueChanged += (_, e) => { if (!updating && e.NewValue is { } value) StageScalarChange(player, descriptor.Field, original, (long)value); };
            Row(descriptor.Label, input);
        }

        // Nationality: one button, the app's search picker (FMRTE's own choice box).
        NationRecord? chosenNation = null;
        var nationPick = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = player.Nationality?.DisplayName ?? "Choose nation…" };
        nationPick.Click += async (_, _) =>
        {
            var chosen = await PickerDialog.Show<NationRecord>(this, "Choose nationality", q => nations.Where(n => PickerSearch.Hit(q, n.DisplayName, n.ShortName)).Select(n => (n.DisplayName, n)).ToList());
            if (chosen is null) return;
            chosenNation = chosen;
            nationPick.Content = chosen.DisplayName;
            Refresh();
        };
        checks.Add(() => chosenNation is { } pickedNation && pickedNation.Address != (player.Nationality?.Address ?? 0));
        Row("Nationality", nationPick);
        var nationalityInfo = Choice("Nationality Info", PlayerField.NationalityInfo, InformationLabels.NationalityInfos);

        // City Of Birth: the same search picker over the game's cities.
        var nationNames = nations.ToDictionary(n => n.Address, n => n.DisplayName);
        string CityLabel(CityRecord c) => nationNames.TryGetValue(c.Nation, out var n) ? $"{c.Name} ({n})" : c.Name;
        RelationTarget? chosenCity = city is null ? null : new RelationTarget(CityLabel(city), city.Address);
        var cityPick = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = city is null ? "Choose city…" : CityLabel(city) };
        cityPick.Click += async (_, _) =>
        {
            cityCatalogue ??= cities.LoadAll();
            var choices = cityCatalogue.Select(c => new RelationTarget(CityLabel(c), c.Address)).ToList();
            var chosen = await PickerDialog.Show<RelationTarget>(this, "Choose city of birth", q => choices.Where(t => PickerSearch.Hit(q, t.Label)).Select(t => (t.Label, t)).ToList());
            if (chosen is null) return;
            chosenCity = chosen;
            cityPick.Content = chosen.Label;
            Refresh();
        };
        checks.Add(() => chosenCity?.Address != cityBefore);
        if (session.Profile.Cities is not null)
            Row("City Of Birth", cityPick);

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
            days = new TextBox { HorizontalAlignment = HorizontalAlignment.Stretch, Text = daysBefore.Value.ToString(Inv), HorizontalContentAlignment = HorizontalAlignment.Right };
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
                retirementDate = plans.TryGetValue(person, out var plan) ? plan.Date is { } retires ? DateSetting.Format(retires) : "Unknown" : "None";
            }
            catch (Exception ex) when (ex is StaleObjectException or Fm27Editor.Core.Memory.MemoryAccessException)
            {
                retirementDate = "Unavailable";
            }
            Row("Planned Retirement", new TextBlock { Text = retirementDate, VerticalAlignment = VerticalAlignment.Center });
        }

        panel.Children.Add(UiKit.Card("Personal Details", rowsStack));
        bool SaveInformation()
        {
            int? newDays = null;
            if (days is not null && days.Text != daysBefore!.Value.ToString(Inv))
            {
                if (!int.TryParse(days.Text, out var d) || d < 0 || d > 36500) { status.Text = "Days in Country: enter 0–36500"; return false; }
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
            if (chosenNation is { } nation && nation.Address != (player.Nationality?.Address ?? 0) && player.Nationality is { } oldNation)
                writes.Add(() => session.Origin.SetNationality(player, oldNation.Address, nation));
            if (chosenCity is { } pickedCity && pickedCity.Address != cityBefore && cities.Read(pickedCity.Address) is { } newCity)
                writes.Add(() => session.Origin.SetCity(player, cityBefore, newCity));
            if (retired() is { } ret && ret != (statuses.InternationalRetirement is not null))
                writes.Add(ret ? () => session.AddInternationalRetirement(player, statuses)
                               : () => new RelationshipWriter(session.Memory, session.Profile, session.Game.ModuleBase).Remove(player, statuses.InternationalRetirement!));
            try
            {
                backend.Edits.Execute("Player", player.DisplayName, "Page save", () => { foreach (var w in writes) w(); });
                status.Text = "Saved information. Continue in FM to see updates.";
                // The nationality lives on the player record; reload it so the next write checks against the new nation.
                if ((chosenNation?.Address ?? 0) != (player.Nationality?.Address ?? 0) && backend.Players.Reload(player.Address) is { } fresh) { fresh.GameDate = backend.GameDate; selected = fresh; ReplaceRow(fresh); }
                return true;
            }
            catch (Exception ex) { status.Text = $"Some information changes may have been saved. {ex.Message}"; return false; }
        }
        informationDirty = () => checks.Any(changed => changed());
        saveInformation = SaveInformation;
        return panel;
    }
}

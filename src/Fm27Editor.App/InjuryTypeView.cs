using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

internal sealed record InjuryTypeRow(InjuryType Type, string Name, int UniqueId, int MinimumDays, int ExtraDays, string Seriousness);

/// <summary>The injury kinds of the game, with FMRTE 27's Injury page: type details, duration and treatment.</summary>
internal sealed class InjuryTypeScreen(IScreenHost host)
    : EntityScreen<InjuryType, InjuryTypeRow, Dictionary<InjuryTypeField, string>>(host, "injury type", "Injuries", "injury types", "Search injury...")
{
    static readonly (int Value, string Label)[] Seriousness =
        [(0, "Invalid"), (4, "Minor Injury"), (3, "Moderate Injury"), (1, "Potentially Career Threatening"), (2, "Serious Injury"), (5, "Walking Wounded")];
    static readonly (int Value, string Label)[] AgeRules = [(2, "Any"), (1, "Over 28")];
    static readonly (int Value, string Label)[] Seasons = [(1, "Any"), (2, "Summer"), (3, "Winter")];
    static readonly (InjuryTreatment Flag, string Label)[] Treatments =
        [(InjuryTreatment.Physio, "By Physio"), (InjuryTreatment.Specialist, "By Specialist"), (InjuryTreatment.Injection, "By Injection"), (InjuryTreatment.Isolation, "By Isolation")];

    static string SeriousnessName(int value) => Seriousness.FirstOrDefault(s => s.Value == value).Label ?? value.ToString(Inv);

    protected override void ConfigureColumns(DataGrid grid)
    {
        grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(InjuryTypeRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Seriousness", Binding = new Binding(nameof(InjuryTypeRow.Seriousness)), Width = new DataGridLength(190) });
        foreach (var (title, property, width) in new[] { ("Min", nameof(InjuryTypeRow.MinimumDays), 64), ("Extra", nameof(InjuryTypeRow.ExtraDays), 64), ("ID", nameof(InjuryTypeRow.UniqueId), 64) })
        {
            var column = new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = new DataGridLength(width) };
            column.CellStyleClasses.Add("numeric");
            grid.Columns.Add(column);
        }
    }

    static InjuryTypeRow Row(InjuryType type, InjuryTypeDetails d) =>
        new(type, d.Name ?? type.Name ?? "", d.UniqueId, d.MinimumDays, d.ExtraDays, SeriousnessName(d.Seriousness));

    protected override IReadOnlyList<InjuryTypeRow> LoadRows(GameSession session)
    {
        var players = Host.Players();
        var template = players.Count > 0 ? session.InjuryTemplate(players[0], players) : null;
        var access = session.InjuryTypeFields;
        return session.InjuryTypes(template).Select(t => access.Read(t) is { } d ? Row(t, d) : null).OfType<InjuryTypeRow>()
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    protected override bool Matches(InjuryTypeRow row, string q) => Fold(row.Name).Contains(q, StringComparison.Ordinal) || row.UniqueId.ToString(Inv) == q;
    protected override InjuryType RecordOf(InjuryTypeRow row) => row.Type;
    protected override ulong AddressOf(InjuryType type) => type.Address;
    protected override string NameOf(InjuryType type) => type.Name ?? type.Id.ToString(Inv);
    protected override Dictionary<InjuryTypeField, string> NewDraft() => [];
    protected override bool HasChanges(InjuryType type, Dictionary<InjuryTypeField, string> draft) => draft.Count != 0;
    protected override InjuryType? Reload(InjuryType type) => Host.Backend.Session.InjuryTypeFields.Read(type) is null ? null : type;
    protected override InjuryTypeRow Refresh(InjuryTypeRow row, InjuryType fresh) =>
        Host.Backend.Session.InjuryTypeFields.Read(fresh) is { } d ? Row(fresh, d) : row;

    static string Label(InjuryTypeField field) => field switch
    {
        InjuryTypeField.MinimumDays => "Minimum Days", InjuryTypeField.ExtraDays => "Extra Days", InjuryTypeField.InactiveRatio => "Inactive Ratio",
        InjuryTypeField.OccurrenceRatio => "Occurrence Ratio", InjuryTypeField.MatchInjuryPercentage => "Match Injury Percentage",
        InjuryTypeField.GoalkeeperPercentage => "Goalkeeper Percentage", InjuryTypeField.DefenderCentralPercentage => "Defender Central Percentage",
        InjuryTypeField.AgeRule => "Age Rule", _ => field.ToString(),
    };

    Control Combo(Dictionary<InjuryTypeField, string> draft, InjuryTypeField field, int original, (int Value, string Label)[] options, Action changed)
    {
        int current = draft.TryGetValue(field, out var text) && int.TryParse(text, out int d) ? d : original;
        var box = new ComboBox { Width = 230, ItemsSource = options.Select(o => o.Label).ToArray(), SelectedIndex = Array.FindIndex(options, o => o.Value == current) };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0) return;
            int value = options[box.SelectedIndex].Value;
            if (value == original) draft.Remove(field); else draft[field] = value.ToString(Inv);
            changed();
        };
        return box;
    }

    Control Tick(Dictionary<InjuryTypeField, string> draft, InjuryTypeField field, bool original, Action changed)
    {
        bool current = draft.TryGetValue(field, out var text) ? text == "1" : original;
        var box = new CheckBox { IsChecked = current };
        box.IsCheckedChanged += (_, _) =>
        {
            bool now = box.IsChecked == true;
            if (now == original) draft.Remove(field); else draft[field] = now ? "1" : "0";
            changed();
        };
        return box;
    }

    protected override void BuildEditor(InjuryType type, Dictionary<InjuryTypeField, string> draft, Action changed)
    {
        var session = Host.Backend.Session;
        if (session.InjuryTypeFields.Read(type) is not { } d) return;
        HeaderHost.Content = UiKit.EntityBanner(d.Name ?? "", new TextBlock { Text = $"ID {d.UniqueId.ToString(Inv)}" }, null);
        var injury = CardRows("Injury", new (string, Control)[]
        {
            ("Unique ID", Label(d.UniqueId.ToString(Inv), "#F1F5F9")), ("Name", Label(d.Name ?? "", "#F1F5F9")),
            ("Seriousness", Combo(draft, InjuryTypeField.Seriousness, d.Seriousness, Seriousness, changed)),
            ("Age Rule", Combo(draft, InjuryTypeField.AgeRule, d.AgeRule, AgeRules, changed)),
            ("Season", Combo(draft, InjuryTypeField.Season, d.Season, Seasons, changed)),
            ("Recurring", Tick(draft, InjuryTypeField.Recurring, d.Recurring, changed)),
            ("Contagious", Tick(draft, InjuryTypeField.Contagious, d.Contagious, changed)),
        });
        Control Field(InjuryTypeField f, string original) => FieldBox(draft, f, original, 96, changed);
        var duration = CardRows("Duration", new (string, Control)[]
        {
            (Label(InjuryTypeField.MinimumDays), Field(InjuryTypeField.MinimumDays, d.MinimumDays.ToString(Inv))),
            (Label(InjuryTypeField.ExtraDays), Field(InjuryTypeField.ExtraDays, d.ExtraDays.ToString(Inv))),
            (Label(InjuryTypeField.InactiveRatio), Field(InjuryTypeField.InactiveRatio, d.InactiveRatio.ToString(Inv))),
            (Label(InjuryTypeField.OccurrenceRatio), Field(InjuryTypeField.OccurrenceRatio, d.OccurrenceRatio.ToString("0.###", Inv))),
            (Label(InjuryTypeField.MatchInjuryPercentage), Field(InjuryTypeField.MatchInjuryPercentage, d.MatchInjuryPercentage.ToString(Inv))),
            (Label(InjuryTypeField.GoalkeeperPercentage), Field(InjuryTypeField.GoalkeeperPercentage, d.GoalkeeperPercentage.ToString(Inv))),
            (Label(InjuryTypeField.DefenderCentralPercentage), Field(InjuryTypeField.DefenderCentralPercentage, d.DefenderCentralPercentage.ToString(Inv))),
        });
        int treatment = draft.TryGetValue(InjuryTypeField.Treatment, out var t) && int.TryParse(t, out int tv) ? tv : (int)d.Treatment;
        var boxes = Treatments.Select(x => new CheckBox { IsChecked = (treatment & (int)x.Flag) != 0 }).ToArray();
        foreach (var box in boxes)
            box.IsCheckedChanged += (_, _) =>
            {
                int value = 0;
                for (int i = 0; i < boxes.Length; i++) if (boxes[i].IsChecked == true) value |= (int)Treatments[i].Flag;
                if (value == (int)d.Treatment) draft.Remove(InjuryTypeField.Treatment); else draft[InjuryTypeField.Treatment] = value.ToString(Inv);
                changed();
            };
        var treatments = CardRows("Treatment", Treatments.Select((x, i) => (x.Label, (Control)boxes[i])));
        Editor.Children.Add(TwoColumns(new StackPanel { Spacing = 8, Children = { injury } }, new StackPanel { Spacing = 8, Children = { duration, treatments } }));
    }

    protected override string? Validate(InjuryType type, Dictionary<InjuryTypeField, string> draft, out Func<int> write)
    {
        write = () => 0;
        var values = new List<(InjuryTypeField Field, double Value)>();
        foreach (var (field, text) in draft)
        {
            if (!double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, Inv, out double value))
                return $"{Label(field)}: enter a number";
            var (min, max) = field switch
            {
                InjuryTypeField.MinimumDays or InjuryTypeField.ExtraDays => (0, 65535),
                InjuryTypeField.InactiveRatio => (0, 255),
                InjuryTypeField.OccurrenceRatio => (0, 1000),
                InjuryTypeField.MatchInjuryPercentage => (0, 100),
                InjuryTypeField.GoalkeeperPercentage or InjuryTypeField.DefenderCentralPercentage => (-1, 100),
                _ => (0, 15),
            };
            if (value < min || value > max) return $"{Label(field)}: enter a number {min} to {max}";
            values.Add((field, value));
        }
        write = () =>
        {
            var access = Host.Backend.Session.InjuryTypeFields;
            foreach (var (field, value) in values)
            {
                // Each write checks the whole object, so the next one starts from the object as the last one left it.
                var current = access.Read(type) ?? throw new StaleObjectException("The injury type is no longer readable");
                access.Set(current, field, value);
            }
            return values.Count;
        };
        return null;
    }
}

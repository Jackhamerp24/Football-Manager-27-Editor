using Avalonia.Controls;
using Avalonia.Data;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.History;
using Fm27Editor.Core.Officials;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

internal sealed record RefereeRow(RefereeRecord Referee, string Name, int CA, int PA, int Reputation, int Id);

internal sealed class RefereeScreen(IScreenHost host)
    : EntityScreen<RefereeRecord, RefereeRow, Dictionary<RefereeField, string>>(host, "referee", "Referees", "referees", "Search referee or ID...")
{
    static readonly RefereeField[] Ability = [RefereeField.CurrentAbility, RefereeField.PotentialAbility, RefereeField.Reputation];
    static readonly RefereeField[] Skills = [RefereeField.AllowingFlow, RefereeField.Discipline, RefereeField.ImportantMatches,
        RefereeField.Pressure, RefereeField.Refereeing, RefereeField.RunningLine, RefereeField.TimeKeeping];
    static readonly RefereeField[] Flags = [RefereeField.EliteList, RefereeField.Continental, RefereeField.EuropeanCategory];

    protected override void ConfigureColumns(DataGrid grid)
    {
        grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(RefereeRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        foreach (var (title, property, width) in new[] { ("CA", nameof(RefereeRow.CA), 65), ("PA", nameof(RefereeRow.PA), 65),
                     ("Reputation", nameof(RefereeRow.Reputation), 100), ("ID", nameof(RefereeRow.Id), 110) })
        {
            var column = new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = new DataGridLength(width) };
            column.CellStyleClasses.Add("numeric");
            grid.Columns.Add(column);
        }
    }

    static RefereeRow Row(RefereeRecord r) => new(r, r.DisplayName, r.CurrentAbility, r.PotentialAbility, r.Reputation, r.UniqueId);
    protected override IReadOnlyList<RefereeRow> LoadRows(GameSession session) => session.ReadReferees().Select(Row)
        .OrderByDescending(x => x.CA).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    protected override bool Matches(RefereeRow row, string q) => Fold(row.Name).Contains(q, StringComparison.Ordinal) || row.Id.ToString(Inv).Contains(q, StringComparison.Ordinal);
    protected override RefereeRecord RecordOf(RefereeRow row) => row.Referee;
    protected override ulong AddressOf(RefereeRecord r) => r.Address;
    protected override string NameOf(RefereeRecord r) => r.DisplayName;
    protected override Func<ulong, int, ChangeLabel?>? LabelsOf(RefereeRecord r) => EntityChangeLabels.ForReferee(r.Address);
    protected override Dictionary<RefereeField, string> NewDraft() => [];
    protected override bool HasChanges(RefereeRecord r, Dictionary<RefereeField, string> d) => d.Count != 0;
    protected override RefereeRecord? Reload(RefereeRecord r) => Host.Backend.Session.Referees.Read(r.Address);
    protected override RefereeRow Refresh(RefereeRow row, RefereeRecord fresh) => Row(fresh);

    static string LabelFor(RefereeField field) => field switch
    {
        RefereeField.CurrentAbility => "Current Ability", RefereeField.PotentialAbility => "Potential Ability",
        RefereeField.AllowingFlow => "Allowing Flow", RefereeField.ImportantMatches => "Important Matches",
        RefereeField.RunningLine => "Running Line", RefereeField.TimeKeeping => "Time Keeping",
        RefereeField.EliteList => "Elite List", RefereeField.EuropeanCategory => "European Category", _ => field.ToString(),
    };

    static int Value(RefereeRecord r, RefereeField f) => f switch
    {
        RefereeField.CurrentAbility => r.CurrentAbility, RefereeField.PotentialAbility => r.PotentialAbility,
        RefereeField.Reputation => r.Reputation, RefereeField.AllowingFlow => r.AllowingFlow,
        RefereeField.Discipline => r.Discipline, RefereeField.ImportantMatches => r.ImportantMatches,
        RefereeField.Pressure => r.Pressure, RefereeField.Refereeing => r.Refereeing,
        RefereeField.RunningLine => r.RunningLine, RefereeField.TimeKeeping => r.TimeKeeping,
        RefereeField.EliteList => r.EliteList, RefereeField.Continental => r.Continental,
        RefereeField.EuropeanCategory => r.EuropeanCategory, _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };

    protected override void BuildEditor(RefereeRecord r, Dictionary<RefereeField, string> draft, Action changed)
    {
        HeaderHost.Content = UiKit.EntityBanner(r.DisplayName, new TextBlock { Text = $"ID {r.UniqueId.ToString(Inv)}" }, null);
        var info = CardRows("Information", [("ID", Label(r.UniqueId.ToString(Inv), "#F1F5F9")),
            ("Date of Birth", Label(DateSetting.Format(r.DateOfBirth), "#F1F5F9"))]);
        Control Card(string title, RefereeField[] fields) => CardRows(title, fields.Select(f =>
            (LabelFor(f), (Control)FieldBox(draft, f, Value(r, f).ToString(Inv), 96, changed))));
        var left = new StackPanel { Spacing = 8, Children = { info, Card("Ability", Ability) } };
        var right = new StackPanel { Spacing = 8, Children = { Card("Skills", Skills), Card("Flags", Flags) } };
        Editor.Children.Add(TwoColumns(left, right));
    }

    protected override string? Validate(RefereeRecord r, Dictionary<RefereeField, string> draft, out Func<int> write)
    {
        write = () => 0;
        var values = new Dictionary<RefereeField, int>();
        foreach (var (field, text) in draft)
        {
            int min = field is RefereeField.Reputation or RefereeField.EliteList or RefereeField.Continental or RefereeField.EuropeanCategory ? 0 : 1;
            int max = field switch { RefereeField.CurrentAbility or RefereeField.PotentialAbility => 200,
                RefereeField.Reputation => 10000, RefereeField.EliteList or RefereeField.Continental => 1,
                RefereeField.EuropeanCategory => 5, _ => 20 };
            if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int value) || value < min || value > max)
                return $"{LabelFor(field)}: enter a whole number {min} to {max}";
            values[field] = value;
        }
        write = () =>
        {
            var session = Host.Backend.Session;
            var current = r;
            foreach (var (field, value) in values)
            {
                session.SetReferee(current, field, value);
                current = session.Referees.Read(r.Address) ?? throw new StaleObjectException("The referee is no longer readable");
                if (current.UniqueId != r.UniqueId || current.VtableRva != r.VtableRva)
                    throw new StaleObjectException("The referee changed while saving");
            }
            return values.Count;
        };
        return null;
    }
}

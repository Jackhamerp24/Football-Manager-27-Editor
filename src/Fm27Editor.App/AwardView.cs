using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Awards;
using Fm27Editor.Core.Game;

namespace Fm27Editor.App;

internal sealed record AwardRow(AwardRecord Award, string Name, int Reputation, int Id);

/// <summary>Awards: a reputation and what a winner's reputation becomes.</summary>
internal sealed class AwardScreen(IScreenHost host)
    : EntityScreen<AwardRecord, AwardRow, Dictionary<AwardField, string>>(host, "award", "Awards", "awards", "Search award or ID...")
{
    protected override void ConfigureColumns(DataGrid grid)
    {
        grid.Columns.Add(new DataGridTextColumn { Header = "Award", Binding = new Binding(nameof(AwardRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var rep = new DataGridTextColumn { Header = "Reputation", Binding = new Binding(nameof(AwardRow.Reputation)), Width = new DataGridLength(115) };
        rep.CellStyleClasses.Add("numeric");
        grid.Columns.Add(rep);
        var id = new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(AwardRow.Id)), Width = new DataGridLength(115) };
        id.CellStyleClasses.Add("numeric");
        grid.Columns.Add(id);
    }

    protected override IReadOnlyList<AwardRow> LoadRows(GameSession session) => session.Awards.LoadAll()
        .Select(a => new AwardRow(a, a.DisplayName, a.Reputation, a.Id))
        .OrderByDescending(r => r.Reputation).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

    protected override bool Matches(AwardRow row, string q) =>
        Fold(row.Name).Contains(q, StringComparison.Ordinal) || row.Id.ToString(Inv).Contains(q, StringComparison.Ordinal);

    protected override AwardRecord RecordOf(AwardRow row) => row.Award;
    protected override ulong AddressOf(AwardRecord record) => record.Address;
    protected override string NameOf(AwardRecord record) => record.DisplayName;
    protected override Func<ulong, int, Fm27Editor.Core.History.ChangeLabel?>? LabelsOf(AwardRecord record) =>
        Fm27Editor.Core.History.EntityChangeLabels.ForAward(Host.Backend.Session.Profile, record.Address);
    protected override Dictionary<AwardField, string> NewDraft() => [];
    protected override bool HasChanges(AwardRecord record, Dictionary<AwardField, string> draft) => draft.Count > 0;
    protected override AwardRecord? Reload(AwardRecord record) => Host.Backend.Session.Awards.Reload(record.Address);
    protected override AwardRow Refresh(AwardRow row, AwardRecord fresh) => row with { Award = fresh, Reputation = fresh.Reputation };

    static string FieldLabel(AwardField f) => f switch
    {
        AwardField.Reputation => "Reputation", AwardField.WinnerHomeReputation => "Winner home reputation", _ => "Winner world reputation",
    };

    static string FieldValue(AwardRecord a, AwardField f) => f switch
    {
        AwardField.Reputation => a.Reputation.ToString(Inv), AwardField.WinnerHomeReputation => a.WinnerHomeReputation.ToString(Inv), _ => a.WinnerWorldReputation.ToString(Inv),
    };

    protected override void BuildEditor(AwardRecord a, Dictionary<AwardField, string> draft, Action changed)
    {
        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        badges.Children.Add(UiKit.MetaText($"ID {a.Id.ToString(Inv)}"));
        if (!string.IsNullOrEmpty(a.ShortName) && !string.Equals(a.ShortName, a.DisplayName, StringComparison.Ordinal))
        {
            badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText(a.ShortName, highlight: true));
        }
        var repText = new TextBlock
        {
            Text = a.Reputation.ToString(Inv), FontSize = 16, FontWeight = FontWeight.Bold, Foreground = UiKit.Brush("#C084FC"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children = { UiKit.StatBox("REP", repText) },
        };
        HeaderHost.Content = UiKit.EntityBanner(a.DisplayName, badges, actions, Host.Graphics.Image("award", a.Id, 72));

        var identification = CardRows("Identification", [
            ("Unique ID", Label(a.Id.ToString(Inv), "#F1F5F9")),
            ("Full Name", Label(a.Name ?? a.DisplayName, "#F1F5F9")),
            ("Short Name", Label(a.ShortName ?? "", "#F1F5F9")),
        ]);
        var reputation = CardRows("Reputation", Enum.GetValues<AwardField>()
            .Select(f => (FieldLabel(f), (Control)FieldBox(draft, f, FieldValue(a, f), 96, changed))));
        Editor.Children.Add(TwoColumns(identification, reputation));
    }

    protected override string? Validate(AwardRecord a, Dictionary<AwardField, string> draft, out Func<int> write)
    {
        var values = new Dictionary<AwardField, int>();
        write = () => 0;
        int count = draft.Count;
        foreach (var (f, text) in draft)
        {
            int max = f == AwardField.Reputation ? AwardWriter.MaxReputation : AwardWriter.MaxWinnerReputation;
            if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int v) || v < 0 || v > max)
                return $"{FieldLabel(f)}: enter a whole number 0 to {max}";
            values[f] = v;
        }
        var writer = Host.Backend.Session.AwardWriter;
        write = () => { foreach (var (f, v) in values) writer.Set(a, f, v); return count; };
        return null;
    }
}

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

internal sealed record NationRow(NationRecord Nation, string Name, int? Id, long? Fifa, long? Youth, long? Economy)
{
    public override string ToString() => Name;
}

internal sealed record AgreementTargetOption(ulong Address, string Name) { public override string ToString() => Name; }
internal sealed record AgreementTypeOption(AgreementKind Kind, string Name) { public override string ToString() => Name; }

/// <summary>A row the user asked to add to an agreement list: a copy of <see cref="Source"/> with its own type, target and dates.</summary>
internal sealed class AgreementAddition(NationAgreementRow source, ulong sourceNation, byte[]? emptyHeader)
{
    public NationAgreementRow Source { get; } = source;
    public ulong SourceNation { get; } = sourceNation;
    public byte[]? EmptyHeader { get; } = emptyHeader;
    public AgreementKind Kind { get; set; } = source.Kind;
    public ulong Target { get; set; } = source.Target;
    public string From { get; set; } = DateSetting.Format(source.From);
    public string Until { get; set; } = DateSetting.Format(source.Until);
}

/// <summary>Everything typed on one nation's pages and not saved yet.</summary>
internal sealed class NationDraft
{
    public Dictionary<NationField, string> Fields { get; } = [];
    public NationRivalryDraft Rivalry { get; } = new();
    public NationPreferenceDraft Preference { get; } = new();
    public NationTransferValues? TransferValuesSnapshot { get; set; }
    public Dictionary<int, string> TransferValues { get; } = [];
    /// <summary>National-team reputation text by team address.</summary>
    public Dictionary<ulong, string> Teams { get; } = [];
    /// <summary>The address of the continent object chosen, or null when unchanged.</summary>
    public ulong? Continent { get; set; }
    public bool? LicensedKits { get; set; }
    public (string? Background, string? Foreground)? Colours { get; set; }
    // Transfer preferences and continent preferences, by the vector they live in.
    public Dictionary<int, Dictionary<ulong, ulong>> PreferenceTargets { get; } = [];
    public Dictionary<int, HashSet<ulong>> PreferenceRemovals { get; } = [];
    public Dictionary<int, int> PreferenceAdds { get; } = [];
    // Agreements, by row address (additions by vector).
    public Dictionary<ulong, (NationAgreementRow Row, string From, string Until)> AgreementDates { get; } = [];
    public Dictionary<ulong, (NationAgreementRow Row, AgreementKind Kind, ulong Target)> AgreementTargets { get; } = [];
    public Dictionary<ulong, NationAgreementRow> AgreementRemovals { get; } = [];
    public Dictionary<int, List<AgreementAddition>> AgreementAdds { get; } = [];

    public bool Any =>
        Fields.Count > 0 || Rivalry.Count > 0 || Preference.Count > 0 || TransferValues.Count > 0 || Teams.Count > 0 || Continent is not null || LicensedKits is not null
        || Colours is not null || PreferenceTargets.Values.Any(v => v.Count > 0) || PreferenceRemovals.Values.Any(v => v.Count > 0)
        || PreferenceAdds.Values.Any(v => v > 0) || AgreementDates.Count > 0 || AgreementTargets.Count > 0 || AgreementRemovals.Count > 0 || AgreementAdds.Count > 0;

    public Dictionary<ulong, ulong> TargetsIn(int vector) => PreferenceTargets.TryGetValue(vector, out var t) ? t : PreferenceTargets[vector] = [];
    public HashSet<ulong> RemovalsIn(int vector) => PreferenceRemovals.TryGetValue(vector, out var r) ? r : PreferenceRemovals[vector] = [];
}

/// <summary>Nations: information, national teams, squads, kits and colours, relationships, transfer preferences and agreements.</summary>
internal sealed class NationScreen(IScreenHost host)
    : EntityScreen<NationRecord, NationRow, NationDraft>(host, "nation", "Nations", "nations", "Search nation or ID...")
{
    static readonly string[] TransferValueRanges = ["50 – 549", "550 – 1,049", "1,050 – 1,549", "1,550 – 2,049", "2,050 – 2,549",
        "2,550 – 3,049", "3,050 – 3,549", "3,550 – 4,049", "4,050 – 4,549", "4,550 – 5,049",
        "5,050 – 5,549", "5,550 – 6,049", "6,050 – 6,549", "6,550 – 7,049", "7,050 – 7,549",
        "7,550 – 8,049", "8,050 – 8,549", "8,550 – 9,049", "9,050 – 9,549", "9,550 – 10,000"];
    int selectedPage;
    readonly Dictionary<ulong, int> squadTeam = [];
    IReadOnlyList<AgreementTargetOption>? agreementOrganisations;
    // What the open page was built from, for the save.
    IReadOnlyDictionary<NationField, long> snapshot = new Dictionary<NationField, long>();
    ulong continentNow;
    IReadOnlyList<ContinentRecord> continents = [];

    protected override bool HasRail => true;

    protected override void ConfigureColumns(DataGrid grid)
    {
        grid.Columns.Add(new DataGridTextColumn { Header = "Nation", Binding = new Binding(nameof(NationRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        foreach (var (header, property) in new[] { ("ID", nameof(NationRow.Id)), ("FIFA", nameof(NationRow.Fifa)), ("Youth", nameof(NationRow.Youth)), ("Economy", nameof(NationRow.Economy)) })
        {
            var column = new DataGridTextColumn { Header = header, Binding = new Binding(property), Width = new DataGridLength(header == "Economy" ? 94 : 84) };
            column.CellStyleClasses.Add("numeric");
            grid.Columns.Add(column);
        }
    }

    protected override IReadOnlyList<NationRow> LoadRows(GameSession session)
    {
        var fields = session.NationFields;
        return session.Nations.LoadAll().Select(n =>
        {
            var v = fields.ReadAll(n.Address);
            return new NationRow(n, n.DisplayName, n.Id, v.TryGetValue(NationField.FifaPosition, out var a) ? a : null,
                v.TryGetValue(NationField.YouthRating, out var y) ? y : null, v.TryGetValue(NationField.EconomicFactor, out var e) ? e : null);
        }).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    protected override bool Matches(NationRow row, string q) =>
        Fold(row.Name).Contains(q, StringComparison.Ordinal) || (row.Id?.ToString(Inv).Contains(q, StringComparison.Ordinal) ?? false);

    protected override NationRecord RecordOf(NationRow row) => row.Nation;
    protected override ulong AddressOf(NationRecord record) => record.Address;
    protected override string NameOf(NationRecord record) => record.DisplayName;

    protected override Func<ulong, int, Fm27Editor.Core.History.ChangeLabel?>? LabelsOf(NationRecord record) =>
        Fm27Editor.Core.History.EntityChangeLabels.ForNation(Host.Backend.Session.NationFields, record.Address);
    protected override NationDraft NewDraft() => new();
    protected override bool HasChanges(NationRecord n, NationDraft draft) => draft.Any || Host.KitDraftOf(n.Address) is { Count: > 0 };
    protected override NationRecord? Reload(NationRecord record) => record;
    protected override NationRow Refresh(NationRow row, NationRecord fresh) => row;
    protected override void Saved(NationRecord n) => Host.ClearKitDraft(n.Address);

    protected override void OnReconnected()
    {
        agreementOrganisations = null;
        squadTeam.Clear();
    }

    static Border RowsCard(string title, IEnumerable<(string Label, Control Control)> entries)
    {
        var stack = new StackPanel { Spacing = 0 };
        int idx = 0;
        foreach (var (label, control) in entries)
        {
            var line = new DockPanel { MinHeight = 25, LastChildFill = true };
            DockPanel.SetDock(control, Dock.Right);
            line.Children.Add(control);
            line.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = UiKit.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(new Border { Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent, Padding = new Thickness(10, 2), Child = line });
        }
        return UiKit.Card(title, stack);
    }

    static TextBox NumberBox(string text, double width = 84)
        => new() { Text = text, Width = width, MinHeight = 24, Padding = new Thickness(5, 1), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center };

    static TextBlock Heading(string text, bool right) => new()
    {
        Text = text, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = UiKit.Brush("#94A3B8"),
        HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
    };

    protected override void BuildEditor(NationRecord n, NationDraft draft, Action changed)
    {
        var session = Host.Backend.Session;
        var values = session.NationFields.ReadAll(n.Address);
        snapshot = values;
        var nations = Rows.Select(r => r.Nation).ToArray();

        Control Value(NationField f, long original)
        {
            if (f is NationField.AllowMultipleAssistantManagers or NationField.DoesNotAllowDualNationality)
            {
                var check = new CheckBox { IsChecked = (draft.Fields.TryGetValue(f, out var d) ? long.Parse(d) : original) != 0 };
                check.IsCheckedChanged += (_, _) =>
                {
                    long value = check.IsChecked == true ? 1 : 0;
                    if (value == original) draft.Fields.Remove(f); else draft.Fields[f] = value.ToString(Inv);
                    changed();
                };
                return check;
            }
            if (f is NationField.Development or NationField.GainNationalityType or NationField.Importance)
            {
                Func<long, string> name = f switch
                {
                    NationField.Development => NationLabels.DevelopmentName,
                    NationField.Importance => NationLabels.ImportanceName,
                    _ => NationLabels.GainNationalityTypeName,
                };
                long[] codes = f == NationField.Importance ? [1, 2, 3, 4] : [1, 2];
                var combo = new ComboBox { ItemsSource = codes.Select(name).ToArray(), MinWidth = 148 };
                combo.SelectedItem = name(draft.Fields.TryGetValue(f, out var d) ? long.Parse(d) : original);
                combo.SelectionChanged += (_, _) =>
                {
                    long code = combo.SelectedIndex + 1;
                    if (code == original) draft.Fields.Remove(f); else draft.Fields[f] = code.ToString(Inv);
                    changed();
                };
                return combo;
            }
            string text = original.ToString(Inv);
            var box = NumberBox(draft.Fields.GetValueOrDefault(f, text));
            box.TextChanged += (_, _) => { if (box.Text == text) draft.Fields.Remove(f); else draft.Fields[f] = box.Text ?? ""; changed(); };
            return box;
        }

        var informationColumns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var info = new[] { new StackPanel { Spacing = 12 }, new StackPanel { Spacing = 12 } };
        Grid.SetColumn(info[0], 0); Grid.SetColumn(info[1], 2);
        informationColumns.Children.Add(info[0]); informationColumns.Children.Add(info[1]);

        var teamColumns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var team = new[] { new StackPanel { Spacing = 12 }, new StackPanel { Spacing = 12 } };
        Grid.SetColumn(team[0], 0); Grid.SetColumn(team[1], 2);
        teamColumns.Children.Add(team[0]); teamColumns.Children.Add(team[1]);

        var pages = Enumerable.Range(0, 7).Select(_ => new StackPanel { Spacing = 12 }).ToArray();
        pages[0].Children.Add(informationColumns);
        pages[1].Children.Add(teamColumns);

        var links = session.NationLinks;
        continentNow = links.ContinentPointer(n.Address);
        continents = links.Continents(nations);
        var locationItems = new List<(string Label, Control Control)>();
        if (links.Capital(n.Address) is { } capital)
            locationItems.Add(("Capital", new TextBlock { Text = capital.Name, FontSize = 13, FontWeight = FontWeight.Medium, Foreground = UiKit.TextPrimary, VerticalAlignment = VerticalAlignment.Center }));
        if (links.Stadium(n.Address) is { } stadium)
            locationItems.Add(("Stadium", new TextBlock { Text = stadium.DisplayName, FontSize = 13, FontWeight = FontWeight.Medium, Foreground = UiKit.TextPrimary, VerticalAlignment = VerticalAlignment.Center }));
        if (continents.Any(c => c.Address == continentNow))
        {
            var combo = new ComboBox { ItemsSource = continents.Select(c => c.Name).ToArray(), MinWidth = 152 };
            combo.SelectedIndex = continents.ToList().FindIndex(c => c.Address == (draft.Continent ?? continentNow));
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex < 0) return;
                ulong chosen = continents[combo.SelectedIndex].Address;
                draft.Continent = chosen == continentNow ? null : chosen;
                changed();
            };
            locationItems.Add(("Continent", combo));
        }

        for (int g = 0; g < NationLabels.Groups.Length; g++)
        {
            var (title, items) = NationLabels.Groups[g];
            var cardItems = items.Where(i => values.ContainsKey(i.Field)).Select(i => (i.Label, Value(i.Field, values[i.Field]))).ToList();
            if (g == 0) cardItems.InsertRange(0, locationItems);
            if (cardItems.Count == 0) continue;
            (g == 0 ? info[0] : team[(g - 1) % 2]).Children.Add(RowsCard(title, cardItems));
        }


        KitsAndColours(n, draft, changed, pages[3]);
        Squads(n, pages[2]);
        TeamReputations(n, draft, changed, team[1]);
        RivalNations(n, draft, changed, nations, pages[4]);
        Preferences(n, draft, changed, nations, pages[5]);
        StaffPage(n, pages[6]);
        pages[0].Children.Add(UiKit.Card("Agreements", AgreementsPanel(n, draft, changed, nations)));

        var coefficients = session.NationEuroCoefficients.Read(n.Address);
        if (coefficients.Count > 0)
        {
            var season = (session.ReadGameDate()?.Year ?? 0) is var year && year > 1900 ? year : 0;
            var labels = NationEuroCoefficients.Labels(season, coefficients.Count);
            var rows = Enumerable.Range(0, coefficients.Count)
                .Select(i => (labels[i], (Control)new TextBlock { Text = coefficients[i].ToString("0.###", Inv), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = UiKit.TextPrimary, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }));
            team[0].Children.Add(RowsCard("Euro Coefficients", rows));
        }

        string[] tabNames = ["Information", "National Teams", "Squads", "Kits and Colours", "Relationships", "Transfer Preferences", "Staff"];
        var sections = tabNames.Select((name, index) => new SectionHost.Section(name, name, pages[index])).ToArray();
        Editor.Children.Add(SectionHost.Build("nation", sections, tabNames[Math.Clamp(selectedPage, 0, tabNames.Length - 1)], key => selectedPage = Array.IndexOf(tabNames, key), RailHost));

        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (n.Id is { } nid) badges.Children.Add(UiKit.MetaText($"ID {nid.ToString(Inv)}"));
        if (values.TryGetValue(NationField.FifaPosition, out var fifa) && fifa > 0)
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText($"FIFA #{fifa.ToString(Inv)}", highlight: true));
        }
        if (values.TryGetValue(NationField.YouthRating, out var youth))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText($"Youth {youth.ToString(Inv)}"));
        }
        HeaderHost.Content = UiKit.EntityBanner(n.DisplayName, badges, null, Host.Graphics.Image("nation", n.Id, 72));
    }

    // Kits and Colours
    void KitsAndColours(NationRecord n, NationDraft draft, Action changed, Panel page)
    {
        var session = Host.Backend.Session;
        if (session.NationColours.Read(n.Address) is { } colours)
        {
            string backgroundOriginal = NationColours.Format(colours.Background);
            string foregroundOriginal = NationColours.Format(colours.Foreground);
            var pending = draft.Colours ?? (null, null);
            var rows = new List<(string Label, Control Control)>();
            foreach (var (title, original, isBackground) in new[] { ("Background Colour", backgroundOriginal, true), ("Foreground Colour", foregroundOriginal, false) })
            {
                var box = new TextBox { Text = (isBackground ? pending.Item1 : pending.Item2) ?? original, Width = 106, MinHeight = 24, Padding = new Thickness(5, 1), HorizontalContentAlignment = HorizontalAlignment.Left };
                box.TextChanged += (_, _) =>
                {
                    string text = box.Text ?? "";
                    var entry = draft.Colours ?? (null, null);
                    entry = isBackground ? (text == backgroundOriginal ? null : text, entry.Item2) : (entry.Item1, text == foregroundOriginal ? null : text);
                    draft.Colours = entry.Item1 is null && entry.Item2 is null ? null : entry;
                    changed();
                };
                rows.Add((title, ColourInputControl.Create(box)));
            }
            var card = RowsCard("Colours", rows);
            card.VerticalAlignment = VerticalAlignment.Top;
            card.Margin = new Thickness(0, 0, 0, 10);
            page.Children.Add(card);
        }
        var kits = session.NationKits.Read(n.Address);
        if (kits.Count > 0) page.Children.Add(Host.BuildKits(n.Address, kits, changed));
    }

    // Squads
    void Squads(NationRecord n, Panel page)
    {
        var session = Host.Backend.Session;
        var squads = session.NationSquads.ReadNation(n.Address);
        if (squads.Count == 0) return;
        int chosen = Math.Clamp(squadTeam.GetValueOrDefault(n.Address), 0, squads.Count - 1);
        var picker = new ComboBox
        {
            Width = 186, Height = 22, MinHeight = 22, FontSize = 12.5,
            ItemsSource = Enumerable.Range(0, squads.Count).ToArray(),
            SelectedIndex = chosen,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<int>((index, _) => new TextBlock { Text = index == 0 ? n.DisplayName : $"{n.DisplayName} {index + 1}" }),
        };
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0) { squadTeam[n.Address] = picker.SelectedIndex; Render(); }
        };

        ulong teamAddress = squads[chosen].Team.Address;
        IReadOnlyList<PlayerRecord> GetSquad()
        {
            var list = session.NationSquads.Read(teamAddress);
            if (session.ReadGameDate() is { } today) foreach (var p in list) p.GameDate = today;
            return list;
        }
        page.Children.Add(UiKit.Card("Squads", new StackPanel { Spacing = 6, Children = { Host.BuildSquad(GetSquad) } }, rightHeader: picker));
    }

    // Staff: the national appointments (managers, coaches, medical); the table and its Tools act on the national contract only.
    void StaffPage(NationRecord n, Panel page)
    {
        var session = Host.Backend.Session;
        IReadOnlyList<Fm27Editor.Core.Nations.NationStaffRow> staff;
        try { staff = session.NationStaff.ReadDetails(n.Address); }
        catch (Exception ex) { Host.SetStatus(UserMessage.For(ex)); return; }
        if (staff.Count == 0) return;
        var scope = new NationStaffScope(session);
        var today = session.ReadGameDate();
        var lines = staff.Select(s => new StaffListRow(s.Staff, s.JobName, n.DisplayName + s.TeamSuffix, s.Staff.Nationality?.DisplayName ?? "",
            ClubStaffLabels.Age(s.Staff.DateOfBirth, today), s.Staff.CurrentAbility, s.Staff.PotentialAbility, scope.ExpiryText(s.Staff))).ToList();
        page.Children.Add(UiKit.Card("Staff", Host.BuildStaffTable(lines, scope, Render)));
    }
    // Teams Reputation
    void TeamReputations(NationRecord n, NationDraft draft, Action changed, Panel column)
    {
        var session = Host.Backend.Session;
        var teams = session.NationTeams.Read(n.Address);
        if (teams.Count == 0) return;
        var stack = new StackPanel { Spacing = 0 };
        int idx = 0;
        if (session.NationTeams.AllowLicensedKits(teams[0].Address) is { } kitNow)
        {
            var kits = new CheckBox { Content = "Allow Licensed Kits", IsChecked = draft.LicensedKits ?? kitNow, FontSize = 13, Foreground = UiKit.TextSecondary };
            kits.IsCheckedChanged += (_, _) =>
            {
                bool value = kits.IsChecked == true;
                draft.LicensedKits = value == kitNow ? null : value;
                changed();
            };
            stack.Children.Add(new Border { Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent, Padding = new Thickness(10, 4), Child = kits });
        }
        foreach (var team in teams)
        {
            string original = team.Reputation.ToString(Inv);
            var input = NumberBox(draft.Teams.GetValueOrDefault(team.Address, original));
            input.VerticalContentAlignment = VerticalAlignment.Top;
            input.TextChanged += (_, _) => { string t = input.Text ?? ""; if (t == original) draft.Teams.Remove(team.Address); else draft.Teams[team.Address] = t; changed(); };
            var line = new DockPanel { MinHeight = 25, LastChildFill = true };
            DockPanel.SetDock(input, Dock.Right);
            line.Children.Add(input);
            line.Children.Add(new TextBlock { Text = team.Index == 0 ? n.DisplayName : $"{n.DisplayName} {team.Index + 1}", FontSize = 13, Foreground = UiKit.TextSecondary, VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(new Border { Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent, Padding = new Thickness(10, 2), Child = line });
        }
        column.Children.Add(UiKit.Card("Teams Reputation", stack));
    }

    // Rival Nations
    void RivalNations(NationRecord n, NationDraft draft, Action changed, NationRecord[] nations, Panel page)
    {
        var rivals = Host.Backend.Session.NationRivalries.Read(n, nations);
        if (rivals.Count == 0) return;
        var rows = new List<(string Label, Control Control)>();
        foreach (var rival in rivals)
        {
            var input = NumberBox(draft.Rivalry.TextFor(rival));
            input.VerticalContentAlignment = VerticalAlignment.Top;
            input.TextChanged += (_, _) => { draft.Rivalry.Set(rival, input.Text ?? ""); changed(); };
            rows.Add((rival.Opponent, input));
        }
        var card = RowsCard("Rival Nations", rows);
        card.MaxWidth = 540;
        card.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(card);
    }

    // Transfer Preferences and Continents
    void Preferences(NationRecord n, NationDraft draft, Action changed, NationRecord[] nations, Panel page)
    {
        var session = Host.Backend.Session;
        draft.TransferValuesSnapshot ??= session.ReadNationTransferValues(n);
        Control? transferCard = null;
        if (draft.TransferValuesSnapshot is { } transferValues)
        {
            var fields = new List<(string, Control)> { ("Reputation", Heading("Transfer Value", true)) };
            fields.AddRange(Enumerable.Range(0, NationTransferValuesReader.Count).Select(i =>
                (TransferValueRanges[i], (Control)FieldBox(draft.TransferValues, i, transferValues.Values[i].ToString(Inv), 128, changed))));
            transferCard = CardRows("Transfer Values", fields);
        }
        int nationVector = session.Profile.Nation?.Preferences?.Vector ?? 0xF0;
        var targets = draft.TargetsIn(nationVector);
        var removals = draft.RemovalsIn(nationVector);
        var preferences = session.NationPreferences.Read(n, nations).Where(p => !removals.Contains(p.Address)).ToArray();
        {
            var options = Rows.ToArray();
            var list = new Grid { ColumnDefinitions = new ColumnDefinitions("*,64,64,64,64,28"), RowSpacing = 4, ColumnSpacing = 4, Margin = new Thickness(12, 8) };
            list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            foreach (var (text, i) in new[] { "Nation", "Rating", "Scouting", "Min age", "Max age" }.Select((text, i) => (text, i)))
            {
                var label = Heading(text, i >= 1);
                Grid.SetColumn(label, i);
                list.Children.Add(label);
            }
            foreach (var preference in preferences)
            {
                int row = list.RowDefinitions.Count;
                list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var pick = new ComboBox
                {
                    ItemsSource = options, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 24, FontSize = 13,
                    ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<NationRow>((entry, _) => new TextBlock { Text = entry is null ? "" : entry.Name }),
                    SelectedItem = options.FirstOrDefault(o => session.NationPreferences.TargetFor(o.Nation) == (targets.TryGetValue(preference.Address, out ulong staged) ? staged : preference.TargetTeam)),
                };
                pick.SelectionChanged += (_, _) =>
                {
                    if (pick.SelectedItem is not NationRow chosen) return;
                    ulong chosenTeam = session.NationPreferences.TargetFor(chosen.Nation);
                    if (chosenTeam == preference.TargetTeam) targets.Remove(preference.Address); else targets[preference.Address] = chosenTeam;
                    changed();
                };
                Grid.SetRow(pick, row);
                list.Children.Add(pick);
                var texts = draft.Preference.TextFor(preference);
                for (int i = 0; i < 4; i++)
                {
                    int column = i;
                    var input = new TextBox { Text = texts[i], MinHeight = 24, Padding = new Thickness(4, 1), HorizontalContentAlignment = HorizontalAlignment.Right };
                    input.TextChanged += (_, _) => { draft.Preference.Set(preference, column, input.Text ?? ""); changed(); };
                    Grid.SetRow(input, row); Grid.SetColumn(input, i + 1); list.Children.Add(input);
                }
                var remove = new Button { Content = "−", Width = 26, Padding = new Thickness(0) };
                remove.Click += (_, _) => { removals.Add(preference.Address); Render(); };
                Grid.SetRow(remove, row); Grid.SetColumn(remove, 5); list.Children.Add(remove);
            }
            var add = new Button { Content = "+", Width = 24, Height = 21, MinHeight = 21, Padding = new Thickness(0) };
            add.Click += (_, _) => { draft.PreferenceAdds[nationVector] = draft.PreferenceAdds.GetValueOrDefault(nationVector) + 1; Render(); };
            var preferenceCard = UiKit.Card("Transfer Preferences", list, rightHeader: add);
            page.Children.Add(transferCard is null ? preferenceCard : TwoColumns(preferenceCard, transferCard));
        }

        int continentVector = NationPreferences.ContinentsVector;
        var continentTargets = draft.TargetsIn(continentVector);
        var continentRemovals = draft.RemovalsIn(continentVector);
        var continentPreferences = session.NationPreferences.ReadContinents(n, continents).Where(p => !continentRemovals.Contains(p.Address)).ToArray();
        {
            var list = new Grid { ColumnDefinitions = new ColumnDefinitions("*,64,64,64,28"), RowSpacing = 4, ColumnSpacing = 4, Margin = new Thickness(12, 8) };
            list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            foreach (var (text, i) in new[] { "Continent", "Rating", "Min age", "Max age" }.Select((text, i) => (text, i)))
            {
                var label = Heading(text, i >= 1);
                Grid.SetColumn(label, i);
                list.Children.Add(label);
            }
            foreach (var preference in continentPreferences)
            {
                int row = list.RowDefinitions.Count;
                list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var pick = new ComboBox
                {
                    ItemsSource = continents, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 24, FontSize = 13,
                    ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ContinentRecord>((entry, _) => new TextBlock { Text = entry is null ? "" : entry.Name }),
                    SelectedItem = continents.FirstOrDefault(c => c.Address == (continentTargets.TryGetValue(preference.Address, out ulong staged) ? staged : preference.TargetTeam)),
                };
                pick.SelectionChanged += (_, _) =>
                {
                    if (pick.SelectedItem is not ContinentRecord chosen) return;
                    if (chosen.Address == preference.TargetTeam) continentTargets.Remove(preference.Address); else continentTargets[preference.Address] = chosen.Address;
                    changed();
                };
                Grid.SetRow(pick, row);
                list.Children.Add(pick);
                var texts = draft.Preference.TextFor(preference);
                // Rating, Min age and Max age sit in grid columns 1..3; the draft keeps them at 0, 2 and 3.
                foreach (var (index, gridColumn) in new[] { (0, 1), (2, 2), (3, 3) })
                {
                    var input = new TextBox { Text = texts[index], MinHeight = 24, Padding = new Thickness(4, 1), HorizontalContentAlignment = HorizontalAlignment.Right };
                    input.TextChanged += (_, _) => { draft.Preference.Set(preference, index, input.Text ?? ""); changed(); };
                    Grid.SetRow(input, row); Grid.SetColumn(input, gridColumn); list.Children.Add(input);
                }
                var remove = new Button { Content = "−", Width = 26, Padding = new Thickness(0) };
                remove.Click += (_, _) => { continentRemovals.Add(preference.Address); Render(); };
                Grid.SetRow(remove, row); Grid.SetColumn(remove, 4); list.Children.Add(remove);
            }
            var add = new Button { Content = "+", Width = 24, Height = 21, MinHeight = 21, Padding = new Thickness(0) };
            add.Click += (_, _) => { draft.PreferenceAdds[continentVector] = draft.PreferenceAdds.GetValueOrDefault(continentVector) + 1; Render(); };
            page.Children.Add(UiKit.Card("Continents", list, rightHeader: add));
        }
    }

    // Agreements
    static List<AgreementTypeOption> TypeOptions(bool withRegion, AgreementKind current)
    {
        var types = new List<AgreementTypeOption> { new(AgreementKind.Nation, "Nation"), new(AgreementKind.ExcludedNation, "Excluded Nation"), new(AgreementKind.Continent, "Continent"), new(AgreementKind.Agreement, "Agreement") };
        if (withRegion || current == AgreementKind.Region) types.Add(new(AgreementKind.Region, "Region"));
        if (current == AgreementKind.Unknown) types.Add(new(AgreementKind.Unknown, "?"));
        return types;
    }

    Control AgreementsPanel(NationRecord n, NationDraft draft, Action changed, NationRecord[] rowNations)
    {
        var session = Host.Backend.Session;
        var panel = new StackPanel { Spacing = 0 };
        var allNations = rowNations.Length > 0 ? rowNations : Host.NationCatalogue.ToArray();
        var nationOptions = allNations.Select(x => new AgreementTargetOption(x.Address, x.DisplayName)).OrderBy(x => x.Name).ToArray();
        var continentOptions = continents.Select(x => new AgreementTargetOption(x.Address, x.Name)).ToArray();
        var regionOptions = session.NationAgreements.Regions().Select(x => new AgreementTargetOption(x.Address, x.Name)).ToArray();
        if (agreementOrganisations is null || agreementOrganisations.Count == 0)
        {
            var found = new Dictionary<ulong, AgreementTargetOption>();
            foreach (var nation in allNations)
                foreach (int source in new[] { NationAgreements.EuVector, NationAgreements.NonForeignVector })
                {
                    try
                    {
                        foreach (var item in session.NationAgreements.Read(nation.Address, source, continents, allNations))
                            if (item.Kind == AgreementKind.Agreement && !string.IsNullOrWhiteSpace(item.TargetName))
                                found.TryAdd(item.Target, new(item.Target, item.TargetName));
                    }
                    catch (Fm27Editor.Core.Memory.MemoryAccessException) { }
                }
            agreementOrganisations = found.Values.OrderBy(x => x.Name).ToArray();
        }

        int groupIndex = 0;
        foreach (var (title, vector) in new[] { ("NATIONS TREATED AS EU", NationAgreements.EuVector), ("NATIONS TREATED AS NON-FOREIGN", NationAgreements.NonForeignVector) })
        {
            var rows = session.NationAgreements.Read(n.Address, vector, continents, allNations);
            var list = new Grid { ColumnDefinitions = new ColumnDefinitions("122,*,96,96,28"), RowSpacing = 4, ColumnSpacing = 4, Margin = new Thickness(12, 6, 12, 8) };
            var add = new Button { Content = "+", Width = 24, Height = 21, MinHeight = 21, Padding = new Thickness(0) };
            add.Click += (_, _) =>
            {
                var source = rows.FirstOrDefault();
                ulong sourceNation = n.Address;
                byte[]? emptyHeader = null;
                if (source is null)
                {
                    if (vector != NationAgreements.NonForeignVector) { Host.SetStatus("Agreements: this list has no row to copy"); return; }
                    if (draft.AgreementAdds.TryGetValue(vector, out var existing) && existing.Count > 0)
                    {
                        source = existing[0].Source;
                        sourceNation = existing[0].SourceNation;
                        emptyHeader = existing[0].EmptyHeader;
                    }
                    else
                    {
                        foreach (var other in allNations.Where(candidate => candidate.Address != n.Address))
                        {
                            var template = session.NationAgreements.Read(other.Address, vector, continents, allNations).FirstOrDefault(candidate => candidate.Kind == AgreementKind.Nation);
                            if (template is null) continue;
                            source = template;
                            sourceNation = other.Address;
                            break;
                        }
                        if (source is null) { Host.SetStatus("Agreements: no Non-Foreign template is available"); return; }
                        emptyHeader = session.Memory.Read(n.Address + (ulong)vector, 24);
                    }
                }
                if (!draft.AgreementAdds.TryGetValue(vector, out var pending)) draft.AgreementAdds[vector] = pending = [];
                pending.Add(new AgreementAddition(source, sourceNation, emptyHeader));
                Render();
            };
            var subHeader = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(add, Dock.Right);
            subHeader.Children.Add(add);
            subHeader.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = UiKit.Brush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(new Border
            {
                Background = UiKit.Brush("#161A26"), BorderBrush = UiKit.BorderSubtle,
                BorderThickness = new Thickness(0, groupIndex++ > 0 ? 1 : 0, 0, 1), Padding = new Thickness(12, 4), Child = subHeader,
            });
            list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            foreach (var (text, i) in new[] { "Type", "Target", "From", "Until" }.Select((text, i) => (text, i)))
            {
                var label = new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = UiKit.Brush("#94A3B8") };
                Grid.SetColumn(label, i);
                list.Children.Add(label);
            }

            foreach (var row in rows)
            {
                if (draft.AgreementRemovals.ContainsKey(row.Address)) continue;
                int index = list.RowDefinitions.Count;
                list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var types = TypeOptions(regionOptions.Length > 0, row.Kind);
                var pendingTarget = draft.AgreementTargets.GetValueOrDefault(row.Address);
                var chosenKind = pendingTarget.Row is null ? row.Kind : pendingTarget.Kind;
                ulong chosenTarget = pendingTarget.Row is null ? row.Target : pendingTarget.Target;
                var typeBox = new ComboBox { ItemsSource = types, SelectedItem = types.First(x => x.Kind == chosenKind), MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Stretch };
                var targetBox = new ComboBox { MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Stretch };
                IReadOnlyList<AgreementTargetOption> Choices(AgreementKind kind)
                {
                    var baseChoices = kind switch
                    {
                        AgreementKind.Nation or AgreementKind.ExcludedNation => nationOptions,
                        AgreementKind.Continent => continentOptions,
                        AgreementKind.Agreement => agreementOrganisations ?? [],
                        AgreementKind.Region => regionOptions.Length > 0 ? regionOptions : (row.Kind == AgreementKind.Region && row.Target != 0 ? [new(row.Target, row.TargetName ?? "")] : []),
                        _ => row.Target == 0 ? [] : [new(row.Target, row.TargetName ?? $"0x{row.Target:X}")],
                    };
                    if (kind == row.Kind && row.Target != 0 && !baseChoices.Any(x => x.Address == row.Target))
                    {
                        string name = !string.IsNullOrWhiteSpace(row.TargetName) ? row.TargetName : $"0x{row.Target:X}";
                        return baseChoices.Concat([new AgreementTargetOption(row.Target, name)]).ToArray();
                    }
                    return baseChoices;
                }
                void SetChoices(AgreementKind kind, ulong target)
                {
                    var choices = Choices(kind);
                    if (target != 0 && !choices.Any(x => x.Address == target))
                    {
                        string name = target == row.Target && !string.IsNullOrWhiteSpace(row.TargetName) ? row.TargetName : $"0x{target:X}";
                        choices = choices.Concat([new AgreementTargetOption(target, name)]).ToArray();
                    }
                    targetBox.ItemsSource = choices;
                    targetBox.SelectedItem = choices.FirstOrDefault(x => x.Address == target) ?? choices.FirstOrDefault();
                }
                SetChoices(chosenKind, chosenTarget);
                void TargetChanged()
                {
                    if (typeBox.SelectedItem is not AgreementTypeOption type || targetBox.SelectedItem is not AgreementTargetOption target) return;
                    if (type.Kind == row.Kind && target.Address == row.Target) draft.AgreementTargets.Remove(row.Address);
                    else draft.AgreementTargets[row.Address] = (row, type.Kind, target.Address);
                    changed();
                }
                typeBox.SelectionChanged += (_, _) => { if (typeBox.SelectedItem is AgreementTypeOption type) { SetChoices(type.Kind, 0); TargetChanged(); } };
                targetBox.SelectionChanged += (_, _) => TargetChanged();
                string fromOriginal = DateSetting.Format(row.From);
                string untilOriginal = DateSetting.Format(row.Until);
                var pendingDates = draft.AgreementDates.GetValueOrDefault(row.Address);
                var fromBox = new DateBox { Text = pendingDates.Row is null ? fromOriginal : pendingDates.From, Watermark = "No limit", MinHeight = 24 };
                var untilBox = new DateBox { Text = pendingDates.Row is null ? untilOriginal : pendingDates.Until, Watermark = "No limit", MinHeight = 24 };
                void DatesChanged()
                {
                    string from = fromBox.Text ?? "", until = untilBox.Text ?? "";
                    if (from == fromOriginal && until == untilOriginal) draft.AgreementDates.Remove(row.Address); else draft.AgreementDates[row.Address] = (row, from, until);
                    changed();
                }
                fromBox.TextChanged += (_, _) => DatesChanged();
                untilBox.TextChanged += (_, _) => DatesChanged();
                Control[] cells = [typeBox, targetBox, fromBox, untilBox];
                for (int i = 0; i < cells.Length; i++) { Grid.SetRow(cells[i], index); Grid.SetColumn(cells[i], i); list.Children.Add(cells[i]); }
                var remove = new Button { Content = "−", Width = 26, Padding = new Thickness(0) };
                remove.Click += (_, _) =>
                {
                    draft.AgreementDates.Remove(row.Address);
                    draft.AgreementTargets.Remove(row.Address);
                    draft.AgreementRemovals[row.Address] = row;
                    Render();
                };
                Grid.SetRow(remove, index); Grid.SetColumn(remove, 4); list.Children.Add(remove);
            }

            if (draft.AgreementAdds.TryGetValue(vector, out var additions))
            {
                foreach (var addition in additions.ToArray())
                {
                    int index = list.RowDefinitions.Count;
                    list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    var kinds = TypeOptions(regionOptions.Length > 0, addition.Kind);
                    var type = new ComboBox { ItemsSource = kinds, SelectedItem = kinds.First(x => x.Kind == addition.Kind), MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Stretch };
                    var target = new ComboBox { MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Stretch };
                    void SelectTargets(AgreementKind kind, ulong selected)
                    {
                        var options = kind switch
                        {
                            AgreementKind.Nation or AgreementKind.ExcludedNation => nationOptions,
                            AgreementKind.Continent => continentOptions,
                            AgreementKind.Agreement => agreementOrganisations ?? [],
                            AgreementKind.Region => regionOptions.Length > 0 ? regionOptions : addition.Source.Kind == AgreementKind.Region && addition.Source.Target != 0 ? [new(addition.Source.Target, addition.Source.TargetName ?? "")] : [],
                            _ => addition.Source.Target == 0 ? [] : [new(addition.Source.Target, addition.Source.TargetName ?? "")],
                        };
                        if (selected != 0 && !options.Any(x => x.Address == selected))
                        {
                            string name = selected == addition.Source.Target && !string.IsNullOrWhiteSpace(addition.Source.TargetName) ? addition.Source.TargetName : $"0x{selected:X}";
                            options = options.Concat([new AgreementTargetOption(selected, name)]).ToArray();
                        }
                        target.ItemsSource = options;
                        target.SelectedItem = options.FirstOrDefault(x => x.Address == selected) ?? options.FirstOrDefault();
                    }
                    SelectTargets(addition.Kind, addition.Target);
                    type.SelectionChanged += (_, _) => { if (type.SelectedItem is AgreementTypeOption chosen) { addition.Kind = chosen.Kind; SelectTargets(chosen.Kind, 0); if (target.SelectedItem is AgreementTargetOption item) addition.Target = item.Address; } };
                    target.SelectionChanged += (_, _) => { if (target.SelectedItem is AgreementTargetOption chosen) addition.Target = chosen.Address; };
                    var from = new DateBox { Text = addition.From, Watermark = "No limit", MinHeight = 24 };
                    var until = new DateBox { Text = addition.Until, Watermark = "No limit", MinHeight = 24 };
                    from.TextChanged += (_, _) => addition.From = from.Text ?? "";
                    until.TextChanged += (_, _) => addition.Until = until.Text ?? "";
                    Control[] cells = [type, target, from, until];
                    for (int column = 0; column < cells.Length; column++) { Grid.SetRow(cells[column], index); Grid.SetColumn(cells[column], column); list.Children.Add(cells[column]); }
                    var cancel = new Button { Content = "−", Width = 26, Padding = new Thickness(0) };
                    cancel.Click += (_, _) => { var pending = draft.AgreementAdds[vector]; pending.Remove(addition); if (pending.Count == 0) draft.AgreementAdds.Remove(vector); Render(); };
                    Grid.SetRow(cancel, index); Grid.SetColumn(cancel, 4); list.Children.Add(cancel);
                }
            }
            panel.Children.Add(list);
        }
        return panel;
    }

    // Saving
    static bool TryParseDate(string text, out DateOnly? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!DateSetting.TryParse(text.Trim(), out var value) || value.Year <= 1900) return false;
        result = value;
        return true;
    }

    static int AgreementTargetOffset(AgreementKind kind) => kind switch
    {
        AgreementKind.Nation => 0, AgreementKind.Continent => 8, AgreementKind.ExcludedNation => 16, AgreementKind.Region => 24, AgreementKind.Agreement => 32,
        _ => throw new InvalidOperationException("Unsupported agreement type"),
    };

    protected override string? Validate(NationRecord n, NationDraft draft, out Func<int> write)
    {
        write = () => 0;
        var session = Host.Backend.Session;
        if (!NationLabels.TryParse(draft.Fields, out var fieldValues, out var error)) return error;
        if (!draft.Rivalry.TryParse(out var rivals)) return "Rivalry: enter a whole number 0–100";
        if (!draft.Preference.TryParse(out var preferences)) return "Transfer preferences: check values and age range";
        var transferValues = new Dictionary<int, int>();
        foreach (var (index, text) in draft.TransferValues)
        {
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, Inv, out int value) || value < 0 || value > NationTransferValuesWriter.MaxValue)
                return $"Transfer value {TransferValueRanges[index]}: enter a whole number 0 to {NationTransferValuesWriter.MaxValue}";
            transferValues[index] = value;
        }
        var transferSnapshot = draft.TransferValuesSnapshot;
        if (transferValues.Count > 0 && transferSnapshot is null) return "Transfer values are unavailable";
        var dates = new List<(NationAgreementRow Row, DateOnly? From, DateOnly? Until)>();
        foreach (var (_, entry) in draft.AgreementDates)
        {
            if (!TryParseDate(entry.From, out var from) || !TryParseDate(entry.Until, out var until)) return $"Agreement dates: use {DateSetting.Hint} or leave blank";
            dates.Add((entry.Row, from, until));
        }
        var teamValues = new Dictionary<ulong, int>();
        foreach (var (address, text) in draft.Teams)
        {
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, Inv, out int rep) || rep < 0 || rep > NationTeamReader.MaxReputation)
                return $"Team reputation: enter a whole number 0 to {NationTeamReader.MaxReputation}";
            teamValues[address] = rep;
        }
        var targetChanges = draft.AgreementTargets.ToDictionary(kv => kv.Key, kv => kv.Value);
        var adds = draft.AgreementAdds.SelectMany(kv => kv.Value).ToArray();
        if (adds.GroupBy(addition => addition.Source.Vector).Any(group => (group.First().EmptyHeader is null ? (group.First().Source.End - group.First().Source.Begin) / 8 : 0) + (ulong)group.Count() > 64))
            return "Too many agreement rows.";
        var addDates = new Dictionary<AgreementAddition, (DateOnly? From, DateOnly? Until)>();
        foreach (var addition in adds)
        {
            if (!TryParseDate(addition.From, out var from) || !TryParseDate(addition.Until, out var until)) return $"Agreement dates: use {DateSetting.Hint} or leave blank";
            addDates[addition] = (from, until);
        }

        var removals = draft.AgreementRemovals.Values.ToArray();
        var before = snapshot;
        var continentList = continents;
        ulong continentBefore = continentNow;
        var rowNations = Rows.Select(x => x.Nation).ToArray();

        write = () =>
        {
            int kitChanges = 0, colourChanges = 0, preferenceStructure = 0, kitColourChanges = 0;
            if (transferValues.Count > 0 && transferSnapshot is { } originalValues)
                session.SetNationTransferValues(n, originalValues, transferValues);
            var fieldAccess = session.NationFields;
            var agreements = session.NationAgreements;
            foreach (var addition in adds)
            {
                var source = addition.Source;
                var current = agreements.Read(addition.SourceNation, source.Vector, continentList).FirstOrDefault(x => x.Address == source.Address);
                if (current is null || current.Begin != source.Begin || current.End != source.End || current.Capacity != source.Capacity || current.Slot != source.Slot || !current.Snapshot.AsSpan().SequenceEqual(source.Snapshot))
                    throw new StaleObjectException("Agreement source changed since it was selected for duplication");
                if (addition.EmptyHeader is { } empty && !session.Memory.Read(n.Address + (ulong)source.Vector, 24).AsSpan().SequenceEqual(empty))
                    throw new StaleObjectException("The empty agreement list changed since it was read");
            }
            foreach (var removal in removals)
            {
                var current = agreements.Read(n.Address, removal.Vector, continentList).FirstOrDefault(x => x.Address == removal.Address);
                if (current is null || current.Begin != removal.Begin || current.End != removal.End || current.Capacity != removal.Capacity || current.Slot != removal.Slot || !current.Snapshot.AsSpan().SequenceEqual(removal.Snapshot))
                    throw new StaleObjectException("Agreement row changed since it was selected for removal");
            }
            foreach (var change in targetChanges.Values) agreements.SetTarget(n.Address, change.Row, change.Kind, change.Target);
            foreach (var date in dates)
            {
                var row = date.Row;
                if (targetChanges.TryGetValue(row.Address, out var change))
                {
                    var current = agreements.Read(n.Address, row.Vector, continentList, rowNations).Single(x => x.Address == row.Address);
                    byte[] expected = (byte[])row.Snapshot.Clone();
                    Array.Clear(expected, 0, 0x28);
                    BitConverter.GetBytes(change.Target).CopyTo(expected, AgreementTargetOffset(change.Kind));
                    if (current.Begin != row.Begin || current.End != row.End || current.Capacity != row.Capacity || current.Slot != row.Slot || !current.Snapshot.AsSpan().SequenceEqual(expected))
                        throw new StaleObjectException("Agreement changed while saving");
                    row = current;
                }
                agreements.SetDates(n.Address, row, date.From, date.Until);
            }
            foreach (var addition in adds)
            {
                byte[] expected = (byte[])addition.Source.Snapshot.Clone();
                if (targetChanges.TryGetValue(addition.Source.Address, out var changedTarget))
                {
                    Array.Clear(expected, 0, 0x28);
                    BitConverter.GetBytes(changedTarget.Target).CopyTo(expected, AgreementTargetOffset(changedTarget.Kind));
                }
                var changedDate = dates.FirstOrDefault(date => date.Row.Address == addition.Source.Address);
                if (changedDate.Row is not null)
                {
                    void EncodeDate(int offset, DateOnly? value)
                    {
                        var date = value ?? new DateOnly(1900, 1, 1);
                        ushort day = (ushort)((BitConverter.ToUInt16(expected, offset) & 0xFE00) | date.DayOfYear);
                        BitConverter.GetBytes(day).CopyTo(expected, offset);
                        BitConverter.GetBytes((short)date.Year).CopyTo(expected, offset + 2);
                    }
                    EncodeDate(NationAgreements.FromOffset, changedDate.From);
                    EncodeDate(NationAgreements.UntilOffset, changedDate.Until);
                }
                var source = agreements.Read(addition.SourceNation, addition.Source.Vector, continentList).FirstOrDefault(x => x.Address == addition.Source.Address);
                if (source is null || !source.Snapshot.AsSpan().SequenceEqual(expected))
                    throw new StaleObjectException("Agreement source changed since it was selected for duplication");
                var allocate = session.Memory is Fm27Editor.Core.Memory.WindowsProcessMemory wpm ? wpm.Allocate : (Func<int, ulong>?)null;
                NationAgreementRow? added;
                if (addition.EmptyHeader is { } empty)
                {
                    var currentRows = agreements.Read(n.Address, addition.Source.Vector, continentList);
                    added = currentRows.Count == 0
                        ? agreements.AddToEmpty(n.Address, empty, addition.SourceNation, source, allocate, continentList)
                        : agreements.Duplicate(n.Address, currentRows[0], allocate, continentList);
                }
                else added = agreements.Duplicate(n.Address, source, allocate, continentList);
                if (added is null) throw new Fm27Editor.Core.Memory.MemoryAccessException("Could not read the new agreement");
                if (added.Kind != addition.Kind || added.Target != addition.Target)
                {
                    agreements.SetTarget(n.Address, added, addition.Kind, addition.Target);
                    added = agreements.Read(n.Address, addition.Source.Vector, continentList).FirstOrDefault(x => x.Address == added.Address)
                        ?? throw new StaleObjectException("The new agreement changed while saving");
                }
                var addDate = addDates[addition];
                if (added.From != addDate.From || added.Until != addDate.Until) agreements.SetDates(n.Address, added, addDate.From, addDate.Until);
            }
            foreach (var removal in removals)
            {
                var current = agreements.Read(n.Address, removal.Vector, continentList).FirstOrDefault(x => x.Address == removal.Address)
                    ?? throw new StaleObjectException("Agreement row changed since it was selected for removal");
                if (!current.Snapshot.AsSpan().SequenceEqual(removal.Snapshot)) throw new StaleObjectException("Agreement row changed since it was selected for removal");
                agreements.Remove(n.Address, current);
            }

            foreach (var (f, value) in fieldValues) fieldAccess.Set(n.Address, f, before[f], value);
            foreach (var (rival, value) in rivals) session.NationRivalries.SetValue(rival, value);
            foreach (var p in preferences)
            {
                bool isContinent = continentList.Any(c => c.Address == p.Row.TargetTeam);
                if (isContinent) session.NationPreferences.SetContinent(p.Row, p.Rating, p.MinAge, p.MaxAge, p.Knowledge);
                else session.NationPreferences.Set(p.Row, p.Rating, p.MinAge, p.MaxAge, p.Knowledge);
            }
            int nationVector = session.Profile.Nation?.Preferences?.Vector ?? 0xF0;
            foreach (int vector in new[] { nationVector, NationPreferences.ContinentsVector })
            {
                bool continentsVector = vector == NationPreferences.ContinentsVector;
                IReadOnlyList<NationPreference> PreferenceRows() => continentsVector
                    ? session.NationPreferences.ReadContinents(n, continentList)
                    : session.NationPreferences.Read(n, session.Nations.LoadAll());
                if (draft.PreferenceTargets.TryGetValue(vector, out var targets))
                    foreach (var (address, target) in targets.ToArray())
                    {
                        var row = PreferenceRows().FirstOrDefault(x => x.Address == address) ?? throw new StaleObjectException("Preference row changed");
                        session.NationPreferences.SetTarget(row, vector, target);
                        preferenceStructure++;
                    }
                if (draft.PreferenceRemovals.TryGetValue(vector, out var removed))
                    foreach (var address in removed.ToArray())
                    {
                        var row = PreferenceRows().FirstOrDefault(x => x.Address == address) ?? throw new StaleObjectException("Preference row changed");
                        session.NationPreferences.Remove(row, vector);
                        preferenceStructure++;
                    }
                if (draft.PreferenceAdds.TryGetValue(vector, out int added) && added > 0)
                {
                    var rows = PreferenceRows();
                    ulong team = rows.Count > 0 ? rows[0].Team : session.NationPreferences.TargetFor(n);
                    ulong target = rows.Count > 0 ? rows[0].TargetTeam
                        : continentsVector ? (continentList.Count > 0 ? continentList[0].Address : 0)
                        : session.NationPreferences.TargetFor(session.Nations.LoadAll().First(x => x.Address != n.Address));
                    if (target != 0)
                        for (int i = 0; i < added; i++)
                        {
                            sbyte rating = rows.Count > 0 ? rows[0].Rating : (sbyte)-1;
                            byte minAge = rows.Count > 0 ? rows[0].MinAge : (byte)14;
                            byte maxAge = rows.Count > 0 ? rows[0].MaxAge : (byte)45;
                            byte knowledge = rows.Count > 0 ? rows[0].Knowledge : (byte)0;
                            session.NationPreferences.Add(team, vector, target, rating, minAge, maxAge, knowledge, session.Memory is Fm27Editor.Core.Memory.WindowsProcessMemory wpm ? wpm.Allocate : null);
                            preferenceStructure++;
                        }
                }
            }
            if (draft.Continent is { } chosenContinent)
                using (Fm27Editor.Core.History.ChangeContext.Describe("Continent"))
                    session.NationLinks.SetContinent(n, continentBefore, continentList.First(c => c.Address == chosenContinent));
            var teamReader = session.NationTeams;
            using (Fm27Editor.Core.History.ChangeContext.Describe("Team Reputation"))
                foreach (var team in teamReader.Read(n.Address).Where(t => teamValues.ContainsKey(t.Address))) teamReader.SetReputation(team, teamValues[team.Address]);
            if (draft.Colours is { } colourDraft && session.NationColours.Read(n.Address) is { } colourNow)
            {
                if (colourDraft.Background is { } backgroundText)
                {
                    if (!NationColours.TryParse(backgroundText, out uint bg)) throw new InvalidOperationException("Background colour must be #RRGGBB");
                    session.NationColours.Set(n.Address, colourNow, true, bg);
                    colourChanges++;
                }
                if (colourDraft.Foreground is { } foregroundText)
                {
                    if (!NationColours.TryParse(foregroundText, out uint fg)) throw new InvalidOperationException("Foreground colour must be #RRGGBB");
                    session.NationColours.Set(n.Address, colourNow, false, fg);
                    colourChanges++;
                }
            }
            if (Host.KitDraftOf(n.Address) is { Count: > 0 } kitDrafts)
            {
                var kitNow = session.NationKits.Read(n.Address);
                foreach (var (slot, kit) in kitDrafts.ToArray())
                {
                    var item = kitNow.FirstOrDefault(x => x.Slot == slot) ?? throw new StaleObjectException("Kit item changed");
                    foreach (var (index, hex) in kit.Colours)
                    {
                        string clean = hex.Trim().TrimStart('#');
                        uint rgb;
                        if (clean.Length == 8 && uint.TryParse(clean, NumberStyles.HexNumber, Inv, out uint argb)) rgb = argb & 0x00FFFFFFu;
                        else if (clean.Length == 6 && uint.TryParse(clean, NumberStyles.HexNumber, Inv, out uint rawRgb)) rgb = rawRgb;
                        else if (NationColours.TryParse(hex, out uint nc)) rgb = nc;
                        else throw new InvalidOperationException($"Invalid colour format '{hex}'");
                        session.NationKits.SetColour(item, index, rgb);
                        kitColourChanges++;
                    }
                    if (kit.Style is { } styleText)
                    {
                        if (!int.TryParse(styleText, out int style)) throw new InvalidOperationException("Kit style must be a whole number");
                        session.NationKits.SetStyle(item, style);
                        kitColourChanges++;
                    }
                }
            }
            if (draft.LicensedKits is { } kitsWanted && teamReader.Read(n.Address).FirstOrDefault() is { } seniorTeam)
            {
                teamReader.SetAllowLicensedKits(seniorTeam, kitsWanted);
                kitChanges = 1;
            }
            return fieldValues.Count + rivals.Count + preferences.Count + transferValues.Count + (draft.Continent is null ? 0 : 1) + teamValues.Count + kitChanges + colourChanges + preferenceStructure + kitColourChanges;
        };
        return null;
    }
}

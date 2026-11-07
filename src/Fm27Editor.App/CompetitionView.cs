using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Game;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    public sealed record CompetitionNavItem
    {
        public string Title => "Competitions";
        public override string ToString() => Title;
    }

    public sealed record CompetitionRow(CompetitionRecord Competition, string Name, int Reputation, int Id);

    readonly CompetitionNavItem competitionNavItem = new();
    readonly Grid competitionViewRoot = new() { IsVisible = false };
    readonly TextBox competitionSearch = new() { Watermark = "Search competition or ID..." };
    readonly DataGrid competitionGrid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserResizeColumns = true, CanUserSortColumns = true,
        SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Brush("#1D2130"), RowHeight = 28, Background = Brush("#11131C"), BorderThickness = new Thickness(0),
    };
    readonly ContentControl competitionHeaderHost = new() { Margin = new Thickness(12, 9, 12, 8) };
    readonly ContentControl competitionRailHost = new() { Width = 168, Margin = new Thickness(12, 0, 0, 10) };
    readonly StackPanel competitionEditor = new() { Spacing = 10, Margin = new Thickness(0, 0, 12, 12) };
    string competitionSectionKey = "Overview";

    /// <summary>Text the user typed per competition; null fields keep the value read from the game.</summary>
    sealed record CompetitionDraft(string? Reputation, string? SubstitutionRules, string? Foreground, string? Background,
        string? Trim, string? WinterBall, string? WinterBallStart, string? WinterBallEnd,
        string? PrizeWin = null, string? PrizeDraw = null, string? PrizeDefeat = null,
        IReadOnlyDictionary<int, string>? Positions = null, string? MaximumSquad = null, string? Homegrown = null,
        IReadOnlyDictionary<(int ClubId, CompetitionStandingField Field), string>? StandingPoints = null,
        byte? VarType = null, bool? VarTvOnly = null, bool? VarEnabled = null, string? VarFromDate = null, string? HomegrownClub = null,
        IReadOnlyDictionary<int, string>? CommonRules = null, bool? ClassificationEnabled = null)
    {
        public bool Any => new[] { Reputation, SubstitutionRules, Foreground, Background, Trim, WinterBall, WinterBallStart, WinterBallEnd, PrizeWin, PrizeDraw, PrizeDefeat }
            .Any(v => v is not null) || Positions is { Count: > 0 } || MaximumSquad is not null || Homegrown is not null || StandingPoints is { Count: > 0 } || VarType is not null || VarTvOnly is not null || VarEnabled is not null || VarFromDate is not null || HomegrownClub is not null || CommonRules is { Count: > 0 } || ClassificationEnabled is not null;
    }
    static readonly (byte Value, string Label)[] KnownSubstitutionRules =
    [
        (CompetitionWriter.SubstitutionRulesNone, "Do Not Override Sub Rules"),
        (CompetitionWriter.SubstitutionRulesFiveFromNine, "5 from 9"),
    ];
    readonly Dictionary<ulong, CompetitionDraft> competitionDrafts = [];
    readonly Dictionary<ulong, CompetitionPrizeMoney> competitionPrizeSnapshots = [];
    readonly Dictionary<ulong, CompetitionClassificationPrizes> competitionClassificationSnapshots = [];
    readonly Dictionary<ulong, CompetitionSquadRules> competitionRuleSnapshots = [];
    readonly Dictionary<ulong, CompetitionSquadRules> competitionHomegrownSnapshots = [];
    readonly Dictionary<ulong, CompetitionSquadRules> competitionHomegrownClubSnapshots = [];
    readonly Dictionary<(ulong Address, int Code), CompetitionSquadRules> competitionCommonRuleSnapshots = [];
    static readonly (int Code, string Label)[] CompetitionCommonRuleTypes =
    [
        (0xA9, "Outfield players in the squad"), (0x12, "Non-EU players"),
        (0x27, "Homegrown in nation (15–21)"), (0x92, "Homegrown in nation"),
        (0x26, "Homegrown at club (15–21)"), (0xFD, "Homegrown at club or in nation"),
        (0x57, "Goalkeepers in the squad"),
    ];
    readonly Dictionary<ulong, IReadOnlyList<CompetitionStanding>> competitionStandingSnapshots = [];
    readonly Dictionary<ulong, CompetitionVar> competitionVarSnapshots = [];
    IReadOnlyList<CompetitionRow> competitionRows = [];
    CompetitionRecord? selectedCompetition;
    Button? competitionSaveButton;
    bool competitionActive;
    Control competitionDetailRoot = new Panel();

    void OpenSelectedCompetition()
    {
        selectedCompetition = (competitionGrid.SelectedItem as CompetitionRow)?.Competition;
        if (selectedCompetition is null) return;
        RenderCompetitionEditor();
        OpenKindTab("competition", selectedCompetition.Name ?? selectedCompetition.Address.ToString("X"), competitionDetailRoot);
    }

    void RegisterCompetitionView()
    {
        AddModuleTab("Competitions", competitionViewRoot, competitionNavItem);
        modules.ItemsSource = (object[])[.. (object[])modules.ItemsSource!, competitionNavItem];

        competitionGrid.Columns.Add(new DataGridTextColumn { Header = "Competition", Binding = new Binding(nameof(CompetitionRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var repCol = new DataGridTextColumn { Header = "Reputation", Binding = new Binding(nameof(CompetitionRow.Reputation)), Width = new DataGridLength(100) };
        repCol.CellStyleClasses.Add("numeric");
        competitionGrid.Columns.Add(repCol);
        var idCol = new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(CompetitionRow.Id)), Width = new DataGridLength(96) };
        idCol.CellStyleClasses.Add("numeric");
        competitionGrid.Columns.Add(idCol);
        competitionSearch.TextChanged += (_, _) => FilterCompetitions();
        competitionGrid.SelectionChanged += (_, _) => selectedCompetition = (competitionGrid.SelectedItem as CompetitionRow)?.Competition;
        competitionGrid.DoubleTapped += (_, _) => OpenSelectedCompetition();
        competitionGrid.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { OpenSelectedCompetition(); e.Handled = true; }
        };

        var left = new DockPanel { Background = Brush("#11131C") };
        var searchBar = new Border
        {
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 7),
            Child = competitionSearch,
        };
        DockPanel.SetDock(searchBar, Dock.Top);
        left.Children.Add(searchBar);
        left.Children.Add(competitionGrid);
        competitionViewRoot.Children.Add(left);

        var scroll = new ScrollViewer
        {
            Content = competitionEditor,
            Background = UiKit.WindowBg,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var detailDock = new DockPanel { Background = UiKit.WindowBg };
        DockPanel.SetDock(competitionHeaderHost, Dock.Top);
        DockPanel.SetDock(competitionRailHost, Dock.Left);
        detailDock.Children.Add(competitionHeaderHost);
        detailDock.Children.Add(competitionRailHost);
        detailDock.Children.Add(scroll);
        competitionDetailRoot = detailDock;

        // Registered after Clubs, Staff, Nations, Stadiums and Cities: those show the players screen when none of them is selected.
        modules.SelectionChanged += async (_, _) =>
        {
            bool toCompetitions = ReferenceEquals(modules.SelectedItem, competitionNavItem);
            if (toCompetitions == competitionActive) return;
            competitionActive = toCompetitions;
            if (toCompetitions && competitionRows.Count == 0 && backend.State.Status == ConnectionStatus.Connected) await LoadCompetitionsAsync();
        };
        backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            competitionRows = [];
            competitionDrafts.Clear();
            competitionPrizeSnapshots.Clear();
            competitionClassificationSnapshots.Clear();
            competitionRuleSnapshots.Clear();
            competitionHomegrownSnapshots.Clear();
            competitionHomegrownClubSnapshots.Clear();
            competitionCommonRuleSnapshots.Clear();
            competitionStandingSnapshots.Clear();
            competitionVarSnapshots.Clear();
            selectedCompetition = null;
            competitionSaveButton = null;
            competitionGrid.ItemsSource = null;
            competitionHeaderHost.Content = null;
            competitionRailHost.Content = null;
            competitionEditor.Children.Clear();
            if (competitionActive && backend.State.Status == ConnectionStatus.Connected) await LoadCompetitionsAsync();
        });
    }

    async Task LoadCompetitionsAsync()
    {
        status.Text = "Loading competitions...";
        try
        {
            var session = backend.Session;
            bool fallbackUsed = false;
            var cachedPlayers = Players().ToArray();
            competitionRows = await Task.Run(() =>
            {
                try
                {
                    return session.Competitions.LoadAll()
                        .Select(c => new CompetitionRow(c, c.DisplayName, c.Reputation, c.Id))
                        .OrderByDescending(r => r.Reputation).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch (Fm27Editor.Core.Memory.MemoryAccessException)
                {
                    fallbackUsed = true;
                    var sourcePlayers = cachedPlayers.Length > 0 ? (IReadOnlyList<Fm27Editor.Core.Players.PlayerRecord>)cachedPlayers : session.Players.LoadAll();
                    var active = new List<CompetitionRow>();
                    var seen = new HashSet<ulong>();
                    foreach (var p in sourcePlayers)
                    {
                        if (p.Division is not { Address: > 0 } div || !seen.Add(div.Address)) continue;
                        try
                        {
                            if (session.Competitions.Reload(div.Address) is { } c)
                                active.Add(new CompetitionRow(c, c.DisplayName, c.Reputation, c.Id));
                        }
                        catch (Fm27Editor.Core.Memory.MemoryAccessException) { }
                    }
                    return active.OrderByDescending(r => r.Reputation).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
                }
            });
            FilterCompetitions();
            status.Text = fallbackUsed
                ? $"{competitionRows.Count:N0} active divisions loaded (full competition list unavailable on this build)"
                : $"{competitionRows.Count:N0} competitions";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    void FilterCompetitions()
    {
        string q = Fold((competitionSearch.Text ?? "").Trim());
        competitionGrid.ItemsSource = q.Length == 0 ? competitionRows : competitionRows.Where(r =>
            Fold(r.Name).Contains(q, StringComparison.Ordinal) || r.Id.ToString(Inv).Contains(q, StringComparison.Ordinal)).ToList();
    }

    void RenderCompetitionEditor()
    {
        competitionHeaderHost.Content = null;
        competitionRailHost.Content = null;
        competitionEditor.Children.Clear();
        if (selectedCompetition is not { } c) { competitionSaveButton = null; return; }
        var peers = competitionRows.Count > 0 ? competitionRows.Select(row => row.Competition).ToList() : null;
        competitionDrafts.TryGetValue(c.Address, out var draft);
        draft ??= new(null, null, null, null, null, null, null, null);
        CompetitionPrizeMoney? prize;
        if (draft.Any && competitionPrizeSnapshots.TryGetValue(c.Address, out var stagedPrize)) prize = stagedPrize;
        else
        {
            prize = backend.Session.CompetitionPrizeMoney.Read(c);
            if (prize is not null) competitionPrizeSnapshots[c.Address] = prize;
            else competitionPrizeSnapshots.Remove(c.Address);
        }
        CompetitionClassificationPrizes? classification;
        if (draft.Any && competitionClassificationSnapshots.TryGetValue(c.Address, out var stagedClassification)) classification = stagedClassification;
        else
        {
            classification = prize is null ? null : backend.Session.CompetitionPrizeMoney.ReadClassification(prize);
            if (classification is not null) competitionClassificationSnapshots[c.Address] = classification;
            else competitionClassificationSnapshots.Remove(c.Address);
        }
        CompetitionSquadRules? rules;
        if (draft.Any && competitionRuleSnapshots.TryGetValue(c.Address, out var stagedRules)) rules = stagedRules;
        else
        {
            rules = backend.Session.CompetitionSquadRules.Read(c, peers);
            if (rules is not null) competitionRuleSnapshots[c.Address] = rules;
            else competitionRuleSnapshots.Remove(c.Address);
        }
        CompetitionSquadRules? homegrown;
        if (draft.Any && competitionHomegrownSnapshots.TryGetValue(c.Address, out var stagedHomegrown)) homegrown = stagedHomegrown;
        else
        {
            homegrown = backend.Session.CompetitionSquadRules.ReadHomegrown(c, peers);
            if (homegrown is not null) competitionHomegrownSnapshots[c.Address] = homegrown;
            else competitionHomegrownSnapshots.Remove(c.Address);
        }
        IReadOnlyList<CompetitionStanding>? standings;
        CompetitionSquadRules? homegrownClub;
        if (draft.Any && competitionHomegrownClubSnapshots.TryGetValue(c.Address, out var stagedClub)) homegrownClub = stagedClub;
        else
        {
            homegrownClub = backend.Session.CompetitionSquadRules.ReadHomegrownClub(c, peers);
            if (homegrownClub is not null) competitionHomegrownClubSnapshots[c.Address] = homegrownClub;
            else competitionHomegrownClubSnapshots.Remove(c.Address);
        }
        if (draft.Any && competitionStandingSnapshots.TryGetValue(c.Address, out var stagedStandings)) standings = stagedStandings;
        else
        {
            standings = backend.Session.CompetitionStandings.Read(c);
            if (standings is not null) competitionStandingSnapshots[c.Address] = standings;
            else competitionStandingSnapshots.Remove(c.Address);
        }
        CompetitionVar? varSettings;
        if (draft.Any && competitionVarSnapshots.TryGetValue(c.Address, out var stagedVar)) varSettings = stagedVar;
        else
        {
            varSettings = backend.Session.CompetitionVar.Read(c);
            if (varSettings is not null) competitionVarSnapshots[c.Address] = varSettings;
            else competitionVarSnapshots.Remove(c.Address);
        }
        Button? save = null, discard = null;

        TextBlock Label(string t, string color = "#CBD5E1") => new() { Text = t, FontSize = 11.5, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
        Control Swatch(uint argb) => new Border { Width = 70, Height = 16, Background = new SolidColorBrush(Color.FromUInt32(argb)), BorderBrush = Brush("#3A3F55"), BorderThickness = new Thickness(1) };
        string Date(DateOnly? d) => d is { } v ? v.ToString("yyyy-MM-dd", Inv) : "";
        static string Hex(uint argb) => $"#{argb:X8}";
        TextBox Field(string? text, int width, bool right = false) => new()
        {
            Text = text ?? "", Width = width, MinHeight = 24, Padding = new Thickness(5, 1),
            HorizontalContentAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var reputation = Field(draft.Reputation ?? c.Reputation.ToString(Inv), 76, true);
        var subsOptions = SubstitutionOptions(c.SubstitutionRules);
        byte currentSubs = byte.TryParse(draft.SubstitutionRules, out byte drafted) ? drafted : c.SubstitutionRules;
        var subs = new ComboBox { Width = 196, ItemsSource = subsOptions, SelectedItem = subsOptions.First(o => o.Value == currentSubs) };

        var colourFields = new List<(CompetitionColour Slot, uint Current, Border Swatch, TextBox Box)>();
        foreach (var (label, current, slot) in new (string, uint, CompetitionColour)[]
        {
            ("Foreground Colour", c.Foreground, CompetitionColour.Foreground),
            ("Background Colour", c.Background, CompetitionColour.Background),
            ("Trim Colour", c.Trim, CompetitionColour.Trim),
            ("Winter Ball Colour", c.WinterBallColour, CompetitionColour.WinterBall),
        })
        {
            string? staged = slot switch
            {
                CompetitionColour.Foreground => draft.Foreground,
                CompetitionColour.Background => draft.Background,
                CompetitionColour.Trim => draft.Trim,
                _ => draft.WinterBall,
            };
            var box = Field(staged ?? Hex(current), 106);
            var swatch = (Border)Swatch(ParseColour(box.Text) ?? current);
            colourFields.Add((slot, current, swatch, box));
        }
        var start = Field(draft.WinterBallStart ?? Date(c.WinterBallStart), 106);
        var end = Field(draft.WinterBallEnd ?? Date(c.WinterBallEnd), 106);
        var prizeWin = prize is null ? null : Field(draft.PrizeWin ?? prize.PerWin.ToString(Inv), 132, true);
        var prizeDraw = prize is null ? null : Field(draft.PrizeDraw ?? prize.PerDraw.ToString(Inv), 132, true);
        var prizeDefeat = prize is null ? null : Field(draft.PrizeDefeat ?? prize.PerDefeat.ToString(Inv), 132, true);
        var positionFields = classification?.Amounts.Select((value, index) =>
            Field(draft.Positions?.GetValueOrDefault(index + 1) ?? value.ToString(Inv), 132, true)).ToArray() ?? [];
        var classificationEnabled = classification is null ? null : new CheckBox { IsChecked = draft.ClassificationEnabled ?? (classification.Amounts.Count > 0) };
        var maximumSquad = rules is null ? null : Field(draft.MaximumSquad ?? rules.MaximumSquadSize.ToString(Inv), 116, true);
        var homegrownBox = homegrown is null ? null : Field(draft.Homegrown ?? homegrown.MaximumSquadSize.ToString(Inv), 116, true);
        var homegrownClubBox = homegrownClub is null ? null : Field(draft.HomegrownClub ?? homegrownClub.MaximumSquadSize.ToString(Inv), 116, true);
        var commonRuleFields = new List<(int Code, string Label, CompetitionSquadRules Snapshot, TextBox Box)>();
        foreach (var (code, label) in CompetitionCommonRuleTypes)
        {
            var key = (c.Address, code);
            CompetitionSquadRules? snapshot;
            if (draft.Any && competitionCommonRuleSnapshots.TryGetValue(key, out var staged)) snapshot = staged;
            else
            {
                snapshot = backend.Session.CompetitionSquadRules.ReadRule(c, code, null, peers);
                if (snapshot is not null) competitionCommonRuleSnapshots[key] = snapshot;
                else competitionCommonRuleSnapshots.Remove(key);
            }
            if (snapshot is not null) commonRuleFields.Add((code, label, snapshot,
                Field(draft.CommonRules?.GetValueOrDefault(code) ?? snapshot.MaximumSquadSize.ToString(Inv), 116, true)));
        }
        var varOptions = new List<SubstitutionOption> { new(1, "Referee Review"), new(2, "Remote Review"), new(3, "Referee And/Or Remote Review"), new(4, "Unknown VAR Type"), new(5, "No VAR") };
        if (varSettings is not null && varOptions.All(o => o.Value != varSettings.Type)) varOptions.Insert(0, new(varSettings.Type, $"Value 0x{varSettings.Type:X2}"));
        var varType = varSettings is null ? null : new ComboBox { Width = 198, ItemsSource = varOptions, SelectedItem = varOptions.First(o => o.Value == (draft.VarType ?? varSettings.Type)) };
        var varTvOnly = varSettings is null ? null : new CheckBox { IsChecked = draft.VarTvOnly ?? (varSettings.TvOnly != 0) };
        var varEnabled = varSettings is null ? null : new CheckBox { IsChecked = draft.VarEnabled ?? (varSettings.Enabled != 0) };
        var varFromDate = varSettings is null ? null : Field(draft.VarFromDate ?? Date(varSettings.FromDate), 106);
        CompetitionStandingField[] standingColumns = [CompetitionStandingField.Played, CompetitionStandingField.Won, CompetitionStandingField.Drawn,
            CompetitionStandingField.Lost, CompetitionStandingField.GoalsFor, CompetitionStandingField.GoalsAgainst, CompetitionStandingField.Points];
        var standingFields = standings?.SelectMany(row => standingColumns.Select(field =>
            (Row: row, Kind: field, Box: Field(draft.StandingPoints?.GetValueOrDefault((row.ClubId, field)) ?? CompetitionStandingsAccess.Value(row, field).ToString(Inv), 50, true)))).ToArray() ?? [];
        foreach (var item in standingFields) { item.Box.Width = double.NaN; item.Box.MinWidth = 0; item.Box.HorizontalAlignment = HorizontalAlignment.Stretch; }

        // Any control change rebuilds the draft from the controls, so a staged colour can never be lost by another edit.
        void Refresh()
        {
            static string? Diff(string? text, string original) => Same(text, original) ? null : text;
            var next = new CompetitionDraft(
                Diff(reputation.Text, c.Reputation.ToString(Inv)),
                subs.SelectedItem is SubstitutionOption option && option.Value != c.SubstitutionRules ? option.Value.ToString(Inv) : null,
                Diff(colourFields[0].Box.Text, Hex(colourFields[0].Current)),
                Diff(colourFields[1].Box.Text, Hex(colourFields[1].Current)),
                Diff(colourFields[2].Box.Text, Hex(colourFields[2].Current)),
                Diff(colourFields[3].Box.Text, Hex(colourFields[3].Current)),
                Diff(start.Text, Date(c.WinterBallStart)),
                Diff(end.Text, Date(c.WinterBallEnd)),
                prize is null ? null : Diff(prizeWin!.Text, prize.PerWin.ToString(Inv)),
                prize is null ? null : Diff(prizeDraw!.Text, prize.PerDraw.ToString(Inv)),
                prize is null ? null : Diff(prizeDefeat!.Text, prize.PerDefeat.ToString(Inv)),
                positionFields.Select((box, index) => (Position: index + 1, Text: Diff(box.Text, classification!.Amounts[index].ToString(Inv))))
                    .Where(item => item.Text is not null).ToDictionary(item => item.Position, item => item.Text!),
                rules is null ? null : Diff(maximumSquad!.Text, rules.MaximumSquadSize.ToString(Inv)),
                homegrown is null ? null : Diff(homegrownBox!.Text, homegrown.MaximumSquadSize.ToString(Inv)),
                standingFields.Select(item => (Key: (item.Row.ClubId, item.Kind), Text: Diff(item.Box.Text, CompetitionStandingsAccess.Value(item.Row, item.Kind).ToString(Inv))))
                    .Where(item => item.Text is not null).ToDictionary(item => item.Key, item => item.Text!),
                varSettings is not null && varType!.SelectedItem is SubstitutionOption varOption && varOption.Value != varSettings.Type ? varOption.Value : null,
                varSettings is not null && varTvOnly!.IsChecked != (varSettings.TvOnly != 0) ? varTvOnly.IsChecked : null,
                varSettings is not null && varEnabled!.IsChecked != (varSettings.Enabled != 0) ? varEnabled.IsChecked : null,
                varSettings is null ? null : Diff(varFromDate!.Text, Date(varSettings.FromDate)),
                homegrownClub is null ? null : Diff(homegrownClubBox!.Text, homegrownClub.MaximumSquadSize.ToString(Inv)),
                commonRuleFields.Select(item => (item.Code, Text: Diff(item.Box.Text, item.Snapshot.MaximumSquadSize.ToString(Inv))))
                    .Where(item => item.Text is not null).ToDictionary(item => item.Code, item => item.Text!),
                classification is not null && classificationEnabled!.IsChecked != (classification.Amounts.Count > 0) ? classificationEnabled.IsChecked : null);
            if (next.Any) competitionDrafts[c.Address] = next; else competitionDrafts.Remove(c.Address);
            if (save is not null) save.IsEnabled = discard!.IsEnabled = next.Any;
        }
        reputation.TextChanged += (_, _) => Refresh();
        subs.SelectionChanged += (_, _) => Refresh();
        foreach (var (slot, current, swatch, box) in colourFields)
            box.TextChanged += (_, _) => { swatch.Background = new SolidColorBrush(Color.FromUInt32(ParseColour(box.Text) ?? current)); Refresh(); };
        start.TextChanged += (_, _) => Refresh();
        end.TextChanged += (_, _) => Refresh();
        if (prizeWin is not null) prizeWin.TextChanged += (_, _) => Refresh();
        if (prizeDraw is not null) prizeDraw.TextChanged += (_, _) => Refresh();
        if (prizeDefeat is not null) prizeDefeat.TextChanged += (_, _) => Refresh();
        foreach (var box in positionFields) box.TextChanged += (_, _) => Refresh();
        if (classificationEnabled is not null) classificationEnabled.IsCheckedChanged += (_, _) =>
        {
            Refresh();
            if (classificationEnabled.IsChecked == true && classification is { Amounts.Count: 0 } && standings is { Count: > 0 } &&
                (classification.Begin == 0 || classification.Capacity - classification.Begin < (ulong)standings.Count * 4))
                status.Text = "Enabling allocates a prize table outside the game's allocator; keep a backup of the save.";
        };
        if (maximumSquad is not null) maximumSquad.TextChanged += (_, _) => Refresh();
        if (homegrownBox is not null) homegrownBox.TextChanged += (_, _) => Refresh();
        if (homegrownClubBox is not null) homegrownClubBox.TextChanged += (_, _) => Refresh();
        foreach (var item in commonRuleFields) item.Box.TextChanged += (_, _) => Refresh();
        foreach (var item in standingFields) item.Box.TextChanged += (_, _) => Refresh();
        if (varType is not null) varType.SelectionChanged += (_, _) => Refresh();
        if (varTvOnly is not null) varTvOnly.IsCheckedChanged += (_, _) => Refresh();
        if (varEnabled is not null) varEnabled.IsCheckedChanged += (_, _) => Refresh();
        if (varFromDate is not null) varFromDate.TextChanged += (_, _) => Refresh();

        Border CardRows(string title, IEnumerable<(string Label, Control Value)> items)
        {
            var stack = new StackPanel { Spacing = 0 };
            int idx = 0;
            foreach (var (label, value) in items)
            {
                var line = new DockPanel { MinHeight = 26, LastChildFill = true };
                DockPanel.SetDock(value, Dock.Right);
                line.Children.Add(value);
                line.Children.Add(Label(label));
                stack.Children.Add(new Border
                {
                    Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                    Padding = new Thickness(10, 3),
                    Child = line,
                });
            }
            return UiKit.Card(title, stack);
        }

        save = new Button { Content = "Save changes", IsEnabled = draft.Any, Classes = { "accent" } };
        discard = new Button { Content = "Discard", IsEnabled = draft.Any };
        competitionSaveButton = save;
        save.Click += (_, _) => SaveCompetition(c);
        discard.Click += (_, _) =>
        {
            competitionDrafts.Remove(c.Address);
            competitionPrizeSnapshots.Remove(c.Address);
            competitionClassificationSnapshots.Remove(c.Address);
            competitionRuleSnapshots.Remove(c.Address);
            competitionHomegrownSnapshots.Remove(c.Address);
            competitionHomegrownClubSnapshots.Remove(c.Address);
            foreach (var (code, _) in CompetitionCommonRuleTypes) competitionCommonRuleSnapshots.Remove((c.Address, code));
            competitionStandingSnapshots.Remove(c.Address);
            competitionVarSnapshots.Remove(c.Address);
            RenderCompetitionEditor();
        };

        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        badges.Children.Add(UiKit.MetaText($"ID {c.Id.ToString(Inv)}"));
        if (standings is { Count: > 0 })
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText($"{standings.Count} teams", highlight: true));
        }

        var repText = new TextBlock
        {
            Text = c.Reputation.ToString(Inv),
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#C084FC"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        };
        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { UiKit.StatBox("REP", repText) },
        };
        var logoImg = graphics.Image("comp", c.Id, 72);
        competitionHeaderHost.Content = UiKit.EntityBanner(c.DisplayName, badges, headerActions, logoImg);

        // Section 1: Overview (2-column grid)
        var overviewLeft = new StackPanel { Spacing = 12 };
        overviewLeft.Children.Add(CardRows("Competition",
        [
            ("Reputation", reputation),
            ("Substitution Rules", subs),
        ]));
        if (varSettings is not null)
        {
            overviewLeft.Children.Add(CardRows("VAR Settings",
            [
                ("Enable custom VAR settings", varEnabled!),
                ("VAR", varType!),
                ("From date", varFromDate!),
                ("VAR - TV Matches Only", varTvOnly!),
            ]));
        }

        var colourRows = new List<(string Label, Control Value)>();
        foreach (var (slot, _, _, box) in colourFields)
            colourRows.Add((slot switch
            {
                CompetitionColour.Foreground => "Foreground Colour",
                CompetitionColour.Background => "Background Colour",
                CompetitionColour.Trim => "Trim Colour",
                _ => "Winter Ball Colour",
            }, ColourInput(box)));
        colourRows.Add(("Winter Ball Start Date", start));
        colourRows.Add(("Winter Ball End Date", end));
        var overviewRight = CardRows("Colours & Winter Ball", colourRows);

        var overviewGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        Grid.SetColumn(overviewLeft, 0);
        Grid.SetColumn(overviewRight, 2);
        overviewGrid.Children.Add(overviewLeft);
        overviewGrid.Children.Add(overviewRight);

        var compSections = new List<SectionHost.Section>
        {
            new("Overview", "Overview", overviewGrid),
        };

        // Section 2: Prize Money
        if (prize is not null || classificationEnabled is not null)
        {
            Border? matchPrizeCard = prize is null ? null : CardRows("Match Prize Money",
            [
                ("Per Win", prizeWin!),
                ("Per Draw", prizeDraw!),
                ("Per Defeat", prizeDefeat!),
            ]);
            Border? classPrizeCard = classificationEnabled is null ? null : CardRows("Classification Prize Money",
                new[] { ("Enabled", (Control)classificationEnabled) }
                    .Concat(positionFields.Select((box, index) => ($"Position {index + 1}", (Control)box))));

            Control prizeBody;
            if (matchPrizeCard is not null && classPrizeCard is not null)
            {
                matchPrizeCard.VerticalAlignment = VerticalAlignment.Top;
                var pg = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
                Grid.SetColumn(matchPrizeCard, 0);
                Grid.SetColumn(classPrizeCard, 2);
                pg.Children.Add(matchPrizeCard);
                pg.Children.Add(classPrizeCard);
                prizeBody = pg;
            }
            else
            {
                var single = (matchPrizeCard ?? classPrizeCard)!;
                single.MaxWidth = 540;
                single.HorizontalAlignment = HorizontalAlignment.Left;
                prizeBody = single;
            }
            compSections.Add(new("Prize Money", "Prize Money", prizeBody));
        }

        // Section 3: Squad Rules
        var squadFields = new List<(string, Control)>();
        if (maximumSquad is not null) squadFields.Add(("Maximum squad size", maximumSquad));
        foreach (var item in commonRuleFields) squadFields.Add((item.Label, item.Box));
        var sharedNames = new List<string>();
        foreach (var snapshot in new[] { rules, homegrown, homegrownClub }.Concat(commonRuleFields.Select(item => item.Snapshot)))
        {
            if (snapshot is null) continue;
            foreach (int id in snapshot.AlsoAppliesTo)
            {
                string name = competitionRows.FirstOrDefault(row => row.Id == id)?.Name ?? id.ToString(Inv);
                if (!sharedNames.Contains(name, StringComparer.OrdinalIgnoreCase)) sharedNames.Add(name);
            }
        }
        if (sharedNames.Count > 0)
            squadFields.Add(("Also Applies To", new TextBlock { Text = string.Join("\n", sharedNames), Foreground = UiKit.TextSecondary, TextAlignment = TextAlignment.Right }));
        if (squadFields.Count > 0)
        {
            var squadCard = CardRows("Squad Registration", squadFields);
            squadCard.MaxWidth = 620;
            squadCard.HorizontalAlignment = HorizontalAlignment.Left;
            compSections.Add(new("Squad Rules", "Squad Rules", squadCard));
        }

        // Section 4: Standings
        if (standingFields.Length > 0)
        {
            var names = (clubCache ??= backend.Session.LoadClubs(Players())).ToDictionary(club => club.Address);
            var tableStack = new StackPanel { Spacing = 0 };

            var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*,50,46,46,46,48,48,54"), ColumnSpacing = 4 };
            string[] headers = ["Pos", "Team", "GP", "W", "D", "L", "GS", "GA", "Pts"];
            for (int col = 0; col < headers.Length; col++)
            {
                var label = Label(headers[col], "#94A3B8");
                label.FontWeight = FontWeight.SemiBold;
                if (col == 0 || col >= 2) label.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(label, col);
                headerGrid.Children.Add(label);
            }
            tableStack.Children.Add(new Border
            {
                Background = Brush("#161A26"),
                BorderBrush = UiKit.BorderSubtle,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 5),
                Child = headerGrid,
            });

            int rIdx = 0;
            foreach (var row in standings!)
            {
                var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*,50,46,46,46,48,48,54"), ColumnSpacing = 4 };
                var pos = Label(row.Position.ToString(Inv), "#94A3B8");
                pos.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(pos, 0);
                rowGrid.Children.Add(pos);

                ulong clubAddr = row.Club;
                var name = Label(names.GetValueOrDefault(clubAddr)?.DisplayName ?? row.ClubId.ToString(Inv), "#F1F5F9");
                name.FontWeight = FontWeight.Medium;
                name.Cursor = new Cursor(StandardCursorType.Hand);
                name.PointerPressed += async (_, e) => { if (e.ClickCount >= 2 && clubAddr != 0) await OpenPlayerClubAsync(clubAddr); };
                Grid.SetColumn(name, 1);
                rowGrid.Children.Add(name);

                foreach (var item in standingFields.Where(item => item.Row.ClubId == row.ClubId))
                {
                    Grid.SetColumn(item.Box, 2 + Array.IndexOf(standingColumns, item.Kind));
                    rowGrid.Children.Add(item.Box);
                }

                tableStack.Children.Add(new Border
                {
                    Background = rIdx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                    Padding = new Thickness(10, 2),
                    Child = rowGrid,
                });
            }
            compSections.Add(new("Standings", "Standings", UiKit.Card("League Table", tableStack)));
        }

        string chosenSection = compSections.Any(s => s.Key == competitionSectionKey) ? competitionSectionKey : compSections[0].Key;
        competitionSectionKey = chosenSection;
        var hostContent = SectionHost.Build(
            "competition",
            compSections,
            chosenSection,
            key => competitionSectionKey = key,
            competitionRailHost);
        competitionEditor.Children.Add(hostContent);
    }

    sealed record SubstitutionOption(byte Value, string Label)
    {
        public override string ToString() => Label;
    }

    static List<SubstitutionOption> SubstitutionOptions(byte current)
    {
        var list = KnownSubstitutionRules.Select(x => new SubstitutionOption(x.Value, x.Label)).ToList();
        if (list.All(o => o.Value != current)) list.Insert(0, new SubstitutionOption(current, $"Value 0x{current:X2}"));
        return list;
    }

    static bool Same(string? text, string? original) => (text?.Trim() ?? "") == (original?.Trim() ?? "");

    static uint? ParseColour(string? text)
    {
        var t = text?.Trim().TrimStart('#');
        return uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, Inv, out uint value) ? value : null;
    }

    void SaveCompetition(CompetitionRecord c)
    {
        if (!competitionDrafts.TryGetValue(c.Address, out var draft)) return;
        var writer = backend.Session.CompetitionWriter;
        var steps = new List<string>();
        try
        {
            var prizeEdits = new List<(CompetitionPrizeField Field, int Value)>();
            var positionEdits = new List<(int Position, int Value)>();
            if (draft.ClassificationEnabled is not null && draft.Positions is { Count: > 0 })
            { status.Text = "Save prize amounts before changing Enabled"; return; }
            var standingEdits = new List<((int ClubId, CompetitionStandingField Field) Key, int Value)>();
            var varEdits = new List<(CompetitionVarField Field, byte Value)>();
            DateOnly? fromDate = null;
            if (draft.VarFromDate is { } dateText && dateText.Trim().Length > 0)
            {
                if (!DateOnly.TryParseExact(dateText.Trim(), "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var parsedDate))
                { status.Text = "VAR date must be yyyy-MM-dd or empty"; return; }
                fromDate = parsedDate;
            }
            if (draft.VarType is { } vt) varEdits.Add((CompetitionVarField.Type, vt));
            if (draft.VarTvOnly is { } tv) varEdits.Add((CompetitionVarField.TvOnly, tv ? (byte)1 : (byte)0));
            if (draft.VarEnabled is { } enabled) varEdits.Add((CompetitionVarField.Enabled, enabled ? (byte)1 : (byte)0));
            foreach (var (key, text) in draft.StandingPoints ?? new Dictionary<(int, CompetitionStandingField), string>())
            {
                if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int value) ||
                    (key.Field == CompetitionStandingField.Points ? value is < short.MinValue or > short.MaxValue : value is < byte.MinValue or > byte.MaxValue))
                { status.Text = $"{key.Field}: invalid value"; return; }
                standingEdits.Add((key, value));
            }
            int? maxSquadEdit = null;
            int? homegrownEdit = null;
            int? homegrownClubEdit = null;
            var commonRuleEdits = new List<(int Code, int Value)>();
            foreach (var (code, text) in draft.CommonRules ?? new Dictionary<int, string>())
            {
                if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int value) || value < 0)
                { status.Text = "Squad rule: enter a non-negative whole number"; return; }
                commonRuleEdits.Add((code, value));
            }
            if (draft.HomegrownClub is { } clubText)
            {
                if (!int.TryParse(clubText.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int value) || value < 0)
                { status.Text = "Homegrown At Club: enter a non-negative whole number"; return; }
                homegrownClubEdit = value;
            }
            if (draft.Homegrown is { } homegrownText)
            {
                if (!int.TryParse(homegrownText.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int value) || value < 0)
                { status.Text = "Homegrown in Nation: enter a non-negative whole number"; return; }
                homegrownEdit = value;
            }
            if (draft.MaximumSquad is { } maxText)
            {
                if (!int.TryParse(maxText.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int value) || value < 0)
                { status.Text = "Maximum squad size: enter a non-negative whole number"; return; }
                maxSquadEdit = value;
            }
            foreach (var (position, text) in draft.Positions ?? new Dictionary<int, string>())
            {
                if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int value) || value < 0)
                { status.Text = $"Position {position}: enter a non-negative whole number"; return; }
                positionEdits.Add((position, value));
            }
            foreach (var (field, text) in new (CompetitionPrizeField, string?)[]
            {
                (CompetitionPrizeField.PerWin, draft.PrizeWin), (CompetitionPrizeField.PerDraw, draft.PrizeDraw),
                (CompetitionPrizeField.PerDefeat, draft.PrizeDefeat),
            })
            {
                if (text is null) continue;
                if (!int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, Inv, out int value) || value < 0)
                { status.Text = $"{field}: enter a non-negative whole number"; return; }
                prizeEdits.Add((field, value));
            }
            if (draft.Reputation is { } repText)
            {
                if (!short.TryParse(repText.Trim(), System.Globalization.NumberStyles.Integer, Inv, out short value) || value is < 0 or > CompetitionWriter.MaxReputation)
                { status.Text = $"Reputation: enter a whole number 0 to {CompetitionWriter.MaxReputation}"; return; }
                if (value != c.Reputation) steps.Add($"reputation {c.Reputation} → {value}");
                backend.Edits.Execute(() => writer.SetReputation(c, value));
            }
            if (draft.SubstitutionRules is { } subsText)
            {
                if (!byte.TryParse(subsText.Trim(), System.Globalization.NumberStyles.Integer, Inv, out byte subs))
                { status.Text = "Substitution rules: enter a value 0 to 255"; return; }
                if (subs != c.SubstitutionRules) steps.Add($"substitution rules 0x{c.SubstitutionRules:X2} → 0x{subs:X2}");
                backend.Edits.Execute(() => writer.SetSubstitutionRules(c, subs));
            }
            foreach (var (text, current, slot, name) in new (string?, uint?, CompetitionColour, string)[]
            {
                (draft.Foreground, c.Foreground, CompetitionColour.Foreground, "foreground"),
                (draft.Background, c.Background, CompetitionColour.Background, "background"),
                (draft.Trim, c.Trim, CompetitionColour.Trim, "trim"),
                (draft.WinterBall, c.WinterBallColour, CompetitionColour.WinterBall, "winter ball colour"),
            })
            {
                if (text is null) continue;
                if (ParseColour(text) is not { } argb) { status.Text = $"The {name} colour must be #AARRGGBB"; return; }
                if (argb == current) continue;
                steps.Add($"{name} #{current:X8} → #{argb:X8}");
                backend.Edits.Execute(() => writer.SetColour(c, slot, argb));
                c = backend.Session.Competitions.Reload(c.Address) ?? c;
            }
            foreach (var (text, current, startDate, name) in new (string?, DateOnly?, bool, string)[]
            {
                (draft.WinterBallStart, c.WinterBallStart, true, "winter ball start"),
                (draft.WinterBallEnd, c.WinterBallEnd, false, "winter ball end"),
            })
            {
                if (text is null) continue;
                DateOnly? target = null;
                if (text.Trim().Length > 0)
                {
                    if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var parsed))
                    { status.Text = $"The {name} date must be yyyy-MM-dd or empty"; return; }
                    target = parsed;
                }
                if (target == current) continue;
                steps.Add($"{name} {current?.ToString("yyyy-MM-dd") ?? "none"} → {target?.ToString("yyyy-MM-dd") ?? "none"}");
                backend.Edits.Execute(() => writer.SetWinterBallDate(c, startDate, target));
                c = backend.Session.Competitions.Reload(c.Address) ?? c;
            }
            if (prizeEdits.Count > 0)
            {
                if (!competitionPrizeSnapshots.TryGetValue(c.Address, out var prizeSnapshot))
                    throw new InvalidOperationException("Prize money snapshot is unavailable");
                foreach (var (field, value) in prizeEdits)
                {
                    backend.Edits.Execute(() => backend.Session.CompetitionPrizeMoney.Set(prizeSnapshot, field, value));
                    steps.Add($"{field}: {value}");
                }
            }
            if (positionEdits.Count > 0)
            {
                if (!competitionClassificationSnapshots.TryGetValue(c.Address, out var snapshot))
                    throw new InvalidOperationException("Classification prize snapshot is unavailable");
                foreach (var (position, value) in positionEdits)
                {
                    backend.Edits.Execute(() => backend.Session.CompetitionPrizeMoney.SetClassification(snapshot, position, value));
                    steps.Add($"position {position}: {value}");
                }
            }
            if (draft.ClassificationEnabled is { } isEnabled)
            {
                if (!competitionClassificationSnapshots.TryGetValue(c.Address, out var snapshot)) throw new InvalidOperationException("Classification prize snapshot is unavailable");
                backend.Edits.Execute(() => backend.Session.SetCompetitionClassificationEnabled(snapshot, isEnabled));
                steps.Add($"classification prize: {(isEnabled ? "enabled" : "disabled")}");
            }
            var squadEdits = new List<(CompetitionSquadRules Snapshot, int Value)>();
            foreach (var (code, value) in commonRuleEdits)
            {
                if (!competitionCommonRuleSnapshots.TryGetValue((c.Address, code), out var snapshot)) throw new InvalidOperationException("Squad rule snapshot is unavailable");
                squadEdits.Add((snapshot, value));
            }
            if (maxSquadEdit is { } maximum)
            {
                if (!competitionRuleSnapshots.TryGetValue(c.Address, out var snapshot))
                    throw new InvalidOperationException("Squad rules snapshot is unavailable");
                squadEdits.Add((snapshot, maximum));
            }
            if (homegrownEdit is { } homegrownValue)
            {
                if (!competitionHomegrownSnapshots.TryGetValue(c.Address, out var snapshot))
                    throw new InvalidOperationException("Homegrown rules snapshot is unavailable");
                squadEdits.Add((snapshot, homegrownValue));
            }
            if (homegrownClubEdit is { } homegrownClubValue)
            {
                if (!competitionHomegrownClubSnapshots.TryGetValue(c.Address, out var snapshot))
                    throw new InvalidOperationException("Homegrown club rules snapshot is unavailable");
                squadEdits.Add((snapshot, homegrownClubValue));
            }
            if (squadEdits.Count > 0)
            {
                backend.Edits.Execute(() => backend.Session.CompetitionSquadRules.SetMany(squadEdits));
                steps.Add($"{squadEdits.Count} squad rule(s)");
            }
            if (standingEdits.Count > 0)
            {
                if (!competitionStandingSnapshots.TryGetValue(c.Address, out var snapshot))
                    throw new InvalidOperationException("Standings snapshot is unavailable");
                foreach (var (key, value) in standingEdits)
                {
                    var row = snapshot.Single(item => item.ClubId == key.ClubId);
                    backend.Edits.Execute(() => backend.Session.CompetitionStandings.Set(c, row, key.Field, value));
                    steps.Add($"club {key.ClubId} {key.Field}: {value}");
                }
            }
            if (varEdits.Count > 0 || draft.VarFromDate is not null)
            {
                if (!competitionVarSnapshots.TryGetValue(c.Address, out var snapshot)) throw new InvalidOperationException("VAR snapshot is unavailable");
                foreach (var (field, value) in varEdits)
                {
                    backend.Edits.Execute(() => backend.Session.CompetitionVar.Set(snapshot, field, value));
                    steps.Add($"VAR {field}: {value}");
                }
                if (draft.VarFromDate is not null)
                {
                    backend.Edits.Execute(() => backend.Session.CompetitionVar.SetFromDate(snapshot, fromDate));
                    steps.Add($"VAR from date: {fromDate?.ToString("yyyy-MM-dd") ?? "none"}");
                }
            }
        }
        catch (Exception ex)
        {
            competitionDrafts.Remove(c.Address);
            competitionPrizeSnapshots.Remove(c.Address);
            competitionClassificationSnapshots.Remove(c.Address);
            competitionRuleSnapshots.Remove(c.Address);
            competitionHomegrownSnapshots.Remove(c.Address);
            competitionHomegrownClubSnapshots.Remove(c.Address);
            foreach (var (code, _) in CompetitionCommonRuleTypes) competitionCommonRuleSnapshots.Remove((c.Address, code));
            competitionStandingSnapshots.Remove(c.Address);
            competitionVarSnapshots.Remove(c.Address);
            Reopen(c);
            status.Text = steps.Count == 0 ? ex.Message : $"Saved {steps.Count} change(s), then: {ex.Message}";
            return;
        }
        competitionDrafts.Remove(c.Address);
        competitionPrizeSnapshots.Remove(c.Address);
        competitionClassificationSnapshots.Remove(c.Address);
        competitionRuleSnapshots.Remove(c.Address);
        competitionHomegrownSnapshots.Remove(c.Address);
        competitionHomegrownClubSnapshots.Remove(c.Address);
        foreach (var (code, _) in CompetitionCommonRuleTypes) competitionCommonRuleSnapshots.Remove((c.Address, code));
        competitionStandingSnapshots.Remove(c.Address);
        competitionVarSnapshots.Remove(c.Address);
        Reopen(c);
        status.Text = steps.Count == 0 ? "Nothing changed." : $"Saved {steps.Count} change(s) of {c.DisplayName}. Continue in FM to see updates.";
    }

    void Reopen(CompetitionRecord c)
    {
        if (backend.Session.Competitions.Reload(c.Address) is { } fresh)
        {
            selectedCompetition = fresh;
            competitionRows = competitionRows.Select(r => r.Competition.Address == c.Address ? r with { Competition = fresh, Reputation = fresh.Reputation } : r).ToList();
            FilterCompetitions();
            competitionGrid.SelectedItem = (competitionGrid.ItemsSource as IEnumerable<CompetitionRow>)?.FirstOrDefault(r => r.Competition.Address == c.Address);
        }
        RenderCompetitionEditor();
    }
}

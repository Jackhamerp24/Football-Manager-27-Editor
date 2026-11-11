using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;

namespace Fm27Editor.App;

internal sealed record CompetitionRow(CompetitionRecord Competition, string Name, int Reputation, int Id);

internal sealed record SubstitutionOption(byte Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Text the user typed for one competition; null fields keep the value read from the game.</summary>
internal sealed record CompetitionDraft(string? Reputation, string? SubstitutionRules, string? Foreground, string? Background,
    string? Trim, string? WinterBall, string? WinterBallStart, string? WinterBallEnd,
    string? PrizeWin = null, string? PrizeDraw = null, string? PrizeDefeat = null,
    IReadOnlyDictionary<int, string>? Positions = null,
    IReadOnlyDictionary<(int ClubId, CompetitionStandingField Field), string>? StandingPoints = null,
    byte? VarType = null, bool? VarTvOnly = null, bool? VarEnabled = null, string? VarFromDate = null,
    IReadOnlyDictionary<int, string>? CommonRules = null, bool? ClassificationEnabled = null)
{
    public static CompetitionDraft Empty { get; } = new(null, null, null, null, null, null, null, null);

    public bool Any => new[] { Reputation, SubstitutionRules, Foreground, Background, Trim, WinterBall, WinterBallStart, WinterBallEnd, PrizeWin, PrizeDraw, PrizeDefeat }
        .Any(v => v is not null) || Positions is { Count: > 0 } || StandingPoints is { Count: > 0 }
        || VarType is not null || VarTvOnly is not null || VarEnabled is not null || VarFromDate is not null
        || CommonRules is { Count: > 0 } || ClassificationEnabled is not null;
}

/// <summary>
/// One competition's edits and what each was read from. The editor keeps what it read while edits are pending, so a save checks
/// against the values the user saw even if the game moved on.
/// </summary>
internal sealed class CompetitionState
{
    public CompetitionDraft Draft { get; set; } = CompetitionDraft.Empty;
    public CompetitionPrizeMoney? Prize { get; set; }
    public CompetitionClassificationPrizes? Classification { get; set; }
    public CompetitionDetails? Details { get; set; }
    public Dictionary<int, CompetitionSquadRules> CommonRules { get; } = [];
    public IReadOnlyList<CompetitionStanding>? Standings { get; set; }
    public CompetitionVar? Var { get; set; }
}

/// <summary>Competitions: reputation, colours, winter ball, VAR, prize money, squad rules and standings.</summary>
internal sealed class CompetitionScreen(IScreenHost host)
    : EntityScreen<CompetitionRecord, CompetitionRow, CompetitionState>(host, "competition", "Competitions", "competitions", "Search competition or ID...")
{
    static readonly IReadOnlyDictionary<int, string> CommonRuleLabels = new Dictionary<int, string>
    {
        [0x09] = "Maximum squad size",
        [0x0B] = "Homegrown At Club",
        [0x12] = "Non-EU players",
        [0x26] = "Homegrown At Club",
        [0x27] = "Homegrown in Nation",
        [0x57] = "Goalkeepers in the squad",
        [0x6E] = "Squad salary",
        [0x92] = "Homegrown in Nation",
        [0xA5] = "Minimum squad size",
        [0xA9] = "Outfield players in the squad",
        [0xB0] = "Reserve players, not homegrown",
        [0xFD] = "Homegrown in Nation",
        [0x134] = "First-Year Senior Players In Squad",
        [0x1B4] = "Club-trained players in match squad",
    };

    /// <summary>Rules FMRTE labels in its Squad Registration screen. The main card shows these first.</summary>
    static readonly HashSet<int> FmrteRuleCodes = [0x09, 0x0B, 0x12, 0x26, 0x27, 0x57, 0x6E, 0x92, 0xA5, 0xA9, 0xB0, 0xFD, 0x134];

    /// <summary>Rules the game applies to the match squad rather than the registered squad.</summary>
    static readonly HashSet<int> MatchRuleCodes = [0x02, 0x04, 0x3E, 0x40, 0xCF, 0x1B4];

    static string RuleLabel(int code, bool? isMinimum)
    {
        string label = CommonRuleLabels.TryGetValue(code, out var fmrte)
            ? fmrte
            : CompetitionRuleNames.Name(code) ?? $"Rule 0x{code:X}";
        if (isMinimum == true && !label.StartsWith("Minimum", StringComparison.Ordinal)) label += " (min)";
        else if (isMinimum == false && !label.StartsWith("Maximum", StringComparison.Ordinal)) label += " (max)";
        return label;
    }

    /// <summary>FMRTE's club-type conditions on the conditional Liga Portugal rules; other markers stay raw.</summary>
    static string ConditionLabel(string key) => key switch
    {
        "74627770" => "Portuguese Club with B Teams",
        "33326E62" => "Portuguese Club with B Teams (Without U23 Team)",
        "33327577" => "Portuguese Club with U23 Team (Without B Teams)",
        "3332626E" => "Portuguese Club with no B or U23 Team",
        "74627774" => "Clubs With B Teams",
        _ => $"condition {key}",
    };

    /// <summary>Read-only detail rows; anything the reader could not validate reads as unknown.</summary>
    static List<(string Label, Control Value)> DetailRows(CompetitionDetails d)
    {
        static TextBlock Text(string value, bool unknown = false) =>
            new() { Text = value, Foreground = unknown ? UiKit.TextSecondary : UiKit.Brush("#E2E8F0"), TextAlignment = TextAlignment.Right };
        static string YesNo(bool? value) => value is { } v ? (v ? "Yes" : "No") : "Unknown";
        static bool? Flag(byte? value) => value is { } v ? v != 0 : null;
        string Position(short? value) =>
            value is null ? "Unknown" : value < 0 ? "Not available" : value.Value.ToString(Inv);
        return
        [
            ("Three-Letter Name", (Control)Text(d.ThreeLetterName ?? "Unknown", d.ThreeLetterName is null)),
            ("Type", Text(d.TypeName ?? "Unknown", d.TypeName is null)),
            ("Competition Level", Text(d.CompetitionLevel?.ToString(Inv) ?? "Unknown", d.CompetitionLevel is null)),
            ("Trophy", Text(d.TrophyName ?? "Unknown", d.TrophyName is null)),
            ("Lowest Official Category", Text(d.LowestOfficialCategoryName ?? "Unknown", d.LowestOfficialCategoryName is null)),
            ("Continental Position", Text(Position(d.ContinentalPosition), d.ContinentalPosition is null)),
            ("Last Continental Position", Text(Position(d.LastContinentalPosition), d.LastContinentalPosition is null)),
            ("Uses Extra Officials", Text(YesNo(Flag(d.UsesExtraOfficials)), d.UsesExtraOfficials is null)),
            ("Seated Stadiums Only", Text(YesNo(Flag(d.SeatedStadiumsOnly)), d.SeatedStadiumsOnly is null)),
        ];
    }

    string sectionKey = "Overview";
    bool fallbackUsed;
    Fm27Editor.Core.Players.PlayerRecord[] cachedPlayers = [];
    readonly List<string> steps = [];

    protected override bool HasRail => true;

    protected override void ConfigureColumns(DataGrid grid)
    {
        grid.Columns.Add(new DataGridTextColumn { Header = "Competition", Binding = new Binding(nameof(CompetitionRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var reputation = new DataGridTextColumn { Header = "Reputation", Binding = new Binding(nameof(CompetitionRow.Reputation)), Width = new DataGridLength(115) };
        reputation.CellStyleClasses.Add("numeric");
        grid.Columns.Add(reputation);
        var id = new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(CompetitionRow.Id)), Width = new DataGridLength(110) };
        id.CellStyleClasses.Add("numeric");
        grid.Columns.Add(id);
    }

    protected override void BeforeLoad() => cachedPlayers = Host.Players().ToArray();

    protected override IReadOnlyList<CompetitionRow> LoadRows(GameSession session)
    {
        fallbackUsed = false;
        try
        {
            return session.Competitions.LoadAll()
                .Select(c => new CompetitionRow(c, c.DisplayName, c.Reputation, c.Id))
                .OrderByDescending(r => r.Reputation).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (MemoryAccessException)
        {
            fallbackUsed = true;
            var sourcePlayers = cachedPlayers.Length > 0 ? (IReadOnlyList<Fm27Editor.Core.Players.PlayerRecord>)cachedPlayers : session.Players.LoadAll();
            var active = new List<CompetitionRow>();
            var seen = new HashSet<ulong>();
            foreach (var p in sourcePlayers)
            {
                if (p.Division is not { Address: > 0 } division || !seen.Add(division.Address)) continue;
                try
                {
                    if (session.Competitions.Reload(division.Address) is { } c) active.Add(new CompetitionRow(c, c.DisplayName, c.Reputation, c.Id));
                }
                catch (MemoryAccessException) { }
            }
            return active.OrderByDescending(r => r.Reputation).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    protected override string LoadedMessage(int count) => fallbackUsed
        ? $"{count:N0} active divisions loaded (full competition list unavailable on this build)"
        : $"{count:N0} competitions";

    protected override bool Matches(CompetitionRow row, string q) =>
        Fold(row.Name).Contains(q, StringComparison.Ordinal) || row.Id.ToString(Inv).Contains(q, StringComparison.Ordinal);

    protected override CompetitionRecord RecordOf(CompetitionRow row) => row.Competition;
    protected override ulong AddressOf(CompetitionRecord record) => record.Address;
    protected override string NameOf(CompetitionRecord record) => record.DisplayName;
    protected override CompetitionState NewDraft() => new();
    protected override bool HasChanges(CompetitionRecord record, CompetitionState state) => state.Draft.Any;
    protected override CompetitionRecord? Reload(CompetitionRecord record) => Host.Backend.Session.Competitions.Reload(record.Address);
    protected override CompetitionRow Refresh(CompetitionRow row, CompetitionRecord fresh) => row with { Competition = fresh, Reputation = fresh.Reputation };

    static List<SubstitutionOption> SubstitutionOptions(byte current)
    {
        var list = Fm27Editor.Core.Competitions.SubstitutionRules.All.Select(x => new SubstitutionOption(x.Value, x.Label)).ToList();
        if (list.All(o => o.Value != current)) list.Insert(0, new SubstitutionOption(current, $"Value 0x{current:X2}"));
        return list;
    }

    static bool Same(string? text, string? original) => (text?.Trim() ?? "") == (original?.Trim() ?? "");

    static uint? ParseColour(string? text)
    {
        var t = text?.Trim().TrimStart('#');
        return uint.TryParse(t, NumberStyles.HexNumber, Inv, out uint value) ? value : null;
    }

    static string Date(DateOnly? d) => d is { } v ? DateSetting.Format(v) : "";
    static string Hex(uint argb) => $"#{argb:X8}";

    /// <summary>A date entry: typed in any layout or picked from the calendar.</summary>
    static TextBox DateEntry(string? text, int width) => new DateBox { Text = text ?? "", Width = width, MinHeight = 24, HorizontalContentAlignment = HorizontalAlignment.Right };

    static TextBox Field(string? text, int width, bool right = false) => new()
    {
        Text = text ?? "", Width = width, MinHeight = 24, Padding = new Thickness(5, 1),
        HorizontalContentAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    /// <summary>What was read for this competition: kept while edits are pending, read again otherwise.</summary>
    static T? Held<T>(bool pending, T? kept, Func<T?> read, Action<T?> keep) where T : class
    {
        if (pending && kept is not null) return kept;
        var fresh = read();
        keep(fresh);
        return fresh;
    }

    protected override Func<ulong, int, Fm27Editor.Core.History.ChangeLabel?>? LabelsOf(CompetitionRecord c) =>
        Fm27Editor.Core.History.EntityChangeLabels.ForCompetition(Host.Backend.Session.Profile, c.Address);

    protected override void BuildEditor(CompetitionRecord c, CompetitionState state, Action changed)
    {
        var session = Host.Backend.Session;
        var peers = Rows.Count > 0 ? Rows.Select(row => row.Competition).ToList() : null;
        var draft = state.Draft;
        bool pending = draft.Any;

        var prize = Held(pending, state.Prize, () => session.CompetitionPrizeMoney.Read(c), v => state.Prize = v);
        var classification = Held(pending, state.Classification, () => prize is null ? null : session.CompetitionPrizeMoney.ReadClassification(prize), v => state.Classification = v);
        var details = Held(pending, state.Details, () => session.CompetitionDetails.Read(c), v => state.Details = v);
        var standings = Held(pending, state.Standings, () => session.CompetitionStandings.Read(c), v => state.Standings = v);
        var varSettings = Held(pending, state.Var, () => session.CompetitionVar.Read(c), v => state.Var = v);

        var reputation = Field(draft.Reputation ?? c.Reputation.ToString(Inv), 76, true);
        var subsOptions = SubstitutionOptions(c.SubstitutionRules);
        byte currentSubs = byte.TryParse(draft.SubstitutionRules, out byte drafted) ? drafted : c.SubstitutionRules;
        var subs = new ComboBox { Width = 196, ItemsSource = subsOptions, SelectedItem = subsOptions.First(o => o.Value == currentSubs) };

        var colourFields = new List<(CompetitionColour Slot, uint Current, Border Swatch, TextBox Box)>();
        foreach (var (current, slot) in new (uint, CompetitionColour)[]
        {
            (c.Foreground, CompetitionColour.Foreground), (c.Background, CompetitionColour.Background),
            (c.Trim, CompetitionColour.Trim), (c.WinterBallColour, CompetitionColour.WinterBall),
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
            var swatch = new Border { Width = 70, Height = 16, Background = new SolidColorBrush(Color.FromUInt32(ParseColour(box.Text) ?? current)), BorderBrush = UiKit.Brush("#3A3F55"), BorderThickness = new Thickness(1) };
            colourFields.Add((slot, current, swatch, box));
        }
        var start = DateEntry(draft.WinterBallStart ?? Date(c.WinterBallStart), 130);
        var end = DateEntry(draft.WinterBallEnd ?? Date(c.WinterBallEnd), 130);
        var prizeWin = prize is null ? null : Field(draft.PrizeWin ?? prize.PerWin.ToString(Inv), 132, true);
        var prizeDraw = prize is null ? null : Field(draft.PrizeDraw ?? prize.PerDraw.ToString(Inv), 132, true);
        var prizeDefeat = prize is null ? null : Field(draft.PrizeDefeat ?? prize.PerDefeat.ToString(Inv), 132, true);
        var positionFields = classification?.Amounts.Select((value, index) => Field(draft.Positions?.GetValueOrDefault(index + 1) ?? value.ToString(Inv), 132, true)).ToArray() ?? [];
        var classificationEnabled = classification is null ? null : new CheckBox { IsChecked = draft.ClassificationEnabled ?? (classification.Amounts.Count > 0) };
        var commonRuleFields = new List<(int Code, string Label, CompetitionSquadRules? Snapshot, TextBox Box)>();
        var ruleCodes = session.CompetitionSquadRules.RuleCodes(c).ToArray();
        if (ruleCodes.Length == 0) ruleCodes = CommonRuleLabels.Keys.ToArray();
        var variantsByCode = session.CompetitionSquadRules.ReadVariants(c)
            .GroupBy(variant => variant.Code).ToDictionary(group => group.Key, group => group.ToArray());
        foreach (int code in ruleCodes)
        {
            CompetitionSquadRules? snapshot;
            if (pending && state.CommonRules.TryGetValue(code, out var staged)) snapshot = staged;
            else
            {
                snapshot = session.CompetitionSquadRules.ReadRule(c, code, null, peers);
                if (snapshot is not null) state.CommonRules[code] = snapshot; else state.CommonRules.Remove(code);
            }
            string label = RuleLabel(code, snapshot?.IsMinimum);
            if (snapshot is not null)
            {
                commonRuleFields.Add((code, label, snapshot, Field(draft.CommonRules?.GetValueOrDefault(code) ?? snapshot.MaximumSquadSize.ToString(Inv), 116, true)));
                continue;
            }
            // The game can hold several records with the same code (club-type or stage conditions).
            // Show each one with its condition instead of silently picking the first.
            if (variantsByCode.TryGetValue(code, out var variants) && variants.Length > 0)
            {
                foreach (var variant in variants)
                {
                    string variantLabel = RuleLabel(code, variant.IsMinimum);
                    if (variant.Condition.Length > 0) variantLabel += " — " + ConditionLabel(variant.Condition);
                    var variantBox = Field(variant.Value.ToString(Inv), 116, true);
                    variantBox.IsReadOnly = true;
                    ToolTip.SetTip(variantBox, variant.Condition.Length > 0
                        ? "Read-only: conditional variant; the game keeps more than one value for this rule."
                        : "Read-only: the game has no single validated value for this rule.");
                    commonRuleFields.Add((code, variantLabel, null, variantBox));
                }
                continue;
            }
            var primary = session.CompetitionSquadRules.ReadPrimaryValue(c, code);
            if (primary is not { } value) continue;
            var readOnly = Field(value.ToString(Inv), 116, true);
            readOnly.IsReadOnly = true;
            ToolTip.SetTip(readOnly, "Read-only: the game has no single validated value for this rule.");
            commonRuleFields.Add((code, label, null, readOnly));
        }
        // A competition with no explicit maximum rule can still carry one in the fallback structure. Keep the old
        // "Maximum squad size" field for those, rather than silently dropping it from the generic list.
        if (commonRuleFields.All(item => item.Code != 0x09) &&
            session.CompetitionSquadRules.Read(c, peers) is { } fallbackMaximum)
        {
            state.CommonRules[0x09] = fallbackMaximum;
            commonRuleFields.Add((0x09, RuleLabel(0x09, false), fallbackMaximum,
                Field(draft.CommonRules?.GetValueOrDefault(0x09) ?? fallbackMaximum.MaximumSquadSize.ToString(Inv), 116, true)));
        }
        var varOptions = new List<SubstitutionOption> { new(1, "Referee Review"), new(2, "Remote Review"), new(3, "Referee And/Or Remote Review"), new(4, "Unknown VAR Type"), new(5, "No VAR") };
        if (varSettings is not null && varOptions.All(o => o.Value != varSettings.Type)) varOptions.Insert(0, new(varSettings.Type, $"Value 0x{varSettings.Type:X2}"));
        var varType = varSettings is null ? null : new ComboBox { Width = 198, ItemsSource = varOptions, SelectedItem = varOptions.First(o => o.Value == (draft.VarType ?? varSettings.Type)) };
        var varTvOnly = varSettings is null ? null : new CheckBox { IsChecked = draft.VarTvOnly ?? (varSettings.TvOnly != 0) };
        var varEnabled = varSettings is null ? null : new CheckBox { IsChecked = draft.VarEnabled ?? (varSettings.Enabled != 0) };
        var varFromDate = varSettings is null ? null : DateEntry(draft.VarFromDate ?? Date(varSettings.FromDate), 130);
        CompetitionStandingField[] standingColumns = [CompetitionStandingField.Played, CompetitionStandingField.Won, CompetitionStandingField.Drawn,
            CompetitionStandingField.Lost, CompetitionStandingField.GoalsFor, CompetitionStandingField.GoalsAgainst, CompetitionStandingField.Points];
        var standingFields = standings?.SelectMany(row => standingColumns.Select(field =>
            (Row: row, Kind: field, Box: Field(draft.StandingPoints?.GetValueOrDefault((row.ClubId, field)) ?? CompetitionStandingsAccess.Value(row, field).ToString(Inv), 50, true)))).ToArray() ?? [];
        foreach (var item in standingFields) { item.Box.Width = double.NaN; item.Box.MinWidth = 0; item.Box.HorizontalAlignment = HorizontalAlignment.Stretch; }

        // Any control change rebuilds the draft from the controls, so a staged colour can never be lost by another edit.
        void Restage()
        {
            static string? Diff(string? text, string original) => Same(text, original) ? null : text;
            state.Draft = new CompetitionDraft(
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
                standingFields.Select(item => (Key: (item.Row.ClubId, item.Kind), Text: Diff(item.Box.Text, CompetitionStandingsAccess.Value(item.Row, item.Kind).ToString(Inv))))
                    .Where(item => item.Text is not null).ToDictionary(item => item.Key, item => item.Text!),
                varSettings is not null && varType!.SelectedItem is SubstitutionOption varOption && varOption.Value != varSettings.Type ? varOption.Value : null,
                varSettings is not null && varTvOnly!.IsChecked != (varSettings.TvOnly != 0) ? varTvOnly.IsChecked : null,
                varSettings is not null && varEnabled!.IsChecked != (varSettings.Enabled != 0) ? varEnabled.IsChecked : null,
                varSettings is null ? null : Diff(varFromDate!.Text, Date(varSettings.FromDate)),
                commonRuleFields.Where(item => item.Snapshot is not null).Select(item => (item.Code, Text: Diff(item.Box.Text, item.Snapshot!.MaximumSquadSize.ToString(Inv))))
                    .Where(item => item.Text is not null).ToDictionary(item => item.Code, item => item.Text!),
                classification is not null && classificationEnabled!.IsChecked != (classification.Amounts.Count > 0) ? classificationEnabled.IsChecked : null);
            changed();
        }
        reputation.TextChanged += (_, _) => Restage();
        subs.SelectionChanged += (_, _) => Restage();
        foreach (var (_, current, swatch, box) in colourFields)
            box.TextChanged += (_, _) => { swatch.Background = new SolidColorBrush(Color.FromUInt32(ParseColour(box.Text) ?? current)); Restage(); };
        start.TextChanged += (_, _) => Restage();
        end.TextChanged += (_, _) => Restage();
        if (prizeWin is not null) prizeWin.TextChanged += (_, _) => Restage();
        if (prizeDraw is not null) prizeDraw.TextChanged += (_, _) => Restage();
        if (prizeDefeat is not null) prizeDefeat.TextChanged += (_, _) => Restage();
        foreach (var box in positionFields) box.TextChanged += (_, _) => Restage();
        if (classificationEnabled is not null) classificationEnabled.IsCheckedChanged += (_, _) =>
        {
            Restage();
            if (classificationEnabled.IsChecked == true && classification is { Amounts.Count: 0 } && standings is { Count: > 0 } &&
                (classification.Begin == 0 || classification.Capacity - classification.Begin < (ulong)standings.Count * 4))
                Host.SetStatus("Enabling allocates a prize table outside the game's allocator; keep a backup of the save.");
        };
        foreach (var item in commonRuleFields) item.Box.TextChanged += (_, _) => Restage();
        foreach (var item in standingFields) item.Box.TextChanged += (_, _) => Restage();
        if (varType is not null) varType.SelectionChanged += (_, _) => Restage();
        if (varTvOnly is not null) varTvOnly.IsCheckedChanged += (_, _) => Restage();
        if (varEnabled is not null) varEnabled.IsCheckedChanged += (_, _) => Restage();
        if (varFromDate is not null) varFromDate.TextChanged += (_, _) => Restage();

        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        badges.Children.Add(UiKit.MetaText($"ID {c.Id.ToString(Inv)}"));
        if (standings is { Count: > 0 })
        {
            badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText($"{standings.Count} teams", highlight: true));
        }
        var repText = new TextBlock
        {
            Text = c.Reputation.ToString(Inv), FontSize = 16, FontWeight = FontWeight.Bold, Foreground = UiKit.Brush("#C084FC"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
        };
        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children = { UiKit.StatBox("REP", repText) },
        };
        HeaderHost.Content = UiKit.EntityBanner(c.DisplayName, badges, headerActions, Host.Graphics.Image("comp", c.Id, 72));

        // Overview
        var overviewLeft = new StackPanel { Spacing = 12 };
        overviewLeft.Children.Add(CardRows("Competition", [("Reputation", reputation), ("Substitution Rules", subs)]));
        if (details is not null)
            overviewLeft.Children.Add(CardRows("Details", DetailRows(details)));
        if (varSettings is not null)
            overviewLeft.Children.Add(CardRows("VAR Settings", [
                ("Enable custom VAR settings", varEnabled!), ("VAR", varType!), ("From date", varFromDate!), ("VAR - TV Matches Only", varTvOnly!),
            ]));
        var colourRows = new List<(string Label, Control Value)>();
        foreach (var (slot, _, _, box) in colourFields)
            colourRows.Add((slot switch
            {
                CompetitionColour.Foreground => "Foreground Colour",
                CompetitionColour.Background => "Background Colour",
                CompetitionColour.Trim => "Trim Colour",
                _ => "Winter Ball Colour",
            }, ColourInputControl.Create(box)));
        colourRows.Add(("Winter Ball Start Date", start));
        colourRows.Add(("Winter Ball End Date", end));
        var overviewRight = CardRows("Colours & Winter Ball", colourRows);
        var overview = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        Grid.SetColumn(overviewLeft, 0);
        Grid.SetColumn(overviewRight, 2);
        overview.Children.Add(overviewLeft);
        overview.Children.Add(overviewRight);
        var sections = new List<SectionHost.Section> { new("Overview", "Overview", overview) };

        // Prize Money
        if (prize is not null || classificationEnabled is not null)
        {
            Border? matchCard = prize is null ? null : CardRows("Match Prize Money", [("Per Win", prizeWin!), ("Per Draw", prizeDraw!), ("Per Defeat", prizeDefeat!)]);
            Border? classCard = classificationEnabled is null ? null : CardRows("Classification Prize Money",
                new[] { ("Enabled", (Control)classificationEnabled) }.Concat(positionFields.Select((box, index) => ($"Position {index + 1}", (Control)box))));
            Control body;
            if (matchCard is not null && classCard is not null)
            {
                matchCard.VerticalAlignment = VerticalAlignment.Top;
                body = TwoColumnsWide(matchCard, classCard);
            }
            else
            {
                var single = (matchCard ?? classCard)!;
                single.MaxWidth = 540;
                single.HorizontalAlignment = HorizontalAlignment.Left;
                body = single;
            }
            sections.Add(new("Prize Money", "Prize Money", body));
        }

        // Rule presets: stage a planner's values into the draft boxes; Save applies them through the guarded writer.
        if (commonRuleFields.Any(item => item.Snapshot is not null))
        {
            void StagePreset(Func<RulePresetPlan> plan)
            {
                RulePresetPlan p;
                try { p = plan(); }
                catch (Exception ex) { Host.SetStatus($"Preset: {ex.Message}"); return; }
                int staged = 0;
                foreach (var edit in p.Edits)
                {
                    var box = commonRuleFields.FirstOrDefault(item => item.Code == edit.Code && item.Snapshot is not null).Box;
                    if (box is null || box.IsReadOnly) continue;
                    box.Text = edit.Value.ToString(Inv);
                    staged++;
                }
                var notes = new List<string> { $"Preset staged {staged} rule(s); press Save to apply" };
                notes.AddRange(p.Skipped.Select(skip => $"{RuleLabel(skip.Code, null)} skipped: {skip.Reason}"));
                Host.SetStatus(string.Join("; ", notes));
            }
            var access = session.CompetitionSquadRules;
            var nationality = new Button { Content = "Lift nationality limits" };
            nationality.Click += (_, _) => StagePreset(() => CompetitionRulePresets.LiftNationality(c, access, peers));
            var loans = new Button { Content = "Lift loan limits" };
            loans.Click += (_, _) => StagePreset(() => CompetitionRulePresets.LiftLoans(c, access, peers));
            sections.Add(new("Rule Presets", "Rule Presets",
                new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Children = { nationality, loans } }));
        }

        // Squad Rules: FMRTE's labelled squad rules first, then match-squad rules, then the remaining codes.
        var primaryFields = commonRuleFields.Where(item => FmrteRuleCodes.Contains(item.Code))
            .Select(item => (item.Label, (Control)item.Box)).ToList();
        var matchFields = commonRuleFields.Where(item => !FmrteRuleCodes.Contains(item.Code) && MatchRuleCodes.Contains(item.Code))
            .Select(item => (item.Label, (Control)item.Box)).ToList();
        var otherFields = commonRuleFields.Where(item => !FmrteRuleCodes.Contains(item.Code) && !MatchRuleCodes.Contains(item.Code))
            .Select(item => (item.Label, (Control)item.Box)).ToList();
        var sharedNames = new List<string>();
        foreach (var snapshot in commonRuleFields.Select(item => item.Snapshot))
        {
            if (snapshot is null) continue;
            foreach (int id in snapshot.AlsoAppliesTo)
            {
                string name = Rows.FirstOrDefault(row => row.Id == id)?.Name ?? id.ToString(Inv);
                if (!sharedNames.Contains(name, StringComparer.OrdinalIgnoreCase)) sharedNames.Add(name);
            }
        }
        if (sharedNames.Count > 0)
            (primaryFields.Count > 0 ? primaryFields : matchFields.Count > 0 ? matchFields : otherFields).Add(("Also Applies To",
                new TextBlock { Text = string.Join("\n", sharedNames), Foreground = UiKit.TextSecondary, TextAlignment = TextAlignment.Right }));
        if (primaryFields.Count > 0)
        {
            var squadCard = CardRows("Squad Registration", primaryFields);
            squadCard.MaxWidth = 620;
            squadCard.HorizontalAlignment = HorizontalAlignment.Left;
            sections.Add(new("Squad Rules", "Squad Rules", squadCard));
        }
        if (matchFields.Count > 0)
        {
            var matchCard = CardRows("Match Rules", matchFields);
            matchCard.MaxWidth = 620;
            matchCard.HorizontalAlignment = HorizontalAlignment.Left;
            sections.Add(new("Match Rules", "Match Rules", matchCard));
        }
        if (otherFields.Count > 0)
        {
            var otherCard = CardRows("Other Rules", otherFields);
            otherCard.MaxWidth = 620;
            otherCard.HorizontalAlignment = HorizontalAlignment.Left;
            sections.Add(new("Other Rules", "Other Rules", otherCard));
        }

        // Standings
        if (standingFields.Length > 0) sections.Add(new("Standings", "Standings", UiKit.Card("League Table", StandingsTable(standings!, standingFields, standingColumns))));

        string chosen = sections.Any(s => s.Key == sectionKey) ? sectionKey : sections[0].Key;
        sectionKey = chosen;
        Editor.Children.Add(SectionHost.Build("competition", sections, chosen, key => sectionKey = key, RailHost));
    }

    static Grid TwoColumnsWide(Control left, Control right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    Control StandingsTable(IReadOnlyList<CompetitionStanding> standings, (CompetitionStanding Row, CompetitionStandingField Kind, TextBox Box)[] fields, CompetitionStandingField[] columns)
    {
        var names = Host.Clubs().ToDictionary(club => club.Address);
        var table = new StackPanel { Spacing = 0 };
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
        table.Children.Add(new Border { Background = UiKit.Brush("#161A26"), BorderBrush = UiKit.BorderSubtle, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 5), Child = headerGrid });

        int index = 0;
        foreach (var row in standings)
        {
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*,50,46,46,46,48,48,54"), ColumnSpacing = 4 };
            var position = Label(row.Position.ToString(Inv), "#94A3B8");
            position.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(position, 0);
            rowGrid.Children.Add(position);

            ulong clubAddress = row.Club;
            var name = Label(names.GetValueOrDefault(clubAddress)?.DisplayName ?? row.ClubId.ToString(Inv), "#F1F5F9");
            name.FontWeight = FontWeight.Medium;
            name.Cursor = new Cursor(StandardCursorType.Hand);
            name.PointerPressed += async (_, e) => { if (e.ClickCount >= 2 && clubAddress != 0) await Host.OpenClubAsync(clubAddress); };
            Grid.SetColumn(name, 1);
            rowGrid.Children.Add(name);

            foreach (var item in fields.Where(item => item.Row.ClubId == row.ClubId))
            {
                Grid.SetColumn(item.Box, 2 + Array.IndexOf(columns, item.Kind));
                rowGrid.Children.Add(item.Box);
            }
            table.Children.Add(new Border { Background = index++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent, Padding = new Thickness(10, 2), Child = rowGrid });
        }
        return table;
    }

    // Saving. Every edit is parsed first; then each runs in turn, so a failure part-way leaves earlier edits written (and recorded).
    protected override string? Validate(CompetitionRecord record, CompetitionState state, out Func<int> write)
    {
        write = () => 0;
        var d = state.Draft;
        var session = Host.Backend.Session;
        var writer = session.CompetitionWriter;

        if (d.ClassificationEnabled is not null && d.Positions is { Count: > 0 }) return "Save prize amounts before changing Enabled";

        short? reputation = null;
        if (d.Reputation is { } repText)
        {
            if (!short.TryParse(repText.Trim(), NumberStyles.Integer, Inv, out short value) || value is < 0 or > CompetitionWriter.MaxReputation)
                return $"Reputation: enter a whole number 0 to {CompetitionWriter.MaxReputation}";
            reputation = value;
        }
        byte? substitution = null;
        if (d.SubstitutionRules is { } subsText)
        {
            if (!byte.TryParse(subsText.Trim(), NumberStyles.Integer, Inv, out byte value)) return "Substitution rules: enter a value 0 to 255";
            substitution = value;
        }
        var colours = new List<(uint Argb, uint Current, CompetitionColour Slot, string Name)>();
        foreach (var (text, current, slot, name) in new (string?, uint, CompetitionColour, string)[]
        {
            (d.Foreground, record.Foreground, CompetitionColour.Foreground, "foreground"),
            (d.Background, record.Background, CompetitionColour.Background, "background"),
            (d.Trim, record.Trim, CompetitionColour.Trim, "trim"),
            (d.WinterBall, record.WinterBallColour, CompetitionColour.WinterBall, "winter ball colour"),
        })
        {
            if (text is null) continue;
            if (ParseColour(text) is not { } argb) return $"The {name} colour must be #AARRGGBB";
            colours.Add((argb, current, slot, name));
        }
        var winterDates = new List<(DateOnly? Target, DateOnly? Current, bool Start, string Name)>();
        foreach (var (text, current, isStart, name) in new (string?, DateOnly?, bool, string)[]
        {
            (d.WinterBallStart, record.WinterBallStart, true, "winter ball start"), (d.WinterBallEnd, record.WinterBallEnd, false, "winter ball end"),
        })
        {
            if (text is null) continue;
            DateOnly? target = null;
            if (text.Trim().Length > 0)
            {
                if (!DateSetting.TryParse(text.Trim(), out var parsed)) return $"The {name} date must be {DateSetting.Hint} or empty";
                target = parsed;
            }
            winterDates.Add((target, current, isStart, name));
        }
        var prizeEdits = new List<(CompetitionPrizeField Field, int Value)>();
        foreach (var (field, text) in new (CompetitionPrizeField, string?)[] { (CompetitionPrizeField.PerWin, d.PrizeWin), (CompetitionPrizeField.PerDraw, d.PrizeDraw), (CompetitionPrizeField.PerDefeat, d.PrizeDefeat) })
        {
            if (text is null) continue;
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, Inv, out int value) || value < 0) return $"{field}: enter a non-negative whole number";
            prizeEdits.Add((field, value));
        }
        var positionEdits = new List<(int Position, int Value)>();
        foreach (var (position, text) in d.Positions ?? new Dictionary<int, string>())
        {
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, Inv, out int value) || value < 0) return $"Position {position}: enter a non-negative whole number";
            positionEdits.Add((position, value));
        }
        var commonRuleEdits = new List<(int Code, int Value)>();
        foreach (var (code, text) in d.CommonRules ?? new Dictionary<int, string>())
        {
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, Inv, out int value) || value < 0) return "Squad rule: enter a non-negative whole number";
            commonRuleEdits.Add((code, value));
        }
        var standingEdits = new List<((int ClubId, CompetitionStandingField Field) Key, int Value)>();
        foreach (var (key, text) in d.StandingPoints ?? new Dictionary<(int, CompetitionStandingField), string>())
        {
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, Inv, out int value) ||
                (key.Field == CompetitionStandingField.Points ? value is < short.MinValue or > short.MaxValue : value is < byte.MinValue or > byte.MaxValue))
                return $"{key.Field}: invalid value";
            standingEdits.Add((key, value));
        }
        DateOnly? varFromDate = null;
        if (d.VarFromDate is { } dateText && dateText.Trim().Length > 0)
        {
            if (!DateSetting.TryParse(dateText.Trim(), out var parsedDate)) return $"VAR date must be {DateSetting.Hint} or empty";
            varFromDate = parsedDate;
        }
        var varEdits = new List<(CompetitionVarField Field, byte Value)>();
        if (d.VarType is { } vt) varEdits.Add((CompetitionVarField.Type, vt));
        if (d.VarTvOnly is { } tv) varEdits.Add((CompetitionVarField.TvOnly, tv ? (byte)1 : (byte)0));
        if (d.VarEnabled is { } enabled) varEdits.Add((CompetitionVarField.Enabled, enabled ? (byte)1 : (byte)0));

        write = () =>
        {
            steps.Clear();
            var c = record;
            if (reputation is { } rep)
            {
                if (rep != c.Reputation) steps.Add($"reputation {c.Reputation} → {rep}");
                writer.SetReputation(c, rep);
            }
            if (substitution is { } subs)
            {
                if (subs != c.SubstitutionRules) steps.Add($"substitution rules 0x{c.SubstitutionRules:X2} → 0x{subs:X2}");
                writer.SetSubstitutionRules(c, subs);
            }
            foreach (var (argb, current, slot, name) in colours)
            {
                if (argb == current) continue;
                steps.Add($"{name} #{current:X8} → #{argb:X8}");
                writer.SetColour(c, slot, argb);
                c = session.Competitions.Reload(c.Address) ?? c;
            }
            foreach (var (target, current, isStart, name) in winterDates)
            {
                if (target == current) continue;
                steps.Add($"{name} {(current is { } was ? DateSetting.Format(was) : "none")} → {(target is { } will ? DateSetting.Format(will) : "none")}");
                writer.SetWinterBallDate(c, isStart, target);
                c = session.Competitions.Reload(c.Address) ?? c;
            }
            if (prizeEdits.Count > 0)
            {
                var snapshot = state.Prize ?? throw new InvalidOperationException("Prize money snapshot is unavailable");
                foreach (var (field, value) in prizeEdits) { session.CompetitionPrizeMoney.Set(snapshot, field, value); steps.Add($"{field}: {value}"); }
            }
            if (positionEdits.Count > 0)
            {
                var snapshot = state.Classification ?? throw new InvalidOperationException("Classification prize snapshot is unavailable");
                foreach (var (position, value) in positionEdits) { session.CompetitionPrizeMoney.SetClassification(snapshot, position, value); steps.Add($"position {position}: {value}"); }
            }
            if (d.ClassificationEnabled is { } isEnabled)
            {
                var snapshot = state.Classification ?? throw new InvalidOperationException("Classification prize snapshot is unavailable");
                session.SetCompetitionClassificationEnabled(snapshot, isEnabled);
                steps.Add($"classification prize: {(isEnabled ? "enabled" : "disabled")}");
            }
            var squadEdits = new List<(CompetitionSquadRules Snapshot, int Value)>();
            foreach (var (code, value) in commonRuleEdits)
                squadEdits.Add((state.CommonRules.GetValueOrDefault(code) ?? throw new InvalidOperationException("Squad rule snapshot is unavailable"), value));
            if (squadEdits.Count > 0)
            {
                session.CompetitionSquadRules.SetMany(squadEdits);
                steps.Add($"{squadEdits.Count} squad rule(s)");
            }
            if (standingEdits.Count > 0)
            {
                var snapshot = state.Standings ?? throw new InvalidOperationException("Standings snapshot is unavailable");
                foreach (var (key, value) in standingEdits)
                {
                    session.CompetitionStandings.Set(c, snapshot.Single(item => item.ClubId == key.ClubId), key.Field, value);
                    steps.Add($"club {key.ClubId} {key.Field}: {value}");
                }
            }
            if (varEdits.Count > 0 || d.VarFromDate is not null)
            {
                var snapshot = state.Var ?? throw new InvalidOperationException("VAR snapshot is unavailable");
                foreach (var (field, value) in varEdits) { session.CompetitionVar.Set(snapshot, field, value); steps.Add($"VAR {field}: {value}"); }
                if (d.VarFromDate is not null)
                {
                    session.CompetitionVar.SetFromDate(snapshot, varFromDate);
                    steps.Add($"VAR from date: {(varFromDate is { } from ? DateSetting.Format(from) : "none")}");
                }
            }
            return steps.Count;
        };
        return null;
    }

    protected override bool OnSaveFailed(CompetitionRecord record, Exception error)
    {
        Drafts.Remove(record.Address);
        ReloadAndRender(record);
        Host.SetStatus(steps.Count == 0 ? error.Message : $"Saved {steps.Count} change(s), then: {error.Message}");
        return true;
    }
}

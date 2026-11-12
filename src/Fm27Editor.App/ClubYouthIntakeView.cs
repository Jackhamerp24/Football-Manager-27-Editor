using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

/// <summary>Staged youth-intake edits for one club: snapshots read from the game plus changed values.</summary>
sealed class YouthIntakeDraft
{
    public Dictionary<ulong, YouthCandidate> Snapshots { get; set; } = [];
    public Dictionary<(ulong Address, string Field), int> Staged { get; } = [];
    public HashSet<(ulong Address, string Field)> Invalid { get; } = [];
    public int Count => Staged.Count;
    public int InvalidCount => Invalid.Count;
    public bool HasChanges => Staged.Count > 0 && Invalid.Count == 0;

    /// <summary>Fresh candidates replace snapshots without staged edits; staged rows keep their baseline for stale checks.</summary>
    public void Absorb(IEnumerable<YouthCandidate> fresh)
    {
        var stagedAddresses = new HashSet<ulong>(Staged.Keys.Select(k => k.Address));
        var next = new Dictionary<ulong, YouthCandidate>();
        foreach (var c in fresh)
            next[c.Address] = stagedAddresses.Contains(c.Address) && Snapshots.TryGetValue(c.Address, out var old) ? old : c;
        Snapshots = next;
    }
}

public sealed partial class MainWindow
{
    readonly Dictionary<ulong, YouthIntakeDraft> youthIntakeDrafts = [];

    static int YouthValue(YouthCandidate c, string field) =>
        field switch
        {
            "CA" => c.CurrentAbility,
            "PA" => c.PotentialAbility,
            "Height" => c.Height,
            _ when field.StartsWith("attr:", StringComparison.Ordinal) =>
                c.Attribute(Enum.Parse<PlayerAttribute>(field[5..])),
            _ => c.Position(Enum.Parse<PlayerPosition>(field[4..])),
        };

    static (int Min, int Max) YouthBounds(string field) =>
        field switch
        {
            "CA" or "PA" => (1, 200),
            "Height" => (100, 250),
            _ => (1, 20),
        };

    static void ApplyYouthField(YouthCandidateWriter writer, YouthCandidate snapshot, string field, int value)
    {
        if (field == "CA") writer.SetCurrentAbility(snapshot, value);
        else if (field == "PA") writer.SetPotentialAbility(snapshot, value);
        else if (field == "Height") writer.SetHeight(snapshot, value);
        else if (field.StartsWith("attr:", StringComparison.Ordinal))
            writer.SetAttribute(snapshot, Enum.Parse<PlayerAttribute>(field[5..]), value);
        else writer.SetPosition(snapshot, Enum.Parse<PlayerPosition>(field[4..]), value);
    }

    static string YouthPositionLabel(PlayerPosition p) => p.ToString();

    string YouthWindowSuffix(ClubEntry club)
    {
        // The game-read window takes precedence when it is ever found in memory; until then the nation's
        // usual window from the intake guide stands in, always labelled as usual, never as game data.
        string? nation = null;
        try
        {
            ulong nationAddress = backend.Session.NationOfClub(club.Address);
            nation = nationCatalogue?.FirstOrDefault(n => n.Address == nationAddress)?.DisplayName;
        }
        catch { }
        var usual = YouthIntakeWindows.UsualFor(nation);
        return usual is null ? "" : $" · usually {YouthIntakeWindows.FormatUsual(usual)}";
    }

    async void RenderClubYouthIntake(GameSession session, ClubEntry club)
    {
        var version = clubRenderGeneration;
        var section = selectedClubSection;
        IReadOnlyList<YouthCandidate> candidates = [];
        DateOnly? today = null;
        try
        {
            var result = await Task.Run(() =>
            {
                var list = session.YouthCandidates.ReadForClub(club.Address);
                return (List: list, Date: session.ReadGameDate());
            });
            candidates = result.List;
            today = result.Date;
        }
        catch (Exception ex) { if (version == clubRenderGeneration && clubsActive) status.Text = UserMessage.For(ex); return; }
        if (version != clubRenderGeneration || !clubsActive || selectedClub?.Address != club.Address || selectedClubSection != section) return;

        foreach (var c in candidates) c.GameDate = today;
        if (!youthIntakeDrafts.TryGetValue(club.Address, out var draft))
            youthIntakeDrafts[club.Address] = draft = new();
        draft.Absorb(candidates);

        string suffix = YouthWindowSuffix(club);
        clubEditorPanel.Children.Add(new TextBlock
        {
            Text = $"Youth Intake · {candidates.Count} candidate{(candidates.Count == 1 ? "" : "s")}{suffix}",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            FontSize = 13.5,
            Foreground = Brush("#F1F5F9"),
            Margin = new Thickness(4, 0, 0, 10),
        });

        if (session.Profile.YouthCandidates is null)
        {
            clubEditorPanel.Children.Add(YouthLine("Youth intake is not available for this game build."));
            return;
        }
        if (candidates.Count == 0)
        {
            clubEditorPanel.Children.Add(YouthLine($"No youth candidates.{suffix}"));
            return;
        }

        var key = (club.Address, section);
        int selected = clubListSelectedRow.TryGetValue(key, out int saved) ? Math.Clamp(saved, 0, candidates.Count - 1) : 0;
        clubListSelectedRow[key] = selected;
        var detail = new Border { Margin = new Thickness(0, 8, 0, 0) };
        var grid = BuildYouthIntakeGrid(candidates);
        grid.SelectionMode = Avalonia.Controls.DataGridSelectionMode.Single;
        grid.MaxHeight = 29 * 12 + 30;
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is not YouthCandidate c) return;
            clubListSelectedRow[key] = candidates.ToList().IndexOf(c);
            detail.Child = BuildYouthIntakeDetail(club, draft, c);
        };
        clubEditorPanel.Children.Add(grid);
        clubEditorPanel.Children.Add(detail);
        grid.SelectedItem = candidates[selected];
        detail.Child = BuildYouthIntakeDetail(club, draft, candidates[selected]);
    }

    /// <summary>The candidate list in the editor's own table (same look as Squads), one row per candidate.</summary>
    static Avalonia.Controls.DataGrid BuildYouthIntakeGrid(IReadOnlyList<YouthCandidate> candidates)
    {
        var grid = ToolGrid.Create();
        grid.Columns.Add(ToolGrid.Text<YouthCandidate>("Name", c => c.DisplayName, 190, "Name", "#F1F5F9", bold: true));
        grid.Columns.Add(ToolGrid.Text<YouthCandidate>("Position", c => c.PositionSummary, 110, "Positions rated 15 or more"));
        grid.Columns.Add(ToolGrid.Number<YouthCandidate>("Age", c => c.Age?.ToString(Inv) ?? "", 46, "Age"));
        grid.Columns.Add(ToolGrid.Number<YouthCandidate>("Born", c => c.DateOfBirth is { } d ? DateSetting.Format(d) : "", 96, "Date of birth"));
        grid.Columns.Add(ToolGrid.Text<YouthCandidate>("Nationality", c => c.NationalityName ?? "", 130, "Nationality"));
        grid.Columns.Add(ToolGrid.Text<YouthCandidate>("City", c => c.CityOfBirthName ?? "", 150, "City of birth"));
        grid.Columns.Add(ToolGrid.Number<YouthCandidate>("Height", c => c.Height.ToString(Inv), 56, "Height (cm)"));
        grid.Columns.Add(ToolGrid.Number<YouthCandidate>("Left", c => c.Attribute(PlayerAttribute.LeftFoot).ToString(Inv), 46, "Left foot"));
        grid.Columns.Add(ToolGrid.Number<YouthCandidate>("Right", c => c.Attribute(PlayerAttribute.RightFoot).ToString(Inv), 46, "Right foot"));
        grid.Columns.Add(ToolGrid.Number<YouthCandidate>("CA", c => c.CurrentAbility.ToString(Inv), 46, "Current Ability"));
        grid.Columns.Add(ToolGrid.Number<YouthCandidate>("PA", c => c.PotentialAbility.ToString(Inv), 46, "Potential Ability"));
        ToolGrid.SetItems(grid, candidates.ToList());
        return grid;
    }

    /// <summary>
    /// One candidate's attributes in the player Information page's look: the same cards, zebra rows, labels and coloured
    /// 1-20 rating boxes (<see cref="AttributeCard"/>, <see cref="AttributeRow"/>, <see cref="RatingBox"/>), Technical and
    /// Goalkeeping behind the same Tec./GK switch, then Mental, Physical with Hidden, and Ability.
    /// </summary>
    Control BuildYouthIntakeDetail(ClubEntry club, YouthIntakeDraft draft, YouthCandidate candidate)
    {
        var snapshot = draft.Snapshots.GetValueOrDefault(candidate.Address, candidate);

        int Current(string field) => draft.Staged.TryGetValue((snapshot.Address, field), out int staged) ? staged : YouthValue(snapshot, field);
        void Stage(string field, int v)
        {
            var key = (snapshot.Address, field);
            draft.Invalid.Remove(key);
            if (v == YouthValue(snapshot, field)) draft.Staged.Remove(key);
            else draft.Staged[key] = v;
            UpdateClubStatusFromDraft();
        }

        Control Rating(string label, string field)
        {
            var (min, max) = YouthBounds(field);
            return AttributeRow(label, RatingBox(Current(field), min, max, v => Stage(field, v), () => Current(field)));
        }

        // CA, PA and height are not 1-20 ratings: a plain number box, checked against the field's range.
        Control Number(string label, string field)
        {
            var (min, max) = YouthBounds(field);
            var key = (snapshot.Address, field);
            var box = new TextBox
            {
                Text = draft.Invalid.Contains(key) ? "" : Current(field).ToString(Inv), Width = 56, MinWidth = 0, Height = 20, MinHeight = 20,
                Padding = new Thickness(4, 0), FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold,
                HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center,
            };
            box.TextChanged += (_, _) =>
            {
                if (int.TryParse(box.Text, out int v) && v >= min && v <= max) Stage(field, v);
                else { draft.Invalid.Add(key); draft.Staged.Remove(key); UpdateClubStatusFromDraft(); }
            };
            return AttributeRow(label, box);
        }

        string Label(PlayerAttribute a) => PlayerFields.All.First(f => f.Id == a.ToString()).Label;
        Control Group(string title, AttributeGroup group, Control? header = null) =>
            AttributeCard(title, Enum.GetValues<PlayerAttribute>().Where(a => PlayerAttributeInfo.GroupOf(a) == group)
                .Select(a => Rating(Label(a), $"attr:{a}")), header);

        // Technical / Goalkeeping switch, as on the player page; a goalkeeper candidate opens on Goalkeeping.
        var picker = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var technical = new Button { Content = "Tec.", Padding = new Thickness(7, 0), MinHeight = 20, Height = 20, FontSize = 12 };
        var goalkeeping = new Button { Content = "GK", Padding = new Thickness(7, 0), MinHeight = 20, Height = 20, FontSize = 12 };
        var firstColumn = new ContentControl { HorizontalAlignment = HorizontalAlignment.Stretch };
        void Select(bool keeper)
        {
            technical.Classes.Set("accent", !keeper);
            goalkeeping.Classes.Set("accent", keeper);
            if (picker.Parent is Panel oldParent) oldParent.Children.Remove(picker);
            firstColumn.Content = keeper ? Group("Goalkeeping", AttributeGroup.Goalkeeping, picker) : Group("Technical", AttributeGroup.Technical, picker);
        }
        technical.Click += (_, _) => Select(false);
        goalkeeping.Click += (_, _) => Select(true);
        picker.Children.Add(technical); picker.Children.Add(goalkeeping);
        Select(Current($"pos:{PlayerPosition.Goalkeeper}") >= 15);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 6 };
        Control[] parts =
        [
            firstColumn,
            Group("Mental", AttributeGroup.Mental),
            new StackPanel { Spacing = 8, Children = { Group("Physical", AttributeGroup.Physical), Group("Hidden", AttributeGroup.Hidden) } },
            AttributeCard("Ability", [Number("Current Ability", "CA"), Number("Potential Ability", "PA"), Number("Height", "Height")]),
        ];
        for (int i = 0; i < parts.Length; i++) { Grid.SetColumn(parts[i], i); columns.Children.Add(parts[i]); }

        var positions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 14, LineSpacing = 2, Margin = new Thickness(4, 2) };
        foreach (var p in Enum.GetValues<PlayerPosition>())
        {
            var field = $"pos:{p}";
            var (min, max) = YouthBounds(field);
            var row = AttributeRow(PositionLabels.Short(p), RatingBox(Current(field), min, max, v => Stage(field, v), () => Current(field)));
            row.Width = 74;
            positions.Children.Add(row);
        }

        return new StackPanel { Spacing = 8, Children = { columns, UiKit.Card("Positions", positions) } };
    }

    static TextBlock YouthLine(string text) => new()
    {
        Text = text,
        Foreground = Brush("#94A3B8"),
        FontSize = 13,
        Margin = new Thickness(4, 12, 0, 0),
    };

    /// <summary>Writes the staged youth edits as one history group; returns how many values were written.</summary>
    int SaveYouthIntake(GameSession session, ClubEntry club)
    {
        if (!youthIntakeDrafts.TryGetValue(club.Address, out var draft)) return 0;
        if (draft.InvalidCount > 0) throw new FormatException("Fix invalid youth intake values before saving");
        if (draft.Staged.Count == 0) return 0;
        var writer = session.YouthCandidateWriter;
        int saved = 0;
        using (backend.Edits.Group("Club", club.DisplayName, "Youth intake save"))
        {
            foreach (var ((address, field), value) in draft.Staged.ToArray())
            {
                if (!draft.Snapshots.TryGetValue(address, out var snapshot)) continue;
                backend.Edits.Execute(() => ApplyYouthField(writer, snapshot, field, value));
                saved++;
            }
        }
        var fresh = session.YouthCandidates.ReadForClub(club.Address);
        draft.Snapshots = fresh.ToDictionary(c => c.Address);
        draft.Staged.Clear();
        return saved;
    }
}

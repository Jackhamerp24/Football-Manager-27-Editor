using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

/// <summary>
/// The coach General page laid out as FMRTE 27's: Personal Details, Reputation and Ability on the left, Training Rating
/// on the right, each a card of the usual field rows (action, then value).
/// </summary>
internal sealed partial class MassEditWindow
{
    const int DetailCaps = 0, DetailGoals = 1, DetailAge = 2, DetailLicense = 3, DetailNationality = 4, DetailRole = 100, DetailTraining = 200;

    /// <summary>FMRTE's Roles Attributes list order.</summary>
    static readonly StaffRole[] RoleOrder =
    [
        StaffRole.AssistantManager, StaffRole.Coach, StaffRole.FitnessCoach, StaffRole.GoalkeepingCoach, StaffRole.Manager,
        StaffRole.Physio, StaffRole.Scout, StaffRole.DirectorOfFootball, StaffRole.HeadOfYouthDevelopment, StaffRole.Chairperson,
        StaffRole.DataAnalyst, StaffRole.SportsScientist, StaffRole.LoanManager, StaffRole.TechnicalDirector, StaffRole.SetPieceCoach,
    ];

    IReadOnlyList<(int Id, ulong Address, string Name)> nationChoices = [];

    /// <summary>Two cards side by side leave less room: a narrower action column and a plain number box, as FMRTE's.</summary>
    const string CompactColumns = "*,118,64";

    Control CoachGeneral()
    {
        try
        {
            var personal = Panel();
            Add(personal, Kind.StaffDetail, DetailAge, "Personal Details", "Age", 14, 100);
            AddNationPicker(personal, Kind.StaffDetail);
            Add(personal, Kind.StaffDetail, DetailCaps, "Personal Details", "Caps", 0, 10000);
            Add(personal, Kind.StaffDetail, DetailGoals, "Personal Details", "Goals", 0, 10000);
            AddSelect(personal, Kind.StaffDetail, DetailLicense, "License", StaffDetailsMassEdit.Licenses.Select(l => ((int)l.Value, l.Name)).ToList());

            var reputation = Panel();
            Add(reputation, Kind.StaffField, (int)StaffMassField.HomeReputation, "Reputation", "Home", 0, 10000);
            Add(reputation, Kind.StaffField, (int)StaffMassField.CurrentReputation, "Reputation", "Current", 0, 10000);
            Add(reputation, Kind.StaffField, (int)StaffMassField.WorldReputation, "Reputation", "World", 0, 10000);

            var ability = Panel();
            Add(ability, Kind.StaffField, (int)StaffMassField.CurrentAbility, "Ability", "CA", 1, 200);
            Add(ability, Kind.StaffField, (int)StaffMassField.PotentialAbility, "Ability", "PA", 1, 200);
            AddCheck(ability, "Match CA to PA", out _);

            var contract = Panel();
            Add(contract, Kind.StaffWage, 0, "Contract", "Wages", 0, 10_000_000);

            var training = Panel();
            foreach (var c in Enum.GetValues<TrainingCategory>()) AddStars(training, c);

            var outer = Panel();
        foreach (var (title, body) in new (string, StackPanel)[] { ("Personal Details", personal), ("Reputation", reputation), ("Ability", ability), ("Contract", contract) })
        {
            AddHeader(outer, title);
            foreach (var child in body.Children.ToArray()) { body.Children.Remove(child); outer.Children.Add(child); }
        }
        AddHeader(outer, "Training Rating", "How good this coach is at training each area, from 0 to 5 stars, based on his attributes. " +
            "Changing a rating changes the attributes behind it, so other ratings can move too.");
        foreach (var child in training.Children.ToArray()) { training.Children.Remove(child); outer.Children.Add(child); }
        return outer;
        }
        finally { }
    }

    /// <summary>
    /// A dropdown with no action combo, as FMRTE's Nationality and License: the first entry ("Unset") leaves the field alone,
    /// any other entry sets it.
    /// </summary>
    readonly List<ComboBox> nationBoxes = [];
    bool nationsLoading;

    /// <summary>
    /// A Nationality dropdown that opens at once with "Unset"; the nation list is read in the background (it takes a moment
    /// on a full save) and fills every Nationality dropdown of the window when ready.
    /// </summary>
    void AddNationSelect(StackPanel panel, Kind kind)
    {
        AddSelect(panel, kind, DetailNationality, "Nationality", []);
        var row = fields[^1];
        var box = (ComboBox)row.ValueControl;
        nationBoxes.Add(box);
        // The row reads the chosen nation's id from the list as it is when Review runs.
        fields[^1] = new FieldRow(row.Kind, row.Index, row.Group, row.Label, int.MinValue, int.MaxValue, row.Mode, box,
            () => box.SelectedIndex > 0 && box.SelectedIndex - 1 < nationChoices.Count ? nationChoices[box.SelectedIndex - 1].Id : 0,
            () => box.SelectedIndex = 0);
        if (nationsLoading || nationChoices.Count > 0 || backend.State.Status != ConnectionStatus.Connected) { Fill(); return; }
        nationsLoading = true;
        _ = Task.Run(() => backend.Session.Nations.LoadAll().Where(n => n.Id is > 0 && n.Name.Length > 0)
                .OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).Select(n => (n.Id!.Value, n.Address, n.Name)).ToList())
            .ContinueWith(t => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                nationsLoading = false;
                if (t.IsCompletedSuccessfully) { nationChoices = t.Result; Fill(); }
            }));

        void Fill()
        {
            foreach (var b in nationBoxes)
            {
                if (b.SelectedIndex > 0) continue;
                b.ItemsSource = new[] { "Unset" }.Concat(nationChoices.Select(n => n.Name)).ToList();
                b.SelectedIndex = 0;
            }
        }
    }

    sealed class NationPick(int id, string name) { public int Id { get; } = id; public string Name { get; } = name; }

    /// <summary>Nationality as a search picker — the same PickerDialog the Advanced Search reuses; "Unset" leaves it alone.</summary>
    void AddNationPicker(StackPanel panel, Kind kind)
    {
        var mode = new ComboBox { ItemsSource = new[] { "Don't change", "Set to" }, SelectedIndex = ModeNone, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Stretch };
        var pick = new Button { Content = "Unset", FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        int picked = 0;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(rowColumns), ColumnSpacing = 10, Margin = new Thickness(4, 1) };
        Grid.SetColumn(pick, 1);
        Grid.SetColumnSpan(pick, 2);
        grid.Children.Add(new TextBlock { Text = "Nationality", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary });
        grid.Children.Add(pick);
        panel.Children.Add(grid);
        var row = new FieldRow(kind, DetailNationality, "Personal Details", "Nationality", int.MinValue, int.MaxValue, mode, pick,
            () => picked,
            () => { picked = 0; pick.Content = "Unset"; mode.SelectedIndex = ModeNone; });
        fields.Add(row);
        mode.SelectionChanged += (_, _) => MarkDirty();
        pick.Click += async (_, _) =>
        {
            if (nationChoices.Count == 0 && backend.State.Status == ConnectionStatus.Connected)
                nationChoices = await Task.Run(() => backend.Session.Nations.LoadAll().Where(n => n.Id is > 0 && n.Name.Length > 0)
                    .OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).Select(n => (n.Id!.Value, n.Address, n.Name)).ToList());
            var options = nationChoices.Select(n => (n.Name, new NationPick(n.Id, n.Name))).ToList();
            options.Insert(0, ("Unset", new NationPick(0, "Unset")));
            var chosen = await PickerDialog.Show<NationPick>(this, "Choose nationality", q => options.Where(o => PickerSearch.Hit(q, o.Item2.Name)).ToList());
            if (chosen is null) return;
            picked = chosen.Id;
            pick.Content = chosen.Name;
            mode.SelectedIndex = picked == 0 ? ModeNone : ModeSet;
            MarkDirty();
        };
    }
    void AddSelect(StackPanel panel, Kind kind, int index, string label, IReadOnlyList<(int Code, string Name)> options)
    {
        var mode = new ComboBox { ItemsSource = new[] { "Don't change", "Set to" }, SelectedIndex = ModeNone };
        var names = new List<string> { "Unset" };
        names.AddRange(options.Select(o => o.Name));
        var choice = new ComboBox { ItemsSource = names, SelectedIndex = 0, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 420 };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(rowColumns), ColumnSpacing = 10, Margin = new Thickness(4, 1) };
        var text = new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary };
        Grid.SetColumn(choice, 1);
        Grid.SetColumnSpan(choice, 2);
        grid.Children.Add(text);
        grid.Children.Add(choice);
        panel.Children.Add(grid);
        var codes = options.Select(o => o.Code).ToArray();
        var row = new FieldRow(kind, index, "Personal Details", label, codes.DefaultIfEmpty().Min(), codes.DefaultIfEmpty().Max(), mode, choice,
            () => choice.SelectedIndex > 0 ? codes[choice.SelectedIndex - 1] : 0, () => choice.SelectedIndex = 0);
        fields.Add(row);
        choice.SelectionChanged += (_, _) => { mode.SelectedIndex = choice.SelectedIndex > 0 ? ModeSet : ModeNone; MarkDirty(); };
    }

    /// <summary>A training rating row: action, then stars 0-5 in half steps (kept as half-stars in the row's number).</summary>
    void AddStars(StackPanel panel, TrainingCategory category)
    {
        var mode = new ComboBox { ItemsSource = new[] { "Don't change", "Set to", "Change by" }, SelectedIndex = ModeNone, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Stretch };
        var value = new NumericUpDown { Minimum = -5, Maximum = 5, Increment = 0.5m, Value = 0, FontSize = 12.5, FormatString = "0.0", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch, ShowButtonSpinner = false, HorizontalContentAlignment = HorizontalAlignment.Right };
        panel.Children.Add(Row(TrainingRatings.Label(category), mode, value));
        var row = new FieldRow(Kind.StaffDetail, DetailTraining + (int)category, "Training Rating", TrainingRatings.Label(category), -10, 10, mode, value,
            () => (long)Math.Round((value.Value ?? 0) * 2), () => value.Value = 0);
        fields.Add(row);
        mode.SelectionChanged += (_, _) =>
        {
            value.IsEnabled = mode.SelectedIndex != ModeNone;
            value.Minimum = mode.SelectedIndex == ModeSet ? 0 : -5;
            MarkDirty();
        };
        value.ValueChanged += (_, _) => MarkDirty();
    }

    IEnumerable<StaffDetailChange> CoachDetailPlan(IReadOnlyList<StaffRecord> staff, FieldInput row, MassEditMode mode)
    {
        var writer = backend.Session.StaffWriter;
        int value = (int)row.Number;
        return row.Index switch
        {
            DetailCaps => StaffDetailsMassEdit.Caps(staff, mode, value, writer),
            DetailGoals => StaffDetailsMassEdit.Goals(staff, mode, value, writer),
            DetailAge => StaffDetailsMassEdit.Age(staff, mode, value,
                backend.Session.ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read"), writer),
            DetailLicense => StaffDetailsMassEdit.License(staff, (StaffLicense)(sbyte)value, writer),
            DetailNationality => nationChoices.FirstOrDefault(n => n.Id == value) is { Address: not 0 } nation
                ? StaffDetailsMassEdit.Nationality(staff, nation.Id, nation.Address, writer)
                : throw new ArgumentException("Choose a nation"),
            >= DetailTraining => StaffDetailsMassEdit.Training(staff, (TrainingCategory)(row.Index - DetailTraining), mode, value / 2.0, writer),
            >= DetailRole => StaffDetailsMassEdit.Role(staff, (StaffRole)(row.Index - DetailRole), mode, value, writer),
            _ => throw new ArgumentException("Unknown coach field"),
        };
    }

    /// <summary>Review text for a detail value: names for licence and nationality, numbers otherwise.</summary>
    object ShowDetail(FieldInput row, int value) => row.Index switch
    {
        DetailLicense => StaffDetailsMassEdit.Licenses.FirstOrDefault(l => (int)l.Value == value).Name ?? $"Code {value}",
        DetailNationality => nationChoices.FirstOrDefault(n => n.Id == value).Name ?? (value == 0 ? "" : $"Nation {value}"),
        >= DetailTraining => $"{value / 2.0:0.0} stars",
        _ => value,
    };
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    /// <summary>One promise on the page: the controls and the values they hold until Save changes.</summary>
    sealed class PromiseEditor(PromiseRecord record)
    {
        public PromiseRecord Record { get; } = record;
        public ComboBox Type { get; } = new() { MinWidth = 420 };
        public DateBox Made { get; } = new() { Width = 120 };
        public DateBox Deadline { get; } = new() { Width = 120 };
        public int Parameter0 { get; set; } = record.Parameter0;
        public int Parameter1 { get; set; } = record.Parameter1;
        public int TypeId => (Type.SelectedItem as PromiseType)?.Id ?? record.TypeId;

        /// <summary>The list for the type box: FMRTE's 68 types, plus "Unknown (N)" when the game made a promise of a kind FMRTE does not list (67 on the Steam save).</summary>
        public IReadOnlyList<PromiseType> TypeChoices => PromiseTypes.Find(record.TypeId) is null
            ? [.. PromiseTypes.All, new PromiseType(record.TypeId, $"Unknown ({record.TypeId})", PromiseParameter.None)]
            : PromiseTypes.All;
    }

    /// <summary>FMRTE's Promises tab: each promise's type, manager, what it asks for, and the two dates. Add and Remove write at once like FMRTE's +/−.</summary>
    Control PromisesPanel(PlayerRecord player)
    {
        var session = backend.Session;
        var store = session.Promises;
        var panel = new StackPanel { Spacing = 8, MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Left };
        if (!store.Available) return panel;
        var owner = session.Owner(player);
        var promises = store.Read(owner);
        var editors = promises.Select(p => new PromiseEditor(p)).ToList();
        var save = new Button { IsVisible = false, Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };

        IReadOnlyList<Fm27Editor.Core.Competitions.CompetitionRecord> Competitions() =>
            unifiedCompetitions.Count > 0 ? unifiedCompetitions : unifiedCompetitions = session.Competitions.LoadAll();
        IReadOnlyList<Fm27Editor.Core.Staff.StaffRecord> Staff() =>
            unifiedStaff.Count > 0 ? unifiedStaff : unifiedStaff = session.Staff.LoadAll();
        string CompetitionName(int index) => index < 0 ? "" : Competitions().FirstOrDefault(c => c.Index == index)?.Name ?? $"Competition {index}";
        string PersonName(int index) => index < 0 ? "" : PersonNames().TryGetValue((uint)index, out var n) ? n : $"Person {index}";
        // The person who made the promise is usually a manager, but the game also makes some on a player's own behalf (Bruno Fernandes shows himself).
        string ManagerName(ulong person) => Staff().FirstOrDefault(s => s.Address == person)?.DisplayName
            ?? AllPlayers().FirstOrDefault(p => p.Address + (ulong)(session.Profile.Relations?.PlayerPerson ?? 0x288) == person)?.DisplayName ?? "";

        bool Pending(PromiseEditor e)
        {
            var r = e.Record;
            return e.TypeId != r.TypeId || e.Made.Text != Day(r.Made) || e.Deadline.Text != Day(r.Deadline)
                || e.Parameter0 != r.Parameter0 || e.Parameter1 != r.Parameter1;
        }
        void Refresh() => save.IsEnabled = discard.IsEnabled = editors.Any(Pending);

        void Change(Action change, string done)
        {
            if (editors.Any(Pending)) { status.Text = "Save or discard the promise edits first"; return; }
            try { backend.Edits.Execute("Player", player.DisplayName, "Promises", change); status.Text = done; RenderEditor(); }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        Control Row(string label, Control value) => new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, MinHeight = 28,
            Children = { new TextBlock { Text = label, Width = 100, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.Brush("#94A3B8") }, value },
        };

        Control Picker(string title, string current, Func<Task<(string Name, int Id)?>> choose, Action<int> set)
        {
            var text = new TextBlock { Text = current, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.Brush("#F1F5F9") };
            var change = new Button { Content = "Change", Margin = new Thickness(8, 0, 0, 0) };
            change.Click += async (_, _) =>
            {
                if (await choose() is not { } picked) return;
                set(picked.Id);
                text.Text = picked.Name;
                Refresh();
            };
            return new StackPanel { Orientation = Orientation.Horizontal, Children = { text, change } };
        }

        Task<(string Name, int Id)?> ChooseCompetition() => PickerDialog.Show(this, "Search Competitions", q =>
        {
            string folded = Fold(q);
            return Competitions().Where(c => c.Name is not null && Fold(c.Name).Contains(folded, StringComparison.Ordinal)).Take(200)
                .Select(c => (c.Name!, (object)c.Index)).ToList();
        }).ContinueWith(t => t.Result is int index ? ((string, int)?)(CompetitionName(index), index) : null);

        Task<(string Name, int Id)?> ChoosePerson() => PickerDialog.Show(this, "Searching Persons", q =>
        {
            string folded = Fold(q);
            var found = new List<(string, object)>();
            foreach (var (index, name) in PersonNames()) if (Fold(name).Contains(folded, StringComparison.Ordinal)) { found.Add((name, (int)index)); if (found.Count >= 200) break; }
            return found;
        }).ContinueWith(t => t.Result is int index ? ((string, int)?)(PersonName(index), index) : null);

        foreach (var e in editors)
        {
            var record = e.Record;
            e.Type.ItemsSource = e.TypeChoices;
            e.Type.SelectedItem = e.TypeChoices.FirstOrDefault(t => t.Id == record.TypeId);
            e.Made.Text = Day(record.Made);
            e.Deadline.Text = Day(record.Deadline);
            var parameterHost = new ContentControl();

            void BuildParameter()
            {
                var type = e.Type.SelectedItem as PromiseType;
                parameterHost.Content = type?.Parameter switch
                {
                    PromiseParameter.Competition => Row("Competition", Picker("Competition", CompetitionName(e.Parameter1), ChooseCompetition, id => e.Parameter1 = id)),
                    PromiseParameter.Person => Row("Person", Picker("Person", PersonName(e.Parameter1), ChoosePerson, id => e.Parameter1 = id)),
                    PromiseParameter.PlayingTime => Row("Playing Time", PlayingTimeBox(e, Refresh)),
                    PromiseParameter.Number => Row("Number", NumberBox(e, Refresh)),
                    _ => null,
                };
            }
            e.Type.SelectionChanged += (_, _) =>
            {
                if (e.Type.SelectedItem is not PromiseType type) return;
                if (type.Id != record.TypeId) { e.Parameter0 = -1; e.Parameter1 = -1; }
                else { e.Parameter0 = record.Parameter0; e.Parameter1 = record.Parameter1; }
                BuildParameter();
                Refresh();
            };
            BuildParameter();
            e.Made.TextChanged += (_, _) => Refresh();
            e.Deadline.TextChanged += (_, _) => Refresh();

            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) => Change(() => store.Remove(owner, record), "Removed the promise. Continue in FM to see updates.");
            var body = new StackPanel
            {
                Spacing = 4, Margin = new Thickness(12, 8),
                Children =
                {
                    Row("Type", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { e.Type, remove } }),
                    Row("Manager", new TextBlock { Text = ManagerName(record.Manager), VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.Brush("#F1F5F9") }),
                    parameterHost,
                    Row("Made on", e.Made),
                    Row("Deadline", e.Deadline),
                },
            };
            panel.Children.Add(UiKit.Card("Promise", body));
        }

        var add = new Button { Content = "Add promise" };
        add.Click += (_, _) =>
        {
            ulong manager = promises.FirstOrDefault()?.Manager ?? 0, team = promises.FirstOrDefault()?.Team ?? 0;
            if (manager == 0 || team == 0)
            {
                var club = player.Club;
                var entry = club is null ? null : session.LoadClubs(AllPlayers()).FirstOrDefault(c => c.Address == club.Address);
                manager = club is null ? 0 : Staff().Where(s => s.JobCode == 16 && s.Club?.Address == club.Address)
                    .OrderByDescending(s => s.TeamName is { } n && (n == club.Name || n == club.ShortName || n == club.DisplayName)).ThenByDescending(s => s.CurrentReputation)
                    .FirstOrDefault()?.Address ?? 0;
                team = entry?.FirstTeam?.Address ?? 0;
            }
            if (manager == 0 || team == 0) { status.Text = $"{player.DisplayName} has no club manager to make the promise"; return; }
            if (session.ReadGameDate() is not { } today) { status.Text = "The game date could not be read"; return; }
            var values = new PromiseValues(1, today, today.AddDays(91), -1, 0);
            Change(() => store.Add(owner, manager, team, values, session.Process.Allocate),
                "Added a promise. Its record lives outside the game's allocator; keep a backup of the save. Continue in FM to see updates.");
        };
        panel.Children.Add(add);

        save.Click += (_, _) =>
        {
            var changes = new List<(PromiseEditor Editor, PromiseValues Values)>();
            foreach (var e in editors.Where(Pending))
            {
                if (!DateBox.TryParse(e.Made.Text, out var made) || !DateBox.TryParse(e.Deadline.Text, out var deadline)) { status.Text = "Promises: enter dates like 2026-12-30"; return; }
                changes.Add((e, new PromiseValues(e.TypeId, made, deadline, e.Parameter0, e.Parameter1)));
            }
            try
            {
                backend.Edits.Execute("Player", player.DisplayName, "Page save", () => { foreach (var (e, v) in changes) store.Update(owner, e.Record, v); });
                status.Text = "Saved promises. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        discard.Click += (_, _) => RenderEditor();
        if (editors.Count > 0) panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
        return panel;
    }

    /// <summary>Every player in the game, not just the rows the list is showing.</summary>
    PlayerRecord[] AllPlayers() => rows.OfType<PlayerRecord>().ToArray() is { Length: > 0 } loaded ? loaded : playerSource ??= backend.Session.Players.LoadAll().ToArray();

    Dictionary<uint, string>? personNames;

    /// <summary>Person index → name for every player and staff member, built once per loaded save.</summary>
    Dictionary<uint, string> PersonNames()
    {
        if (personNames is not null) return personNames;
        var session = backend.Session;
        var store = session.Promises;
        var names = new Dictionary<uint, string>();
        foreach (var p in AllPlayers()) names[store.PersonIndex(p.Address + 0x288)] = $"{p.DisplayName} ({p.ClubName})";
        foreach (var s in unifiedStaff.Count > 0 ? unifiedStaff : unifiedStaff = session.Staff.LoadAll()) names[store.PersonIndex(s.Address)] = $"{s.DisplayName} ({s.Club?.DisplayName})";
        return personNames = names;
    }

    static string Day(DateOnly? d) => DateSetting.Format(d);

    static Control PlayingTimeBox(PromiseEditor e, Action refresh)
    {
        var values = Enum.GetValues<PlayingTime>().Where(v => v != PlayingTime.Unset).ToArray();
        var box = new ComboBox { ItemsSource = values.Select(ContractLabels.Of).ToArray(), MinWidth = 220 };
        box.SelectedIndex = Array.IndexOf(values, (PlayingTime)Math.Clamp(e.Parameter0, 0, 255));
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) { e.Parameter0 = (int)values[box.SelectedIndex]; refresh(); } };
        return box;
    }

    static Control NumberBox(PromiseEditor e, Action refresh)
    {
        var box = new TextBox { Width = 84, Text = e.Parameter0 > 0 ? e.Parameter0.ToString(Inv) : "", HorizontalContentAlignment = HorizontalAlignment.Right };
        box.TextChanged += (_, _) => { e.Parameter0 = int.TryParse(box.Text, out int n) && n is > 0 and <= 99 ? n : -1; refresh(); };
        return box;
    }
}

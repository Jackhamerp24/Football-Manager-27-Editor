using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.App;

public static class ContractYearInput
{
    public static string Format(int? year) => year is null or 1900 ? "" : year.Value.ToString(CultureInfo.InvariantCulture);

    // An empty editor means leave the snapshot alone, never replace it with the sentinel.
    public static string NormalizeDraftValue(string? text, int? original) =>
        string.IsNullOrWhiteSpace(text) ? Format(original) : text;

    public static int Resolve(string? text, int original)
    {
        if (string.IsNullOrWhiteSpace(text)) return original;
        int year = int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        return year is >= 1901 and <= 2300 ? year
            : throw new ArgumentOutOfRangeException(nameof(text), "Year must be 1901-2300");
    }
}

public sealed partial class MainWindow
{
    sealed class ContractDraft(PlayerRecord player, ContractDetails snapshot)
    {
        public PlayerRecord Player = player;
        public ContractDetails Snapshot = snapshot;
        public Dictionary<string, string> Changes = [];
        /// <summary>Transfer clauses as read when the tab opened; Save contract checks them against memory.</summary>
        public IReadOnlyList<TransferClause>? TransferClauses;
    }
    readonly Dictionary<ulong, ContractDraft> contractDrafts = [];
    IReadOnlyList<ClubEntry>? clubCache;
    IReadOnlyDictionary<int, CompetitionInfo>? competitionCache;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string D(DateOnly? d) => d?.ToString("yyyy-MM-dd", Inv) ?? "";

    /// <summary>The Contract tab; with <paramref name="future"/> the same editor on a future transfer's contract (FMRTE's Future Contract).</summary>
    Control ContractPanel(PlayerRecord player, ulong future = 0)
    {
        var writer = future == 0 ? backend.Session.Contracts : backend.Session.FutureContract(future);
        ulong key = future == 0 ? player.Address : future;
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 6), MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Left };
        ContractDraft draft;
        try
        {
            if (!contractDrafts.TryGetValue(key, out draft!))
            {
                var details = writer.Read(player);
                if (details is null) { panel.Children.Add(new TextBlock { Text = "Free agent", Foreground = Brush("#94A3B8") }); return panel; }
                contractDrafts[key] = draft = new(player, details);
            }
        }
        catch (Exception ex) { panel.Children.Add(new TextBlock { Text = UserMessage.For(ex), Foreground = Brush("#94A3B8") }); return panel; }
        var s = draft.Snapshot;
        Button? save = null, discard = null;
        void Changed(string key, string original, string value)
        {
            if (key == "Future Year") value = ContractYearInput.NormalizeDraftValue(value, s.FuturePlayingTimeYear);
            if (value == original) draft.Changes.Remove(key); else draft.Changes[key] = value;
            if (save is not null) save.IsEnabled = draft.Changes.Count > 0;
            if (discard is not null) discard.IsEnabled = draft.Changes.Count > 0;
            status.Text = draft.Changes.Count > 0 ? $"{draft.Changes.Count} unsaved contract change(s)" : "";
        }
        Control Text(string key, string original, double width = 140)
        {
            var box = new TextBox
            {
                Width = width,
                Text = draft.Changes.GetValueOrDefault(key, original),
                HorizontalContentAlignment = HorizontalAlignment.Right
            };
            box.TextChanged += (_, _) => Changed(key, original, box.Text ?? "");
            return box;
        }
        Control Choice<T>(string key, T original, Func<T, string> label, IEnumerable<T> values) where T : struct, Enum
        {
            var items = values.Select(v => new EnumOption<T>(v, label(v))).ToArray();
            var current = draft.Changes.TryGetValue(key, out var changed) && Enum.TryParse<T>(changed, out var c) ? c : original;
            var combo = new ComboBox
            {
                Width = 196,
                ItemsSource = items,
                SelectedItem = items.FirstOrDefault(i => i.Value.Equals(current))
            };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is EnumOption<T> o) Changed(key, original.ToString(), o.Value.ToString()); };
            return combo;
        }

        // Two columns of label + value, in the order FMRTE's contract screen uses.
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*,24,160,*"), RowSpacing = 6 };
        int[] rowOf = [0, 0];
        void Row(int column, string label, Control? control, Control? extra = null)
        {
            if (control is null) return;
            int row = rowOf[column]++;
            while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#CBD5E1") };
            Grid.SetRow(name, row); Grid.SetColumn(name, column == 0 ? 0 : 3); grid.Children.Add(name);
            // Let the value cell shrink with the editor pane instead of overflowing it.
            control.MaxWidth = control.Width;
            control.Width = double.NaN;
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            Control value = control;
            if (extra is not null)
            {
                var pair = new Grid { ColumnDefinitions = new ColumnDefinitions("*,64"), ColumnSpacing = 6 };
                Grid.SetColumn(extra, 1);
                pair.Children.Add(control);
                pair.Children.Add(extra);
                value = pair;
            }
            value.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetRow(value, row); Grid.SetColumn(value, column == 0 ? 1 : 4); grid.Children.Add(value);
        }
        Row(0, "Type", s.Type is { } t ? Choice("Type", t, ContractLabels.Of, Enum.GetValues<ContractType>()) : null);
        Row(0, "Started", s.Started is not null || backend.Session.Profile.Contract?.Started is not null ? Text("Started", D(s.Started)) : null);
        Row(0, "Signed", Text("Signed", D(s.Signed)));
        Row(0, "Wages (per week)", Text("Wage", s.Wage.ToString(Inv)));
        Row(0, "Loyalty Bonus (per week)", s.LoyaltyBonus is { } lb ? Text("Loyalty Bonus", lb.ToString(Inv)) : null);
        Row(0, "Number", s.SquadNumber is { } n ? Text("Number", n.ToString(Inv), 64) : null);
        Row(1, "Expires", Text("Expires", D(s.Expires)));
        Row(1, "Joined Club", Text("Joined Club", D(s.JoinedClub)));
        Row(1, "Agreed Playing Time", s.PlayingTime is { } pt ? Choice("Playing Time", pt, ContractLabels.Of, Enum.GetValues<PlayingTime>()) : null);
        Row(1, "Future Playing Time", s.FuturePlayingTime is { } fp ? Choice("Future Playing Time", fp, ContractLabels.Of, Enum.GetValues<PlayingTime>()) : null,
            s.FuturePlayingTimeYear is { } fy ? Text("Future Year", ContractYearInput.Format(fy), 64) : null);
        Row(1, "Happiness", s.Happiness is { } h ? Text("Happiness", h.ToString(Inv), 64) : null);
        Row(1, "Playing Time Happiness", s.PlayingTimeHappiness is { } ph ? Text("Playing Time Happiness", ph.ToString(Inv), 64) : null);

        panel.Children.Add(new Border
        {
            Background = Brush("#161822"),
            BorderBrush = Brush("#242636"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(14, 12),
            Child = grid
        });

        if (backend.Session.Profile.Contract?.Clauses is not null)
        {
            var clauseStack = new StackPanel { Spacing = 0 };
            clauseStack.Children.Add(new Border
            {
                Background = Brush("#1D1F2D"),
                BorderBrush = Brush("#6D28D9"),
                BorderThickness = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(12, 6),
                Child = new TextBlock
                {
                    Text = "BONUSES & CLAUSES",
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brush("#E2E8F0")
                }
            });

            var terms = new Grid { ColumnDefinitions = new ColumnDefinitions("300,60,80,50,130,*"), RowSpacing = 6, Margin = new Thickness(12, 10, 12, 8) };
            foreach (var term in s.Terms)
            {
                int row = terms.RowDefinitions.Count; terms.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                void Cell(Control c, int col) { c.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetRow(c, row); Grid.SetColumn(c, col); terms.Children.Add(c); }
                var remove = new Button { Content = "Remove" };
                remove.Click += (_, _) => ChangeTerms(player, draft, () => writer.RemoveTerm(draft.Player, draft.Snapshot, term.Index), $"Removed {ContractLabels.Term(term.Type)}", future);
                Cell(remove, 5);
                Cell(new TextBlock { Text = ContractLabels.Term(term.Type), FontSize = 12.5, Foreground = Brush("#F1F5F9"), VerticalAlignment = VerticalAlignment.Center }, 0);
                if (ContractLabels.TermParameter(term.Type) is { } unit)
                {
                    Cell(new TextBlock { Text = unit, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#94A3B8") }, 1);
                    Cell(Text(TermParameterKey(term), term.Parameter.ToString(Inv), 64), 2);
                }
                if (ContractLabels.TermHasValue(term.Type))
                {
                    Cell(new TextBlock { Text = "Value", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#94A3B8") }, 3);
                    Cell(Text(TermValueKey(term), term.Value.ToString(Inv), 116), 4);
                }
            }
            if (s.Terms.Count > 0)
                clauseStack.Children.Add(terms);

            // Add row: type, parameter, value. Written at once, like Remove.
            var addType = new ComboBox { Width = 300, ItemsSource = ContractLabels.Terms.Select(kv => new TermOption(kv.Key, kv.Value)).ToArray(), PlaceholderText = "Add bonus or clause" };
            var addParam = new TextBox { Width = 64, Text = "", IsVisible = false, HorizontalContentAlignment = HorizontalAlignment.Right };
            var addValue = new TextBox { Width = 116, Text = "", HorizontalContentAlignment = HorizontalAlignment.Right };
            var paramLabel = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#94A3B8"), Width = 52 };
            var add = new Button { Content = "Add", IsEnabled = false };
            addType.SelectionChanged += (_, _) =>
            {
                var type = (addType.SelectedItem as TermOption)?.Type;
                paramLabel.Text = type is { } t ? ContractLabels.TermParameter(t) ?? "" : "";
                addParam.IsVisible = paramLabel.Text.Length > 0;
                addValue.IsVisible = type is { } v && ContractLabels.TermHasValue(v);
                add.IsEnabled = type is not null;
            };
            add.Click += (_, _) =>
            {
                if (addType.SelectedItem is not TermOption o) return;
                try
                {
                    int value = addValue.IsVisible ? int.Parse(addValue.Text ?? "", NumberStyles.Integer, Inv) : -1;
                    short parameter = addParam.IsVisible ? short.Parse(addParam.Text ?? "", Inv) : (short)-1;
                    ChangeTerms(player, draft, () => backend.Session.AddContractTerm(draft.Player, draft.Snapshot, o.Type, value, parameter, future), $"Added {o.Label}", future);
                }
                catch (FormatException) { status.Text = "Enter whole numbers for the new clause"; }
            };
            var addBar = new Border
            {
                BorderBrush = Brush("#222432"),
                BorderThickness = new Thickness(0, s.Terms.Count > 0 ? 1 : 0, 0, 0),
                Padding = new Thickness(12, 10),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { addType, paramLabel, addParam, new TextBlock { Text = "Value", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#94A3B8") }, addValue, add }
                }
            };
            clauseStack.Children.Add(addBar);

            panel.Children.Add(new Border
            {
                Background = Brush("#161822"),
                BorderBrush = Brush("#242636"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                ClipToBounds = true,
                Child = clauseStack
            });
        }

        if (future == 0 && TransferClausesPanel(player, draft, (k, original, width) => Text(k, original, width)) is { } transferClauses) panel.Children.Add(transferClauses);
        if (future == 0 && UnhappinessPanel(player) is { } unhappiness) panel.Children.Add(unhappiness);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 0) };
        save = new Button { Content = "Save contract", IsEnabled = draft.Changes.Count > 0, Classes = { "accent" } };
        discard = new Button { Content = "Discard", IsEnabled = draft.Changes.Count > 0 };
        save.Click += (_, _) => SaveContract(player, draft, future);
        discard.Click += (_, _) => { contractDrafts.Remove(key); RenderEditor(); };
        buttons.Children.Add(save); buttons.Children.Add(discard); panel.Children.Add(buttons);
        return panel;
    }

    /// <summary>
    /// Clauses from the player's earlier transfers that his club owes. Value edits go into the contract draft and are
    /// written by Save contract; sell-ons are removed and added at once (beneficiary by club ID). Null for free agents.
    /// </summary>
    Control? TransferClausesPanel(PlayerRecord player, ContractDraft draft, Func<string, string, double, Control> text)
    {
        if (player.Id is not { } id || player.Club is not { } club || backend.Session.Profile.Club?.TransferClauses is null) return null;
        var competitions = competitionCache ??= backend.Session.LoadCompetitions(Players());
        var clauses = draft.TransferClauses ??= backend.Session.ReadTransferClauses(club.Address, competitions).Where(c => c.PlayerId == id).ToList();

        TextBlock Label(string s, string color = "#94A3B8") => new() { Text = s, FontSize = 12.5, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        WrapPanel Line(params Control[] items) { var p = new WrapPanel { Orientation = Orientation.Horizontal }; foreach (var i in items) { i.Margin = new Thickness(0, 0, 6, 0); p.Children.Add(i); } return p; }

        var list = new Grid { ColumnDefinitions = new ColumnDefinitions("130,100,110,*,Auto"), RowSpacing = 6, ColumnSpacing = 12, Margin = new Thickness(12, 10, 12, 8) };
        foreach (var c in clauses)
        {
            int row = list.RowDefinitions.Count; list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            void Cell(Control control, int col) { control.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetRow(control, row); Grid.SetColumn(control, col); list.Children.Add(control); }
            Cell(Label(ContractLabels.Of(c.Kind), "#F1F5F9"), 0);
            Cell(Label(c.Beneficiary?.DisplayName ?? "", "#CBD5E1"), 1);
            string main = ClauseKey(c, "main"), extra = ClauseKey(c, "extra");
            switch (c.Kind)
            {
                case TransferClauseKind.SellOn:
                    Cell(Line(text(main, $"{c.Percentage}", 52), Label("%")), 2);
                    Cell(Line(Label("Signed for"), text(extra, c.SignedFor?.ToString(Inv) ?? "", 110)), 3);
                    var remove = new Button { Content = "Remove" };
                    remove.Click += (_, _) => ClauseChange(player, draft, () => backend.Session.TransferClauses.RemoveSellOn(club.Address, c), "Sell-on removed");
                    Cell(remove, 4);
                    break;
                case TransferClauseKind.AppearanceFee:
                    Cell(text(main, $"{c.Amount}", 110), 2);
                    Cell(Line(Label($"{c.AppearancesSoFar} /"), text(extra, $"{c.Appearances}", 52), Label("apps")), 3);
                    break;
                default:
                    Cell(text(main, $"{c.Amount}", 110), 2);
                    Cell(Line(Label(ContractLabels.Competition(c), "#CBD5E1"), Label("until"), text(extra, D(c.Ends), 96)), 3);
                    break;
            }
        }

        // Add a sell-on: beneficiary club by ID, percentage.
        TextBox Box(double width, string watermark = "") => new() { Width = width, Watermark = watermark, HorizontalContentAlignment = HorizontalAlignment.Right };
        var addClub = Box(90, "Club ID");
        var addPercent = Box(52);
        var add = new Button { Content = "Add sell-on" };
        add.Click += (_, _) =>
        {
            try
            {
                int clubId = int.Parse(addClub.Text ?? "", NumberStyles.Integer, Inv), percent = int.Parse(addPercent.Text ?? "", NumberStyles.Integer, Inv);
                var clubs = clubCache ??= backend.Session.LoadClubs(Players());
                var beneficiary = clubs.FirstOrDefault(k => k.Id == clubId);
                if (beneficiary is null) { status.Text = $"No club with ID {clubId}"; return; }
                ClauseChange(player, draft, () => backend.Session.AddSellOn(club.Address, id, beneficiary.Address, percent), $"Sell-on to {beneficiary.DisplayName} added");
            }
            catch (FormatException) { status.Text = "Enter a club ID and a whole percentage"; }
        };
        var addBar = new Border
        {
            BorderBrush = Brush("#222432"),
            BorderThickness = new Thickness(0, clauses.Count > 0 ? 1 : 0, 0, 0),
            Padding = new Thickness(12, 10),
            Child = Line(Label("Sell-On", "#F1F5F9"), addClub, addPercent, Label("%"), add)
        };

        var stack = new StackPanel
        {
            Children =
            {
                new Border
                {
                    Background = Brush("#1D1F2D"),
                    BorderBrush = Brush("#6D28D9"),
                    BorderThickness = new Thickness(0, 0, 0, 2),
                    Padding = new Thickness(12, 6),
                    Child = new TextBlock { Text = "TRANSFER CLAUSES", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#E2E8F0") }
                }
            }
        };
        if (clauses.Count > 0) stack.Children.Add(list);
        stack.Children.Add(addBar);
        return new Border
        {
            Background = Brush("#161822"),
            BorderBrush = Brush("#242636"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            Child = stack
        };
    }

    /// <summary>Active unhappiness motives (as FMRTE lists them), each removable, plus Remove All. Null when there are none.</summary>
    Control? UnhappinessPanel(PlayerRecord player)
    {
        if (backend.Session.Profile.Unhappiness is null || backend.Session.ReadGameDate() is not { } today) return null;
        var motives = backend.Session.ReadUnhappiness(player).Where(m => m.IsActive(today)).ToList();
        if (motives.Count == 0) return null;

        TextBlock Label(string s, string color = "#94A3B8") => new() { Text = s, FontSize = 12.5, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center };
        void Remove(Func<PlayerUnhappiness, bool>? which, string done)
        {
            try
            {
                int n = 0;
                backend.Edits.Execute(() => n = backend.Session.RemoveUnhappiness(player, which));
                RenderEditor();
                status.Text = $"{done} ({n}). Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        var list = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110,90,90,Auto"), RowSpacing = 6, ColumnSpacing = 12, Margin = new Thickness(12, 10, 12, 8) };
        foreach (var m in motives)
        {
            int row = list.RowDefinitions.Count; list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            void Cell(Control control, int col) { control.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetRow(control, row); Grid.SetColumn(control, col); list.Children.Add(control); }
            Cell(Label(UnhappinessLabels.Motive(m.Motive), "#F1F5F9"), 0);
            Cell(Label(UnhappinessLabels.Type(m.Type), "#CBD5E1"), 1);
            Cell(Label(D(m.Start)), 2);
            Cell(Label(m.End is { } e ? D(e) : ""), 3);
            var remove = new Button { Content = "Remove" };
            int index = m.Index;
            remove.Click += (_, _) => Remove(x => x.Index == index, "Unhappiness removed");
            Cell(remove, 4);
        }
        var removeAll = new Button { Content = "Remove All", Padding = new Thickness(8, 1), FontSize = 11 };
        removeAll.Click += (_, _) => Remove(null, "Unhappiness removed");
        var title = new DockPanel();
        DockPanel.SetDock(removeAll, Dock.Right);
        title.Children.Add(removeAll);
        title.Children.Add(new TextBlock { Text = "UNHAPPINESS", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#E2E8F0"), VerticalAlignment = VerticalAlignment.Center });
        return new Border
        {
            Background = Brush("#161822"),
            BorderBrush = Brush("#242636"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            Child = new StackPanel
            {
                Children =
                {
                    new Border { Background = Brush("#1D1F2D"), BorderBrush = Brush("#6D28D9"), BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(12, 4), Child = title },
                    list
                }
            }
        };
    }

    static string ClauseKey(TransferClause c, string part) => $"xfer {c.Address:X} {part}";

    /// <summary>Remove/add a sell-on right away; refuses while clause value edits are pending, since the list moves.</summary>
    void ClauseChange(PlayerRecord player, ContractDraft draft, Action change, string done)
    {
        if (draft.Changes.Keys.Any(k => k.StartsWith("xfer ", StringComparison.Ordinal)))
        { status.Text = "Save or discard the clause edits first"; return; }
        try
        {
            backend.Edits.Execute(change);
            draft.TransferClauses = null;
            RenderEditor();
            status.Text = $"{done}. Continue in FM to see updates.";
        }
        catch (Exception ex) { status.Text = $"Clause change stopped: {UserMessage.For(ex)}"; }
    }

    /// <summary>Adds or removes a clause right away; pending field edits are kept only if nothing else in the list moved.</summary>
    void ChangeTerms(PlayerRecord player, ContractDraft draft, Action change, string done, ulong future = 0)
    {
        if (draft.Changes.Keys.Any(k => k.StartsWith("term ", StringComparison.Ordinal)))
        { status.Text = "Save or discard the clause edits first"; return; }
        try
        {
            backend.Edits.Execute(change);
            var pending = draft.Changes;
            ulong key = future == 0 ? player.Address : future;
            contractDrafts.Remove(key);
            selected = backend.Players.Reload(player.Address);
            if (selected is not null)
            {
                ReplaceRow(selected);
                var writer = future == 0 ? backend.Session.Contracts : backend.Session.FutureContract(future);
                if (writer.Read(selected) is { } fresh) contractDrafts[key] = new(selected, fresh) { Changes = pending };
            }
            RenderEditor();
            status.Text = $"{done}. Continue in FM to see updates.";
        }
        catch (Exception ex) { status.Text = $"Clause change stopped: {UserMessage.For(ex)}"; }
    }

    static string TermValueKey(ContractTerm t) => $"term {t.Index} value";
    static string TermParameterKey(ContractTerm t) => $"term {t.Index} parameter";

    void SaveContract(PlayerRecord player, ContractDraft draft, ulong future = 0)
    {
        try
        {
            var actions = new List<(string[] keys, Action write)>();
            var c = future == 0 ? backend.Session.Contracts : backend.Session.FutureContract(future); var p = draft.Player; var d = draft.Snapshot;
            void Add<T>(string key, Func<string, T> parse, Action<T> action) { if (draft.Changes.TryGetValue(key, out var value)) { T parsed = parse(value); actions.Add(([key], () => action(parsed))); } }
            int Number(string x) => int.Parse(x, NumberStyles.Integer, Inv);
            DateOnly Date(string x) { var date = DateOnly.ParseExact(x, "yyyy-MM-dd", Inv); return date.Year is >= 1901 and <= 2300 ? date : throw new ArgumentOutOfRangeException(nameof(x), "Year must be 1901-2300"); }
            int Range(string x, int min, int max) { int n = Number(x); return n >= min && n <= max ? n : throw new ArgumentOutOfRangeException(nameof(x), $"Expected {min}-{max}"); }
            T EnumValue<T>(string x) where T : struct, Enum { if (!Enum.TryParse<T>(x, true, out var v) || !Enum.IsDefined(v)) throw new FormatException($"Invalid {typeof(T).Name}: {x}"); return v; }
            // Parse everything first, so a typo stops the save before anything is written.
            Add("Wage", x => Range(x, 0, 10_000_000), x => c.SetWage(p, x)); Add("Expires", Date, x => c.SetExpiry(p, d, x));
            Add("Started", Date, x => c.SetStarted(p, d, x)); Add("Signed", Date, x => c.SetSigned(p, d, x)); Add("Joined Club", Date, x => c.SetJoinedClub(p, d, x));
            Add("Type", EnumValue<ContractType>, x => c.SetType(p, d, x));
            Add("Loyalty Bonus", x => Range(x, 0, 10_000_000), x => c.SetLoyaltyBonus(p, d, x)); Add("Number", x => Range(x, -1, 99), x => c.SetSquadNumber(p, d, x));
            Add("Playing Time", EnumValue<PlayingTime>, x => c.SetPlayingTime(p, d, x));
            Add("Happiness", x => Range(x, -100, 100), x => c.SetHappiness(p, d, x)); Add("Playing Time Happiness", x => Range(x, -100, 100), x => c.SetPlayingTimeHappiness(p, d, x));
            if (draft.Changes.ContainsKey("Future Playing Time") || draft.Changes.ContainsKey("Future Year"))
            {
                var time = EnumValue<PlayingTime>(draft.Changes.GetValueOrDefault("Future Playing Time", d.FuturePlayingTime!.Value.ToString()));
                int year = ContractYearInput.Resolve(draft.Changes.GetValueOrDefault("Future Year"), d.FuturePlayingTimeYear!.Value);
                actions.Add((["Future Playing Time", "Future Year"], () => c.SetFuturePlayingTime(p, d, time, year)));
            }
            foreach (var t in d.Terms)
            {
                string vk = TermValueKey(t), pk = TermParameterKey(t);
                if (draft.Changes.ContainsKey(vk) || draft.Changes.ContainsKey(pk))
                {
                    int v = Number(draft.Changes.GetValueOrDefault(vk, t.Value.ToString(Inv)));
                    short par = short.Parse(draft.Changes.GetValueOrDefault(pk, t.Parameter.ToString(Inv)), Inv);
                    actions.Add(([vk, pk], () => c.SetTerm(p, d, t.Index, v, par)));
                }
            }
            var xw = backend.Session.TransferClauses;
            foreach (var t in draft.TransferClauses ?? [])
            {
                string mk = ClauseKey(t, "main"), ek = ClauseKey(t, "extra");
                bool m = draft.Changes.TryGetValue(mk, out var mv), e = draft.Changes.TryGetValue(ek, out var ev);
                if (m || e)
                {
                    var values = ClauseSaveInput.Parse(t.Kind, m, mv, e, ev);
                    string[] keys = m && e ? [mk, ek] : m ? [mk] : [ek];
                    actions.Add((keys, () => xw.SetValues(t, values.Main, values.ChangeExtra, values.ExtraNumber, values.ExtraDate)));
                }
            }
            int saved = 0;
            foreach (var action in actions) { backend.Edits.Execute(action.write); foreach (var key in action.keys) draft.Changes.Remove(key); saved++; }
            contractDrafts.Remove(future == 0 ? player.Address : future); selected = backend.Players.Reload(player.Address); if (selected is not null) ReplaceRow(selected); RenderEditor();
            status.Text = $"Saved {saved} contract change(s). Continue in FM to see updates.";
        }
        catch (Exception ex) { status.Text = $"Contract save stopped: {UserMessage.For(ex)}"; }
    }

    Control MovePanel(PlayerRecord player)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 6), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        var from = new TextBlock { Text = player.Club?.DisplayName is { } club ? $"From  {club}" : "Free agent", FontSize = 12.5, Foreground = Brush("#94A3B8") };
        var searchClub = new TextBox { Watermark = "Club or ID", Width = 340, HorizontalAlignment = HorizontalAlignment.Left };
        var clubs = new ListBox
        {
            Width = 340,
            Height = 190,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brush("#13151E"),
            BorderBrush = Brush("#262837"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3)
        };
        var teams = new ComboBox { Width = 280 };
        var fee = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Value = 0, Increment = 100_000, FormatString = "N0", Width = 200, HorizontalContentAlignment = HorizontalAlignment.Right };
        // Sell-on owed by the buyer to the seller on the next sale; 0 = none.
        var sellOn = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 0, Increment = 5, FormatString = "0'%'", Width = 90, HorizontalContentAlignment = HorizontalAlignment.Right };
        var preview = new Grid { ColumnDefinitions = new ColumnDefinitions("190,260,260"), RowSpacing = 6, ColumnSpacing = 16 };
        var previewBox = new Border
        {
            Background = Brush("#161822"),
            BorderBrush = Brush("#242636"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(14, 10),
            IsVisible = false,
            Child = preview
        };
        var warning = new TextBlock { Foreground = Brush("#F59E0B"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        var apply = new Button { Content = "Confirm transfer", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left, Classes = { "accent" } };
        TransferPlan? plan = null;
        IReadOnlyList<ClubEntry> all = clubCache ?? [];

        void Invalidate() { plan = null; apply.IsEnabled = false; preview.Children.Clear(); preview.RowDefinitions.Clear(); previewBox.IsVisible = false; warning.Text = ""; }
        void Preview()
        {
            Invalidate();
            if (clubs.SelectedItem is not ClubOption chosen) return;
            try
            {
                plan = backend.Session.Transfers.Plan(player, all, chosen.Club, (int)(fee.Value ?? 0), (teams.SelectedItem as TeamOption)?.Team);
                // One row per money field, seller and buyer side by side: before → after.
                void Cell(string text, int row, int col, bool head = false)
                {
                    var tb = new TextBlock
                    {
                        Text = text,
                        FontSize = 12.5,
                        HorizontalAlignment = col == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                        Foreground = col == 0 && !head ? Brush("#94A3B8") : Brush("#F1F5F9"),
                        FontWeight = head ? FontWeight.SemiBold : FontWeight.Normal
                    };
                    Grid.SetRow(tb, row); Grid.SetColumn(tb, col); preview.Children.Add(tb);
                }
                int Row() { preview.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); return preview.RowDefinitions.Count - 1; }
                int head = Row();
                Cell(plan.Seller.DisplayName, head, 1, true); Cell(plan.Buyer.DisplayName, head, 2, true);
                string Money(byte[] b) => BitConverter.ToInt32(b).ToString("N0", Inv);
                var money = plan.Writes.Where(w => w.Expected.Length == 4 && w.Value.Length == 4).ToList();
                foreach (var name in money.Select(w => w.What.Split(' ', 2)[1]).Distinct())
                {
                    int r = Row();
                    Cell(char.ToUpperInvariant(name[0]) + name[1..], r, 0);
                    foreach (var (side, col) in new[] { ("seller", 1), ("buyer", 2) })
                        if (money.FirstOrDefault(w => w.What == $"{side} {name}") is { } w) Cell($"{Money(w.Expected)}  →  {Money(w.Value)}", r, col);
                }
                int squad = Row();
                Cell("Squad", squad, 0);
                Cell($"{plan.ToTeam.SquadCount}  →  {plan.ToTeam.SquadCount + 1}", squad, 2);
                int percent = (int)(sellOn.Value ?? 0);
                if (percent > 0)
                {
                    int s = Row();
                    Cell("Sell-on", s, 0);
                    string signed = plan.Fee > 0 ? $", signed for {plan.Fee.ToString("N0", Inv)}" : "";
                    Cell($"{percent}% owed{signed}", s, 2); Cell($"{percent}% due", s, 1);
                }
                previewBox.IsVisible = true;
                var risks = new List<string>();
                if (plan.NeedsAllocation) risks.Add("The squad is full, so a new squad list");
                if (percent > 0) risks.Add("The sell-on record");
                if (risks.Count > 0) warning.Text = $"{string.Join(" and ", risks)} will be allocated outside the game's allocator; keep a backup of the save.";
                apply.IsEnabled = true;
            }
            catch (Exception ex) { Invalidate(); warning.Text = UserMessage.For(ex); }
        }
        void FilterClubs()
        {
            var q = searchClub.Text?.Trim() ?? "";
            var compare = CultureInfo.InvariantCulture.CompareInfo;
            clubs.ItemsSource = (q.Length == 0 ? all : all.Where(c => compare.IndexOf(c.DisplayName, q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
                                                                  || compare.IndexOf(c.Name ?? "", q, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
                                                                  || c.Id?.ToString(Inv) == q))
                .Where(c => c.FirstTeam is not null && c.Address != player.Club?.Address)
                .OrderBy(c => c.DisplayName).Take(200).Select(c => new ClubOption(c)).ToArray();
            Invalidate();
        }
        searchClub.TextChanged += (_, _) => FilterClubs();
        clubs.SelectionChanged += (_, _) =>
        {
            var club = (clubs.SelectedItem as ClubOption)?.Club;
            var options = club?.Teams.OrderByDescending(t => t.IsFirstTeam).Select((t, i) => new TeamOption(t, i)).ToArray();
            teams.ItemsSource = options; teams.SelectedIndex = options is { Length: > 0 } ? 0 : -1;
            Preview();
        };
        teams.SelectionChanged += (_, _) => Preview();
        fee.ValueChanged += (_, _) => Preview();
        sellOn.ValueChanged += (_, _) => Preview();
        apply.Click += (_, _) =>
        {
            if (plan is null) return;
            int percent = (int)(sellOn.Value ?? 0);
            if (percent > 0 && player.Id is null) { status.Text = "This player's ID can't be read, so no sell-on can be added"; return; }
            try
            {
                backend.Edits.Execute(() => backend.Session.ApplyTransfer(plan));
            }
            catch (Exception ex) { Invalidate(); status.Text = $"Transfer stopped: {UserMessage.For(ex)}"; return; }
            clubCache = null; contractDrafts.Remove(player.Address);
            selected = backend.Players.Reload(player.Address); if (selected is not null) ReplaceRow(selected);
            editorTab = 3; RenderEditor();
            status.Text = $"Moved to {plan.Buyer.DisplayName}. Set the new contract terms; club finances show after Continue in FM.";
            if (percent > 0)
            {
                try
                {
                    backend.Edits.Execute(() => backend.Session.AddSellOn(plan.Buyer.Address, player.Id!.Value, plan.Seller.Address, percent, plan.Fee > 0 ? plan.Fee : null));
                    RenderEditor();
                    status.Text = $"Moved to {plan.Buyer.DisplayName} with a {percent}% sell-on to {plan.Seller.DisplayName}. Club finances show after Continue in FM.";
                }
                catch (Exception ex) { status.Text = $"Moved to {plan.Buyer.DisplayName}, but the sell-on was not added: {UserMessage.For(ex)}"; }
            }
        };

        if (clubCache is null)
        {
            var players = rows.OfType<PlayerRecord>().ToArray();
            var session = backend.Session;
            _ = Task.Run(() => session.LoadClubs(players)).ContinueWith(task => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (task.IsFaulted) { warning.Text = UserMessage.For(task.Exception!.InnerException!); return; }
                clubCache = all = task.Result; FilterClubs();
            }));
        }
        else FilterClubs();

        var formStack = new StackPanel { Spacing = 8 };
        formStack.Children.Add(from);
        formStack.Children.Add(searchClub);
        formStack.Children.Add(clubs);
        formStack.Children.Add(Labeled("Team", teams));
        formStack.Children.Add(Labeled("Fee", fee));
        formStack.Children.Add(Labeled("Sell-On", sellOn));

        panel.Children.Add(new Border
        {
            Background = Brush("#161822"),
            BorderBrush = Brush("#242636"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(14, 12),
            Child = formStack
        });
        panel.Children.Add(previewBox);
        panel.Children.Add(warning);
        panel.Children.Add(apply);
        return panel;
    }

    static Control Labeled(string label, Control control) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 10,
        Children = { new TextBlock { Text = label, Width = 54, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#CBD5E1") }, control },
    };

    sealed record EnumOption<T>(T Value, string Label) { public override string ToString() => Label; }
    sealed record TermOption(short Type, string Label) { public override string ToString() => Label; }
    sealed record ClubOption(ClubEntry Club) { public override string ToString() => $"{Club.DisplayName}   {Club.Id}"; }
    sealed record TeamOption(TeamEntry Team, int Index) { public override string ToString() => $"{(Team.IsFirstTeam ? "First team" : $"Team {Index + 1}")}   {Team.SquadCount}/{Team.SquadCapacity}"; }
}

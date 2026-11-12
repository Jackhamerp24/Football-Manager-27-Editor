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
    static string D(DateOnly? d) => DateSetting.Format(d);

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
        Button? save = null;
        void Changed(string key, string original, string value)
        {
            if (key == "Future Year") value = ContractYearInput.NormalizeDraftValue(value, s.FuturePlayingTimeYear);
            if (value == original) draft.Changes.Remove(key); else draft.Changes[key] = value;
            if (save is not null) save.IsEnabled = draft.Changes.Count > 0;
            status.Text = draft.Changes.Count > 0 ? $"{draft.Changes.Count} unsaved contract change(s)" : "";
            // The contract tab has no Save of its own (the shell's Save writes it), so it stars itself.
            if (draft.Changes.Count > 0) SetActiveTabDirty(true);
            MarkSection(player, "Contract", draft.Changes.Count > 0);
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
        Control DateText(string key, string original)
        {
            var box = new DateBox { Width = 140, Text = draft.Changes.GetValueOrDefault(key, original), HorizontalContentAlignment = HorizontalAlignment.Right };
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
        // Loan Status is a single flag bit, not an enum: None and Not for Loan share the cleared bit and read back as None.
        // Transfer and Loan Status: FMRTE's options, one status byte underneath (see ContractStatusFlags).
        Control StatusChoice(string key, string original, string[] options)
        {
            var current = draft.Changes.TryGetValue(key, out var changed) && Array.IndexOf(options, changed) >= 0 ? changed : original;
            var combo = new ComboBox { Width = 196, ItemsSource = options, SelectedItem = current };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string o) Changed(key, original, o); };
            return combo;
        }
        Control RollingCheck(bool original)
        {
            bool current = draft.Changes.TryGetValue("On Rolling Contract", out var changed) ? changed == "true" : original;
            var box = new CheckBox { IsChecked = current, VerticalAlignment = VerticalAlignment.Center };
            box.IsCheckedChanged += (_, _) => Changed("On Rolling Contract", original ? "true" : "false", box.IsChecked == true ? "true" : "false");
            return box;
        }

        // Two columns of label + value using reusable OOP TwoColumnFormBuilder.
        var form = new UiKit.TwoColumnFormBuilder();
        static TextBlock Unknown() => new() { Text = "Unknown", FontSize = 13, Foreground = Brush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center };
        form.AddRow(0, "Type", s.Type is { } t ? Choice("Type", t, ContractLabels.Of, Enum.GetValues<ContractType>()) : null);
        form.AddRow(0, "Started", s.Started is not null || backend.Session.Profile.Contract?.Started is not null ? DateText("Started", D(s.Started)) : null);
        form.AddRow(0, "Signed", DateText("Signed", D(s.Signed)));
        form.AddRow(0, "Wages (per week)", Text("Wage", s.Wage.ToString(Inv)));
        form.AddRow(0, "Loyalty Bonus (per week)", s.LoyaltyBonus is { } lb ? Text("Loyalty Bonus", lb.ToString(Inv)) : null);
        form.AddRow(0, "Number", s.SquadNumber is { } n ? Text("Number", n < 0 ? "" : n.ToString(Inv), 64) : null);
        form.AddRow(0, "Transfer Status", s.Transfer is { } tl ? StatusChoice("Transfer Status", TransferNames[(int)tl], TransferNames) : null);
        form.AddRow(0, "Happiness", s.Happiness is { } h ? Text("Happiness", h.ToString(Inv), 64) : null);
        form.AddRow(0, "On Rolling Contract", s.OnRollingContract is { } rolling ? RollingCheck(rolling) : null);
        form.AddRow(1, "Expires", DateText("Expires", D(s.Expires)));
        form.AddRow(1, "Joined Club", DateText("Joined Club", D(s.JoinedClub)));
        form.AddRow(1, "Agreed Playing Time", s.PlayingTime is { } pt ? Choice("Playing Time", pt, ContractLabels.Of, Enum.GetValues<PlayingTime>()) : null);
        form.AddRow(1, "Previous Playing Time", s.PreviousPlayingTime is { } pp ? Choice("Previous Playing Time", pp, ContractLabels.Of, Enum.GetValues<PlayingTime>()) : null);
        form.AddRow(1, "Future Playing Time", s.FuturePlayingTime is { } fp ? Choice("Future Playing Time", fp, ContractLabels.Of, Enum.GetValues<PlayingTime>()) : null,
            s.FuturePlayingTimeYear is { } fy && fy > 0 ? Text("Future Year", ContractYearInput.Format(fy), 64) : null);
        form.AddRow(1, "Loan Status", s.Loan is { } ll ? StatusChoice("Loan Status", LoanNames[(int)ll], LoanNames) : null);
        form.AddRow(1, "Playing Time Happiness", s.PlayingTimeHappiness is { } ph ? Text("Playing Time Happiness", ph.ToString(Inv), 64) : null);

        panel.Children.Add(form.BuildCard("Contract Details"));

        if (backend.Session.Profile.Contract?.Clauses is not null)
        {
            var clauseStack = new StackPanel { Spacing = 0 };

            // Name, then up to two label + box pairs on fixed columns, then Remove: every row lines up the same way.
            var terms = new Grid { ColumnDefinitions = new ColumnDefinitions("*,64,112,64,112,88"), RowSpacing = 6, ColumnSpacing = 8, Margin = new Thickness(12, 10, 12, 8) };
            foreach (var term in s.Terms)
            {
                int row = terms.RowDefinitions.Count; terms.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                void Cell(Control c, int col, HorizontalAlignment align = HorizontalAlignment.Left) { c.HorizontalAlignment = align; Grid.SetRow(c, row); Grid.SetColumn(c, col); terms.Children.Add(c); }
                TextBlock Unit(string text) => new() { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#94A3B8") };
                var remove = new Button { Content = "Remove" };
                remove.Click += (_, _) => ChangeTerms(player, draft, () => writer.RemoveTerm(draft.Player, draft.Snapshot, term.Index), $"Removed {ContractLabels.Term(term.Type)}", future);
                Cell(remove, 5, HorizontalAlignment.Right);
                Cell(new TextBlock { Text = ContractLabels.Term(term.Type), FontSize = 13, Foreground = Brush("#F1F5F9"), VerticalAlignment = VerticalAlignment.Center }, 0);
                int next = 1;
                if (ContractLabels.TermHasValue(term.Type))
                {
                    Cell(Unit("Value"), next, HorizontalAlignment.Right);
                    Cell(Text(TermValueKey(term), term.Value.ToString(Inv), 112), next + 1, HorizontalAlignment.Right);
                    next += 2;
                }
                if (ContractLabels.TermParameter(term.Type) is { } unit)
                {
                    Cell(Unit(unit), next, HorizontalAlignment.Right);
                    Cell(Text(TermParameterKey(term), term.Parameter.ToString(Inv), 112), next + 1, HorizontalAlignment.Right);
                }
            }
            if (s.Terms.Count > 0)
                clauseStack.Children.Add(terms);

            // Add row: type, parameter, value. Written at once, like Remove.
            var addType = new ComboBox { Width = 300, ItemsSource = ContractLabels.Terms.Select(kv => new TermOption(kv.Key, kv.Value)).ToArray(), PlaceholderText = "Add bonus or clause" };
            var addParam = new TextBox { Width = 64, Text = "", IsVisible = false, HorizontalContentAlignment = HorizontalAlignment.Right };
            var addValue = new TextBox { Width = 116, Text = "", HorizontalContentAlignment = HorizontalAlignment.Right };
            var paramLabel = new TextBlock { FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#94A3B8"), Width = 52 };
            var add = new Button { Content = "Add", IsEnabled = false };
        static Control InColumn(Control c, int column, HorizontalAlignment align = HorizontalAlignment.Stretch)
        {
            c.HorizontalAlignment = align;
            Grid.SetColumn(c, column);
            return c;
        }
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
                BorderBrush = UiKit.BorderSubtle,
                BorderThickness = new Thickness(0, s.Terms.Count > 0 ? 1 : 0, 0, 0),
                Padding = new Thickness(12, 10),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,64,112,64,112,88"),
                    ColumnSpacing = 8,
                    Children =
                    {
                        InColumn(addType, 0),
                        InColumn(paramLabel, 1, HorizontalAlignment.Right),
                        InColumn(addParam, 2, HorizontalAlignment.Right),
                        InColumn(new TextBlock { Text = "Value", FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#94A3B8") }, 3, HorizontalAlignment.Right),
                        InColumn(addValue, 4, HorizontalAlignment.Right),
                        InColumn(add, 5, HorizontalAlignment.Right),
                    }
                }
            };
            clauseStack.Children.Add(addBar);

            panel.Children.Add(UiKit.Card("Bonuses & Clauses", clauseStack));
        }

        if (future == 0 && TransferClausesPanel(player, draft, (k, original, width) => Text(k, original, width), DateText) is { } transferClauses) panel.Children.Add(transferClauses);
        if (future == 0 && UnhappinessPanel(player) is { } unhappiness) panel.Children.Add(unhappiness);

        return panel;
    }

    /// <summary>
    /// Clauses from the player's earlier transfers that his club owes. Value edits go into the contract draft and are
    /// written by Save contract; sell-ons are removed and added at once (beneficiary by club ID). Null for free agents.
    /// </summary>
    Control? TransferClausesPanel(PlayerRecord player, ContractDraft draft, Func<string, string, double, Control> text, Func<string, string, Control> dateText)
    {
        if (player.Id is not { } id || player.Club is not { } club || backend.Session.Profile.Club?.TransferClauses is null) return null;
        var host = new ContentControl();
        var reading = new TextBlock { Text = "Reading clauses…", FontSize = 13, Foreground = Brush("#94A3B8"), Margin = new Thickness(12, 10) };
        host.Content = UiKit.Card("Transfer Clauses", reading);
        // A later render of the same draft reuses the list read when the tab first opened.
        if (draft.TransferClauses is { } known)
        {
            if (Build(known) is { } knownCard) host.Content = knownCard; else host.IsVisible = false;
            return host;
        }
        // 27.0.1+ finds clauses by scanning the game's heap (several seconds). Read them off the UI thread so opening
        // the Contract tab stays instant; the card fills in when the read finishes.
        _ = Task.Run(() =>
        {
            var players = Players();
            var competitions = backend.Session.LoadCompetitions(players);
            var scan = backend.Session.Profile.Club?.TransferClauses?.Scan == true;
            var clauses = (scan
                ? backend.Session.ReadAllTransferClauses(competitions)
                : backend.Session.ReadTransferClauses(club.Address, competitions)).Where(c => c.PlayerId == id).ToList();
            draft.TransferClauses = clauses;
            return clauses;
        }).ContinueWith(task => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (task.IsFaulted)
            {
                reading.Text = UserMessage.For(task.Exception!.GetBaseException());
                return;
            }
            if (Build(task.Result) is { } card) host.Content = card; else host.IsVisible = false;
        }));
        return host;

        Control? Build(IReadOnlyList<TransferClause> clauses)
        {
            TextBlock Label(string s, string color = "#94A3B8") => new() { Text = s, FontSize = 13, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            WrapPanel Line(params Control[] items) { var p = new WrapPanel { Orientation = Orientation.Horizontal }; foreach (var i in items) { i.Margin = new Thickness(0, 0, 6, 0); p.Children.Add(i); } return p; }

            var list = new Grid { ColumnDefinitions = new ColumnDefinitions("130,100,110,*,Auto"), RowSpacing = 6, ColumnSpacing = 12, Margin = new Thickness(12, 10, 12, 8) };
            foreach (var c in clauses)
            {
                int row = list.RowDefinitions.Count; list.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                void Cell(Control control, int col) { control.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetRow(control, row); Grid.SetColumn(control, col); list.Children.Add(control); }
                Cell(Label(ContractLabels.Of(c), "#F1F5F9"), 0);
                Cell(Label(c.Beneficiary?.DisplayName ?? "", "#CBD5E1"), 1);
                string main = ClauseKey(c, "main"), extra = ClauseKey(c, "extra");
                switch (c.Kind)
                {
                    case TransferClauseKind.SellOn:
                        Cell(Line(text(main, $"{c.Percentage}", 52), Label("%")), 2);
                        Cell(Line(Label("Signed for"), text(extra, c.SignedFor?.ToString(Inv) ?? "", 110)), 3);
                        break;
                    case TransferClauseKind.AppearanceFee:
                        Cell(text(main, $"{c.Amount}", 110), 2);
                        Control[] appItems = c.AppearancesSoFar is { } soFar
                            ? [Label($"{soFar} /"), text(extra, $"{c.Appearances}", 52), Label(ContractLabels.CountUnit(c))]
                            : [text(extra, $"{c.Appearances}", 52), Label(ContractLabels.CountUnit(c))];
                        Cell(Line(appItems), 3);
                        break;
                    default:
                        Cell(text(main, $"{c.Amount}", 110), 2);
                        Cell(Line(Label(ContractLabels.Competition(c), "#CBD5E1"), Label("until"), dateText(extra, D(c.Ends))), 3);
                        break;
                }
                if (c.Origin is { } origin)
                {
                    var removeClause = new Button { Content = "Remove" };
                    string removed = $"{ContractLabels.Of(c)} removed";
                    removeClause.Click += (_, _) => ClauseChange(player, draft, () => backend.Session.TransferClauses.RemoveClause(club.Address, c), removed);
                    Cell(removeClause, 4);
                }
            }

            return clauses.Count > 0 ? UiKit.Card("Transfer Clauses", list) : null;
        }
    }

    /// <summary>
    /// Unhappiness as FMRTE shows it on the Contract tab: empty unless the player shows motives; each motive is a block with
    /// Motive, Type, Start and End dates and, when the motive names someone, Person, each editable and removable; the +
    /// in the header opens an empty block to add one, and Remove All clears them. Null only when the build has no offsets.
    /// </summary>
    Control? UnhappinessPanel(PlayerRecord player)
    {
        var session = backend.Session;
        if (session.Profile.Unhappiness is null || session.ReadGameDate() is not { } today) return null;
        var motives = session.ReadUnhappiness(player).Where(m => m.IsActive(today)).ToList();
        var players = rows.OfType<PlayerRecord>().ToArray();
        // A motive names its person by the game's person index (player + personIndex), not by the player id; the box
        // shows and takes the player id, as the rest of the editor does.
        PlayerRecord? ByIndex(int index) => index <= 0 ? null : players.FirstOrDefault(p => p.PersonIndex == index);
        PlayerRecord? ById(int id) => id <= 0 ? null : players.FirstOrDefault(p => p.Id == id);
        string PersonName(int id) => id <= 0 ? "" : ById(id)?.DisplayName ?? "";
        var motiveCodes = UnhappinessMotives.All.OrderBy(m => m.Value.Name, StringComparer.OrdinalIgnoreCase).Select(m => m.Key).ToList();

        TextBlock Label(string text) => new() { Text = text, FontSize = 13, Foreground = Brush("#94A3B8"), VerticalAlignment = VerticalAlignment.Center, Width = 80 };
        void Run(string what, Action action, string done)
        {
            try
            {
                backend.Edits.Execute("Player", player.DisplayName, what, action);
                RenderEditor();
                status.Text = $"{done}. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        }

        // One motive block. Null motive/type = nothing chosen yet (a new block).
        (StackPanel Block, Func<UnhappinessWriter.MotiveValues?> Read) Editor(short? motive, byte? type, DateOnly? start, DateOnly? end, int personIndex)
        {
            var known = ByIndex(personIndex);
            int person = known?.Id ?? -1;
            var codes = motive is { } m0 && !motiveCodes.Contains(m0) ? motiveCodes.Prepend(m0).ToList() : motiveCodes;
            var motiveBox = new ComboBox { ItemsSource = codes.Select(UnhappinessLabels.Motive).ToList(), SelectedIndex = motive is { } m ? codes.IndexOf(m) : -1, MinWidth = 420, MaxDropDownHeight = 420 };
            var typeCodes = UnhappinessLabels.TypeCodes.ToList();
            if (type is { } t0 && !typeCodes.Contains(t0)) typeCodes.Add(t0);
            var typeBox = new ComboBox { ItemsSource = typeCodes.Select(UnhappinessLabels.Type).ToList(), SelectedIndex = type is { } t ? typeCodes.IndexOf(t) : -1, MinWidth = 140 };
            var startBox = new DateBox { Text = start is { } st ? D(st) : "", Width = 130 };
            var endBox = new DateBox { Text = end is { } en ? D(en) : "", Width = 130 };
            var personBox = new TextBox { Text = person > 0 ? person.ToString(CultureInfo.InvariantCulture) : "", Width = 110 };
            var personName = new TextBlock { Text = known?.DisplayName ?? (personIndex > 0 ? $"person {personIndex}" : ""), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            personBox.TextChanged += (_, _) => personName.Text = int.TryParse(personBox.Text, out int id) ? PersonName(id) : "";
            var personRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Label("Person"), personBox, personName } };
            void ShowPerson() => personRow.IsVisible = motiveBox.SelectedIndex >= 0 && UnhappinessLabels.NeedsPerson(codes[motiveBox.SelectedIndex]);
            motiveBox.SelectionChanged += (_, _) => ShowPerson();
            ShowPerson();
            StackPanel Row(params Control[] c) { var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var x in c) r.Children.Add(x); return r; }
            var block = new StackPanel
            {
                Spacing = 6,
                Children = { Row(Label("Motive"), motiveBox), Row(Label("Type"), typeBox), Row(Label("Start Date"), startBox, Label("End Date"), endBox), personRow },
            };
            UnhappinessWriter.MotiveValues? Values()
            {
                if (motiveBox.SelectedIndex < 0) { status.Text = "Choose a motive."; return null; }
                if (typeBox.SelectedIndex < 0) { status.Text = "Choose a type."; return null; }
                if (!DateSetting.TryParse(startBox.Text, out var s1)) { status.Text = $"Enter the start date as {DateSetting.Hint}."; return null; }
                DateOnly? e1 = null;
                if (!string.IsNullOrWhiteSpace(endBox.Text))
                {
                    if (!DateSetting.TryParse(endBox.Text, out var parsed)) { status.Text = $"Enter the end date as {DateSetting.Hint}, or leave it empty for none."; return null; }
                    e1 = parsed;
                }
                int item1 = -1;
                if (personRow.IsVisible && !string.IsNullOrWhiteSpace(personBox.Text))
                {
                    if (!int.TryParse(personBox.Text, out int personId)) { status.Text = "Enter the person's id as a number."; return null; }
                    if (personId == person && known is null) item1 = personIndex;
                    else if (ById(personId)?.PersonIndex is { } index) item1 = index;
                    else { status.Text = "No player with that id in this list."; return null; }
                }
                else if (personRow.IsVisible && personBox.Text is null or "" && known is null && personIndex > 0) item1 = personIndex;
                return new(codes[motiveBox.SelectedIndex], typeCodes[typeBox.SelectedIndex], s1, e1, personRow.IsVisible ? item1 : -1);
            }
            return (block, Values);
        }

        var list = new StackPanel { Spacing = 12, Margin = new Thickness(12, 10, 12, 8) };
        foreach (var m in motives)
        {
            var (block, values) = Editor(m.Motive, m.Type, m.Start, m.End, m.Item1);
            var row = m;
            var save = new Button { Content = "Save" };
            save.Click += (_, _) => { if (values() is { } v) Run("Unhappiness: edit", () => session.EditUnhappiness(player, row, v), "Unhappiness saved"); };
            var remove = new Button { Content = "Remove" };
            int index = m.Index;
            remove.Click += (_, _) => Run("Page save", () => session.RemoveUnhappiness(player, x => x.Index == index), "Unhappiness removed");
            block.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, remove } } as Control);
            list.Children.Add(block);
        }

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var addOpen = new Button { Content = "+", Padding = new Thickness(8, 1), MinHeight = 20, Height = 20, FontSize = 12 };
        ToolTip.SetTip(addOpen, "Add a motive");
        addOpen.Click += (_, _) =>
        {
            addOpen.IsEnabled = false;
            var (block, values) = Editor(null, null, null, null, -1);
            var add = new Button { Content = "Add" };
            var cancel = new Button { Content = "Cancel" };
            var holder = new StackPanel { Spacing = 6 };
            add.Click += (_, _) => { if (values() is { } v) Run("Unhappiness: add", () => session.AddUnhappiness(player, v), "Unhappiness added"); };
            cancel.Click += (_, _) => { list.Children.Remove(holder); addOpen.IsEnabled = true; };
            holder.Children.Add(block);
            holder.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, cancel } });
            list.Children.Add(holder);
        };
        var removeAll = new Button { Content = "Remove All", Padding = new Thickness(8, 1), MinHeight = 20, Height = 20, FontSize = 12, IsVisible = motives.Count > 0 };
        removeAll.Click += (_, _) => Run("Page save", () => session.RemoveUnhappiness(player, null), "Unhappiness removed");
        header.Children.Add(removeAll);
        header.Children.Add(addOpen);
        return UiKit.Card("Unhappiness", list, rightHeader: header);
    }

    static readonly string[] TransferNames = ["None", "Listed", "By request", "Not for Sale"];
    static readonly string[] LoanNames = ["None", "Listed", "Not for Loan"];

    static string ClauseKey(TransferClause c, string part) => $"xfer {c.Address:X} {part}";

    /// <summary>Remove/add a sell-on right away; refuses while clause value edits are pending, since the list moves.</summary>
    void ClauseChange(PlayerRecord player, ContractDraft draft, Action change, string done)
    {
        if (draft.Changes.Keys.Any(k => k.StartsWith("xfer ", StringComparison.Ordinal)))
        { status.Text = "Save the clause edits first"; return; }
        try
        {
            backend.Edits.Execute("Player", player.DisplayName, "Contract: Clauses", change);
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
        { status.Text = "Save the clause edits first"; return; }
        try
        {
            backend.Edits.Execute("Player", player.DisplayName, "Contract: Clauses", change);
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
            DateOnly Date(string x) { var date = DateSetting.TryParse(x, out var typed) ? typed : throw new FormatException($"Enter the date as {DateSetting.Hint}"); return date.Year is >= 1901 and <= 2300 ? date : throw new ArgumentOutOfRangeException(nameof(x), "Year must be 1901-2300"); }
            int Range(string x, int min, int max) { int n = Number(x); return n >= min && n <= max ? n : throw new ArgumentOutOfRangeException(nameof(x), $"Expected {min}-{max}"); }
            T EnumValue<T>(string x) where T : struct, Enum { if (!Enum.TryParse<T>(x, true, out var v) || !Enum.IsDefined(v)) throw new FormatException($"Invalid {typeof(T).Name}: {x}"); return v; }
            // Parse everything first, so a typo stops the save before anything is written.
            Add("Wage", x => Range(x, 0, 3_500_000), x => c.SetWage(p, x));   // in-game editor: £0 to £3.5M Add("Expires", Date, x => c.SetExpiry(p, d, x));
            Add("Started", Date, x => c.SetStarted(p, d, x)); Add("Signed", Date, x => c.SetSigned(p, d, x)); Add("Joined Club", Date, x => c.SetJoinedClub(p, d, x));
            Add("Type", EnumValue<ContractType>, x => c.SetType(p, d, x));
            Add("Loyalty Bonus", x => Range(x, 0, 10_000_000), x => c.SetLoyaltyBonus(p, d, x)); Add("Number", x => string.IsNullOrWhiteSpace(x) ? -1 : Range(x, 0, 99), x => c.SetSquadNumber(p, d, x));
            Add("Playing Time", EnumValue<PlayingTime>, x => c.SetPlayingTime(p, d, x));
            Add("Previous Playing Time", EnumValue<PlayingTime>, x => c.SetPreviousPlayingTime(p, d, x));
            // Both statuses live in one byte: write them as one change from the byte read, so neither undoes the other.
            if (draft.Changes.ContainsKey("Transfer Status") || draft.Changes.ContainsKey("Loan Status"))
            {
                var transfer = draft.Changes.TryGetValue("Transfer Status", out var tv) ? (TransferListing)Array.IndexOf(TransferNames, tv) : d.Transfer!.Value;
                var loan = draft.Changes.TryGetValue("Loan Status", out var lv) ? (LoanListing)Array.IndexOf(LoanNames, lv) : d.Loan!.Value;
                if ((int)transfer < 0 || (int)loan < 0) throw new FormatException("Invalid transfer or loan status");
                actions.Add((["Transfer Status", "Loan Status"], () =>
                {
                    c.SetTransferListing(p, d, transfer);
                    c.SetLoanListing(p, d with { StatusFlags = ContractStatusFlags.WithTransfer(d.StatusFlags!.Value, transfer) }, loan);
                }));
            }
            Add("On Rolling Contract", x => x == "true", x => c.SetRollingContract(p, d, x));
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
            foreach (var action in actions) { backend.Edits.Execute("Player", player.DisplayName, "Page save", action.write); foreach (var key in action.keys) draft.Changes.Remove(key); saved++; }
            contractDrafts.Remove(future == 0 ? player.Address : future); selected = backend.Players.Reload(player.Address); if (selected is not null) ReplaceRow(selected); RenderEditor();
            status.Text = $"Saved {saved} contract change(s). Continue in FM to see updates.";
        }
        catch (Exception ex) { status.Text = $"Contract save stopped: {UserMessage.For(ex)}"; }
    }

    Control MovePanel(PlayerRecord player)
    {
        var from = new DockPanel { LastChildFill = true };
        var fromLabel = new TextBlock { Text = "Current Club", Width = 96, FontSize = 13, Foreground = UiKit.TextMuted, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(fromLabel, Dock.Left);
        from.Children.Add(fromLabel);
        from.Children.Add(new TextBlock
        {
            Text = player.Club?.DisplayName ?? "Free agent",
            FontSize = 13.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = UiKit.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        });

        ClubOption? chosenClub = null;
        // One field for the whole destination: click it, choose the club in the search window, then its team.
        var target = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Content = "Choose club and team…" };
        TeamOption? chosenTeam = null;
        var fee = new NumericUpDown { Minimum = 0, Maximum = 1_000_000_000, Value = 0, Increment = 100_000, FormatString = "N0", ShowButtonSpinner = false, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Right };
        // Sell-on owed by the buyer to the seller on the next sale; 0 = none.
        var preview = new Grid { ColumnDefinitions = new ColumnDefinitions("114,*,*"), RowSpacing = 6, ColumnSpacing = 12 };
        var warning = new TextBlock { Foreground = Brush("#F59E0B"), FontSize = 13, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var apply = new Button { Content = "Confirm transfer", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Height = 28, Classes = { "accent" }, Margin = new Thickness(0, 4, 0, 0) };
        TransferPlan? plan = null;
        LoanCreatePlan? loanPlan = null;
        IReadOnlyList<ClubEntry> all = clubCache ?? [];

        // Type: a permanent transfer or a loan. A loan has its own fields, which replace Fee and Sell-On.
        var type = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = 0,
            ItemsSource = new[] { "Transfer", "Loan" },
        };
        bool canFuture = backend.Session.CanCreateFutureTransfer;
        var moveNow = new CheckBox { Content = "Move now", IsChecked = true };
        var gameDate = backend.Session.ReadGameDate() ?? DateOnly.FromDateTime(DateTime.Today);
        var futureDate = new DateBox { Text = DateSetting.Format(gameDate.AddMonths(6)), Width = 130, HorizontalAlignment = HorizontalAlignment.Left };
        FutureTransferCreator.Plan? futurePlan = null;
        NumericUpDown Money(decimal increment) => new() { Minimum = 0, Maximum = 1_000_000_000, Value = 0, Increment = increment, FormatString = "N0", ShowButtonSpinner = false, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Right };
        var loanWage = new NumericUpDown { Minimum = 0, Maximum = 200, Value = 100, Increment = 5, FormatString = "0'%'", ShowButtonSpinner = false, Width = 96, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Right };
        var loanStart = new DateBox { Text = DateSetting.Format(gameDate), Width = 130, HorizontalAlignment = HorizontalAlignment.Left };
        var loanEnd = new DateBox { Text = DateSetting.Format(gameDate.AddYears(1)), Width = 130, HorizontalAlignment = HorizontalAlignment.Left };
        var loanPrice = Money(100_000);
        var loanBuyBack = Money(100_000);
        var loanMonthly = Money(1_000); loanMonthly.Maximum = 10_000_000;   // in-game editor: £1-£10M
        var loanAfter = new NumericUpDown { Minimum = 0, Maximum = 27, Value = 0, Increment = 1, FormatString = "0", ShowButtonSpinner = false, Width = 96, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Right };
        bool IsLoan() => type.SelectedIndex == 1;
        bool IsFuture() => canFuture && type.SelectedIndex == 0 && moveNow.IsChecked == false;

        // Transfer terms, in FMRTE's order; every value starts at 0 and 0 writes nothing.
        NumericUpDown Whole(decimal max, string format = "N0") => new() { Minimum = 0, Maximum = max, Value = 0, Increment = 1, FormatString = format, ShowButtonSpinner = false, Width = 64, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Right };
        NumericUpDown Amount() { var box = Money(10_000); box.Width = 120; box.HorizontalAlignment = HorizontalAlignment.Left; return box; }
        TextBlock Unit(string text) => new() { Text = text, Foreground = UiKit.TextMuted, VerticalAlignment = VerticalAlignment.Center };
        Control TermRow(string label, params Control[] parts)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var part in parts) panel.Children.Add(part);
            return Labeled(label, panel);
        }
        var instAmount = Amount(); var instMonths = Whole(255);
        var instPeriod = new ComboBox { ItemsSource = new[] { "Yearly", "Every 6 months" }, SelectedIndex = 0, Width = 130 };
        var leagueAmount = Amount(); var leagueCount = Whole(255);
        var intlAmount = Amount(); var intlCount = Whole(255);
        var perAmount = Amount(); var perCount = Whole(255);
        var goalsAmount = Amount(); var goalsCount = Whole(255);
        var termSellOn = Whole(100, "0'%'");
        var termProfit = new CheckBox { Content = "Of the profit" };
        var buyBack = Amount();
        var wageShare = Whole(100, "0'%'");
        var friendly = new CheckBox();
        TransferTerms ReadTerms()
        {
            static int Int(NumericUpDown box) => (int)(box.Value ?? 0);
            void NeedCount(string name, NumericUpDown amount, NumericUpDown count)
            {
                if (Int(amount) > 0 && Int(count) == 0) throw new InvalidOperationException($"{name}: enter how many as well as the amount");
            }
            NeedCount("Instalments", instAmount, instMonths); NeedCount("League apps", leagueAmount, leagueCount);
            NeedCount("International apps", intlAmount, intlCount); NeedCount("Per league app", perAmount, perCount);
            NeedCount("League goals", goalsAmount, goalsCount);
            int wage = 0;
            if (Int(wageShare) > 0)
                wage = TransferTermsLayout.WageContribution(backend.Session.Contracts.Read(player)?.Wage ?? throw new InvalidOperationException("The player's contract can't be read"), Int(wageShare));
            return new TransferTerms(Int(instAmount), (byte)Int(instMonths), Int(leagueAmount), (byte)Int(leagueCount), Int(intlAmount), (byte)Int(intlCount),
                Int(perAmount), (byte)Int(perCount), Int(goalsAmount), (byte)Int(goalsCount), Int(buyBack), (short)Int(termSellOn), termProfit.IsChecked == true,
                wage, (byte)(instPeriod.SelectedIndex == 1 ? 6 : 12), friendly.IsChecked == true);
        }
        var termRows = new Control[]
        {
            TermRow("Instalments", instAmount, instMonths, Unit("months"), instPeriod),
            TermRow("League apps", leagueAmount, leagueCount, Unit("apps")),
            TermRow("International apps", intlAmount, intlCount, Unit("apps")),
            TermRow("Per league app", perAmount, perCount, Unit("max apps")),
            TermRow("League goals", goalsAmount, goalsCount, Unit("goals")),
            TermRow("Sell-on", termSellOn, termProfit),
            TermRow("Buy Back Fee", buyBack),
            TermRow("Wage contribution", wageShare),
            TermRow("Arrange a friendly", friendly),
        };

        // One row per field, seller and buyer side by side: before → after.
        void Cell(string text, int row, int col, bool head = false)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = head ? 11.5 : 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = col == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                Foreground = col == 0 && !head ? UiKit.TextMuted : UiKit.TextPrimary,
                FontWeight = head ? FontWeight.SemiBold : FontWeight.Normal
            };
            Grid.SetRow(tb, row); Grid.SetColumn(tb, col); preview.Children.Add(tb);
        }
        int Row() { preview.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); return preview.RowDefinitions.Count - 1; }

        void Invalidate()
        {
            plan = null;
            loanPlan = null;
            futurePlan = null;
            apply.IsEnabled = false;
            preview.Children.Clear();
            preview.RowDefinitions.Clear();
            warning.Text = "";
            warning.IsVisible = false;
        }
        void PreviewLoan(ClubOption chosen)
        {
            try
            {
                if (chosenTeam is not { } team) return;
                if (!DateBox.TryParse(loanStart.Text, out var start) || !DateBox.TryParse(loanEnd.Text, out var end))
                { warning.Text = $"Enter the start and end dates as {DateSetting.Hint}"; warning.IsVisible = true; return; }
                int contractWage = backend.Session.Contracts.Read(player)?.Wage ?? throw new InvalidOperationException("The player's contract can't be read");
                int wage = LoanCreator.WageFromPercent(contractWage, (int)(loanWage.Value ?? 0));
                var offer = new LoanOffer(wage, start, end, (int)(loanPrice.Value ?? 0), (int)(loanBuyBack.Value ?? 0), (int)(loanMonthly.Value ?? 0), (int)(loanAfter.Value ?? 0));
                loanPlan = backend.Session.PlanLoanCreate(player, team.Team.Address, offer, rows.OfType<PlayerRecord>());
                int head = Row();
                Cell(player.Club?.DisplayName ?? "", head, 1, true); Cell(chosen.Club.DisplayName, head, 2, true);
                int w = Row(); Cell("Wage", w, 0); Cell(wage.ToString("N0", Inv), w, 2);
                int d = Row(); Cell("Until", d, 0); Cell(DateSetting.Format(end), d, 2);
                int s = Row(); Cell("Squad", s, 0); Cell($"{team.Team.SquadCount} → {team.Team.SquadCount + 1}", s, 2);
                if (loanPlan.NeedsAllocation)
                {
                    warning.Text = "The squad is full, so a new squad list will be allocated outside the game's allocator; keep a backup of the save.";
                    warning.IsVisible = true;
                }
                apply.IsEnabled = true;
            }
            catch (Exception ex) { Invalidate(); warning.Text = UserMessage.For(ex); warning.IsVisible = true; }
        }
        void PreviewFuture(ClubOption chosen)
        {
            try
            {
                if (chosenTeam is not { } team) return;
                if (!DateBox.TryParse(futureDate.Text, out var date))
                { warning.Text = $"Enter the transfer date as {DateSetting.Hint}"; warning.IsVisible = true; return; }
                var terms = ReadTerms();
                futurePlan = backend.Session.PlanFutureTransfer(player, team.Team.Address, (int)(fee.Value ?? 0), date, terms);
                int head = Row();
                Cell(player.Club?.DisplayName ?? "", head, 1, true); Cell(chosen.Club.DisplayName, head, 2, true);
                int f = Row(); Cell("Fee", f, 0); Cell(futurePlan.Fee.ToString("N0", Inv), f, 2);
                int d = Row(); Cell("Date", d, 0); Cell(DateSetting.Format(date), d, 2);
                if (terms.WageContribution > 0) { int w = Row(); Cell("Wage contribution", w, 0); Cell(terms.WageContribution.ToString("N0", Inv), w, 2); }
                warning.Text = "The offer and its contract are allocated in the game's own heap; keep a backup of the save.";
                warning.IsVisible = true;
                apply.IsEnabled = true;
            }
            catch (Exception ex) { Invalidate(); warning.Text = UserMessage.For(ex); warning.IsVisible = true; }
        }
        void Preview()
        {
            Invalidate();
            if (player.Gender == Fm27Editor.Core.Offsets.Gender.Women || player.Club?.Gender == Fm27Editor.Core.Offsets.Gender.Women)
            {
                warning.Text = "Only male players and men's clubs are supported.";
                warning.IsVisible = true;
                return;
            }
            if (chosenClub is not { } chosen) return;
            if (IsLoan()) { PreviewLoan(chosen); return; }
            if (IsFuture()) { PreviewFuture(chosen); return; }
            try
            {
                var now = ReadTerms();
                if (now.Instalments + now.LeagueApps + now.InternationalApps + now.PerLeagueApp + now.LeagueGoals + now.BuyBackFee + now.WageContribution > 0 || now.Friendly || now.SellOnProfit)
                { warning.Text = "Move now supports only the Sell-on percentage. Untick Move now to use the other terms."; warning.IsVisible = true; return; }
                plan = backend.Session.Transfers.Plan(player, all, chosen.Club, (int)(fee.Value ?? 0), chosenTeam?.Team);
                int head = Row();
                Cell(plan.Seller.DisplayName, head, 1, true); Cell(plan.Buyer.DisplayName, head, 2, true);
                string Money(byte[] b) => BitConverter.ToInt32(b).ToString("N0", Inv);
                var money = plan.Writes.Where(w => w.Expected.Length == 4 && w.Value.Length == 4).ToList();
                foreach (var name in money.Select(w => w.What.Split(' ', 2)[1]).Distinct())
                {
                    int r = Row();
                    Cell(char.ToUpperInvariant(name[0]) + name[1..], r, 0);
                    foreach (var (side, col) in new[] { ("seller", 1), ("buyer", 2) })
                        if (money.FirstOrDefault(w => w.What == $"{side} {name}") is { } w) Cell($"{Money(w.Expected)} → {Money(w.Value)}", r, col);
                }
                int squad = Row();
                Cell("Squad", squad, 0);
                Cell($"{plan.ToTeam.SquadCount} → {plan.ToTeam.SquadCount + 1}", squad, 2);
                int percent = (int)(termSellOn.Value ?? 0);
                if (percent > 0)
                {
                    int s = Row();
                    Cell("Sell-on", s, 0);
                    string signed = plan.Fee > 0 ? $", signed for {plan.Fee.ToString("N0", Inv)}" : "";
                    Cell($"{percent}% owed{signed}", s, 2); Cell($"{percent}% due", s, 1);
                }
                var risks = new List<string>();
                if (plan.NeedsAllocation) risks.Add("The squad is full, so a new squad list");
                if (percent > 0) risks.Add("The sell-on record");
                if (risks.Count > 0)
                {
                    warning.Text = $"{string.Join(" and ", risks)} will be allocated outside the game's allocator; keep a backup of the save.";
                    warning.IsVisible = true;
                }
                apply.IsEnabled = true;
            }
            catch (Exception ex) { Invalidate(); warning.Text = UserMessage.For(ex); warning.IsVisible = true; }
        }
        void FilterClubs()
        {
            if (player.Gender == Fm27Editor.Core.Offsets.Gender.Women || player.Club?.Gender == Fm27Editor.Core.Offsets.Gender.Women)
            {
                chosenClub = null;
                warning.Text = "Only male players and men's clubs are supported.";
                warning.IsVisible = true;
            }
            Invalidate();
        }
        void SelectTarget(ClubOption club, TeamOption? team)
        {
            chosenClub = club;
            chosenTeam = team;
            target.Content = team is null ? club.Club.DisplayName : $"{club.Club.DisplayName} — {team.TeamName}";
            Preview();
        }
        target.Click += async (_, _) =>
        {
            if (player.Gender == Fm27Editor.Core.Offsets.Gender.Women || player.Club?.Gender == Fm27Editor.Core.Offsets.Gender.Women) return;
            var clubs = all.Where(c => c.FirstTeam is not null && c.Address != player.Club?.Address && c.Gender != Fm27Editor.Core.Offsets.Gender.Women)
                .OrderBy(c => c.DisplayName).Select(c => new ClubOption(c)).ToList();
            var chosen = await PickerDialog.Show<ClubOption>(this, "Choose destination club", q => clubs.Where(o => PickerSearch.Hit(q, o.Club.DisplayName, o.Club.ShortName, o.Club.Id)).Select(o => (o.Club.DisplayName, o)).ToList());
            if (chosen is null) return;
            var options = chosen.Club.Teams.OrderByDescending(t => t.IsFirstTeam).Select((t, i) => new TeamOption(t, i)).ToList();
            TeamOption? team = options.FirstOrDefault();
            if (options.Count > 1)
            {
                team = await PickerDialog.Show<TeamOption>(this, "Choose team", q => options.Where(o => PickerSearch.Hit(q, o.TeamName)).Select(o => (o.TeamName, o)).ToList());
                if (team is null) return;
            }
            SelectTarget(chosen, team);
        };
        fee.ValueChanged += (_, _) => Preview();
        loanWage.ValueChanged += (_, _) => Preview();
        loanPrice.ValueChanged += (_, _) => Preview();
        loanBuyBack.ValueChanged += (_, _) => Preview();
        loanMonthly.ValueChanged += (_, _) => Preview();
        loanAfter.ValueChanged += (_, _) => Preview();
        loanStart.TextChanged += (_, _) => Preview();
        loanEnd.TextChanged += (_, _) => Preview();
        futureDate.TextChanged += (_, _) => Preview();
        foreach (var box in new[] { instAmount, instMonths, leagueAmount, leagueCount, intlAmount, intlCount, perAmount, perCount, goalsAmount, goalsCount, termSellOn, buyBack, wageShare })
            box.ValueChanged += (_, _) => Preview();
        foreach (var box in new[] { termProfit, friendly }) box.IsCheckedChanged += (_, _) => Preview();
        instPeriod.SelectionChanged += (_, _) => Preview();
        var feeRow = Labeled("Fee", fee);
        var moveNowRow = Labeled(" ", moveNow);
        moveNowRow.IsVisible = canFuture;
        var futureRows = new Control[] { Labeled("Transfer Date", futureDate) };
        foreach (var row in futureRows) row.IsVisible = false;
        var loanRows = new Control[]
        {
            Labeled("Wage (% of current)", loanWage), Labeled("Start", loanStart), Labeled("Until", loanEnd), Labeled("Agreed Price", loanPrice),
            Labeled("Buy Back Fee", loanBuyBack), Labeled("Monthly Fee", loanMonthly), Labeled("Purchase After (matches)", loanAfter),
        };
        foreach (var row in loanRows) row.IsVisible = false;
        void ShowRows()
        {
            feeRow.IsVisible = !IsLoan();
            moveNowRow.IsVisible = canFuture && !IsLoan();
            foreach (var row in futureRows) row.IsVisible = IsFuture();
            foreach (var row in termRows) row.IsVisible = !IsLoan();
            foreach (var row in loanRows) row.IsVisible = IsLoan();
            apply.Content = IsLoan() ? "Confirm loan" : "Confirm transfer";
        }
        moveNow.IsCheckedChanged += (_, _) => { ShowRows(); Preview(); };
        type.SelectionChanged += (_, _) => { ShowRows(); Preview(); };
        apply.Click += async (_, _) =>
        {
            // Tell the player what is missing in plain words before anything is written.
            if (chosenClub is null)
            {
                await NoticeDialog.Show(this, "Where should the player go?", "Choose the club and team the player should join, then select the confirm button again.");
                return;
            }
            if (chosenTeam is null)
            {
                await NoticeDialog.Show(this, "Which team?", "This club has more than one team. Choose the team the player should join, then try again.");
                return;
            }
            if (loanPlan is { } loan)
            {
                try
                {
                    backend.Edits.Execute("Player", player.DisplayName, "Page save", () => backend.Session.ApplyLoanCreate(loan));
                }
                catch (Exception ex) { Invalidate(); status.Text = $"Loan stopped: {UserMessage.For(ex)}"; return; }
                clubCache = null; contractDrafts.Remove(player.Address);
                selected = backend.Players.Reload(player.Address); if (selected is not null) ReplaceRow(selected);
                RenderEditor();
                status.Text = $"Loaned to {chosenClub?.Club.DisplayName}. Continue in FM to see it.";
                return;
            }
            if (futurePlan is { } future)
            {
                try
                {
                    backend.Edits.Execute("Player", player.DisplayName, "Page save", () => backend.Session.ApplyFutureTransfer(future));
                }
                catch (Exception ex) { Invalidate(); status.Text = $"Future transfer stopped: {UserMessage.For(ex)}"; return; }
                clubCache = null; contractDrafts.Remove(player.Address);
                selected = backend.Players.Reload(player.Address); if (selected is not null) ReplaceRow(selected);
                pendingSection = "Contract"; RenderEditor();
                status.Text = $"Future transfer to {chosenClub?.Club.DisplayName} on {DateSetting.Format(future.Date)} created. Continue in FM to see it.";
                return;
            }
            if (plan is null) return;
            int percent = backend.Session.Profile.Club?.TransferClauses?.Scan == true ? 0 : (int)(termSellOn.Value ?? 0);
            if (percent > 0 && player.Id is null) { status.Text = "This player's ID can't be read, so no sell-on can be added"; return; }
            try
            {
                backend.Edits.Execute("Player", player.DisplayName, "Page save", () => backend.Session.ApplyTransfer(plan));
            }
            catch (Exception ex) { Invalidate(); status.Text = $"Transfer stopped: {UserMessage.For(ex)}"; return; }
            clubCache = null; contractDrafts.Remove(player.Address);
            selected = backend.Players.Reload(player.Address); if (selected is not null) ReplaceRow(selected);
            pendingSection = "Contract"; RenderEditor();
            status.Text = $"Moved to {plan.Buyer.DisplayName}. Set the new contract terms; club finances show after Continue in FM.";
            if (percent > 0)
            {
                try
                {
                    backend.Edits.Execute("Player", player.DisplayName, "Page save", () => backend.Session.AddSellOn(plan.Buyer.Address, player.Id!.Value, plan.Seller.Address, percent, plan.Fee > 0 ? plan.Fee : null));
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
                if (task.IsFaulted) { warning.Text = UserMessage.For(task.Exception!.InnerException!); warning.IsVisible = true; return; }
                clubCache = all = task.Result; FilterClubs();
            }));
        }
        else FilterClubs();

        var formStack = new StackPanel { Spacing = 8, Margin = new Thickness(12, 10) };
        formStack.Children.Add(from);
        formStack.Children.Add(Labeled("Team", target));
        formStack.Children.Add(Labeled("Type", type));
        formStack.Children.Add(moveNowRow);
        formStack.Children.Add(feeRow);
        foreach (var row in futureRows) formStack.Children.Add(row);
        foreach (var row in termRows) formStack.Children.Add(row);
        foreach (var row in loanRows) formStack.Children.Add(row);
        formStack.Children.Add(apply);
        formStack.Children.Add(warning);

        var leftCard = UiKit.Card("Transfer Destination", formStack);
        leftCard.VerticalAlignment = VerticalAlignment.Top;
        leftCard.HorizontalAlignment = HorizontalAlignment.Left;
        leftCard.Width = 660;
        leftCard.Margin = new Thickness(0, 4, 0, 8);
        return leftCard;
    }

    static Control Labeled(string label, Control control)
    {
        if (control is NumericUpDown or DateBox) { control.Width = double.NaN; control.HorizontalAlignment = HorizontalAlignment.Stretch; }
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), ColumnSpacing = 8 };
        var lbl = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(control, 1);
        g.Children.Add(lbl);
        g.Children.Add(control);
        return g;
    }

    sealed record EnumOption<T>(T Value, string Label) { public override string ToString() => Label; }
    sealed record TermOption(short Type, string Label) { public override string ToString() => Label; }
    sealed record ClubOption(ClubEntry Club) { public override string ToString() => $"{Club.DisplayName}   {Club.Id}"; }
    sealed record TeamOption(TeamEntry Team, int Index) { public string TeamName => Team.IsFirstTeam ? "First team" : $"Team {Index + 1}"; public override string ToString() => $"{TeamName}   {Team.SquadCount}/{Team.SquadCapacity}"; }
}

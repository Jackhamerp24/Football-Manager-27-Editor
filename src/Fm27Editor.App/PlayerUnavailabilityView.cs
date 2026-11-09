using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    /// <summary>One ban card's editors, laid out like FMRTE's Unavailability card.</summary>
    sealed class BanEditor
    {
        public required ComboBox Scope, Reason;
        public required TextBox Item, Points, Start, Days, Matches, Fulfilled;
        public required Control ItemRow;

        public static IReadOnlyList<string> ScopeItems(int current) =>
            [.. BanLabels.Scopes.Select(s => s.Name), .. BanLabels.Scopes.Any(s => s.Code == current) ? [] : new[] { BanLabels.ScopeName(current) }];
        public static IReadOnlyList<string> ReasonItems(int current) =>
            [.. BanLabels.Reasons.Select(r => r.Name), .. BanLabels.Reasons.Any(r => r.Code == current) ? [] : new[] { BanLabels.ReasonName(current) }];

        int ScopeCode(int fallback) => Scope.SelectedIndex < BanLabels.Scopes.Count ? BanLabels.Scopes[Scope.SelectedIndex].Code : fallback;
        int ReasonCode(int fallback) => Reason.SelectedIndex < BanLabels.Reasons.Count ? BanLabels.Reasons[Reason.SelectedIndex].Code : fallback;

        public void ShowItem() => ItemRow.IsVisible = Item.IsVisible = Scope.SelectedIndex >= BanLabels.Scopes.Count || BanLabels.Scopes[Scope.SelectedIndex].HasItem;

        /// <summary>The fields as typed, or null with the problem in <paramref name="error"/>.</summary>
        public BanWriter.BanFields? Parse(int scopeFallback, int reasonFallback, out string error)
        {
            error = "Unavailability: enter whole numbers, a date like 2026-11-25, 0–127 matches and a fulfilled count within that total";
            int days = -1;
            if (!int.TryParse(Item.Text, out var item) || !int.TryParse(Points.Text, out var points) || (!string.IsNullOrWhiteSpace(Days.Text) && !int.TryParse(Days.Text, out days))
                || !int.TryParse(Matches.Text, out var matches) || !int.TryParse(Fulfilled.Text, out var fulfilled)
                || !DateSetting.TryParse(Start.Text, out var start)
                || points is < 0 or > 127 || days is < -1 or > short.MaxValue || matches is < 0 or > 127 || fulfilled < 0 || fulfilled > matches)
                return null;
            int scope = ScopeCode(scopeFallback);
            if (!(BanLabels.Scopes.FirstOrDefault(s => s.Code == scope)?.HasItem ?? true)) item = -1;
            return new(scope, item, ReasonCode(reasonFallback), points, start, days, matches, fulfilled);
        }
    }

    BanEditor BanCard(StackPanel panel, int scope, int item, int reason, int points, DateOnly? start, int days, int matches, int fulfilled, Control? actions, string? title = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 24, RowSpacing = 4, Margin = new Thickness(12, 8) };
        TextBox Box(string text) => new() { Text = text, Width = 110, HorizontalContentAlignment = HorizontalAlignment.Right };
        var e = new BanEditor
        {
            Scope = new ComboBox { Width = 300, ItemsSource = BanEditor.ScopeItems(scope) },
            Reason = new ComboBox { Width = 300, ItemsSource = BanEditor.ReasonItems(reason) },
            Item = Box(item.ToString(Inv)), Points = Box(points.ToString(Inv)), Start = new DateBox { Text = DateSetting.Format(start), Width = 130, HorizontalContentAlignment = HorizontalAlignment.Right },
            Days = Box(days == -1 ? "" : days.ToString(Inv)), Matches = Box(matches.ToString(Inv)), Fulfilled = Box(fulfilled.ToString(Inv)),
            ItemRow = new TextBlock { Text = "Item", VerticalAlignment = VerticalAlignment.Center },
        };
        e.Days.Watermark = "None";
        int si = BanLabels.Scopes.ToList().FindIndex(s => s.Code == scope);
        e.Scope.SelectedIndex = si >= 0 ? si : BanLabels.Scopes.Count;
        int ri = BanLabels.Reasons.ToList().FindIndex(r => r.Code == reason);
        e.Reason.SelectedIndex = ri >= 0 ? ri : BanLabels.Reasons.Count;
        var rows = new (Control Label, Control Value)[]
        {
            (new TextBlock { Text = "Scope" }, e.Scope), (e.ItemRow, e.Item), (new TextBlock { Text = "Reason" }, e.Reason),
            (new TextBlock { Text = "Number of points" }, e.Points), (new TextBlock { Text = "Start Date" }, e.Start),
            (new TextBlock { Text = "Number of Days" }, e.Days), (new TextBlock { Text = "Num. matches" }, e.Matches),
            (new TextBlock { Text = "Num. matches fulfilled" }, e.Fulfilled),
        };
        for (int r = 0; r < rows.Length; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            rows[r].Label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(rows[r].Label, r); Grid.SetRow(rows[r].Value, r); Grid.SetColumn(rows[r].Value, 1);
            rows[r].Value.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(rows[r].Label); grid.Children.Add(rows[r].Value);
        }
        e.ShowItem();
        e.Scope.SelectionChanged += (_, _) => e.ShowItem();
        var body = new StackPanel { Children = { grid } };
        if (actions is not null) body.Children.Add(new Border { BorderBrush = UiKit.BorderSubtle, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 8), Child = actions });
        panel.Children.Add(UiKit.Card(title ?? BanLabels.ScopeName(scope), body));
        return e;
    }

    Control UnavailabilityPanel(PlayerRecord player)
    {
        var panel = new StackPanel { Spacing = 8 };
        var bans = backend.Session.ReadBans(player);
        var session = backend.Session;
        BanWriter Writer() => new(session.Memory, session.Profile, session.Game.ModuleBase);
        string done = "Continue in FM to see updates.";
        foreach (var ban in bans)
        {
            var save = new Button { IsVisible = false, Content = "Save", Classes = { "accent" } };
            var remove = new Button { Content = "Remove" };
            var e = BanCard(panel, ban.Scope, ban.ItemId, ban.Reason, ban.Points, ban.Start, ban.Days, ban.Matches, ban.MatchesFulfilled,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, remove } });
            save.Click += (_, _) =>
            {
                if (e.Parse(ban.Scope, ban.Reason, out var error) is not { } f) { status.Text = error; return; }
                try { backend.Edits.Execute("Player", player.DisplayName, "Page save", () => Writer().SetFields(player, ban, f)); status.Text = $"Saved unavailability. {done}"; RenderEditor(); }
                catch (Exception ex) { status.Text = ex.Message; }
            };
            remove.Click += (_, _) =>
            {
                try { backend.Edits.Execute("Player", player.DisplayName, "Page save", () => Writer().Remove(player, ban)); status.Text = $"Removed {BanLabels.ScopeName(ban.Scope)}. {done}"; RenderEditor(); }
                catch (Exception ex) { status.Text = ex.Message; }
            };
        }

        var addHost = new StackPanel { Spacing = 8 };
        var showAddBtn = new Button { Content = "Add unavailability" };
        addHost.Children.Add(showAddBtn);

        showAddBtn.Click += (_, _) =>
        {
            addHost.Children.Clear();
            var add = new Button { Content = "Add", Classes = { "accent" } };
            var cancel = new Button { Content = "Cancel" };
            var today = session.ReadGameDate() ?? DateOnly.FromDateTime(DateTime.Today);
            var n = BanCard(addHost, 25, -1, 2, 2, today, -1, 3, 0,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, cancel } },
                "New Unavailability");
            add.Click += (_, _) =>
            {
                if (n.Parse(25, 2, out var error) is not { } f) { status.Text = error; return; }
                try
                {
                    backend.Edits.Execute("Player", player.DisplayName, "Page save", () => session.AddBan(player, bans, f));
                    status.Text = $"Added {BanLabels.ScopeName(f.Scope)}. Its record lives outside the game's allocator; keep a backup of the save. {done}";
                    RenderEditor();
                }
                catch (Exception ex) { status.Text = ex.Message; }
            };
            cancel.Click += (_, _) =>
            {
                addHost.Children.Clear();
                addHost.Children.Add(showAddBtn);
            };
        };

        panel.Children.Add(addHost);
        return panel;
    }
}

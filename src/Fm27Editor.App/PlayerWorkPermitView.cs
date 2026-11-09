using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// FMRTE 27's Work Permit tab. "Work Permit": the permits given to the player (nation and year; add and remove). "Work Permit
    /// Denied": the clubs refused a permit, each with Remove. Every change waits for the page's Save. The permit table is found
    /// in the background the first time (a scan of the game), so the denied list shows at once.
    /// </summary>
    Control WorkPermitPanel(PlayerRecord player)
    {
        var panel = new StackPanel { Spacing = 8 };
        var session = backend.Session;
        var permitHost = new Border();
        var deniedHost = new Border();
        panel.Children.Add(permitHost);
        panel.Children.Add(deniedHost);

        var save = new Button { IsVisible = false, Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var removedDenied = new HashSet<ulong>();
        var removedGrants = new HashSet<ulong>();
        var permanent = new HashSet<ulong>();
        var yearEdits = new Dictionary<ulong, TextBox>();
        var additions = new List<(ulong Nation, string Name, TextBox Year)>();
        IReadOnlyList<WorkPermitGrant> grants = [];
        IReadOnlyList<WorkPermitDenial> denied = [];
        bool permitsLoaded = false;
        bool deniedLoaded = false;

        void Changed() => save.IsEnabled =
            removedDenied.Count > 0 || removedGrants.Count > 0 || permanent.Count > 0 || additions.Count > 0 || grants.Any(g => yearEdits.TryGetValue(g.Record, out var t) && t.Text != g.Year.ToString(Inv));

        Button RemoveToggle(HashSet<ulong> set, ulong key, Control line)
        {
            var button = new Button { Content = "Remove" };
            button.Click += (_, _) =>
            {
                if (!set.Remove(key)) set.Add(key);
                bool now = set.Contains(key);
                button.Content = now ? "Undo remove" : "Remove";
                line.Opacity = now ? 0.45 : 1;
                Changed();
            };
            return button;
        }

        void BuildPermits()
        {
            var rows = new StackPanel { Spacing = 6, Margin = new Thickness(12, 8) };
            Action? removeSelected = null;
            Border? selectedCard = null;
            var remove = new Button { Content = "−", IsEnabled = false, MinWidth = 28 };
            void Select(Border card, Action action)
            {
                if (selectedCard is not null) selectedCard.BorderBrush = UiKit.BorderSubtle;
                selectedCard = card;
                selectedCard.BorderBrush = UiKit.Accent;
                removeSelected = action;
                remove.IsEnabled = true;
            }
            remove.Click += (_, _) => removeSelected?.Invoke();
            Grid Form(Control nation, TextBox year, DateOnly? review, Button? make)
            {
                var form = new Grid { ColumnDefinitions = new ColumnDefinitions("138,*"), RowSpacing = 4, Margin = new Thickness(8, 6) };
                void Row(string label, Control value)
                {
                    int r = form.RowDefinitions.Count;
                    form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextSecondary };
                    Grid.SetRow(text, r); Grid.SetRow(value, r); Grid.SetColumn(value, 1);
                    form.Children.Add(text); form.Children.Add(value);
                }
                Row("Nation", nation);
                Row("Year", year);
                if (review is { } date) Row("Renewal review from", new TextBlock { Text = DateSetting.Format(date), VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextPrimary });
                if (make is not null)
                {
                    int r = form.RowDefinitions.Count; form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    Grid.SetRow(make, r); Grid.SetColumnSpan(make, 2); form.Children.Add(make);
                }
                return form;
            }
            foreach (var grant in grants)
            {
                var year = new TextBox { Text = grant.Year.ToString(Inv), Width = 80, HorizontalContentAlignment = HorizontalAlignment.Right };
                yearEdits[grant.Record] = year;
                year.TextChanged += (_, _) => Changed();
                Button? make = null;
                if (grant.Temporary)
                {
                    make = new Button { Content = "Make permanent", HorizontalAlignment = HorizontalAlignment.Left };
                    make.Click += (_, _) => { if (!permanent.Remove(grant.Record)) permanent.Add(grant.Record); make.Content = permanent.Contains(grant.Record) ? "Undo permanent" : "Make permanent"; Changed(); };
                }
                var card = new Border { Background = UiKit.SurfaceInput, BorderBrush = UiKit.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                    Child = Form(new TextBlock { Text = grant.NationName ?? "Unknown nation", VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextPrimary }, year, grant.ReviewFrom, make) };
                void ToggleRemove() { if (!removedGrants.Remove(grant.Record)) removedGrants.Add(grant.Record); card.Opacity = removedGrants.Contains(grant.Record) ? 0.45 : 1; Changed(); }
                card.PointerPressed += (_, _) => Select(card, ToggleRemove);
                rows.Children.Add(card);
                if (selectedCard is null) Select(card, ToggleRemove);
            }
            var added = new StackPanel { Spacing = 6 };
            rows.Children.Add(added);
            var add = new Button { Content = "+", MinWidth = 28 };
            add.Click += async (_, _) =>
            {
                var nations = NationCatalogueForPicker();
                var nation = await PickerDialog.Show(this, "Work Permit", text =>
                {
                    string folded = Fold(text);
                    return nations.Where(n => Fold(n.DisplayName).Contains(folded, StringComparison.Ordinal)).Take(200).Select(n => (n.DisplayName, n)).ToList();
                }, watermark: "Choose the nation that issues the permit");
                if (nation is null) return;
                var year = new TextBox { Text = session.ReadGameDate()?.Year.ToString(Inv) ?? "", Width = 80, HorizontalContentAlignment = HorizontalAlignment.Right };
                year.TextChanged += (_, _) => Changed();
                var entry = (nation.Address, nation.DisplayName, year);
                additions.Add(entry);
                var card = new Border { Background = UiKit.SurfaceInput, BorderBrush = UiKit.BorderSubtle, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                    Child = Form(new TextBlock { Text = nation.DisplayName, VerticalAlignment = VerticalAlignment.Center, Foreground = UiKit.TextPrimary }, year, null, null) };
                void Cancel() { additions.Remove(entry); added.Children.Remove(card); selectedCard = null; removeSelected = null; remove.IsEnabled = false; Changed(); }
                card.PointerPressed += (_, _) => Select(card, Cancel);
                added.Children.Add(card);
                Select(card, Cancel);
                Changed();
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { add, remove } };
            permitHost.Child = UiKit.Card("Work Permit", rows, rightHeader: buttons);
        }

        void BuildDenied()
        {
            var rows = new StackPanel { Spacing = 4, Margin = new Thickness(12, 8) };
            if (deniedLoaded && denied.Count == 0) rows.Children.Add(new TextBlock { Text = "None", Foreground = UiKit.TextSecondary });
            foreach (var denial in denied)
            {
                var line = new DockPanel();
                var remove = RemoveToggle(removedDenied, denial.Team, line);
                DockPanel.SetDock(remove, Dock.Right);
                line.Children.Add(remove);
                line.Children.Add(new TextBlock { Text = denial.ClubName ?? "Unknown club", VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = UiKit.TextPrimary });
                rows.Children.Add(line);
            }
            deniedHost.Child = UiKit.Card("Work Permit Denied", rows);
        }

        try { denied = session.ReadWorkPermitDenied(player); deniedLoaded = true; } catch (Exception ex) { status.Text = UserMessage.For(ex); }
        BuildDenied();
        permitHost.Child = UiKit.Card("Work Permit", new StackPanel());
        _ = Task.Run(() => session.ReadWorkPermits(player)).ContinueWith(task => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (task.IsFaulted) { status.Text = UserMessage.For(task.Exception!.InnerException!); return; }
            grants = task.Result;
            permitsLoaded = true;
            BuildPermits();
        }));

        save.Click += (_, _) =>
        {
            foreach (var (_, _, year) in additions)
                if (!int.TryParse(year.Text, out int y) || y is < 1900 or > 2300) { status.Text = "Work permit: enter a year like 2026"; return; }
            foreach (var g in grants)
                if (yearEdits.TryGetValue(g.Record, out var t) && (!int.TryParse(t.Text, out int y) || y is < 1900 or > 2300)) { status.Text = "Work permit: enter a year like 2026"; return; }
            if (!permitsLoaded && (additions.Count > 0 || removedGrants.Count > 0)) { status.Text = "The work permits are still loading"; return; }
            try
            {
                backend.Edits.Execute("Player", player.DisplayName, "Work Permit", () =>
                {
                    foreach (var denial in denied.Where(d => removedDenied.Contains(d.Team))) session.RemoveWorkPermitDenied(player, denial);
                    foreach (var grant in grants.Where(g => removedGrants.Contains(g.Record))) session.RemoveWorkPermit(player, grant);
                    foreach (var grant in grants.Where(g => !removedGrants.Contains(g.Record) && yearEdits.TryGetValue(g.Record, out var t) && t.Text != g.Year.ToString(Inv)))
                        session.SetWorkPermitYear(player, grant, int.Parse(yearEdits[grant.Record].Text!));
                    foreach (var grant in grants.Where(g => !removedGrants.Contains(g.Record) && permanent.Contains(g.Record))) session.MakeWorkPermitPermanent(player, grant);
                    foreach (var (nation, _, year) in additions) session.AddWorkPermit(player, nation, int.Parse(year.Text!));
                });
                status.Text = "Saved work permit changes. Continue in FM to see updates.";
                RenderEditor();
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        panel.Children.Add(save);
        return panel;
    }

    IReadOnlyList<Fm27Editor.Core.Nations.NationRecord> NationCatalogueForPicker() => ((IScreenHost)this).NationCatalogue;
}

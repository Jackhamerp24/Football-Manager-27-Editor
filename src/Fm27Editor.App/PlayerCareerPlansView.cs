using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    IReadOnlyList<CareerItem>? careerCatalogue;
    PlayerRecord[]? playerSource;

    /// <summary>The loaded players, or on other screens (Staff, Clubs) every player read once: clubs, competitions and languages come from them.</summary>
    PlayerRecord[] Players() => rows.OfType<PlayerRecord>().ToArray() is { Length: > 0 } shown ? shown : playerSource ??= backend.Session.Players.LoadAll().ToArray();

    /// <summary>One plan's dropdowns, laid out like FMRTE's Options box; also used for a plan that is being added.</summary>
    sealed class PlanEditor
    {
        public required ComboBox Condition, Job, ItemType, Scope;
        public required AutoCompleteBox Item;
        public Control[] Cells = [];

        public CareerPlanFields? Fields() =>
            Condition.SelectedIndex < 0 || Job.SelectedIndex < 0 || Scope.SelectedIndex < 0 || Item.SelectedItem is not CareerTarget t ? null
            : new(CareerPlanLabels.Conditions[Condition.SelectedIndex].Code, CareerPlanLabels.Scopes[Scope.SelectedIndex].Code,
                CareerPlanLabels.Jobs[Job.SelectedIndex].Code, t.Item);
    }

    sealed record CareerTarget(CareerItem Item)
    {
        public override string ToString() => Item.Name;
    }

    PlanEditor PlanRow(Grid grid, int condition, int job, CareerItem? item, int scope, IReadOnlyList<CareerItem> catalogue, Action changed)
    {
        static int Index<T>(IReadOnlyList<(T Code, string Name)> list, T code) => list.ToList().FindIndex(x => Equals(x.Code, code));
        var e = new PlanEditor
        {
            Condition = new ComboBox { Width = 182, Padding = new Thickness(6, 0), ItemsSource = CareerPlanLabels.Conditions.Select(x => x.Name).ToArray(), SelectedIndex = Index(CareerPlanLabels.Conditions, condition) },
            Job = new ComboBox { Width = 152, Padding = new Thickness(6, 0), ItemsSource = CareerPlanLabels.Jobs.Select(x => x.Name).ToArray(), SelectedIndex = Index(CareerPlanLabels.Jobs, job) },
            ItemType = new ComboBox { Width = 98, Padding = new Thickness(6, 0), ItemsSource = CareerPlanLabels.ItemTypes.Select(x => x.Name).ToArray(), SelectedIndex = Index(CareerPlanLabels.ItemTypes, item?.Type ?? CareerItemType.Club) },
            // Type to search the chosen kind (clubs, continents, nations, competitions), then pick.
            Item = new AutoCompleteBox { Width = 270, MinimumPrefixLength = 2, Padding = new Thickness(0), FilterMode = AutoCompleteFilterMode.Custom, MaxDropDownHeight = 300,
                ItemFilter = (q, o) => o is CareerTarget t && Matches(t.Item.Name, q ?? "") },
            Scope = new ComboBox { Width = 96, Padding = new Thickness(6, 0), ItemsSource = CareerPlanLabels.Scopes.Select(x => x.Name).ToArray(), SelectedIndex = Index(CareerPlanLabels.Scopes, scope) },
        };
        void Source()
        {
            if (e.ItemType.SelectedIndex < 0) return;
            var type = CareerPlanLabels.ItemTypes[e.ItemType.SelectedIndex].Type;
            var choices = catalogue.Where(c => c.Type == type).Select(c => new CareerTarget(c)).ToList();
            if (item is not null && item.Type == type && !choices.Any(c => c.Item.Address == item.Address))
                choices.Add(new CareerTarget(item));
            e.Item.ItemsSource = choices;
        }
        Source();
        if (item is not null)
        {
            e.Item.SelectedItem = (e.Item.ItemsSource as IEnumerable<CareerTarget>)?.FirstOrDefault(c => c.Item.Address == item.Address);
            e.Item.Text = item.Name;
            ToolTip.SetTip(e.Item, item.Name);
        }
        e.ItemType.SelectionChanged += (_, _) => { Source(); e.Item.SelectedItem = null; e.Item.Text = ""; changed(); };
        e.Item.SelectionChanged += (_, _) => { ToolTip.SetTip(e.Item, (e.Item.SelectedItem as CareerTarget)?.Item.Name); changed(); };
        foreach (var box in new[] { e.Condition, e.Job, e.Scope }) box.SelectionChanged += (_, _) => changed();
        int r = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Control[] cells = [e.Condition, e.Job, e.ItemType, e.Item, e.Scope];
        for (int c = 0; c < cells.Length; c++) { Grid.SetRow(cells[c], r); Grid.SetColumn(cells[c], c); grid.Children.Add(cells[c]); }
        e.Cells = cells;
        return e;
    }

    Control CareerPlansPanel(PlayerRecord player) => CareerPlansPanel(backend.Session.Owner(player), RenderEditor);

    /// <summary>
    /// FMRTE's Career Plans tab for a player or a staff member (same container on the person). Nothing is written until the one
    /// Save: Add puts an empty row in the list, Remove takes a row out (an unsaved new row just disappears).
    /// </summary>
    Control CareerPlansPanel(PersonOwner player, Action rerender)
    {
        var panel = new StackPanel { Spacing = 8 };
        var session = backend.Session;
        if (session.Profile.CareerPlans is null || session.CareerPlans.Read(player) is not { } plans) return panel;
        clubCache ??= session.LoadClubs(Players());
        competitionCache ??= session.LoadCompetitions(Players());
        careerCatalogue ??= session.CareerPlans.Catalogue(clubCache.Select(c => c.Address), competitionCache.Values.Select(c => c.Address));
        var catalogue = careerCatalogue;

        var save = new Button { IsVisible = false, Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var rowsOf = new List<(CareerPlan Plan, PlanEditor Editor)>();
        var removed = new HashSet<CareerPlan>();
        var added = new List<PlanEditor>();
        bool Changed((CareerPlan Plan, PlanEditor Editor) x) => !removed.Contains(x.Plan) &&
            (x.Editor.Fields() is not { } f || f.Condition != x.Plan.Condition || f.Job != x.Plan.Job || f.Scope != x.Plan.Scope || f.Item.Address != x.Plan.ItemAddress);
        bool Pending() => removed.Count > 0 || added.Count > 0 || rowsOf.Any(Changed);
        void Refresh() => save.IsEnabled = Pending();

        static void Hide(PlanEditor editor, Control remove) { foreach (var c in editor.Cells) c.IsVisible = false; remove.IsVisible = false; }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"), ColumnSpacing = 6, RowSpacing = 4, Margin = new Thickness(12, 8) };
        void PlaceRemove(Button remove) { Grid.SetRow(remove, grid.RowDefinitions.Count - 1); Grid.SetColumn(remove, 5); grid.Children.Add(remove); }
        foreach (var plan in plans)
        {
            // Some nation items have no nation the editor can name (Hulk's); keep them as they are.
            var item = session.CareerPlans.Item(plan.ItemAddress)
                ?? (session.CareerPlans.KindOf(plan.ItemAddress) is { } kind ? new CareerItem(plan.ItemAddress, kind, "") : null);
            var editor = PlanRow(grid, plan.Condition, plan.Job, item, plan.Scope, catalogue, Refresh);
            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) => { removed.Add(plan); Hide(editor, remove); Refresh(); };
            PlaceRemove(remove);
            rowsOf.Add((plan, editor));
        }

        var add = new Button { Content = "Add", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 0, 12, 8) };
        add.Click += (_, _) =>
        {
            var editor = PlanRow(grid, 1, 1, null, 1, catalogue, Refresh);
            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) => { added.Remove(editor); Hide(editor, remove); Refresh(); };
            PlaceRemove(remove);
            added.Add(editor);
            Refresh();
        };

        var list = new StackPanel { Children = { grid, add } };
        panel.Children.Add(UiKit.Card("Career Plans", list));
        save.Click += (_, _) =>
        {
            var changes = rowsOf.Where(Changed).ToList();
            if (changes.Any(c => c.Editor.Fields() is null) || added.Any(a => a.Fields() is null)) { status.Text = "Career Plans: pick an item for every row"; return; }
            var gone = removed.Select(p => p.Address).ToList();
            var fresh = added.Select(a => a.Fields()!).ToList();
            try
            {
                backend.Edits.Execute("Person", player.Name, "Career Plans", () =>
                {
                    foreach (var c in changes) session.CareerPlanWriter.Set(player, plans, c.Plan, c.Editor.Fields()!);
                    // Removing and adding move the list, so it is read again before each one.
                    foreach (var address in gone)
                    {
                        var now = session.CareerPlans.Read(player) ?? throw new StaleObjectException("The player's career plans could not be read");
                        session.CareerPlanWriter.Remove(player, now, now.FirstOrDefault(p => p.Address == address) ?? throw new StaleObjectException("The plan is no longer in the list"));
                    }
                    foreach (var fields in fresh)
                        session.AddCareerPlan(player, session.CareerPlans.Read(player) ?? throw new StaleObjectException("The player's career plans could not be read"), fields);
                });
                rerender();
                status.Text = "Saved career plans. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        panel.Children.Add(save);
        return panel;
    }
}

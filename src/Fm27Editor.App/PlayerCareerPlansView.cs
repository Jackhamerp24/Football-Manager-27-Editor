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

    /// <summary>One plan's dropdowns, laid out like FMRTE's Options box; also used for the new-plan row.</summary>
    sealed class PlanEditor
    {
        public required ComboBox Condition, Job, ItemType, Scope;
        public required AutoCompleteBox Item;

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
            Condition = new ComboBox { Width = 150, ItemsSource = CareerPlanLabels.Conditions.Select(x => x.Name).ToArray(), SelectedIndex = Index(CareerPlanLabels.Conditions, condition) },
            Job = new ComboBox { Width = 150, ItemsSource = CareerPlanLabels.Jobs.Select(x => x.Name).ToArray(), SelectedIndex = Index(CareerPlanLabels.Jobs, job) },
            ItemType = new ComboBox { Width = 100, ItemsSource = CareerPlanLabels.ItemTypes.Select(x => x.Name).ToArray(), SelectedIndex = Index(CareerPlanLabels.ItemTypes, item?.Type ?? CareerItemType.Club) },
            // Type to search the chosen kind (clubs, continents, nations, competitions), then pick.
            Item = new AutoCompleteBox { Width = 170, MinimumPrefixLength = 2, FilterMode = AutoCompleteFilterMode.Custom, MaxDropDownHeight = 300,
                ItemFilter = (q, o) => o is CareerTarget t && Matches(t.Item.Name, q ?? "") },
            Scope = new ComboBox { Width = 95, ItemsSource = CareerPlanLabels.Scopes.Select(x => x.Name).ToArray(), SelectedIndex = Index(CareerPlanLabels.Scopes, scope) },
        };
        void Source()
        {
            if (e.ItemType.SelectedIndex < 0) return;
            var type = CareerPlanLabels.ItemTypes[e.ItemType.SelectedIndex].Type;
            e.Item.ItemsSource = catalogue.Where(c => c.Type == type).Select(c => new CareerTarget(c)).ToArray();
        }
        Source();
        if (item is not null) e.Item.SelectedItem = new CareerTarget(item);
        e.ItemType.SelectionChanged += (_, _) => { Source(); e.Item.SelectedItem = null; e.Item.Text = ""; changed(); };
        e.Item.SelectionChanged += (_, _) => changed();
        foreach (var box in new[] { e.Condition, e.Job, e.Scope }) box.SelectionChanged += (_, _) => changed();
        int r = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Control[] cells = [e.Condition, e.Job, e.ItemType, e.Item, e.Scope];
        for (int c = 0; c < cells.Length; c++) { Grid.SetRow(cells[c], r); Grid.SetColumn(cells[c], c); grid.Children.Add(cells[c]); }
        return e;
    }

    Control CareerPlansPanel(PlayerRecord player) => CareerPlansPanel(backend.Session.Owner(player), RenderEditor);

    /// <summary>FMRTE's Career Plans tab for a player or a staff member (same container on the person).</summary>
    Control CareerPlansPanel(PersonOwner player, Action rerender)
    {
        var panel = new StackPanel { Spacing = 8 };
        var session = backend.Session;
        if (session.Profile.CareerPlans is null || session.CareerPlans.Read(player) is not { } plans) return panel;
        clubCache ??= session.LoadClubs(Players());
        competitionCache ??= session.LoadCompetitions(Players());
        careerCatalogue ??= session.CareerPlans.Catalogue(clubCache.Select(c => c.Address), competitionCache.Values.Select(c => c.Address));
        var catalogue = careerCatalogue;

        var save = new Button { Content = "Save changes", IsEnabled = false, Classes = { "accent" } };
        var discard = new Button { Content = "Discard", IsEnabled = false };
        var rowsOf = new List<(CareerPlan Plan, PlanEditor Editor)>();
        bool Changed((CareerPlan Plan, PlanEditor Editor) x) => x.Editor.Fields() is not { } f
            || f.Condition != x.Plan.Condition || f.Job != x.Plan.Job || f.Scope != x.Plan.Scope || f.Item.Address != x.Plan.ItemAddress;
        bool Pending() => rowsOf.Any(Changed);
        void Refresh() => save.IsEnabled = discard.IsEnabled = Pending();

        // Add and Remove write at once, like FMRTE's +/−; they refuse while edits are pending since the list moves.
        void Change(Action change, string done)
        {
            if (Pending()) { status.Text = "Save or discard the career plan edits first"; return; }
            try { backend.Edits.Execute(change); status.Text = done; rerender(); }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"), ColumnSpacing = 6, RowSpacing = 4, Margin = new Thickness(12, 8) };
        foreach (var plan in plans)
        {
            // Some nation items have no nation the editor can name (Hulk's); keep them as they are.
            var item = session.CareerPlans.Item(plan.ItemAddress)
                ?? (session.CareerPlans.KindOf(plan.ItemAddress) is { } kind ? new CareerItem(plan.ItemAddress, kind, "") : null);
            var editor = PlanRow(grid, plan.Condition, plan.Job, item, plan.Scope, catalogue, Refresh);
            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) => Change(() => session.CareerPlanWriter.Remove(player, plans, plan), "Removed career plan. Continue in FM to see updates.");
            Grid.SetRow(remove, grid.RowDefinitions.Count - 1); Grid.SetColumn(remove, 5); grid.Children.Add(remove);
            rowsOf.Add((plan, editor));
        }

        var addGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"), ColumnSpacing = 6 };
        var add = new Button { Content = "Add" };
        var fresh = PlanRow(addGrid, 1, 1, null, 1, catalogue, () => add.IsEnabled = true);
        Grid.SetColumn(add, 5); addGrid.Children.Add(add);
        add.Click += (_, _) =>
        {
            if (fresh.Fields() is not { } f) { status.Text = "Career Plans: pick an item"; return; }
            Change(() => session.AddCareerPlan(player, plans, f),
                "Added career plan. Its record lives outside the game's allocator; keep a backup of the save. Continue in FM to see updates.");
        };

        var list = new StackPanel();
        if (rowsOf.Count > 0) list.Children.Add(grid);
        list.Children.Add(new Border
        {
            BorderBrush = Brush("#222432"), BorderThickness = new Thickness(0, rowsOf.Count > 0 ? 1 : 0, 0, 0), Padding = new Thickness(12, 8), Child = addGrid,
        });
        panel.Children.Add(new Border { Background = Brush("#161822"), BorderBrush = Brush("#242636"), BorderThickness = new Thickness(1), Child = list });
        save.Click += (_, _) =>
        {
            var changes = rowsOf.Where(Changed).ToList();
            if (changes.Any(c => c.Editor.Fields() is null)) { status.Text = "Career Plans: pick an item for every row"; return; }
            try
            {
                backend.Edits.Execute(() => { foreach (var c in changes) session.CareerPlanWriter.Set(player, plans, c.Plan, c.Editor.Fields()!); });
                status.Text = "Saved career plans. Continue in FM to see updates.";
                rerender();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        discard.Click += (_, _) => rerender();
        if (rowsOf.Count > 0) panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, discard } });
        return panel;
    }
}

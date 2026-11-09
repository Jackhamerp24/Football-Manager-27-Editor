using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.App;

/// <summary>What a screen may ask of the window it lives in. The window implements it; screens never see each other.</summary>
internal interface IScreenHost
{
    EditorBackend Backend { get; }
    GraphicsCatalog Graphics { get; }
    void SetStatus(string text);
    /// <summary>Adds the screen's list to the module row and the navigation box.</summary>
    void AddModule(string title, Control root, object navItem);
    object? SelectedNav { get; }
    event EventHandler NavChanged;
    bool IsShowing(Control body);
    /// <summary>Opens (or re-activates) the entity's tab; <paramref name="restore"/> runs when the tab is clicked again.</summary>
    void OpenTab(string kind, ulong address, string title, Control body, Action restore);
    void SwitchModule(string title);
    IReadOnlyList<NationRecord> NationCatalogue { get; }
    Task OpenNationAsync(ulong address);
    Task OpenClubAsync(ulong address);
    void OpenCity(ulong address);
    void OpenStadium(ulong address);
    void OpenStaff(Fm27Editor.Core.Staff.StaffRecord staff);
    /// <summary>The window, for dialogs.</summary>
    Avalonia.Controls.Window Window { get; }
    /// <summary>The Staff table of a club or nation, with its scoped contract tools.</summary>
    Control BuildStaffTable(IReadOnlyList<StaffListRow> rows, IStaffContractScope scope, Action refresh);
    /// <summary>The kit editor shared by clubs and nations; its pending edits are kept per owner.</summary>
    Control BuildKits(ulong owner, IReadOnlyList<Fm27Editor.Core.Clubs.ClubKitItem> items, Action changed);
    IReadOnlyDictionary<ulong, KitDraft>? KitDraftOf(ulong owner);
    void ClearKitDraft(ulong owner);
    /// <summary>The squad table and its presets, shared by clubs and nations.</summary>
    Control BuildSquad(Func<IReadOnlyList<Fm27Editor.Core.Players.PlayerRecord>> getSquad);
    /// <summary>The players the window has loaded.</summary>
    IReadOnlyList<Fm27Editor.Core.Players.PlayerRecord> Players();
    IReadOnlyList<Fm27Editor.Core.Clubs.ClubEntry> Clubs();
}

/// <summary>What the window needs to know about any entity screen (Awards, Cities, Stadiums...): which body it owns and what is being edited.</summary>
internal interface IRegisterable { void Register(); }

internal interface IEntityScreen : IRegisterable
{
    string Kind { get; }
    bool OwnsBody(Control body);
    Button? SaveButton { get; }
    /// <summary>The thing the user is editing now, as the edit history names it; null when this screen is not the one showing.</summary>
    (string Kind, string Entity)? EditContext { get; }
    /// <summary>Reads again what this screen is showing (the open item, or its list); false when it is not the one showing.</summary>
    bool ReloadCurrent();
}

internal sealed record ScreenNav(string Title)
{
    public override string ToString() => Title;
}

/// <summary>
/// One list-and-editor screen for one kind of thing: a searchable grid, a detail tab per item, drafts kept until Save, and
/// the save recorded in the edit history under the item. A concrete screen says only what is particular to its kind: columns,
/// how rows load and match, the editor, and how a draft is validated and written. Loading, filtering, selection, tabs,
/// reconnecting, saving, history and status are done here once.
/// </summary>
/// <typeparam name="TRecord">The item as the game holds it (AwardRecord, CityRecord...).</typeparam>
/// <typeparam name="TRow">What the grid shows for one item.</typeparam>
/// <typeparam name="TDraft">Edits typed but not saved yet, per item.</typeparam>
internal abstract class EntityScreen<TRecord, TRow, TDraft> : IEntityScreen
    where TRecord : class where TRow : class where TDraft : class
{
    protected static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    protected readonly IScreenHost Host;
    readonly ScreenNav nav;
    readonly string plural;
    readonly Grid viewRoot = new() { IsVisible = false };
    readonly TextBox search = new();
    protected readonly DataGrid ListGrid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserResizeColumns = true, CanUserSortColumns = true,
        SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = UiKit.Brush("#1D2130"), RowHeight = 28, Background = UiKit.Brush("#11131C"), BorderThickness = new Thickness(0),
    };
    protected readonly ContentControl HeaderHost = new() { Margin = new Thickness(12, 9, 12, 8) };
    protected readonly StackPanel Editor = new() { Spacing = 12, Margin = new Thickness(12, 0, 12, 12) };
    /// <summary>The section list down the left of the editor, for kinds that have sections.</summary>
    protected readonly ContentControl RailHost = new() { Width = 196, Margin = new Thickness(12, 0, 0, 10) };
    protected virtual bool HasRail => false;
    protected readonly Dictionary<ulong, TDraft> Drafts = [];
    bool active;
    Control detailRoot = new Panel();

    public string Kind { get; }
    public IReadOnlyList<TRow> Rows { get; protected set; } = [];
    public TRecord? Selected { get; protected set; }
    public Button? SaveButton { get; protected set; }

    protected EntityScreen(IScreenHost host, string kind, string moduleTitle, string plural, string searchWatermark)
    {
        Host = host;
        Kind = kind;
        this.plural = plural;
        nav = new ScreenNav(moduleTitle);
        search.Watermark = searchWatermark;
    }

    // What a kind supplies.
    protected abstract void ConfigureColumns(DataGrid grid);
    protected abstract IReadOnlyList<TRow> LoadRows(GameSession session);
    protected abstract bool Matches(TRow row, string foldedQuery);
    protected abstract TRecord RecordOf(TRow row);
    protected abstract ulong AddressOf(TRecord record);
    /// <summary>The name the tab, the status line and the edit history use.</summary>
    protected abstract string NameOf(TRecord record);
    protected abstract TDraft NewDraft();
    /// <summary>A draft for this record; kinds whose draft needs to know the record override it.</summary>
    protected virtual TDraft NewDraftFor(TRecord record) => NewDraft();
    /// <summary>True while this screen's list is the one showing.</summary>
    public bool IsActive => active || Host.IsShowing(detailRoot);
    protected abstract bool HasChanges(TRecord record, TDraft draft);
    /// <summary>Fills <see cref="HeaderHost"/> and <see cref="Editor"/> for the record; calls <paramref name="changed"/> after any edit to the draft.</summary>
    protected abstract void BuildEditor(TRecord record, TDraft draft, Action changed);
    /// <summary>Checks the draft; returns the message to show, or null with the write to run and how many changes it holds.</summary>
    protected abstract string? Validate(TRecord record, TDraft draft, out Func<int> write);
    /// <summary>Runs after a successful save, to drop state the draft does not hold.</summary>
    protected virtual void Saved(TRecord record) { }
    /// <summary>Runs when the game connection changes, after the open items and drafts were dropped.</summary>
    protected virtual void OnReconnected() { }
    /// <summary>Runs on the window thread before rows load in the background, to collect what the load needs from the window.</summary>
    protected virtual void BeforeLoad() { }
    protected virtual string LoadedMessage(int count) => $"{count:N0} {plural}";
    /// <summary>A save that stopped part-way: what was written stays written. Return true when the screen has told the user.</summary>
    protected virtual bool OnSaveFailed(TRecord record, Exception error) => false;
    protected abstract TRecord? Reload(TRecord record);
    protected abstract TRow Refresh(TRow row, TRecord fresh);

    public bool OwnsBody(Control body) => ReferenceEquals(body, detailRoot);

    public bool ReloadCurrent()
    {
        if (Selected is { } open && Host.IsShowing(detailRoot)) { ReloadAndRender(open); Host.SetStatus($"Reloaded {NameOf(Selected ?? open)}"); return true; }
        if (active) { _ = LoadAsync(); return true; }
        return false;
    }

    public (string Kind, string Entity)? EditContext =>
        Selected is { } record && (active || Host.IsShowing(detailRoot)) ? (HistoryKind, NameOf(record)) : null;

    /// <summary>The kind as the edit history groups it ("Award", "City").</summary>
    /// <summary>Names the bytes this screen writes for the edit history (field names instead of addresses); null when it has none.</summary>
    protected virtual Func<ulong, int, Fm27Editor.Core.History.ChangeLabel?>? LabelsOf(TRecord record) => null;

    protected virtual string HistoryKind => char.ToUpperInvariant(Kind[0]) + Kind[1..];

    protected static string Fold(string text) => MainWindow.Fold(text);

    // Building the list.
    public void Register()
    {
        Host.AddModule(nav.Title, viewRoot, nav);
        ConfigureColumns(ListGrid);

        search.TextChanged += (_, _) => Filter();
        ListGrid.SelectionChanged += (_, _) => Selected = ListGrid.SelectedItem is TRow row ? RecordOf(row) : null;
        ListGrid.DoubleTapped += (_, e) => { if (UiKit.OnRow(e)) OpenSelected(); };
        ListGrid.KeyDown += (_, e) => { if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; } };

        var left = new DockPanel { Background = UiKit.Brush("#11131C") };
        var searchBar = new Border
        {
            Background = UiKit.Surface, BorderBrush = UiKit.BorderSubtle, BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 7), Child = BuildSearchHeader(search),
        };
        DockPanel.SetDock(searchBar, Dock.Top);
        left.Children.Add(searchBar);
        left.Children.Add(ListGrid);
        viewRoot.Children.Add(left);

        var scroll = new ScrollViewer
        {
            Content = Editor, Background = UiKit.WindowBg,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var dock = new DockPanel { Background = UiKit.WindowBg };
        DockPanel.SetDock(HeaderHost, Dock.Top);
        dock.Children.Add(HeaderHost);
        if (HasRail)
        {
            Editor.Margin = new Thickness(0, 0, 12, 12);
            DockPanel.SetDock(RailHost, Dock.Left);
            dock.Children.Add(RailHost);
        }
        dock.Children.Add(scroll);
        detailRoot = dock;

        Host.NavChanged += async (_, _) =>
        {
            bool now = ReferenceEquals(Host.SelectedNav, nav);
            if (now == active) return;
            active = now;
            if (now && Rows.Count == 0 && Host.Backend.State.Status == ConnectionStatus.Connected) await LoadAsync();
        };
        Host.Backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            Rows = [];
            Drafts.Clear();
            Selected = null;
            SaveButton = null;
            ListGrid.ItemsSource = null;
            HeaderHost.Content = null;
            RailHost.Content = null;
            Editor.Children.Clear();
            OnReconnected();
            if (active && Host.Backend.State.Status == ConnectionStatus.Connected) await LoadAsync();
        });
    }

    /// <summary>What sits above the list: the search box alone, or the box with extra filters and buttons.</summary>
    protected virtual Control BuildSearchHeader(TextBox searchBox) => searchBox;

    /// <summary>The rows the search text and the screen's own filters let through.</summary>
    protected virtual IReadOnlyList<TRow> Apply(IReadOnlyList<TRow> rows, string foldedQuery) =>
        foldedQuery.Length == 0 ? rows : rows.Where(r => Matches(r, foldedQuery)).ToList();

    /// <summary>Runs after rows were loaded, before they are shown.</summary>
    protected virtual void RowsLoaded() { }

    /// <summary>The rows the list shows now (after search and filters).</summary>
    public IReadOnlyList<TRow> Visible => ListGrid.ItemsSource as IReadOnlyList<TRow> ?? Rows;

    /// <summary>Loads the rows again from the game.</summary>
    public async Task LoadAsync()
    {
        Host.SetStatus($"Loading {plural}...");
        try
        {
            var session = Host.Backend.Session;
            BeforeLoad();
            Rows = await Task.Run(() => LoadRows(session));
            RowsLoaded();
            Filter();
            Host.SetStatus(LoadedMessage(Rows.Count));
        }
        catch (Exception ex) { Host.SetStatus(ex.Message); }
    }

    protected void Filter()
    {
        string q = Fold((search.Text ?? "").Trim());
        var keep = Selected;
        ListGrid.ItemsSource = Apply(Rows, q);
        if (keep is not null)
        {
            ulong kept = AddressOf(keep);
            if ((ListGrid.ItemsSource as IEnumerable<TRow>)?.FirstOrDefault(r => AddressOf(RecordOf(r)) == kept) is { } same)
                ListGrid.SelectedItem = same;
        }
    }

    // Opening one item.
    void OpenSelected()
    {
        if (Selected is not { } record) return;
        Render();
        OpenTab(record);
    }

    /// <summary>Shows <paramref name="record"/> from anywhere (a link in another screen, the global search).</summary>
    public void Pick(TRecord record)
    {
        Host.SwitchModule(nav.Title);
        Selected = record;
        Render();
        OpenTab(record);
        ulong address = AddressOf(record);
        if (Rows.Count > 0) ListGrid.SelectedItem = Rows.FirstOrDefault(r => AddressOf(RecordOf(r)) == address);
        else if (Host.Backend.State.Status == ConnectionStatus.Connected)
        {
            // Opened from a link before the list was ever loaded: editors that read the other rows (rivals, preferences) came out
            // empty, so load the list and draw the item again.
            Dispatcher.UIThread.Post(async () =>
            {
                if (Rows.Count == 0) await LoadAsync();
                if (Selected is { } open && AddressOf(open) == address) Render();
            });
        }
    }

    void OpenTab(TRecord record)
    {
        var shown = record;
        Host.OpenTab(Kind, AddressOf(record), NameOf(record), detailRoot, () => { Selected = shown; Render(); });
    }

    // The editor.
    protected void Render()
    {
        HeaderHost.Content = null;
        Editor.Children.Clear();
        if (Selected is not { } record) { SaveButton = null; return; }
        ulong address = AddressOf(record);
        if (!Drafts.TryGetValue(address, out var draft)) Drafts[address] = draft = NewDraftFor(record);
        var save = new Button { IsVisible = false, Content = "Save changes", IsEnabled = HasChanges(record, draft), Classes = { "accent" } };
        SaveButton = save;
        void Changed() => save.IsEnabled = HasChanges(record, draft);
        save.Click += (_, _) => Save(record, draft);
        BuildEditor(record, draft, Changed);
    }

    /// <summary>Draws the open item again (after something outside the screen changed it).</summary>
    public void Reopen() { if (Selected is not null) Render(); }

    /// <summary>Reads the open item again (something outside the screen changed it) and redraws it.</summary>
    public void ReloadOpen() { if (Selected is { } record) ReloadAndRender(record); }

    /// <summary>Clears the search box and shows the item with this address in the list.</summary>
    public bool Reveal(Func<TRow, bool> match)
    {
        var row = Rows.FirstOrDefault(match);
        if (row is null) return false;
        search.Text = "";
        Filter();
        ListGrid.SelectedItem = row;
        ListGrid.ScrollIntoView(row, null);
        return true;
    }

    void Save(TRecord record, TDraft draft)
    {
        if (Validate(record, draft, out var write) is { } problem) { Host.SetStatus(problem); return; }
        try
        {
            string name = NameOf(record);
            int count = 0;
            if (LabelsOf(record) is { } labels) Host.Backend.Edits.RegisterLabels(HistoryKind, name, labels);
            Host.Backend.Edits.Execute(HistoryKind, name, "Page save", () => count = write());
            Drafts.Remove(AddressOf(record));
            Saved(record);
            ReloadAndRender(record);
            Host.SetStatus(count == 0 ? "Nothing changed." : $"Saved {count} change{(count == 1 ? "" : "s")} to {name}. Continue in FM to see updates.");
        }
        catch (Exception ex) { if (!OnSaveFailed(record, ex)) Host.SetStatus(ex.Message); }
    }

    /// <summary>Reads the item again from the game, refreshes its row and draws the editor.</summary>
    protected void ReloadAndRender(TRecord record)
    {
        ulong address = AddressOf(record);
        if (Reload(record) is { } fresh)
        {
            Selected = fresh;
            Rows = Rows.Select(r => AddressOf(RecordOf(r)) == address ? Refresh(r, fresh) : r).ToList();
            Filter();
            ListGrid.SelectedItem = (ListGrid.ItemsSource as IEnumerable<TRow>)?.FirstOrDefault(r => AddressOf(RecordOf(r)) == address);
        }
        Render();
    }

    // Pieces every editor is made of.
    protected static TextBlock Label(string text, string color = "#CBD5E1") =>
        new() { Text = text, FontSize = 13, Foreground = UiKit.Brush(color), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A card of "label ... value" lines with alternating shading.</summary>
    protected static Border CardRows(string title, IEnumerable<(string Label, Control Value)> items)
    {
        var stack = new StackPanel { Spacing = 0 };
        int idx = 0;
        foreach (var (label, value) in items)
        {
            var line = new DockPanel { MinHeight = 26, LastChildFill = true };
            DockPanel.SetDock(value, Dock.Right);
            line.Children.Add(value);
            line.Children.Add(Label(label));
            stack.Children.Add(new Border
            {
                Background = idx++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                Padding = new Thickness(12, 3),
                Child = line,
            });
        }
        return UiKit.Card(title, stack);
    }

    /// <summary>Two cards side by side.</summary>
    protected static Grid TwoColumns(Control left, Control right)
    {
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        Avalonia.Controls.Grid.SetColumn(left, 0);
        Avalonia.Controls.Grid.SetColumn(right, 1);
        columns.Children.Add(left);
        columns.Children.Add(right);
        return columns;
    }

    /// <summary>A right-aligned box bound to one field of a dictionary draft; typing the original value again removes the edit.</summary>
    protected static TextBox FieldBox<TField>(Dictionary<TField, string> draft, TField field, string original, double width, Action changed) where TField : notnull
    {
        var box = new TextBox
        {
            Text = draft.GetValueOrDefault(field, original), Width = width, MinHeight = 24, Padding = new Thickness(5, 1),
            HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.TextChanged += (_, _) =>
        {
            string t = box.Text ?? "";
            if (t == original) draft.Remove(field); else draft[field] = t;
            changed();
        };
        return box;
    }
}

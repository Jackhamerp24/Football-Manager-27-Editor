using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed class MainWindow : Window
{
    readonly EditorBackend backend = new();
    readonly ComboBox modules = new() { Width = 125 };
    readonly TextBox search = new() { Watermark = "Search players" };
    readonly TextBlock connection = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#AAB5C5") };
    readonly Button retry = new() { Content = "Retry" };
    readonly DataGrid grid = new() { IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, RowHeight = 30, BorderThickness = new Thickness(0) };
    readonly StackPanel editor = new() { Spacing = 12, Margin = new Thickness(20, 16) };
    readonly TextBlock status = new() { Margin = new Thickness(14, 6), Foreground = Brush("#AAB5C5") };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    IReadOnlyList<object> rows = [];
    IEditorModule? module;
    PlayerRecord? selected;
    bool updating, busy;
    int generation;
    int editorTab;
    PlayerPosition? selectedPosition;
    readonly Dictionary<ulong, PlayerDraft> drafts = [];
    Button? saveButton;
    Button? discardButton;

    public MainWindow()
    {
        Title = "Football Manager 27 Editor"; Width = 1280; Height = 800; MinWidth = 900; MinHeight = 600;
        Background = Brush("#111720"); FontSize = 13;
        var root = new DockPanel();
        var top = new DockPanel { Margin = new Thickness(16, 9), LastChildFill = false };
        var brand = new TextBlock { Text = "FM 27  /  EDITOR", FontSize = 14, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
        DockPanel.SetDock(brand, Dock.Left); top.Children.Add(brand);
        DockPanel.SetDock(modules, Dock.Left); top.Children.Add(modules);
        DockPanel.SetDock(retry, Dock.Right); top.Children.Add(retry);
        connection.Margin = new Thickness(0, 0, 14, 0); DockPanel.SetDock(connection, Dock.Right); top.Children.Add(connection);
        var topBar = new Border { Background = Brush("#1A2330"), BorderBrush = Brush("#303D4D"), BorderThickness = new Thickness(0, 0, 0, 1), Child = top };
        DockPanel.SetDock(topBar, Dock.Top); root.Children.Add(topBar);
        var statusBar = new Border { Background = Brush("#1A2330"), BorderBrush = Brush("#303D4D"), BorderThickness = new Thickness(0, 1, 0, 0), Child = status };
        DockPanel.SetDock(statusBar, Dock.Bottom); root.Children.Add(statusBar);
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("380,5,*") };
        var left = new DockPanel { Background = Brush("#171F2A") };
        var searchArea = new StackPanel { Margin = new Thickness(12), Spacing = 8 };
        searchArea.Children.Add(new TextBlock { Text = "PLAYERS", FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#91A2B6") });
        searchArea.Children.Add(search); DockPanel.SetDock(searchArea, Dock.Top); left.Children.Add(searchArea); left.Children.Add(grid);
        Grid.SetColumn(left, 0); split.Children.Add(left);
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(splitter, 1); split.Children.Add(splitter);
        var scroll = new ScrollViewer { Content = editor, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 2); split.Children.Add(scroll); root.Children.Add(split); Content = root;
        modules.ItemsSource = Modules.Registered; modules.DisplayMemberBinding = new Binding("Title");
        modules.SelectionChanged += async (_, _) => { module = modules.SelectedItem as IEditorModule; ConfigureColumns(); if (backend.State.Status == ConnectionStatus.Connected) await LoadAsync(); };
        search.TextChanged += (_, _) => Filter();
        grid.SelectionChanged += (_, _) => { if (!updating) { selected = grid.SelectedItem as PlayerRecord; RenderEditor(); } };
        retry.Click += async (_, _) => await ConnectAsync();
        Opened += async (_, _) => { modules.SelectedIndex = 0; await ConnectAsync(); timer.Start(); };
        Closed += (_, _) => { timer.Stop(); backend.Dispose(); };
        timer.Tick += async (_, _) => await CheckSelectedAsync();
    }
    async Task ConnectAsync()
    {
        if (busy) return;
        busy = true; retry.IsEnabled = false; connection.Text = "Connecting…";
        try { await backend.ConnectAsync(CancellationToken.None); connection.Text = backend.State.Message; if (backend.State.Status == ConnectionStatus.Connected) await LoadAsync(); }
        finally { busy = false; retry.IsEnabled = true; }
    }
    async Task LoadAsync()
    {
        if (module is null) return;
        int current = ++generation;
        try
        {
            var loaded = await module.LoadAsync(backend, CancellationToken.None);
            if (current != generation) return;
            rows = loaded; selected = null; drafts.Clear(); editor.Children.Clear(); Filter();
            connection.Text = $"{backend.State.Message} · {rows.Count:N0}";
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
    }
    void ConfigureColumns()
    {
        grid.Columns.Clear();
        if (module is null) return;
        foreach (var c in module.Columns) grid.Columns.Add(new DataGridTextColumn { Header = c.Title, Binding = new Binding(c.Binding), Width = c.Title == "Name" ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(c.Title == "Born" ? 78 : 66) });
    }
    void Filter()
    {
        if (module is null) return;
        string query = search.Text?.Trim() ?? "";
        updating = true;
        grid.ItemsSource = query.Length == 0 ? rows : rows.Where(r => module.Matches(r, query)).ToArray();
        updating = false;
    }
    async Task CheckSelectedAsync()
    {
        if (selected is null || busy || backend.State.Status != ConnectionStatus.Connected) return;
        try
        {
            var address = selected.Address;
            var player = await Task.Run(() => backend.Players.Reload(address));
            if (player is null && selected?.Address == address) { status.Text = "Player no longer available; reloading"; await LoadAsync(); }
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
    }
    void RenderEditor()
    {
        editor.Children.Clear();
        if (selected is not { } player) return;
        if (!drafts.TryGetValue(player.Address, out var draft)) drafts[player.Address] = draft = new PlayerDraft(player);
        var identity = new StackPanel { Spacing = 4 };
        identity.Children.Add(new TextBlock { Text = player.DisplayName, FontSize = 23, FontWeight = FontWeight.SemiBold });
        var details = new[] { player.Club?.DisplayName, player.DateOfBirth?.ToString("dd MMM yyyy"), player.Height > 0 ? $"{player.Height} cm" : null }.Where(x => !string.IsNullOrEmpty(x));
        identity.Children.Add(new TextBlock { Text = string.Join("   ·   ", details), Foreground = Brush("#AAB5C5") });
        editor.Children.Add(identity);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        saveButton = new Button { Content = "Save changes", IsEnabled = draft.Count > 0, Background = Brush("#2675C9"), Foreground = Brushes.White };
        saveButton.Click += (_, _) => SaveDraft();
        discardButton = new Button { Content = "Discard", IsEnabled = draft.Count > 0 };
        discardButton.Click += (_, _) => { drafts.Remove(player.Address); status.Text = "Changes discarded"; RenderEditor(); };
        commands.Children.Add(saveButton); commands.Children.Add(discardButton); editor.Children.Add(commands);
        status.Text = draft.Count > 0 ? $"{draft.Count} unsaved change(s)" : "";
        var strip = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var field in PlayerFields.All.Where(f => f.Group is "Ability" or "Reputation"))
        {
            var item = new Border { Width = field.Group == "Ability" ? 110 : 125, Padding = new Thickness(10, 5), Margin = new Thickness(0, 0, 6, 6), Background = Brush("#202B39"), Child = FieldControl(field, player) };
            strip.Children.Add(item);
        }
        editor.Children.Add(strip);
        var tabs = new TabControl();
        var attributes = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var group in new[] { "Technical", "Mental", "Physical", "Goalkeeping" }) attributes.Children.Add(GroupControl(group, player));
        var personality = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var group in new[] { "Hidden", "Personality" }) personality.Children.Add(GroupControl(group, player));
        var pitch = CreatePitch(player, draft);
        tabs.ItemsSource = new[] { new TabItem { Header = "Attributes", Content = attributes }, new TabItem { Header = "Positions", Content = pitch }, new TabItem { Header = "Personality", Content = personality } };
        tabs.SelectedIndex = editorTab;
        tabs.SelectionChanged += (_, _) => { if (tabs.SelectedIndex >= 0) editorTab = tabs.SelectedIndex; };
        editor.Children.Add(tabs);
    }
    Control GroupControl(string group, PlayerRecord player)
    {
        var panel = new StackPanel { Spacing = 1 };
        panel.Children.Add(new TextBlock { Text = group.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brush("#A3B5C9"), Margin = new Thickness(0, 0, 0, 8) });
        foreach (var field in PlayerFields.All.Where(f => f.Group == group)) panel.Children.Add(FieldControl(field, player));
        return new Border { Width = 210, Margin = new Thickness(0, 12, 12, 0), Padding = new Thickness(12), Background = Brush("#1A2430"), BorderBrush = Brush("#2C3A49"), BorderThickness = new Thickness(1), Child = panel };
    }
    Control CreatePitch(PlayerRecord player, PlayerDraft draft)
    {
        const double width = 420, height = 280, dotSize = 22;
        var result = new StackPanel { Margin = new Thickness(0, 16), Spacing = 14, HorizontalAlignment = HorizontalAlignment.Left };
        var pitch = new Canvas { Width = width, Height = height, Background = Brush("#10473F"), ClipToBounds = true };
        var lines = Brush("#759D92");
        void Rectangle(double x, double y, double w, double h)
        {
            var shape = new Avalonia.Controls.Shapes.Rectangle { Width = w, Height = h, Stroke = lines, StrokeThickness = 1.5, IsHitTestVisible = false };
            Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); pitch.Children.Add(shape);
        }
        Rectangle(1, 1, width - 2, height - 2);
        Rectangle(0, 74, 45, 132); Rectangle(0, 105, 15, 70);
        Rectangle(width - 45, 74, 45, 132); Rectangle(width - 15, 105, 15, 70);
        var halfway = new Line { StartPoint = new Point(width / 2, 0), EndPoint = new Point(width / 2, height), Stroke = lines, StrokeThickness = 1.5, IsHitTestVisible = false };
        pitch.Children.Add(halfway);
        var circle = new Ellipse { Width = 56, Height = 56, Stroke = lines, StrokeThickness = 1.5, IsHitTestVisible = false };
        Canvas.SetLeft(circle, width / 2 - 28); Canvas.SetTop(circle, height / 2 - 28); pitch.Children.Add(circle);
        var dots = new Dictionary<PlayerPosition, Button>();
        var selection = new ContentControl { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        void Select(PitchPosition position)
        {
            selectedPosition = position.Position;
            foreach (var (key, dot) in dots) { dot.BorderBrush = key == position.Position ? Brushes.White : Brush("#244B42"); dot.BorderThickness = new Thickness(key == position.Position ? 2 : 1); }
            var field = PlayerFields.All.Single(f => f.Group == "Positions" && f.Id == position.Position.ToString());
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = position.Label, FontSize = 16, FontWeight = FontWeight.SemiBold });
            panel.Children.Add(FieldControl(field, player, onChanged: value => dots[position.Position].Background = PositionColor(value), labelOverride: "Position rating"));
            selection.Content = panel;
        }
        foreach (var position in PitchLayout.Positions)
        {
            var field = PlayerFields.All.Single(f => f.Group == "Positions" && f.Id == position.Position.ToString());
            var dot = new Button { Width = dotSize, Height = dotSize, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(dotSize / 2), Background = PositionColor(draft.Value(field)) };
            ToolTip.SetTip(dot, position.Label);
            Avalonia.Automation.AutomationProperties.SetName(dot, position.Label);
            Canvas.SetLeft(dot, position.X * width - dotSize / 2); Canvas.SetTop(dot, position.Y * height - dotSize / 2);
            dot.Click += (_, _) => Select(position); dots[position.Position] = dot; pitch.Children.Add(dot);
        }
        result.Children.Add(pitch); result.Children.Add(selection);
        var initial = PitchLayout.Positions.FirstOrDefault(p => p.Position == selectedPosition) ?? PitchLayout.Positions.MaxBy(p => draft.Value(PlayerFields.All.Single(f => f.Group == "Positions" && f.Id == p.Position.ToString())))!;
        Select(initial);
        return result;
    }
    Control FieldControl(FieldDescriptor<PlayerRecord> field, PlayerRecord player, bool compact = false, Action<int>? onChanged = null, string? labelOverride = null)
    {
        var line = new DockPanel { Height = 30, LastChildFill = true, Margin = new Thickness(0, 0, 0, 1) };
        var label = new TextBlock { Text = labelOverride ?? (compact ? field.Label.Replace("Defensive Midfielder", "DM").Replace("Attacking Mid", "AM").Replace("Midfielder", "M").Replace("Defender", "D").Replace("Wing Back", "WB").Replace("Goalkeeper", "GK").Replace("Striker", "ST") : field.Label), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (field.Write is null)
        {
            var value = new TextBlock { Text = field.Read(player).ToString(), Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(value, Dock.Right); line.Children.Add(value); line.Children.Add(label); return line;
        }
        var draft = drafts[player.Address];
        var input = new NumericUpDown { Minimum = field.Min, Maximum = field.Max, Increment = 1, Value = draft.Value(field), ShowButtonSpinner = false, Width = 54, Height = 28, Padding = new Thickness(4, 0), FontSize = 13, Background = Brush("#111A24"), Foreground = field.Kind == FieldKind.Rating20 ? RatingColor(draft.Value(field)) : Brushes.White };
        DockPanel.SetDock(input, Dock.Right); line.Children.Add(input); line.Children.Add(label);
        input.ValueChanged += (_, e) =>
        {
            if (updating || e.NewValue is null || e.OldValue == e.NewValue) return;
            int value = (int)e.NewValue.Value;
            draft.Stage(field, value);
            onChanged?.Invoke(value);
            if (field.Kind == FieldKind.Rating20) input.Foreground = RatingColor(value);
            if (selected?.Address == player.Address)
            {
                if (saveButton is not null) saveButton.IsEnabled = draft.Count > 0;
                if (discardButton is not null) discardButton.IsEnabled = draft.Count > 0;
                status.Text = draft.Count > 0 ? $"{draft.Count} unsaved change(s)" : "";
            }
        };
        return line;
    }
    void SaveDraft()
    {
        if (selected is null || !drafts.TryGetValue(selected.Address, out var draft) || draft.Count == 0) return;
        var address = selected.Address;
        var count = draft.Count;
        try
        {
            selected = draft.Save(backend.Edits);
            drafts.Remove(address);
            ReplaceRow(selected); RenderEditor();
            status.Text = $"Saved {count} change(s). Continue in FM to see updates.";
        }
        catch (Exception ex)
        {
            // Some earlier fields may already have succeeded; never present a partial save as atomic.
            int saved = count - draft.Count;
            try
            {
                var fresh = backend.Players.Reload(address);
                if (fresh is null) { drafts.Remove(address); _ = LoadAsync(); }
                else { selected = fresh; drafts[address] = draft.Rebase(fresh); ReplaceRow(fresh); RenderEditor(); }
                status.Text = $"{saved} saved. {UserMessage.For(ex)} Review remaining changes.";
            }
            catch (Exception reloadError) { status.Text = $"{saved} saved. {UserMessage.For(reloadError)}"; }
        }
    }
    void ReplaceRow(PlayerRecord p)
    {
        var arr = rows.ToArray(); int index = Array.FindIndex(arr, x => x is PlayerRecord old && old.Address == p.Address);
        if (index >= 0) { arr[index] = p; rows = arr; }
        Filter(); updating = true; grid.SelectedItem = p; updating = false;
    }
    static IBrush RatingColor(int value) => new SolidColorBrush(Color.Parse(value <= 5 ? "#a8adb3" : value <= 10 ? "#dfd68b" : value <= 15 ? "#6fd0a1" : "#43c2e8"));
    static IBrush PositionColor(int value) => Brush(value <= 5 ? "#303B3A" : value <= 10 ? "#F38C25" : value <= 15 ? "#F5CD35" : "#8EDC35");
    static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}

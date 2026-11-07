using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    readonly TextBox unifiedSearch = new()
    {
        Width = 280,
        Height = 26,
        FontSize = 11.5,
        Watermark = "Search player, club, nation or ID",
    };
    readonly Border searchOverlay = new()
    {
        Background = new SolidColorBrush(Color.Parse("#E6080A10")),
        IsVisible = false,
        ZIndex = 200,
    };
    readonly TextBlock unifiedResultsLabel = new() { FontSize = 11.5, FontWeight = FontWeight.Bold, Foreground = Brush("#F1F5F9") };

    Control BuildSearchOverlay()
    {
        unifiedSearch.Width = double.NaN;
        unifiedSearch.Height = 32;
        unifiedSearch.FontSize = 13;
        unifiedSearch.HorizontalAlignment = HorizontalAlignment.Stretch;
        unifiedSearch.Watermark = "Search player, club, nation, staff, competition or ID";

        var closeBtn = new Button
        {
            Content = "Close",
            FontSize = 11.5,
            Padding = new Thickness(12, 3),
            MinHeight = 32,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
        };
        closeBtn.Click += (_, _) => CloseUnified();

        var fieldRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(closeBtn, Dock.Right);
        fieldRow.Children.Add(closeBtn);
        fieldRow.Children.Add(unifiedSearch);

        var resultsRow = new DockPanel { LastChildFill = false, Margin = new Thickness(2, 6, 2, 4) };
        resultsRow.Children.Add(unifiedResultsLabel);

        var panel = new Border
        {
            MaxWidth = 720,
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderControl,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12),
            Margin = new Thickness(28, 72, 28, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 14, Blur = 36, Color = Color.Parse("#AA000000") }),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    fieldRow,
                    resultsRow,
                    new Border
                    {
                        BorderBrush = UiKit.BorderSubtle,
                        BorderThickness = new Thickness(0, 1, 0, 0),
                        Padding = new Thickness(0, 4, 0, 0),
                        Child = new ScrollViewer
                        {
                            MaxHeight = 420,
                            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                            Content = unifiedList,
                        },
                    },
                },
            },
        };

        searchOverlay.Child = panel;
        searchOverlay.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, searchOverlay)) CloseUnified();
        };
        return searchOverlay;
    }

    void OpenUnified()
    {
        searchOverlay.IsVisible = true;
        unifiedResultsLabel.Text = "Results";
        unifiedList.Children.Clear();
        unifiedSearch.Focus();
        unifiedSearch.SelectAll();
        if (!string.IsNullOrEmpty(unifiedSearch.Text)) RunUnifiedSearch();
    }

    readonly StackPanel unifiedList = new() { Spacing = 2 };
    readonly DispatcherTimer unifiedTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    IReadOnlyList<ClubEntry> unifiedClubs = [];
    IReadOnlyList<NationRecord> unifiedNations = [];
    bool unifiedCacheLoaded;

    Control BuildTopBar()
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

        left.Children.Add(new Border
        {
            Background = UiKit.Accent,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 3),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "FM27",
                FontSize = 10.5,
                FontWeight = FontWeight.Black,
                Foreground = Brushes.White,
                LetterSpacing = 0.5,
            },
        });

        static Border Divider() => new()
        {
            Width = 1,
            Height = 14,
            Background = UiKit.BorderSubtle,
            Margin = new Thickness(3, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Button ToolBtn(string text, string tip, Action action)
        {
            var b = new Button
            {
                Content = text,
                FontSize = 11.5,
                Padding = new Thickness(8, 0),
                MinHeight = 25,
                Height = 25,
                Background = Brush("#181C29"),
                BorderBrush = Brush("#272D42"),
                BorderThickness = new Thickness(1),
                Foreground = Brush("#DCE2EE"),
            };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => action();
            return b;
        }

        left.Children.Add(ToolBtn("⌂", "Return to module list", GoHome));
        var saveBtn = ToolBtn("Save", "Save changes on current page (Ctrl+S)", SaveCurrentEntity);
        saveBtn.Classes.Add("accent");
        left.Children.Add(saveBtn);
        left.Children.Add(ToolBtn("History", "Session edit history", () => _ = ShowEditHistoryAsync()));
        left.Children.Add(Divider());
        left.Children.Add(ToolBtn("Mass", "Mass edit current module", OpenCurrentMassEdit));
        left.Children.Add(ToolBtn("Freezer", "Freeze current player or club", OpenCurrentFreezer));
        left.Children.Add(ToolBtn("Graphics", "Choose graphics folder", () => _ = ChooseGraphicsAsync()));

        modules.Width = 118;
        modules.MinHeight = 25;
        modules.FontSize = 11.5;
        modules.Margin = new Thickness(4, 0, 0, 0);
        modules.IsVisible = false;
        left.Children.Add(modules);

        var searchRow = new DockPanel { LastChildFill = true };
        var shortcutPill = new Border
        {
            Background = Brush("#181C29"),
            BorderBrush = Brush("#272D42"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(5, 1),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Ctrl+K", FontSize = 10, Foreground = Brush("#94A3B8") },
        };
        DockPanel.SetDock(shortcutPill, Dock.Right);
        searchRow.Children.Add(shortcutPill);
        searchRow.Children.Add(new TextBlock
        {
            Text = "Search player, club, nation or ID…",
            FontSize = 11.5,
            Foreground = Brush("#64748B"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var searchOpen = new Button
        {
            Content = searchRow,
            MinWidth = 260,
            MaxWidth = 340,
            HorizontalAlignment = HorizontalAlignment.Center,
            Height = 26,
            MinHeight = 26,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(9, 0, 5, 0),
            Background = UiKit.SurfaceInput,
            BorderBrush = Brush("#272D42"),
            BorderThickness = new Thickness(1),
        };
        searchOpen.Click += (_, _) => OpenUnified();
        var searchHost = new Panel { VerticalAlignment = VerticalAlignment.Center };
        searchHost.Children.Add(searchOpen);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var github = new Button
        {
            Content = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12"),
                Fill = Brush("#CBD5E1"),
                Stretch = Stretch.Uniform,
                Width = 15,
                Height = 15,
            },
            Width = 28,
            Height = 25,
            MinHeight = 25,
            Padding = new Thickness(5, 3),
            Background = Brush("#181C29"),
            BorderBrush = Brush("#272D42"),
            BorderThickness = new Thickness(1),
        };
        ToolTip.SetTip(github, "Open the project repository");
        github.Click += (_, _) => OpenUrl("https://github.com/Jackhamerp24/Football-Manager-27-Editor");
        var loadGame = new Button
        {
            Content = "Load Game",
            FontSize = 11.5,
            Padding = new Thickness(12, 0),
            MinHeight = 25,
            Height = 25,
            Classes = { "accent" },
        };
        loadGame.Click += async (_, _) =>
        {
            unifiedCacheLoaded = false;
            status.Text = "Loading game…";
            await ConnectAsync();
        };
        retry.Height = 25;
        retry.MinHeight = 25;
        right.Children.Add(github);
        right.Children.Add(loadGame);
        right.Children.Add(retry);

        var dock = new Grid { Margin = new Thickness(10, 5), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        left.HorizontalAlignment = HorizontalAlignment.Left;
        right.HorizontalAlignment = HorizontalAlignment.Right;
        searchHost.HorizontalAlignment = HorizontalAlignment.Center;
        searchHost.Margin = new Thickness(8, 0);
        Grid.SetColumn(left, 0); dock.Children.Add(left);
        Grid.SetColumn(searchHost, 1); dock.Children.Add(searchHost);
        Grid.SetColumn(right, 2); dock.Children.Add(right);

        unifiedSearch.TextChanged += (_, _) =>
        {
            if (!searchOverlay.IsVisible) return;
            unifiedTimer.Stop();
            unifiedTimer.Start();
        };
        unifiedTimer.Tick += (_, _) =>
        {
            unifiedTimer.Stop();
            if (searchOverlay.IsVisible) RunUnifiedSearch();
        };
        unifiedSearch.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CloseUnified(); e.Handled = true; }
            else if (e.Key == Key.Enter) { ChooseFirstUnified(); e.Handled = true; }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                OpenUnified();
                e.Handled = true;
            }
            else if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                SaveCurrentEntity();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && searchOverlay.IsVisible)
            {
                CloseUnified();
                e.Handled = true;
            }
        };
        return new Border
        {
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = dock,
        };
    }

    async Task ChooseGraphicsAsync()
    {
        try { if (await graphics.ChooseAsync(this)) status.Text = $"Graphics: {graphics.Folder}"; }
        catch (Exception ex) { status.Text = $"Graphics: {ex.Message}"; }
    }

    static void RaiseClick(Button? button)
    {
        if (button is null || !button.IsEnabled) return;
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    void GoHome()
    {
        CloseUnified();
        ShowModule(activeModule ?? "Players");
    }

    sealed class PlayerTab
    {
        public PlayerRecord Player = null!;
        public StackPanel Panel = null!;
        public Control Root = null!;
        public ContentControl Sticky = null!;
        public ContentControl Header = null!;
        public ContentControl Rail = null!;
        public Border Button = null!;
    }

    readonly StackPanel moduleBar = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    readonly StackPanel entityStrip = new() { Orientation = Orientation.Horizontal, Spacing = 0 };
    readonly ScrollViewer entityScroll = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
    };
    readonly StackPanel entityScrollButtons = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    readonly ContentControl bodyHost = new() { Background = UiKit.WindowBg };
    readonly Dictionary<string, Control> moduleRoots = new(StringComparer.Ordinal);
    readonly Dictionary<string, object?> moduleNav = new(StringComparer.Ordinal);
    readonly Dictionary<string, Button> moduleButtons = new(StringComparer.Ordinal);
    readonly List<PlayerTab> playerTabs = [];
    readonly Dictionary<string, KindTab> kindTabs = new(StringComparer.Ordinal);
    string? activeModule;
    Border? activeEntityButton;
    Border? entityRowHost;

    Border BuildEntityRow()
    {
        entityScroll.Content = entityStrip;
        entityScroll.PointerWheelChanged += (_, e) =>
        {
            double delta = Math.Abs(e.Delta.X) > 0.01 ? e.Delta.X : -e.Delta.Y;
            entityScroll.Offset = new Vector(Math.Max(0, entityScroll.Offset.X + delta * 48), 0);
            UpdateEntityScrollButtons();
            e.Handled = true;
        };
        entityScroll.ScrollChanged += (_, _) => UpdateEntityScrollButtons();

        Button NavBtn(string glyph, double dir)
        {
            var b = new Button
            {
                Content = glyph,
                Width = 22,
                Height = 22,
                MinHeight = 22,
                Padding = new Thickness(0),
                FontSize = 12,
            };
            b.Click += (_, _) =>
            {
                entityScroll.Offset = new Vector(Math.Max(0, entityScroll.Offset.X + dir * 160), 0);
                UpdateEntityScrollButtons();
            };
            return b;
        }
        entityScrollButtons.Children.Add(NavBtn("‹", -1));
        entityScrollButtons.Children.Add(NavBtn("›", 1));

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(entityScrollButtons, Dock.Right);
        dock.Children.Add(entityScrollButtons);
        dock.Children.Add(entityScroll);

        var row = new Border
        {
            Background = Brush("#0E111A"),
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Height = 29,
            IsVisible = false,
            Child = dock,
        };
        row.SizeChanged += (_, _) => UpdateEntityScrollButtons();
        entityRowHost = row;
        return row;
    }

    void UpdateEntityScrollButtons()
    {
        entityScrollButtons.IsVisible = entityScroll.Extent.Width > entityScroll.Viewport.Width + 2;
    }

    sealed class KindTab
    {
        public Control Body = null!;
        public Border Button = null!;
        public Action Restore = null!;
    }

    void UpdateEntityRow()
    {
        if (entityRowHost is not null)
            entityRowHost.IsVisible = entityStrip.Children.Count > 0;
        Dispatcher.UIThread.Post(UpdateEntityScrollButtons, DispatcherPriority.Loaded);
    }

    /// <summary>Opens one tab per entity and restores its selection when the tab is activated.</summary>
    void OpenKindTab(string kind, string title, Control body)
    {
        ulong address = kind switch
        {
            "club" => selectedClub?.Address ?? 0, "nation" => selectedNation?.Address ?? 0,
            "staff" => selectedStaff?.Address ?? 0, "city" => selectedCity?.Address ?? 0,
            "stadium" => selectedStadium?.Address ?? 0, "competition" => selectedCompetition?.Address ?? 0,
            "award" => selectedAward?.Address ?? 0, _ => 0,
        };
        if (address == 0) return;
        Action restore = kind switch
        {
            "club" when selectedClub is { } item => () => { selectedClub = item; RenderClubEditor(); },
            "nation" when selectedNation is { } item => () => { selectedNation = item; RenderNationEditor(); },
            "staff" when selectedStaff is { } item => () => { selectedStaff = item; RenderStaffEditor(); },
            "city" when selectedCity is { } item => () => { selectedCity = item; RenderCityEditor(); },
            "stadium" when selectedStadium is { } item => () => { selectedStadium = item; RenderStadiumEditor(); },
            "competition" when selectedCompetition is { } item => () => { selectedCompetition = item; RenderCompetitionEditor(); },
            "award" when selectedAward is { } item => () => { selectedAward = item; RenderAwardEditor(); },
            _ => () => { },
        };
        string key = $"{kind}:{address:X}";
        if (kindTabs.TryGetValue(key, out var existing))
        {
            if (!ReferenceEquals(existing.Body, body)) existing.Body = body;
            existing.Restore = restore;
            SetEntityTitle(existing.Button, title);
            activeEntityButton = existing.Button;
            bodyHost.Content = existing.Body;
            HighlightEntityTab();
            UpdateModuleBarStyles();
            Dispatcher.UIThread.Post(() => existing.Button.BringIntoView(), DispatcherPriority.Loaded);
            return;
        }

        var tab = new KindTab { Body = body, Restore = restore };
        tab.Button = MakeEntityButton(title, () => ActivateKindTab(tab), () => CloseKindTab(tab));
        kindTabs[key] = tab;
        entityStrip.Children.Add(tab.Button);
        UpdateEntityRow();
        bodyHost.Content = tab.Body;
        activeEntityButton = tab.Button;
        HighlightEntityTab();
        UpdateModuleBarStyles();
        Dispatcher.UIThread.Post(() => tab.Button.BringIntoView(), DispatcherPriority.Loaded);
    }

    void ActivateKindTab(KindTab tab)
    {
        tab.Restore();
        bodyHost.Content = tab.Body;
        activeEntityButton = tab.Button;
        HighlightEntityTab();
        UpdateModuleBarStyles();
        Dispatcher.UIThread.Post(() => tab.Button.BringIntoView(), DispatcherPriority.Loaded);
    }

    void CloseKindTab(KindTab tab)
    {
        foreach (var (kind, entry) in kindTabs.ToArray())
            if (ReferenceEquals(entry, tab)) kindTabs.Remove(kind);
        entityStrip.Children.Remove(tab.Button);
        if (activeEntityButton == tab.Button) activeEntityButton = null;
        UpdateEntityRow();
        ShowModule(activeModule ?? "Players");
    }

    static void SetEntityTitle(Border button, string title)
    {
        ToolTip.SetTip(button, title);
        if (button.Child is StackPanel { Children: [TextBlock text, _] }) text.Text = title;
    }

    /// <summary>Registers one list screen; the module row is the app's navigation, entity tabs stay in their own strip.</summary>
    void AddModuleTab(string header, Control content, object? navItem)
    {
        if (moduleRoots.ContainsKey(header)) return;
        moduleRoots[header] = content;
        moduleNav[header] = navItem;

        var button = new Button
        {
            Content = header,
            FontSize = 11.5,
            Padding = new Thickness(11, 3),
            MinHeight = 25,
            Height = 25,
            CornerRadius = new CornerRadius(3),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = Brushes.Transparent,
            Foreground = Brush("#94A3B8"),
        };
        button.Click += (_, _) => ShowModule(header);
        moduleButtons[header] = button;
        moduleBar.Children.Add(button);
    }

    void UpdateModuleBarStyles()
    {
        bool onListScreen = activeEntityButton is null;
        foreach (var (name, button) in moduleButtons)
        {
            bool active = name == activeModule;
            button.Foreground = Brush(active ? "#F8FAFC" : "#94A3B8");
            button.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
            button.Background = active && onListScreen ? Brush("#201B38") : Brushes.Transparent;
            button.BorderBrush = active ? UiKit.Accent : Brushes.Transparent;
        }
    }

    void ShowModule(string header)
    {
        if (!moduleRoots.TryGetValue(header, out var root)) return;
        activeModule = header;
        activeEntityButton = null;
        HighlightEntityTab();
        bodyHost.Content = root;
        root.IsVisible = true;
        UpdateModuleBarStyles();

        if (moduleNav[header] is { } nav) modules.SelectedItem = nav;
        else modules.SelectedIndex = 0;
    }

    void ShowDetailTab(PlayerRecord? player)
    {
        if (player is null) { CloseDetailTab(); return; }
        var existing = playerTabs.FirstOrDefault(tab => tab.Player.Address == player.Address);
        if (existing is not null)
        {
            existing.Player = player;
            ActivatePlayerTab(existing);
            return;
        }

        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 8, 10) };
        var scroll = new ScrollViewer
        {
            Content = panel,
            Background = UiKit.WindowBg,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        var sticky = new ContentControl();
        DockPanel.SetDock(sticky, Dock.Bottom);
        var header = new ContentControl { Margin = new Thickness(10, 8, 10, 7) };
        DockPanel.SetDock(header, Dock.Top);
        var rail = new ContentControl { Width = 140, Margin = new Thickness(10, 0, 0, 8), VerticalAlignment = VerticalAlignment.Top };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
        body.Children.Add(rail);
        Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        var root = new DockPanel { Background = UiKit.WindowBg, Children = { header, sticky, body } };
        var tab = new PlayerTab { Player = player, Panel = panel, Root = root, Sticky = sticky, Header = header, Rail = rail };
        tab.Button = MakeEntityButton(player.DisplayName, () => ActivatePlayerTab(tab), () => ClosePlayerTab(tab));
        playerTabs.Add(tab);
        entityStrip.Children.Add(tab.Button);
        UpdateEntityRow();
        ActivatePlayerTab(tab);
    }

    void ActivatePlayerTab(PlayerTab tab)
    {
        selected = tab.Player;
        editor = tab.Panel;
        bodyHost.Content = tab.Root;
        activeEntityButton = tab.Button;
        HighlightEntityTab();
        UpdateModuleBarStyles();
        RenderEditor();
        Dispatcher.UIThread.Post(() => tab.Button.BringIntoView(), DispatcherPriority.Loaded);
    }

    void ClosePlayerTab(PlayerTab tab)
    {
        playerTabs.Remove(tab);
        entityStrip.Children.Remove(tab.Button);
        if (activeEntityButton == tab.Button) activeEntityButton = null;
        UpdateEntityRow();
        if (playerTabs.Count > 0)
        {
            ActivatePlayerTab(playerTabs[^1]);
            return;
        }
        ShowModule(activeModule ?? "Players");
    }

    void CloseDetailTab()
    {
        foreach (var tab in playerTabs.ToArray()) entityStrip.Children.Remove(tab.Button);
        playerTabs.Clear();
        foreach (var tab in kindTabs.Values.ToArray()) entityStrip.Children.Remove(tab.Button);
        kindTabs.Clear();
        activeEntityButton = null;
        UpdateEntityRow();
    }

    Border MakeEntityButton(string title, Action activate, Action close)
    {
        var label = new TextBlock
        {
            Text = title,
            FontSize = 11.5,
            MaxWidth = 148,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("#CBD5E1"),
        };
        var closeButton = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(4, 0),
            MinHeight = 16,
            Height = 18,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush("#94A3B8"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        closeButton.Click += (_, e) => { e.Handled = true; close(); };

        var border = new Border
        {
            Background = Brush("#0E111A"),
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, 2, 1, 0),
            Padding = new Thickness(10, 0, 4, 0),
            Height = 28,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { label, closeButton },
            },
        };
        ToolTip.SetTip(border, title);
        border.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual v && (ReferenceEquals(v, closeButton) || closeButton.IsVisualAncestorOf(v))) return;
            activate();
        };
        return border;
    }

    void HighlightEntityTab()
    {
        foreach (var child in entityStrip.Children)
        {
            if (child is not Border border) continue;
            bool active = ReferenceEquals(border, activeEntityButton);
            border.Background = Brush(active ? "#171B2A" : "#0E111A");
            border.BorderBrush = active ? UiKit.Accent : UiKit.BorderSubtle;
            if (border.Child is StackPanel { Children: [TextBlock text, _] })
            {
                text.Foreground = Brush(active ? "#F8FAFC" : "#94A3B8");
                text.FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal;
            }
        }
    }

    void CloseUnified()
    {
        unifiedTimer.Stop();
        searchOverlay.IsVisible = false;
        unifiedList.Children.Clear();
    }

    void EnsureUnifiedCache()
    {
        if (unifiedCacheLoaded || backend.State.Status != ConnectionStatus.Connected) return;
        var session = backend.Session;
        var players = rows.OfType<PlayerRecord>().ToArray();
        if (players.Length == 0) players = session.Players.LoadAll().ToArray();
        unifiedClubs = session.LoadClubs(players);
        unifiedNations = session.Nations.LoadAll();
        unifiedCacheLoaded = true;
    }

    static string FoldText(string? text) => string.IsNullOrEmpty(text)
        ? ""
        : new string(text.Normalize(System.Text.NormalizationForm.FormD).Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();

    /// <summary>Matches "man utd" against "Manchester United": every token must appear or start a word.</summary>
    static bool MatchesLoose(string? text, string[] tokens)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var folded = FoldText(text);
        var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.All(t => folded.Contains(t, StringComparison.Ordinal) || words.Any(w => w.StartsWith(t, StringComparison.Ordinal)));
    }

    void RunUnifiedSearch()
    {
        string raw = (unifiedSearch.Text ?? "").Trim();
        string query = FoldText(raw);
        if (query.Length == 0)
        {
            unifiedList.Children.Clear();
            unifiedResultsLabel.Text = "Results";
            return;
        }
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool numericQuery = int.TryParse(raw, NumberStyles.Integer, Inv, out int idQuery);
        EnsureUnifiedCache();

        var results = new List<(string Kind, string Name, string Meta, Action Pick)>();
        foreach (var player in rows.OfType<PlayerRecord>())
        {
            if (results.Count >= 6) break;
            if ((numericQuery && player.Id == idQuery) || FoldText(player.DisplayName).Contains(query, StringComparison.Ordinal) || FoldText(player.FullName).Contains(query, StringComparison.Ordinal))
                results.Add(("PLAYER", player.DisplayName, $"ID {player.Id?.ToString(Inv) ?? "—"} · {player.Club?.DisplayName ?? "Free agent"}", () => PickPlayer(player)));
        }
        foreach (var club in unifiedClubs)
        {
            if (results.Count >= 10) break;
            string name = club.Name ?? club.DisplayName;
            if ((numericQuery && club.Id == idQuery) || MatchesLoose(name, tokens) || MatchesLoose(club.ShortName, tokens))
                results.Add(("CLUB", name, $"ID {club.Id?.ToString(Inv) ?? "—"}", () => PickClub(club)));
        }
        foreach (var nation in unifiedNations)
        {
            if (results.Count >= 14) break;
            if ((numericQuery && nation.Id == idQuery) || FoldText(nation.DisplayName).Contains(query, StringComparison.Ordinal))
                results.Add(("NATION", nation.DisplayName, $"ID {nation.Id?.ToString(Inv) ?? "—"}", () => PickNation(nation)));
        }
        foreach (var staffRow in staffRows)
        {
            if (results.Count >= 17) break;
            if ((numericQuery && staffRow.Staff.Id == idQuery) || FoldText(staffRow.Name).Contains(query, StringComparison.Ordinal))
                results.Add(("STAFF", staffRow.Name, $"ID {staffRow.Staff.Id.ToString(Inv)} · {staffRow.Job}", () => PickStaff(staffRow.Staff)));
        }
        foreach (var compRow in competitionRows)
        {
            if (results.Count >= 19) break;
            if ((numericQuery && compRow.Id == idQuery) || FoldText(compRow.Name).Contains(query, StringComparison.Ordinal))
                results.Add(("COMP", compRow.Name, $"ID {compRow.Id.ToString(Inv)}", () =>
                {
                    SwitchModule("Competitions");
                    selectedCompetition = compRow.Competition;
                    RenderCompetitionEditor();
                    OpenKindTab("competition", compRow.Competition.DisplayName, competitionDetailRoot);
                }));
        }

        unifiedList.Children.Clear();
        if (results.Count == 0)
        {
            unifiedResultsLabel.Text = "Results 0";
            searchOverlay.IsVisible = true;
            return;
        }
        unifiedResultsLabel.Text = $"Results {results.Count}";
        foreach (var (kind, name, meta, pick) in results)
        {
            var rowContent = new DockPanel { LastChildFill = true };
            var pill = UiKit.MetaPill(kind, kind == "PLAYER");
            pill.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(pill, Dock.Right);
            rowContent.Children.Add(pill);
            rowContent.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = UiKit.TextPrimary, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = meta, FontSize = 11, Foreground = UiKit.TextMuted, VerticalAlignment = VerticalAlignment.Center },
                },
            });
            var row = new Button
            {
                Content = rowContent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                MinHeight = 30,
                Height = 30,
                Padding = new Thickness(8, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            row.Click += (_, _) => { CloseUnified(); pick(); };
            unifiedList.Children.Add(row);
        }
        searchOverlay.IsVisible = true;
    }

    void ChooseFirstUnified()
    {
        if (!searchOverlay.IsVisible) return;
        var first = unifiedList.Children.OfType<Button>().FirstOrDefault();
        if (first is not null) first.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    void SaveCurrentEntity()
    {
        Button? primary = null;
        if (playerTabs.Any(t => ReferenceEquals(t.Root, bodyHost.Content))) primary = saveButton;
        else if (ReferenceEquals(bodyHost.Content, clubDetailRoot)) primary = clubSaveButton;
        else if (ReferenceEquals(bodyHost.Content, nationDetailRoot)) primary = nationSaveButton;
        else if (ReferenceEquals(bodyHost.Content, staffDetailRoot)) primary = staffSaveButton;
        else if (ReferenceEquals(bodyHost.Content, competitionDetailRoot)) primary = competitionSaveButton;
        else if (ReferenceEquals(bodyHost.Content, stadiumDetailRoot)) primary = stadiumSaveButton;
        else if (ReferenceEquals(bodyHost.Content, cityDetailRoot)) primary = citySaveButton;
        else if (ReferenceEquals(bodyHost.Content, awardDetailRoot)) primary = awardSaveButton;

        if (primary is { IsEnabled: true })
        {
            RaiseClick(primary);
            return;
        }

        if (bodyHost.Content is Visual root)
        {
            var subSave = root.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.IsVisible && b.IsEnabled && b.Content is string label &&
                    (label.Equals("Save changes", StringComparison.OrdinalIgnoreCase) || label.Equals("Save contract", StringComparison.OrdinalIgnoreCase)));
            if (subSave is not null)
            {
                RaiseClick(subSave);
                return;
            }
        }

        status.Text = "No unsaved changes";
    }

    async void OpenCurrentMassEdit()
    {
        if (backend.State.Status != ConnectionStatus.Connected) { status.Text = "Load game first"; return; }
        if (ReferenceEquals(bodyHost.Content, clubDetailRoot) || activeModule == "Clubs")
        {
            if (loadedClubs.Count == 0) await LoadClubsScreenAsync(forceReloadPlayers: false);
            await ShowClubMassEditAsync();
            return;
        }
        if (ReferenceEquals(bodyHost.Content, staffDetailRoot) || activeModule == "Staff")
        {
            if (staffRows.Count == 0) await LoadStaffAsync();
            await ShowStaffMassEditAsync();
            return;
        }
        RaiseClick(massEditButton);
    }

    void OpenCurrentFreezer()
    {
        if (backend.State.Status != ConnectionStatus.Connected) { status.Text = "Load game first"; return; }
        if (backend.Freezer is null) { status.Text = backend.FreezeError is { } err ? $"Freezer unavailable: {err}" : "Freezer unavailable"; return; }
        if ((ReferenceEquals(bodyHost.Content, clubDetailRoot) || (activeModule == "Clubs" && activeEntityButton is null)) && selectedClub is { } club)
        {
            _ = ShowClubFreezerAsync(club);
            return;
        }
        if (selected is { } player)
        {
            _ = ShowPlayerFreezerAsync(player);
            return;
        }
        if (selectedClub is { } fallbackClub)
        {
            _ = ShowClubFreezerAsync(fallbackClub);
            return;
        }
        status.Text = "Select a player or club first";
    }

    sealed record HistoryRow(string Time, string Field, int OldValue, int NewValue, string Result, EditRecord Raw);

    async Task ShowEditHistoryAsync()
    {
        var window = new Window
        {
            Title = "Session Edit History",
            Width = 640,
            Height = 460,
            MinWidth = 480,
            MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var history = backend.State.Status == ConnectionStatus.Connected && backend.Edits is not null
            ? backend.Edits.History
            : [];
        var table = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = false,
            CanUserSortColumns = false,
            RowHeight = 27,
            Background = UiKit.SurfaceAlt,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(1),
        };
        table.Columns.Add(new DataGridTextColumn { Header = "Time", Binding = new Binding(nameof(HistoryRow.Time)), Width = new DataGridLength(82) });
        table.Columns.Add(new DataGridTextColumn { Header = "Field", Binding = new Binding(nameof(HistoryRow.Field)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var oldCol = new DataGridTextColumn { Header = "Before", Binding = new Binding(nameof(HistoryRow.OldValue)), Width = new DataGridLength(85) };
        oldCol.CellStyleClasses.Add("numeric");
        var newCol = new DataGridTextColumn { Header = "After", Binding = new Binding(nameof(HistoryRow.NewValue)), Width = new DataGridLength(85) };
        newCol.CellStyleClasses.Add("numeric");
        table.Columns.Add(oldCol);
        table.Columns.Add(newCol);
        table.Columns.Add(new DataGridTextColumn { Header = "Result", Binding = new Binding(nameof(HistoryRow.Result)), Width = new DataGridLength(90) });

        void RefreshRows()
        {
            var current = backend.State.Status == ConnectionStatus.Connected && backend.Edits is not null ? backend.Edits.History : [];
            table.ItemsSource = current.Reverse().Select(r => new HistoryRow(
                r.Time.ToLocalTime().ToString("HH:mm:ss", Inv),
                r.Field,
                r.OldValue,
                r.NewValue,
                r.Succeeded ? "Saved" : (r.Error ?? "Failed"),
                r)).ToArray();
        }
        RefreshRows();

        var info = new TextBlock
        {
            Text = history.Count == 0
                ? "No scalar edits recorded in this session."
                : $"{history.Count:N0} scalar edit(s) recorded. Structural or multi-step writes require a save backup.",
            Foreground = Brush("#94A3B8"),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var undoSelected = new Button { Content = "Undo selected", IsEnabled = false };
        var close = new Button { Content = "Close", Classes = { "accent" } };
        close.Click += (_, _) => window.Close();

        table.SelectionChanged += (_, _) =>
        {
            undoSelected.IsEnabled = table.SelectedItem is HistoryRow hr && hr.Raw.Succeeded &&
                PlayerFields.All.Any(f => f.Id == hr.Raw.Field && f.Write is not null) &&
                rows.OfType<PlayerRecord>().Any(p => p.Address == hr.Raw.Entity);
        };
        undoSelected.Click += (_, _) =>
        {
            if (table.SelectedItem is not HistoryRow hr) return;
            var field = PlayerFields.All.FirstOrDefault(f => f.Id == hr.Raw.Field && f.Write is not null);
            var target = rows.OfType<PlayerRecord>().FirstOrDefault(p => p.Address == hr.Raw.Entity);
            if (field is null || target is null) return;
            try
            {
                var updated = backend.Edits!.Undo(target, field, hr.Raw);
                updated.GameDate = backend.GameDate;
                ReplaceRow(updated);
                if (selected?.Address == updated.Address) { selected = updated; RenderEditor(); }
                RefreshRows();
                info.Text = $"Reverted {hr.Field} on {updated.DisplayName} to {hr.OldValue}.";
            }
            catch (Exception ex) { info.Text = UserMessage.For(ex); }
        };

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { undoSelected, close } };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(info);

        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(table);
        window.Content = root;
        await window.ShowDialog(this);
    }

    void PickPlayer(PlayerRecord player)
    {
        SwitchModule("Players");
        updating = true;
        grid.SelectedItem = player;
        updating = false;
        selected = player;
        ShowDetailTab(player);
    }

    void PickClub(ClubEntry club)
    {
        SwitchModule("Clubs");
        SelectClubInList(club);
        OpenKindTab("club", club.DisplayName, clubDetailRoot);
    }

    void PickStaff(Fm27Editor.Core.Staff.StaffRecord staff)
    {
        SwitchModule("Staff");
        selectedStaff = staff;
        RenderStaffEditor();
        OpenKindTab("staff", staff.DisplayName, staffDetailRoot);
    }

    async Task OpenPlayerClubAsync(ulong address)
    {
        try
        {
            var clubs = clubCache ?? await Task.Run(() => backend.Session.LoadClubs(rows.OfType<PlayerRecord>().ToArray()));
            clubCache = clubs;
            var club = clubs.FirstOrDefault(c => c.Address == address);
            if (club is null) { status.Text = "Club unavailable"; return; }
            PickClub(club);
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
    }

    async Task OpenPlayerNationAsync(ulong address)
    {
        try
        {
            var nations = nationCatalogue ?? await Task.Run(() => backend.Session.Nations.LoadAll());
            nationCatalogue = nations;
            var nation = nations.FirstOrDefault(n => n.Address == address);
            if (nation is null) { status.Text = "Nation unavailable"; return; }
            PickNation(nation);
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
    }

    void OpenPlayerCompetition(ulong address)
    {
        try
        {
            var competition = backend.Session.Competitions.Reload(address);
            if (competition is null) { status.Text = "Competition unavailable"; return; }
            SwitchModule("Competitions");
            selectedCompetition = competition;
            RenderCompetitionEditor();
            OpenKindTab("competition", competition.DisplayName, competitionDetailRoot);
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
    }

    void PickNation(NationRecord nation)
    {
        SwitchModule("Nations");
        selectedNation = nation;
        RenderNationEditor();
        OpenKindTab("nation", nation.DisplayName, nationDetailRoot);
    }

    void SwitchModule(string title)
    {
        foreach (var header in moduleRoots.Keys)
        {
            if (header.StartsWith(title, StringComparison.OrdinalIgnoreCase))
            {
                ShowModule(header);
                return;
            }
        }
        for (int i = 0; i < modules.ItemCount; i++)
        {
            if (modules.Items[i] is IEditorModule candidate && candidate.Title.StartsWith(title, StringComparison.OrdinalIgnoreCase))
            {
                modules.SelectedIndex = i;
                return;
            }
        }
    }
}

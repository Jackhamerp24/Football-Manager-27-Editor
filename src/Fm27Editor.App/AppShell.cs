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
using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    readonly TextBox unifiedSearch = new()
    {
        Width = 280,
        Height = 26,
        FontSize = 13,
        Watermark = "Universal search: player, club, stadium, staff, competition, award or ID",
    };
    readonly Border searchOverlay = new()
    {
        Background = UiKit.Brush("#E6080A10"),
        IsVisible = false,
        ZIndex = 200,
    };
    readonly TextBlock unifiedResultsLabel = new() { FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = Brush("#F1F5F9"), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock unifiedResultsCountText = new() { Text = "0", FontSize = 12, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    readonly Border unifiedCountBadge = new()
    {
        Background = UiKit.Accent,
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(6, 1),
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
    };
    bool unifiedGroupByType;
    Button? unifiedGroupToggleBtn;
    sealed record UnifiedSearchResult(string Kind, string Name, string Meta, int TotalScore, Action Pick);
    List<UnifiedSearchResult> lastUnifiedResults = [];

    Control BuildSearchOverlay()
    {
        unifiedSearch.Width = double.NaN;
        unifiedSearch.Height = 32;
        unifiedSearch.FontSize = 14.5;
        unifiedSearch.HorizontalAlignment = HorizontalAlignment.Stretch;
        unifiedSearch.Watermark = "Universal search: player, club, stadium, staff, competition, award or ID";

        var closeBtn = new Button
        {
            Content = "Close",
            FontSize = 13,
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

        var resultsRow = new DockPanel { LastChildFill = false, Margin = new Thickness(2, 6, 2, 6) };

        var leftSummary = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        leftSummary.Children.Add(unifiedResultsLabel);
        unifiedCountBadge.Child = unifiedResultsCountText;
        leftSummary.Children.Add(unifiedCountBadge);
        resultsRow.Children.Add(leftSummary);

        var rightActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(rightActions, Dock.Right);

        unifiedGroupToggleBtn = new Button
        {
            Content = unifiedGroupByType ? "Grouped ✓" : "Group by type",
            Classes = { "meta-link" },
            FontSize = 13,
            Foreground = unifiedGroupByType ? UiKit.Brush("#C084FC") : UiKit.TextMuted,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        unifiedGroupToggleBtn.Click += (_, _) =>
        {
            unifiedGroupByType = !unifiedGroupByType;
            unifiedGroupToggleBtn.Content = unifiedGroupByType ? "Grouped ✓" : "Group by type";
            unifiedGroupToggleBtn.Foreground = unifiedGroupByType ? UiKit.Brush("#C084FC") : UiKit.TextMuted;
            RenderCurrentUnifiedResults();
        };

        var advSearchBtn = new Button
        {
            Content = "Advanced search...",
            Classes = { "meta-link" },
            FontSize = 13,
            Foreground = UiKit.Brush("#A78BFA"),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        advSearchBtn.Click += (_, _) =>
        {
            CloseUnified();
            SwitchModule("Players");
            _ = OpenAttributeFilterDialogAsync();
        };

        rightActions.Children.Add(unifiedGroupToggleBtn);
        rightActions.Children.Add(advSearchBtn);
        resultsRow.Children.Add(rightActions);

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
        unifiedReleaseTimer.Stop();
        searchOverlay.IsVisible = true;
        unifiedResultsLabel.Text = "Results";
        unifiedCountBadge.IsVisible = false;
        unifiedList.Children.Clear();
        unifiedSearch.Focus();
        unifiedSearch.SelectAll();
        if (!string.IsNullOrEmpty(unifiedSearch.Text)) RunUnifiedSearch();
    }

    readonly StackPanel unifiedList = new() { Spacing = 2 };
    readonly DispatcherTimer unifiedTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    readonly DispatcherTimer unifiedReleaseTimer;
    IReadOnlyList<ClubEntry> unifiedClubs = [];
    IReadOnlyList<NationRecord> unifiedNations = [];
    IReadOnlyList<CompetitionRecord> unifiedCompetitions = [];
    IReadOnlyList<Fm27Editor.Core.Staff.StaffRecord> unifiedStaff = [];
    IReadOnlyList<Fm27Editor.Core.Stadiums.StadiumRecord> unifiedStadiums = [];
    IReadOnlyList<Fm27Editor.Core.Awards.AwardRecord> unifiedAwards = [];
    IReadOnlyList<Fm27Editor.Core.Officials.RefereeRecord> unifiedReferees = [];
    IReadOnlyList<CityRecord> unifiedCities = [];
    readonly object unifiedCacheLock = new();
    bool unifiedCacheLoaded;

    Control BuildTopBar()
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

        left.Children.Add(new Border
        {
            Background = UiKit.Accent,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 4),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "FM27",
                FontSize = 12.5,
                FontWeight = FontWeight.Black,
                Foreground = Brushes.White,
                LetterSpacing = 0.5,
            },
        });

        static Border Divider() => new()
        {
            Width = 1,
            Height = 16,
            Background = UiKit.BorderSubtle,
            Margin = new Thickness(4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Button ToolBtn(string iconData, string tip, Action action)
        {
            var b = new Button
            {
                Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(iconData), Fill = Brush("#DCE2EE"), Stretch = Stretch.Uniform, Width = 16, Height = 16 },
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(0),
                MinWidth = 34,
                Width = 34,
                MinHeight = 28,
                Height = 28,
                Background = Brush("#181C29"),
                BorderBrush = Brush("#272D42"),
                BorderThickness = new Thickness(1),
                Foreground = Brush("#DCE2EE"),
            };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => action();
            return b;
        }

        left.Children.Add(ToolBtn("M10,20V14H14V20H19V12H22L12,3L2,12H5V20H10Z", "Return to module list", GoHome));
        var saveBtn = ToolBtn("M15,9H5V5H15M12,19A3,3 0 0,1 9,16A3,3 0 0,1 12,13A3,3 0 0,1 15,16A3,3 0 0,1 12,19M17,3H5C3.89,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V7L17,3Z", "Save changes on current page (Ctrl+S)", SaveCurrentEntity);
        saveBtn.Classes.Add("accent");
        left.Children.Add(saveBtn);
        left.Children.Add(ToolBtn("M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z", "Reload current screen (Ctrl+R)", ReloadCurrentScreen));
        left.Children.Add(ToolBtn("M13.5,8H12V13L16.28,15.54L17,14.33L13.5,12.25V8M13,3A9,9 0 0,0 4,12H1L4.96,16.03L9,12H6A7,7 0 0,1 13,5A7,7 0 0,1 20,12A7,7 0 0,1 13,19C11.07,19 9.32,18.21 8.06,16.94L6.64,18.36C8.27,20 10.5,21 13,21A9,9 0 0,0 22,12A9,9 0 0,0 13,3", "Session edit history", () => _ = ShowEditHistoryAsync()));
        left.Children.Add(Divider());
        left.Children.Add(ToolBtn("M15.5,14L20.5,19L19,20.5L14,15.5V14.71L13.73,14.43C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.43,13.73L14.71,14H15.5M9.5,14C12,14 14,12 14,9.5C14,7 12,5 9.5,5C7,5 5,7 5,9.5C5,12 7,14 9.5,14M12,10H10V12H9V10H7V9H9V7H10V9H12V10Z", "Advanced Search", () => _ = OpenAttributeFilterDialogAsync()));
        left.Children.Add(ToolBtn("M20.79,13.95L18.46,14.57L16.46,13.44V10.56L18.46,9.43L20.79,10.05L21.31,8.12L19.54,7.65L20,5.88L18.07,5.36L17.45,7.69L15.45,8.82L13,7.38V5.12L14.71,3.41L13.29,2L12,3.29L10.71,2L9.29,3.41L11,5.12V7.38L8.55,8.82L6.55,7.69L5.93,5.36L4,5.88L4.46,7.65L2.69,8.12L3.21,10.05L5.54,9.43L7.54,10.56V13.44L5.54,14.57L3.21,13.95L2.69,15.88L4.46,16.35L4,18.12L5.93,18.64L6.55,16.31L8.55,15.18L11,16.62V18.88L9.29,20.59L10.71,22L12,20.71L13.29,22L14.71,20.59L13,18.88V16.62L15.45,15.18L17.45,16.31L18.07,18.64L20,18.12L19.54,16.35L21.31,15.88L20.79,13.95Z", "Freezer Manager", OpenCurrentFreezer));
        left.Children.Add(ToolBtn("M8.5,13.5L11,16.5L14.5,12L19,18H5M21,19V5C21,3.89 20.1,3 19,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19Z", "Choose graphics folder", () => _ = ChooseGraphicsAsync()));
        left.Children.Add(favouritesFilter);
        left.Children.Add(ToolBtn("M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z", "Mass Edit (Ctrl+M)", OpenMassEdit));
        left.Children.Add(BuildThemeToggle());
        left.Children.Add(BuildSettingsButton());

        modules.Width = 124;
        modules.MinHeight = 28;
        modules.Height = 28;
        modules.FontSize = 13.5;
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
            Padding = new Thickness(6, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Ctrl+K", FontSize = 11.5, Foreground = Brush("#94A3B8") },
        };
        DockPanel.SetDock(shortcutPill, Dock.Right);
        searchRow.Children.Add(shortcutPill);
        searchRow.Children.Add(new TextBlock
        {
            Text = "Universal search: player, club, stadium, award or ID…",
            FontSize = 13.5,
            Foreground = Brush("#64748B"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var searchOpen = new Button
        {
            Content = searchRow,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 28,
            MinHeight = 28,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 0, 6, 0),
            Background = UiKit.SurfaceInput,
            BorderBrush = Brush("#272D42"),
            BorderThickness = new Thickness(1),
        };
        searchOpen.Click += (_, _) => OpenUnified();
        var searchHost = new Panel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(10, 0, 10, 0),
        };
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
            Width = 32,
            Height = 28,
            MinHeight = 28,
            Padding = new Thickness(7, 4),
            Background = Brush("#181C29"),
            BorderBrush = Brush("#272D42"),
            BorderThickness = new Thickness(1),
        };
        ToolTip.SetTip(github, "Open the project repository");
        github.Click += (_, _) => OpenUrl("https://github.com/Jackhamerp24/Football-Manager-27-Editor");
        var loadGame = new Button
        {
            Content = "Load Game",
            FontSize = 13.5,
            Padding = new Thickness(14, 0),
            MinHeight = 28,
            Height = 28,
            Classes = { "accent" },
        };
        loadGame.Click += async (_, _) => await ReloadGameAsync();
        right.Children.Add(github);
        right.Children.Add(loadGame);

        var topGrid = new Grid
        {
            Margin = new Thickness(12, 7),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(searchHost, 1);
        Grid.SetColumn(right, 2);

        topGrid.Children.Add(left);
        topGrid.Children.Add(searchHost);
        topGrid.Children.Add(right);

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
            else if (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                ReloadCurrentScreen();
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
            Child = topGrid,
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
                Width = 24,
                Height = 24,
                MinHeight = 24,
                Padding = new Thickness(0),
                FontSize = 13.5,
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
            Height = 33,
            IsVisible = true,
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
            entityRowHost.IsVisible = true;
        Dispatcher.UIThread.Post(UpdateEntityScrollButtons, DispatcherPriority.Loaded);
    }

    /// <summary>Opens one tab for the item at <paramref name="address"/>; <paramref name="restore"/> selects it again when the tab is clicked.</summary>
    void OpenKindTab(string kind, ulong address, string title, Control body, Action restore)
    {
        if (address == 0) return;
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
            FontSize = 13,
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
        selected = tab.Player;
        editor = tab.Panel;
        bodyHost.Content = tab.Root;
        activeEntityButton = tab.Button;
        HighlightEntityTab();
        UpdateModuleBarStyles();
        RenderEditor();
        Dispatcher.UIThread.Post(() => tab.Button.BringIntoView(), DispatcherPriority.Loaded);
    }

    void ActivatePlayerTab(PlayerTab tab)
    {
        selected = tab.Player;
        editor = tab.Panel;
        bodyHost.Content = tab.Root;
        activeEntityButton = tab.Button;
        HighlightEntityTab();
        UpdateModuleBarStyles();
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
            FontSize = 13,
            MaxWidth = 148,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("#CBD5E1"),
        };
        var closeButton = new Button
        {
            Content = "✕",
            FontSize = 11.5,
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
            Height = 32,
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
        unifiedReleaseTimer.Stop();
        unifiedReleaseTimer.Start();
    }

    /// <summary>
    /// The search index holds every club, staff member, city and stadium (about 100 MB). It is dropped once search has been
    /// closed for a minute; the next search loads it again.
    /// </summary>
    void ReleaseUnifiedCache()
    {
        lock (unifiedCacheLock)
        {
            if (!unifiedCacheLoaded) return;
            unifiedClubs = [];
            unifiedNations = [];
            unifiedCompetitions = [];
            unifiedStaff = [];
            unifiedStadiums = [];
            unifiedAwards = [];
            unifiedReferees = [];
            unifiedCities = [];
            unifiedCacheLoaded = false;
        }
    }

    DispatcherTimer CreateUnifiedReleaseTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timer.Tick += (_, _) => { timer.Stop(); ReleaseUnifiedCache(); };
        return timer;
    }

    void EnsureUnifiedCache()
    {
        if (unifiedCacheLoaded || backend.State.Status != ConnectionStatus.Connected) return;
        lock (unifiedCacheLock)
        {
            if (unifiedCacheLoaded || backend.State.Status != ConnectionStatus.Connected) return;
            var session = backend.Session;
            var players = rows.OfType<PlayerRecord>().ToArray();
            if (players.Length == 0)
            {
                try { players = session.Players.LoadAll().ToArray(); } catch { }
            }
            try { unifiedClubs = session.LoadClubs(players); } catch { }
            try { unifiedNations = session.Nations.LoadAll(); } catch { }
            if (session.Profile.Competition is not null)
            {
                try
                {
                    unifiedCompetitions = session.Competitions.LoadAll();
                }
                catch
                {
                    var compList = new List<CompetitionRecord>();
                    var seen = new HashSet<ulong>();
                    foreach (var p in players)
                    {
                        if (p.Division is { Address: > 0 } div && seen.Add(div.Address))
                        {
                            try { if (session.Competitions.Reload(div.Address) is { } c) compList.Add(c); } catch { }
                        }
                    }
                    unifiedCompetitions = compList;
                }
            }
            if (session.Profile.Staff is not null)
            {
                try { unifiedStaff = session.Staff.LoadAll(); } catch { }
            }
            if (session.Profile.Stadium is not null)
            {
                try { unifiedStadiums = session.Stadiums.LoadAll(); } catch { }
            }
            if (session.Profile.Award is not null)
            {
                try { unifiedAwards = session.Awards.LoadAll(); } catch { }
            }
            try { unifiedReferees = session.ReadReferees(); } catch { }
            try { unifiedCities = session.Cities.LoadAll(); } catch { }
            unifiedCacheLoaded = true;
        }
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
            lastUnifiedResults.Clear();
            unifiedList.Children.Clear();
            unifiedResultsLabel.Text = "Results";
            unifiedCountBadge.IsVisible = false;
            return;
        }
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool numericQuery = int.TryParse(raw, NumberStyles.Integer, Inv, out int idQuery);
        EnsureUnifiedCache();

        const int MaxTotalResults = 300;
        const int MaxPerCategory = 120;
        var results = new List<UnifiedSearchResult>();

        // Clubs are shown with an article the game keeps apart from the name ("Le Havre AC" is stored as "Havre AC"), so a query that
        // starts with one is also tried without it.
        string[] articles = ["le", "la", "les", "el", "los", "las", "il", "lo", "the", "de", "do", "da", "os", "as", "al", "das", "der", "die", "den"];
        string withoutArticle = tokens.Length > 1 && articles.Contains(tokens[0]) ? string.Join(' ', tokens.Skip(1)) : query;
        string[] tokensWithoutArticle = withoutArticle.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int ScoreQuery(string? text, string q, string[] qTokens)
        {
            if (string.IsNullOrWhiteSpace(text)) return -1;
            string folded = FoldText(text);
            if (folded == q) return 100;
            if (folded.StartsWith(q, StringComparison.Ordinal)) return 85;
            var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Any(w => w.StartsWith(q, StringComparison.Ordinal))) return 65;
            if (words.Any(w => w == q)) return 55;
            if (qTokens.Length > 1 && MatchesLoose(text, qTokens)) return 45;
            if (folded.Contains(q, StringComparison.Ordinal)) return 20;
            return -1;
        }
        int ScoreText(string? text)
        {
            int score = ScoreQuery(text, query, tokens);
            if (withoutArticle != query) score = Math.Max(score, ScoreQuery(text, withoutArticle, tokensWithoutArticle));
            return score;
        }

        int MatchScore(string? name, string? secondaryName, int? id, int categoryWeight)
        {
            if (numericQuery && id == idQuery) return 1000 + categoryWeight;
            int score = ScoreText(name);
            if (secondaryName is not null)
            {
                int sec = ScoreText(secondaryName);
                if (sec > score) score = sec;
            }
            if (score < 0) return -1;
            return score * 100 + categoryWeight;
        }

        // 1. Competitions (Weight 15)
        int catCount = 0;
        var compSource = competitionScreen.Rows.Count > 0
            ? competitionScreen.Rows.Select(r => r.Competition).ToList()
            : unifiedCompetitions;
        foreach (var comp in compSource)
        {
            if (catCount >= MaxPerCategory) break;
            int score = MatchScore(comp.DisplayName, comp.ShortName, comp.Id, 15);
            if (score > 0)
            {
                string meta = comp.Reputation > 0 ? $"Rep {comp.Reputation}" : "";
                results.Add(new("COMPETITION", comp.DisplayName, meta, score, () => PickCompetition(comp)));
                catCount++;
            }
        }

        // 2. Clubs (Weight 14)
        catCount = 0;
        foreach (var club in unifiedClubs)
        {
            if (catCount >= MaxPerCategory) break;
            string name = club.Name ?? club.DisplayName;
            int score = MatchScore(name, club.ShortName, club.Id, 14);
            if (score > 0)
            {
                results.Add(new("CLUB", name, "", score, () => PickClub(club)));
                catCount++;
            }
        }

        // 3. Players (Weight 13)
        catCount = 0;
        var playerSource = rows.OfType<PlayerRecord>();
        foreach (var player in playerSource)
        {
            if (catCount >= MaxPerCategory) break;
            int score = MatchScore(player.DisplayName, player.FullName, player.Id, 13);
            if (score > 0)
            {
                string playerMeta = string.Join(" · ", new[] { player.NationalityName, player.Club?.DisplayName ?? "Free agent" }.Where(s => !string.IsNullOrEmpty(s)));
                results.Add(new("PLAYER", player.DisplayName, playerMeta, score, () => PickPlayer(player)));
                catCount++;
            }
        }

        // 4. Staff (Weight 12)
        catCount = 0;
        var staffSource = staffRows.Count > 0
            ? staffRows.Select(r => (r.Staff, r.Name, r.Job)).ToList()
            : unifiedStaff.Select(s => (s, s.DisplayName, StaffLabels.Job(s.JobCode))).ToList();
        foreach (var (staff, sName, job) in staffSource)
        {
            if (catCount >= MaxPerCategory) break;
            int score = MatchScore(sName, staff.FullName, staff.Id, 12);
            if (score > 0)
            {
                string meta = string.Join(" · ", new[] { staff.Nationality?.DisplayName, job }.Where(s => !string.IsNullOrEmpty(s)));
                results.Add(new("STAFF", sName, meta, score, () => PickStaff(staff)));
                catCount++;
            }
        }

        // 5. Nations (Weight 11)
        catCount = 0;
        var nationSource = nationScreen.Rows.Count > 0
            ? nationScreen.Rows.Select(r => r.Nation).ToList()
            : unifiedNations;
        foreach (var nation in nationSource)
        {
            if (catCount >= MaxPerCategory) break;
            int score = MatchScore(nation.DisplayName, nation.ShortName, nation.Id, 11);
            if (score > 0)
            {
                results.Add(new("NATION", nation.DisplayName, "", score, () => PickNation(nation)));
                catCount++;
            }
        }

        // 6. Stadiums (Weight 10)
        catCount = 0;
        var stadiumSource = stadiumScreen.Rows.Count > 0
            ? stadiumScreen.Rows.Select(r => (r.Stadium, r.Name, r.Id, r.Owner)).ToList()
            : unifiedStadiums.Select(s => (s, s.DisplayName, s.Id, s.Owner?.DisplayName ?? "")).ToList();
        foreach (var (stadium, sName, sId, sOwner) in stadiumSource)
        {
            if (catCount >= MaxPerCategory) break;
            int score = MatchScore(sName, null, sId, 10);
            if (score > 0)
            {
                string meta = sOwner;
                results.Add(new("STADIUM", sName, meta, score, () => PickStadium(stadium)));
                catCount++;
            }
        }

        // 7. Awards (Weight 9)
        catCount = 0;
        var awardSource = awardScreen.Rows.Count > 0
            ? awardScreen.Rows.Select(r => (r.Award, r.Name, r.Id, (int)r.Reputation)).ToList()
            : unifiedAwards.Select(a => (a, a.DisplayName, a.Id, (int)a.Reputation)).ToList();
        foreach (var (award, aName, aId, aRep) in awardSource)
        {
            if (catCount >= MaxPerCategory) break;
            int score = MatchScore(aName, award.ShortName, aId, 9);
            if (score > 0)
            {
                string meta = aRep > 0 ? $"Rep {aRep}" : "";
                results.Add(new("AWARD", aName, meta, score, () => PickAward(award)));
                catCount++;
            }
        }

        // 8. Referees (Weight 8)
        catCount = 0;
        foreach (var referee in unifiedReferees)
        {
            if (catCount >= MaxPerCategory) break;
            int score = MatchScore(referee.DisplayName, null, referee.UniqueId, 8);
            if (score > 0)
            {
                results.Add(new("REFEREE", referee.DisplayName, "", score, () => refereeScreen.Pick(referee)));
                catCount++;
            }
        }

        // 9. Cities (Weight 7)
        catCount = 0;
        var nationNames = unifiedNations.Where(nat => nat.Address != 0).GroupBy(nat => nat.Address).ToDictionary(g => g.Key, g => g.First().DisplayName);
        foreach (var city in unifiedCities)
        {
            if (catCount >= MaxPerCategory) break;
            int score = MatchScore(city.Name, null, city.Id, 7);
            if (score > 0)
            {
                results.Add(new("CITY", city.Name, nationNames.GetValueOrDefault(city.Nation, ""), score, () => cityScreen.Pick(city)));
                catCount++;
            }
        }

        results.Sort((a, b) => b.TotalScore.CompareTo(a.TotalScore));
        if (results.Count > MaxTotalResults) results.RemoveRange(MaxTotalResults, results.Count - MaxTotalResults);

        lastUnifiedResults = results;
        RenderCurrentUnifiedResults();
        searchOverlay.IsVisible = true;
    }

    void RenderCurrentUnifiedResults()
    {
        unifiedList.Children.Clear();
        if (lastUnifiedResults.Count == 0)
        {
            unifiedResultsLabel.Text = "Results";
            unifiedCountBadge.IsVisible = false;
            return;
        }

        unifiedResultsLabel.Text = "Results";
        unifiedResultsCountText.Text = lastUnifiedResults.Count.ToString(Inv);
        unifiedCountBadge.IsVisible = true;

        if (unifiedGroupByType)
        {
            string[] groupOrder = ["COMPETITION", "CLUB", "PLAYER", "STAFF", "REFEREE", "NATION", "STADIUM", "AWARD", "CITY"];
            var groups = lastUnifiedResults
                .GroupBy(r => r.Kind)
                .OrderBy(g =>
                {
                    int idx = Array.IndexOf(groupOrder, g.Key);
                    return idx >= 0 ? idx : 999;
                });

            foreach (var group in groups)
            {
                var groupItems = group.ToList();
                if (groupItems.Count == 0) continue;

                var headerBar = new Border
                {
                    Background = UiKit.SurfaceHeader,
                    BorderBrush = UiKit.BorderSubtle,
                    BorderThickness = new Thickness(0, 1, 0, 1),
                    Padding = new Thickness(10, 4),
                    Margin = new Thickness(0, 6, 0, 2),
                    CornerRadius = new CornerRadius(3),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 6,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new Border
                            {
                                Width = 3,
                                Height = 12,
                                CornerRadius = new CornerRadius(1.5),
                                Background = UiKit.Accent,
                                VerticalAlignment = VerticalAlignment.Center,
                            },
                            new TextBlock
                            {
                                Text = PluralizeKind(group.Key),
                                FontSize = 12.5,
                                FontWeight = FontWeight.Bold,
                                Foreground = UiKit.TextSecondary,
                                LetterSpacing = 0.5,
                                VerticalAlignment = VerticalAlignment.Center,
                            },
                            new Border
                            {
                                Background = UiKit.AccentSoft,
                                BorderBrush = UiKit.AccentBorder,
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(8),
                                Padding = new Thickness(6, 1),
                                VerticalAlignment = VerticalAlignment.Center,
                                Child = new TextBlock
                                {
                                    Text = groupItems.Count.ToString(Inv),
                                    FontSize = 11.5,
                                    FontWeight = FontWeight.Bold,
                                    Foreground = UiKit.Brush("#E9D5FF"),
                                }
                            }
                        }
                    }
                };
                unifiedList.Children.Add(headerBar);

                foreach (var item in groupItems)
                {
                    unifiedList.Children.Add(CreateUnifiedResultRow(item, showPill: false));
                }
            }
        }
        else
        {
            foreach (var item in lastUnifiedResults)
            {
                unifiedList.Children.Add(CreateUnifiedResultRow(item, showPill: true));
            }
        }
    }

    Control CreateUnifiedResultRow(UnifiedSearchResult item, bool showPill)
    {
        var rowContent = new DockPanel { LastChildFill = true };
        var role = new TextBlock
        {
            Text = string.Concat(item.Kind[..1], item.Kind[1..].ToLowerInvariant()),
            FontSize = 12.5,
            Foreground = UiKit.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        DockPanel.SetDock(role, Dock.Right);
        rowContent.Children.Add(role);
        rowContent.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = item.Name, FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = UiKit.TextPrimary, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = item.Meta, FontSize = 12.5, Foreground = UiKit.TextMuted, VerticalAlignment = VerticalAlignment.Center },
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
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        row.Click += (_, _) => { CloseUnified(); item.Pick(); };
        return row;
    }

    static string PluralizeKind(string kind) => kind switch
    {
        "COMPETITION" => "COMPETITIONS",
        "CLUB" => "CLUBS",
        "PLAYER" => "PLAYERS",
        "STAFF" => "STAFF",
        "NATION" => "NATIONS",
        "STADIUM" => "STADIUMS",
        "AWARD" => "AWARDS",
        "REFEREE" => "REFEREES",
        "CITY" => "CITIES",
        _ => kind
    };

    static Border CreateKindPill(string kind)
    {
        string label = kind switch
        {
            "COMPETITION" => "Competition",
            "CLUB" => "Club",
            "PLAYER" => "Player",
            "STAFF" => "Staff",
            "NATION" => "Nation",
            "STADIUM" => "Stadium",
            "AWARD" => "Award",
            _ => kind
        };
        return UiKit.MetaPill(label, highlight: kind is "COMPETITION" or "PLAYER");
    }

    void PickCompetition(CompetitionRecord competition) => competitionScreen.Pick(competition);

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
        else if (bodyHost.Content is Control shownBody && screens.FirstOrDefault(s => s.OwnsBody(shownBody)) is { } screen) primary = screen.SaveButton;

        // The contract tab has no Save of its own: its edits are saved with the player's, by this one button.
        var pendingContracts = playerTabs.Any(t => ReferenceEquals(t.Root, bodyHost.Content)) && selected is { } shownPlayer
            ? contractDrafts.Where(c => c.Value.Player.Address == shownPlayer.Address && c.Value.Changes.Count > 0).ToArray()
            : [];
        // Every tab's own Save button is hidden; this one button runs them all (and the entity's own Save), once each.
        var subSaves = bodyHost.Content is Visual root
            ? root.GetVisualDescendants().OfType<Button>()
                .Where(b => !ReferenceEquals(b, primary) && b.IsEnabled && b.Content is string label &&
                    (label is "Save changes" or "Save contract" or "Save loan" or "Save"))
                .ToArray()
            : [];
        bool ranAny = false;
        if (primary is { IsEnabled: true }) { RaiseClick(primary); ranAny = true; }
        foreach (var (key, draft) in pendingContracts)
            if (selected is { } owner) { SaveContract(owner, draft, key == owner.Address ? 0UL : key); ranAny = true; }
        foreach (var button in subSaves)
            if (button.IsEnabled) { RaiseClick(button); ranAny = true; }
        if (ranAny) return;

        status.Text = "No unsaved changes";
    }

    void OpenCurrentFreezer()
    {
        if (backend.State.Status != ConnectionStatus.Connected) { status.Text = "Load game first"; return; }
        if (backend.Freezer is null) { status.Text = backend.FreezeError is { } err ? $"Freezer unavailable: {err}" : "Freezer unavailable"; return; }
        _ = ShowFreezerManagerAsync();
    }

    async Task ShowEditHistoryAsync()
    {
        if (backend.State.Status != ConnectionStatus.Connected || backend.Edits?.Journal is not { } journal) { status.Text = "Load game first"; return; }
        var window = new EditHistoryWindow(journal, backend.Session.Memory, text => status.Text = text, RefreshAfterHistoryUndo);
        await window.ShowDialog(this);
    }

    /// <summary>After an undo from the history window: re-read the open player so the page shows the restored values.</summary>
    void RefreshAfterHistoryUndo()
    {
        try
        {
            if (selected is { } player && backend.Players.Reload(player.Address) is { } fresh)
            {
                fresh.GameDate = backend.GameDate;
                ReplaceRow(fresh);
                selected = fresh;
                RenderEditor();
            }
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
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
            competitionScreen.Pick(competition);
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
    }

    void PickNation(NationRecord nation) => nationScreen.Pick(nation);

    void PickStadium(Fm27Editor.Core.Stadiums.StadiumRecord stadium) => stadiumScreen.Pick(stadium);

    void PickAward(Fm27Editor.Core.Awards.AwardRecord award) => awardScreen.Pick(award);

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

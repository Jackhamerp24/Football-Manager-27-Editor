using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    public sealed class FreezerRowItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        bool isFrozen;
        public bool IsFrozen
        {
            get => isFrozen;
            set
            {
                if (isFrozen != value)
                {
                    isFrozen = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFrozen)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FrozenText)));
                }
            }
        }
        public string FrozenText => IsFrozen ? "True" : "False";
        public int Id { get; init; }
        public string Inf { get; init; } = "";
        public string Name { get; init; } = "";
        public int? Age { get; init; }
        public int CA { get; init; }
        public int PA { get; init; }
        public string Nationalities { get; init; } = "";
        public string Club { get; init; } = "";
        public long? AskingPrice { get; init; }
        public string AskingPriceText => AskingPrice is > 0 ? $"£{AskingPrice:N0}" : "—";
        public string Positions { get; init; } = "";
        public string Job { get; init; } = "";
        public PlayerRecord? Player { get; init; }
        public StaffRecord? Staff { get; init; }
    }

    async Task ShowFreezerManagerAsync()
    {
        if (backend.Freezer is not { } freezer) return;

        if (rows.Count == 0 && backend.State.Status == ConnectionStatus.Connected)
            await LoadAsync();

        var window = new Window
        {
            Title = "Freezer Manager",
            Width = 980,
            Height = 580,
            MinWidth = 760,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = UiKit.WindowBg,
        };

        bool isStaffTab = false;
        var gridItems = new ObservableCollection<FreezerRowItem>();
        var touchedPlayers = new Dictionary<int, FreezerRowItem>();
        var touchedStaff = new Dictionary<int, FreezerRowItem>();

        var playersPill = new Button
        {
            Content = "Players",
            FontWeight = FontWeight.SemiBold,
            FontSize = 13.5,
            Padding = new Thickness(14, 0),
            Height = 28, MinHeight = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(4),
            Background = Brush("#2563EB"),
            Foreground = Brush("#FFFFFF"),
        };
        var staffPill = new Button
        {
            Content = "Staff",
            FontWeight = FontWeight.SemiBold,
            FontSize = 13.5,
            Padding = new Thickness(14, 0),
            Height = 28, MinHeight = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(4),
            Background = Brush("#181C29"),
            Foreground = Brush("#94A3B8"),
        };

        var clubCombo = new ComboBox
        {
            Width = 210,
            Height = 28,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var nameFilter = new TextBox
        {
            Watermark = "Filter by name",
            Width = 220,
            Height = 28,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        void StylePills()
        {
            if (!isStaffTab)
            {
                playersPill.Background = Brush("#2563EB");
                playersPill.Foreground = Brush("#FFFFFF");
                staffPill.Background = Brush("#181C29");
                staffPill.Foreground = Brush("#94A3B8");
            }
            else
            {
                staffPill.Background = Brush("#2563EB");
                staffPill.Foreground = Brush("#FFFFFF");
                playersPill.Background = Brush("#181C29");
                playersPill.Foreground = Brush("#94A3B8");
            }
        }

        var allLoadedPlayers = rows.OfType<PlayerRecord>().ToList();
        var clubNames = allLoadedPlayers
            .Select(p => p.Club?.Name)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();
        clubNames.Insert(0, "All Clubs");
        clubCombo.ItemsSource = clubNames;
        clubCombo.SelectedIndex = 0;

        var dataGrid = new DataGrid
        {
            IsReadOnly = true,
            AutoGenerateColumns = false,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Extended,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = Brush("#1D2130"),
            RowHeight = 28,
            Background = Brush("#11131C"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ItemsSource = gridItems,
            Margin = new Thickness(14, 0, 14, 10),
        };

        dataGrid.Styles.Add(new Style(x => x.OfType<DataGridColumnHeader>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 0)),
                new Setter(Layoutable.MinHeightProperty, 28.0),
                new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold),
                new Setter(TextBlock.FontSizeProperty, 13.0),
                new Setter(TextBlock.ForegroundProperty, Brush("#CBD5E1")),
            }
        });

        void BuildColumns()
        {
            dataGrid.Columns.Clear();

            dataGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Frozen",
                Width = new DataGridLength(78),
                CellTemplate = new FuncDataTemplate<FreezerRowItem>((row, _) => new TextBlock
                {
                    Text = row.FrozenText,
                    Foreground = row.IsFrozen ? Brush("#38BDF8") : Brush("#64748B"),
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0),
                }),
            });

            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Inf",
                Binding = new Binding(nameof(FreezerRowItem.Inf)),
                Width = new DataGridLength(51),
            });

            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Name",
                Binding = new Binding(nameof(FreezerRowItem.Name)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            });

            dataGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Age",
                Width = new DataGridLength(51),
                CellTemplate = new FuncDataTemplate<FreezerRowItem>((row, _) => new TextBlock
                {
                    Text = row.Age?.ToString() ?? "—",
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 6, 0),
                }),
            });

            dataGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "CA",
                Width = new DataGridLength(51),
                CellTemplate = new FuncDataTemplate<FreezerRowItem>((row, _) => new TextBlock
                {
                    Text = row.CA > 0 ? row.CA.ToString() : "—",
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 6, 0),
                }),
            });

            dataGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "PA",
                Width = new DataGridLength(51),
                CellTemplate = new FuncDataTemplate<FreezerRowItem>((row, _) => new TextBlock
                {
                    Text = row.PA > 0 ? row.PA.ToString() : "—",
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 6, 0),
                }),
            });

            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Nationalities",
                Binding = new Binding(nameof(FreezerRowItem.Nationalities)),
                Width = new DataGridLength(138),
            });

            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Club",
                Binding = new Binding(nameof(FreezerRowItem.Club)),
                Width = new DataGridLength(161),
            });

            if (!isStaffTab)
            {
                dataGrid.Columns.Add(new DataGridTemplateColumn
                {
                    Header = "Asking Price (£)",
                    Width = new DataGridLength(138),
                    CellTemplate = new FuncDataTemplate<FreezerRowItem>((row, _) => new TextBlock
                    {
                        Text = row.AskingPriceText,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 0, 6, 0),
                    }),
                });

                dataGrid.Columns.Add(new DataGridTemplateColumn
                {
                    Header = "Positions",
                    Width = new DataGridLength(109),
                    CellTemplate = new FuncDataTemplate<FreezerRowItem>((row, _) => new TextBlock
                    {
                        Text = row.Positions,
                        Foreground = Brush("#C4B5FD"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 0),
                    }),
                });
            }
            else
            {
                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Role",
                    Binding = new Binding(nameof(FreezerRowItem.Job)),
                    Width = new DataGridLength(161),
                });
            }
        }

        DateOnly? gameDate = backend.Session.ReadGameDate();

        void PopulateItems()
        {
            gridItems.Clear();
            string selectedClub = clubCombo.SelectedItem as string ?? "All Clubs";
            string search = nameFilter.Text?.Trim() ?? "";

            if (!isStaffTab)
            {
                var candidatePlayers = allLoadedPlayers;
                if (selectedClub != "All Clubs")
                    candidatePlayers = candidatePlayers.Where(p => string.Equals(p.Club?.Name, selectedClub, StringComparison.OrdinalIgnoreCase)).ToList();

                if (!string.IsNullOrEmpty(search))
                {
                    candidatePlayers = candidatePlayers.Where(p => p.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                else if (selectedClub == "All Clubs")
                {
                    var frozenIds = freezer.Players.Select(p => p.Id).ToHashSet();
                    candidatePlayers = candidatePlayers.Where(p => p.Id is { } pid && (frozenIds.Contains(pid) || touchedPlayers.ContainsKey(pid))).ToList();
                }

                foreach (var p in candidatePlayers)
                {
                    if (p.Id is not { } pid) continue;
                    if (!touchedPlayers.TryGetValue(pid, out var item))
                    {
                        bool isFrz = freezer.Players.Any(fp => fp.Id == pid);
                        var injuries = backend.Session.ReadInjuries(p);
                        var bans = backend.Session.ReadBans(p);
                        string inf = SquadFormatters.FormatInfFlags(p, injuries.Count > 0, bans.Count > 0, false, isFrz);
                        item = new FreezerRowItem
                        {
                            Id = pid,
                            IsFrozen = isFrz,
                            Inf = inf,
                            Name = p.DisplayName,
                            Age = p.Age,
                            CA = p.CurrentAbility,
                            PA = p.PotentialAbility,
                            Nationalities = p.Nationality?.Name ?? "",
                            Club = p.Club?.Name ?? "",
                            AskingPrice = p.AskingPrice,
                            Positions = SquadFormatters.FormatPositions(p),
                            Player = p,
                        };
                        touchedPlayers[pid] = item;
                    }
                    else
                    {
                        item.IsFrozen = freezer.Players.Any(fp => fp.Id == pid);
                    }
                    gridItems.Add(item);
                }
            }
            else
            {
                var candidateStaff = staffRows;
                if (selectedClub != "All Clubs")
                    candidateStaff = candidateStaff.Where(s => string.Equals(s.Club, selectedClub, StringComparison.OrdinalIgnoreCase)).ToList();

                if (!string.IsNullOrEmpty(search))
                {
                    candidateStaff = candidateStaff.Where(s => s.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                else if (selectedClub == "All Clubs")
                {
                    var frozenIds = freezer.Staff.Select(s => s.Id).ToHashSet();
                    candidateStaff = candidateStaff.Where(s => frozenIds.Contains(s.Staff.Id) || touchedStaff.ContainsKey(s.Staff.Id)).ToList();
                }

                foreach (var s in candidateStaff)
                {
                    int sid = s.Staff.Id;
                    if (!touchedStaff.TryGetValue(sid, out var item))
                    {
                        bool isFrz = freezer.Staff.Any(fs => fs.Id == sid);
                        item = new FreezerRowItem
                        {
                            Id = sid,
                            IsFrozen = isFrz,
                            Inf = "",
                            Name = s.Name,
                            Age = s.Age,
                            CA = s.CA,
                            PA = s.PA,
                            Nationalities = s.Nation,
                            Club = s.Club,
                            Job = s.Job,
                            Staff = s.Staff,
                        };
                        touchedStaff[sid] = item;
                    }
                    else
                    {
                        item.IsFrozen = freezer.Staff.Any(fs => fs.Id == sid);
                    }
                    gridItems.Add(item);
                }
            }
        }

        var selectedCountText = new TextBlock
        {
            Text = "0 of 0 selected",
            Foreground = Brush("#94A3B8"),
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var unfreezeSelected = new Button
        {
            Content = "Unfreeze Selected",
            FontSize = 13,
            Padding = new Thickness(12, 0),
            Height = 27,
            IsEnabled = false,
        };

        var freezeSelected = new Button
        {
            Content = "Freeze Selected",
            FontSize = 13,
            Padding = new Thickness(12, 0),
            Height = 27,
            IsEnabled = false,
        };

        var unfreezeAll = new Button
        {
            Content = "Unfreeze All",
            FontSize = 13,
            Padding = new Thickness(12, 0),
            Height = 27,
        };

        var closeBtn = new Button
        {
            Content = "Close",
            FontSize = 13,
            Padding = new Thickness(16, 0),
            Height = 27,
        };
        closeBtn.Click += (_, _) => window.Close();

        void UpdateSelectionState()
        {
            int selCount = dataGrid.SelectedItems.Count;
            int total = gridItems.Count;
            selectedCountText.Text = $"{selCount} of {total} selected";
            unfreezeSelected.IsEnabled = selCount > 0;
            freezeSelected.IsEnabled = selCount > 0;
        }

        dataGrid.SelectionChanged += (_, _) => UpdateSelectionState();

        unfreezeSelected.Click += (_, _) =>
        {
            var selectedRows = dataGrid.SelectedItems.OfType<FreezerRowItem>().ToList();
            if (selectedRows.Count == 0) return;
            int count = 0;
            foreach (var item in selectedRows)
            {
                try
                {
                    if (item.Player is not null)
                    {
                        backend.Edits.Execute(() => freezer.RemovePlayer(item.Id));
                        item.IsFrozen = false;
                        count++;
                    }
                    else if (item.Staff is not null)
                    {
                        backend.Edits.Execute(() => freezer.RemoveStaff(item.Id));
                        item.IsFrozen = false;
                        count++;
                    }
                }
                catch (Exception ex) { status.Text = $"Freezer: {ex.Message}"; }
            }
            status.Text = $"Unfrozen {count} item(s)";
            UpdateSelectionState();
        };

        freezeSelected.Click += (_, _) =>
        {
            var selectedRows = dataGrid.SelectedItems.OfType<FreezerRowItem>().ToList();
            if (selectedRows.Count == 0) return;
            int count = 0;
            foreach (var item in selectedRows)
            {
                try
                {
                    if (item.Player is { } p)
                    {
                        backend.Edits.Execute(() => freezer.SetPlayer(p, FrozenPlayer.AllAttributes(p)));
                        item.IsFrozen = true;
                        count++;
                    }
                    else if (item.Staff is { } s)
                    {
                        backend.Edits.Execute(() => freezer.SetStaff(s, FrozenStaff.AllAttributes(s)));
                        item.IsFrozen = true;
                        count++;
                    }
                }
                catch (Exception ex) { status.Text = $"Freezer: {ex.Message}"; }
            }
            status.Text = $"Frozen {count} item(s)";
            UpdateSelectionState();
        };

        unfreezeAll.Click += (_, _) =>
        {
            int count = 0;
            foreach (var item in gridItems.ToList())
            {
                try
                {
                    if (item.Player is not null)
                    {
                        backend.Edits.Execute(() => freezer.RemovePlayer(item.Id));
                        item.IsFrozen = false;
                        count++;
                    }
                    else if (item.Staff is not null)
                    {
                        backend.Edits.Execute(() => freezer.RemoveStaff(item.Id));
                        item.IsFrozen = false;
                        count++;
                    }
                }
                catch { }
            }
            status.Text = $"Unfrozen {count} item(s)";
            UpdateSelectionState();
        };

        playersPill.Click += (_, _) =>
        {
            if (!isStaffTab) return;
            isStaffTab = false;
            StylePills();
            BuildColumns();
            PopulateItems();
            UpdateSelectionState();
        };

        staffPill.Click += async (_, _) =>
        {
            if (isStaffTab) return;
            isStaffTab = true;
            StylePills();
            if (staffRows.Count == 0)
                await LoadStaffAsync();
            BuildColumns();
            PopulateItems();
            UpdateSelectionState();
        };

        clubCombo.SelectionChanged += (_, _) =>
        {
            PopulateItems();
            UpdateSelectionState();
        };

        nameFilter.TextChanged += (_, _) =>
        {
            PopulateItems();
            UpdateSelectionState();
        };

        var topBar = new DockPanel
        {
            Margin = new Thickness(14, 12, 14, 10),
            LastChildFill = false,
        };
        var leftPills = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { playersPill, staffPill, clubCombo },
        };
        DockPanel.SetDock(leftPills, Dock.Left);
        DockPanel.SetDock(nameFilter, Dock.Left);
        topBar.Children.Add(leftPills);
        topBar.Children.Add(nameFilter);

        var bottomActions = new DockPanel
        {
            Margin = new Thickness(14, 8, 14, 6),
            LastChildFill = false,
        };
        var leftActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { selectedCountText, unfreezeSelected, freezeSelected },
        };
        DockPanel.SetDock(leftActions, Dock.Left);
        // One footer row: selection actions on the left, Unfreeze All and Close on the right.
        var rightActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { unfreezeAll, closeBtn } };
        DockPanel.SetDock(rightActions, Dock.Right);
        bottomActions.Children.Add(leftActions);
        bottomActions.Children.Add(rightActions);
        bottomActions.Margin = new Thickness(14, 8, 14, 12);
        var footerPanel = bottomActions;

        var root = new DockPanel();
        DockPanel.SetDock(topBar, Dock.Top);
        DockPanel.SetDock(footerPanel, Dock.Bottom);
        root.Children.Add(topBar);
        root.Children.Add(footerPanel);
        root.Children.Add(dataGrid);

        window.Content = root;
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) { window.Close(); e.Handled = true; } };

        BuildColumns();
        PopulateItems();
        UpdateSelectionState();

        await window.ShowDialog(this);
    }

    async Task ShowPlayerFreezerAsync(PlayerRecord player)
    {
        if (backend.Freezer is not { } freezer || player.Id is not > 0) return;
        var existing = freezer.Players.FirstOrDefault(p => p.Id == player.Id)?.Fields.ToDictionary(f => f.Attribute)
            ?? new Dictionary<PlayerAttribute, FrozenAttribute>();
        var window = new Window { Title = $"Freezer — {player.DisplayName}", Width = 560, Height = 640, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var rows = new StackPanel { Spacing = 3, Margin = new Thickness(14, 12) };
        var choices = new List<(PlayerAttribute Attribute, CheckBox Freeze, CheckBox Increase, CheckBox Decrease)>();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,68,108,108"), Margin = new Thickness(0, 0, 0, 4) };
        var attrHeading = new TextBlock { Text = "Attribute", FontWeight = FontWeight.SemiBold, Foreground = Brush("#94A3B8") };
        var holdHeading = new TextBlock { Text = "Freeze", FontWeight = FontWeight.SemiBold, Foreground = Brush("#94A3B8"), HorizontalAlignment = HorizontalAlignment.Center };
        var growHeading = new TextBlock { Text = "Allow increase", FontWeight = FontWeight.SemiBold, Foreground = Brush("#94A3B8"), HorizontalAlignment = HorizontalAlignment.Center };
        var lowerHeading = new TextBlock { Text = "Allow decrease", FontWeight = FontWeight.SemiBold, Foreground = Brush("#94A3B8"), HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(holdHeading, 1); Grid.SetColumn(growHeading, 2); Grid.SetColumn(lowerHeading, 3);
        header.Children.Add(attrHeading); header.Children.Add(holdHeading); header.Children.Add(growHeading); header.Children.Add(lowerHeading);
        rows.Children.Add(header);
        foreach (var attr in Enum.GetValues<PlayerAttribute>())
        {
            if (player.Raw(attr) is < 1 or > 100) continue;
            existing.TryGetValue(attr, out var frozen);
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,68,108,108") };
            var label = PlayerFields.All.First(f => f.Id == attr.ToString()).Label;
            line.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var hold = new CheckBox { IsChecked = frozen is not null, HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 0, Padding = new Thickness(0) };
            var increase = new CheckBox { IsChecked = frozen?.Mode == FreezeMode.AllowIncrease, HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 0, Padding = new Thickness(0) };
            var decrease = new CheckBox { IsChecked = frozen?.Mode == FreezeMode.AllowDecrease, HorizontalAlignment = HorizontalAlignment.Center, MinWidth = 0, Padding = new Thickness(0) };
            increase.IsCheckedChanged += (_, _) => { if (increase.IsChecked == true) decrease.IsChecked = false; };
            decrease.IsCheckedChanged += (_, _) => { if (decrease.IsChecked == true) increase.IsChecked = false; };
            Grid.SetColumn(hold, 1); Grid.SetColumn(increase, 2); Grid.SetColumn(decrease, 3);
            line.Children.Add(hold); line.Children.Add(increase); line.Children.Add(decrease); rows.Children.Add(line);
            choices.Add((attr, hold, increase, decrease));
        }
        var selectAll = new Button { Content = "Select all" };
        selectAll.Click += (_, _) => { foreach (var choice in choices) choice.Freeze.IsChecked = true; };
        var clear = new Button { Content = "Unfreeze" };
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => window.Close();
        var apply = new Button { Content = "Apply", Classes = { "accent" } };
        var footer = new DockPanel
        {
            Margin = new Thickness(14, 10),
            LastChildFill = false,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { selectAll, clear } },
            },
        };
        var rightButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { close, apply } };
        DockPanel.SetDock(rightButtons, Dock.Right);
        footer.Children.Insert(0, rightButtons);
        var footerBorder = new Border { BorderBrush = Brush("#252A3D"), BorderThickness = new Thickness(0, 1, 0, 0), Background = Brush("#131620"), Child = footer };
        var panel = new DockPanel();
        DockPanel.SetDock(footerBorder, Dock.Bottom); panel.Children.Add(footerBorder);
        panel.Children.Add(new ScrollViewer { Content = rows });
        window.Content = panel;
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) { window.Close(); e.Handled = true; } };
        clear.Click += (_, _) =>
        {
            try { backend.Edits.Execute(() => freezer.RemovePlayer(player.Id.Value)); status.Text = "Player unfrozen"; window.Close(); }
            catch (Exception ex) { status.Text = $"Freezer: {ex.Message}"; }
        };
        apply.Click += (_, _) =>
        {
            var fields = choices.Where(c => c.Freeze.IsChecked == true)
                .Select(c => (c.Attribute, c.Increase.IsChecked == true ? FreezeMode.AllowIncrease : c.Decrease.IsChecked == true ? FreezeMode.AllowDecrease : FreezeMode.Fixed)).ToArray();
            if (fields.Length == 0) { status.Text = "Select an attribute"; return; }
            try { backend.Edits.Execute(() => freezer.SetPlayer(player, fields)); status.Text = $"Frozen: {player.DisplayName}"; window.Close(); }
            catch (Exception ex) { status.Text = $"Freezer: {ex.Message}"; }
        };
        await window.ShowDialog(this);
    }
}

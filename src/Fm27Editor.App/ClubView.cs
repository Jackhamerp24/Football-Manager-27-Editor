using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    public sealed record ClubsNavItem
    {
        public string Title => "Clubs";
        public override string ToString() => Title;
    }

    public sealed record ClubSectionOption(ClubSection Section, string Title)
    {
        public override string ToString() => Title;
    }

    public sealed record ClubPickerItem(ClubEntry Club)
    {
        public override string ToString() =>
            Club.Id is { } id ? $"{Club.DisplayName}, {id.ToString(Inv)}" : Club.DisplayName;
    }

    readonly ClubsNavItem clubsNavItem = new();
    readonly Border clubsViewRoot = new() { IsVisible = false };
    readonly TextBox clubSearchBox = new() { Watermark = "Club or ID" };
    readonly ListBox clubPickerList = new()
    {
        Background = Brush("#11131C"),
        BorderThickness = new Thickness(0),
        SelectionMode = SelectionMode.Single,
    };
    readonly ListBox clubSectionList = new()
    {
        Background = Brush("#131620"),
        BorderThickness = new Thickness(0),
        Padding = new Thickness(6, 6),
        SelectionMode = SelectionMode.Single,
    };
    readonly StackPanel clubEditorPanel = new() { Spacing = 12, Margin = new Thickness(18, 14) };
    readonly Dictionary<ulong, ClubDraft> clubDrafts = [];
    readonly Dictionary<ulong, ulong> clubSelectedTeam = [];
    readonly Dictionary<(ulong Club, ClubSection Section), int> clubListSelectedRow = [];

    IReadOnlyList<ClubEntry> loadedClubs = [];
    ClubEntry? selectedClub;
    int? lastSelectedClubId;
    ClubSection selectedClubSection = ClubSection.Information;
    ClubSection selectedFinanceSubSection = ClubSection.Finances;
    bool clubsActive;
    bool clubUpdating;
    int clubLoadGeneration;
    int clubRenderGeneration;
    Button? clubSaveButton;
    Button? clubDiscardButton;

    void RegisterClubsView(DockPanel root, Grid playersSplit)
    {
        ParseClubStartupArgs(out bool startOnClubs, out int? startupClubId);
        if (startupClubId is not null)
            lastSelectedClubId = startupClubId;

        BuildClubsLayout();

        int idx = root.Children.IndexOf(playersSplit);
        if (idx >= 0) root.Children.RemoveAt(idx);
        var bodyHost = new Grid { Children = { playersSplit, clubsViewRoot } };
        root.Children.Add(bodyHost);

        modules.ItemsSource = (object[])[.. Modules.Registered, clubsNavItem];

        modules.SelectionChanged += async (_, _) =>
        {
            bool toClubs = ReferenceEquals(modules.SelectedItem, clubsNavItem);
            if (toClubs == clubsActive) return;

            clubsActive = toClubs;
            playersSplit.IsVisible = !clubsActive;
            clubsViewRoot.IsVisible = clubsActive;
            status.Text = "";

            if (clubsActive)
            {
                if (backend.State.Status == ConnectionStatus.Connected)
                    await LoadClubsScreenAsync(forceReloadPlayers: false);
                else
                    ClearClubsScreen();
            }
        };

        backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            loadedClubs = [];
            clubDrafts.Clear();
            clubMassUndo.Clear();
            clubSelectedTeam.Clear();
            clubListSelectedRow.Clear();
            selectedClub = null;

            if (!clubsActive) return;

            if (backend.State.Status == ConnectionStatus.Connected)
            {
                rows = [];
                clubCache = null;
                competitionCache = null;
                await LoadClubsScreenAsync(forceReloadPlayers: true);
            }
            else
            {
                gameDateBadge.IsVisible = false;
                ClearClubsScreen();
            }
        });

        if (startOnClubs)
        {
            Opened += (_, _) => Dispatcher.UIThread.Post(() => modules.SelectedItem = clubsNavItem);
        }

        timer.Tick += async (_, _) => await CheckSelectedClubAsync();
    }

    static void ParseClubStartupArgs(out bool startOnClubs, out int? clubId)
    {
        startOnClubs = false;
        clubId = null;
        var args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--clubs", StringComparison.OrdinalIgnoreCase))
            {
                startOnClubs = true;
            }
            else if (string.Equals(args[i], "--club", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                     && int.TryParse(args[i + 1], System.Globalization.NumberStyles.Integer, Inv, out int id))
            {
                startOnClubs = true;
                clubId = id;
                i++;
            }
        }
    }

    void BuildClubsLayout()
    {
        clubPickerList.ItemTemplate = new FuncDataTemplate<ClubPickerItem>((item, _) =>
        {
            if (item is null) return null;
            var club = item.Club;
            // Reserve 16px on the right (plus ListBoxItem's 8px padding = 24px) so the non-autohiding ScrollBar never covers the right-aligned ID.
            var line = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 16, 0) };
            Avalonia.Automation.AutomationProperties.SetName(line, item.ToString());
            if (club.Id is { } id)
            {
                var idBlock = new TextBlock
                {
                    Text = id.ToString(Inv),
                    FontSize = 11.5,
                    Foreground = Brush("#94A3B8"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Right,
                    Margin = new Thickness(8, 0, 0, 0),
                };
                DockPanel.SetDock(idBlock, Dock.Right);
                line.Children.Add(idBlock);
            }
            line.Children.Add(new TextBlock
            {
                Text = club.DisplayName,
                FontSize = 12.5,
                Foreground = Brush("#F1F5F9"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            return line;
        });

        var sectionOptions = ClubFieldInfo.Sections
            .Select(s => new ClubSectionOption(s, ClubFieldInfo.SectionTitle(s)))
            .ToArray();
        clubSectionList.ItemTemplate = new FuncDataTemplate<ClubSectionOption>((opt, _) =>
            opt is null
                ? null
                : new TextBlock
                {
                    Text = opt.Title,
                    FontSize = 13,
                    FontWeight = FontWeight.Medium,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 2, 14, 2),
                });
        clubSectionList.ItemsSource = sectionOptions;
        clubSectionList.SelectedIndex = 0;

        clubSearchBox.TextChanged += (_, _) =>
        {
            if (!clubUpdating) FilterClubPicker();
        };

        clubPickerList.SelectionChanged += (_, _) =>
        {
            if (clubUpdating) return;
            selectedClub = (clubPickerList.SelectedItem as ClubPickerItem)?.Club;
            if (selectedClub?.Id is { } id) lastSelectedClubId = id;
            RenderClubEditor();
            UpdateClubStatusFromDraft();
        };

        clubSectionList.SelectionChanged += (_, _) =>
        {
            if (clubUpdating) return;
            if (clubSectionList.SelectedItem is ClubSectionOption opt && opt.Section != selectedClubSection)
            {
                selectedClubSection = opt.Section;
                RenderClubEditor();
                UpdateClubStatusFromDraft();
            }
        };

        var leftGrid = new Grid
        {
            Background = Brush("#11131C"),
            RowDefinitions = new RowDefinitions
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(250)) { MinHeight = 110 },
                new RowDefinition(new GridLength(5, GridUnitType.Pixel)),
                new RowDefinition(new GridLength(1, GridUnitType.Star)) { MinHeight = 140 },
            },
        };

        var clubMassEdit = new Button { Content = "Mass Edit" };
        clubMassEdit.Click += async (_, _) => await ShowClubMassEditAsync();
        var clubSearchControls = new DockPanel();
        DockPanel.SetDock(clubMassEdit, Dock.Right); clubSearchControls.Children.Add(clubMassEdit);
        clubSearchControls.Children.Add(clubSearchBox);
        var searchBar = new Border
        {
            Background = Brush("#141722"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 8),
            Child = clubSearchControls,
        };
        Grid.SetRow(searchBar, 0);
        leftGrid.Children.Add(searchBar);

        Grid.SetRow(clubPickerList, 1);
        leftGrid.Children.Add(clubPickerList);

        var rowSplitter = new GridSplitter
        {
            Height = 5,
            ResizeDirection = GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetRow(rowSplitter, 2);
        leftGrid.Children.Add(rowSplitter);

        Grid.SetRow(clubSectionList, 3);
        leftGrid.Children.Add(clubSectionList);

        var mainSplit = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions
            {
                new ColumnDefinition(new GridLength(296)) { MinWidth = 236, MaxWidth = 460 },
                new ColumnDefinition(new GridLength(6, GridUnitType.Pixel)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 460 },
            },
        };
        Grid.SetColumn(leftGrid, 0);
        mainSplit.Children.Add(leftGrid);

        var colSplitter = new GridSplitter
        {
            Width = 6,
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetColumn(colSplitter, 1);
        mainSplit.Children.Add(colSplitter);

        var rightScroll = new ScrollViewer
        {
            Content = clubEditorPanel,
            Background = Brush("#0D0F17"),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetColumn(rightScroll, 2);
        mainSplit.Children.Add(rightScroll);

        clubsViewRoot.Child = mainSplit;
    }

    void ClearClubsScreen()
    {
        clubUpdating = true;
        loadedClubs = [];
        clubListSelectedRow.Clear();
        selectedClub = null;
        clubPickerList.ItemsSource = Array.Empty<ClubPickerItem>();
        clubEditorPanel.Children.Clear();
        clubUpdating = false;
    }

    async Task LoadClubsScreenAsync(bool forceReloadPlayers)
    {
        if (backend.State.Status != ConnectionStatus.Connected) return;
        int current = ++clubLoadGeneration;
        try
        {
            var session = backend.Session;
            var cachedClubs = !forceReloadPlayers ? clubCache : null;
            var existingPlayers = !forceReloadPlayers ? rows.OfType<PlayerRecord>().ToArray() : [];

            var (clubs, loadedPlayers, gameDate) = await Task.Run(() =>
            {
                var gd = session.ReadGameDate();
                IReadOnlyList<PlayerRecord>? freshPlayers = null;
                IReadOnlyList<ClubEntry> clubEntries;
                if (cachedClubs is not null)
                {
                    clubEntries = cachedClubs;
                }
                else
                {
                    PlayerRecord[] src;
                    if (existingPlayers.Length > 0)
                    {
                        src = existingPlayers;
                    }
                    else
                    {
                        var all = session.Players.LoadAll();
                        foreach (var p in all) p.GameDate = gd;
                        freshPlayers = all;
                        src = all.ToArray();
                    }
                    clubEntries = session.LoadClubs(src);
                }
                return (clubEntries, freshPlayers, gd);
            });

            if (current != clubLoadGeneration || !clubsActive) return;

            if (loadedPlayers is not null && rows.Count == 0)
                rows = loadedPlayers.OrderByDescending(p => p.CurrentAbility).Cast<object>().ToArray();

            clubCache = clubs;
            loadedClubs = ClubCatalog.Filter(clubs, null);

            if (gameDate is { } d)
            {
                gameDateText.Text = d.ToString("dd MMM yyyy", Inv);
                gameDateBadge.IsVisible = true;
            }
            else
            {
                gameDateBadge.IsVisible = false;
            }

            FilterClubPicker();
            UpdateConnectionBadge();
        }
        catch (Exception ex)
        {
            status.Text = ClubUserMessage(ex);
        }
    }

    void FilterClubPicker()
    {
        var filtered = ClubCatalog.Filter(loadedClubs, clubSearchBox.Text);
        var prevAddr = selectedClub?.Address;
        var prevId = selectedClub?.Id ?? lastSelectedClubId;

        ClubEntry? next = null;
        if (prevAddr is { } addr)
            next = filtered.FirstOrDefault(c => c.Address == addr);
        if (next is null && prevId is { } id)
            next = filtered.FirstOrDefault(c => c.Id == id);
        next ??= filtered.FirstOrDefault();

        var items = filtered.Select(c => new ClubPickerItem(c)).ToArray();
        var nextItem = next is null ? null : items.FirstOrDefault(i => ReferenceEquals(i.Club, next));

        clubUpdating = true;
        clubPickerList.ItemsSource = items;
        clubPickerList.SelectedItem = nextItem;
        if (nextItem is not null)
            clubPickerList.ScrollIntoView(nextItem);
        clubUpdating = false;

        if (clubsActive && backend.State.Status == ConnectionStatus.Connected)
        {
            connection.Text = filtered.Count == loadedClubs.Count
                ? $"{backend.State.Message} · {loadedClubs.Count:N0}"
                : $"{backend.State.Message} · {filtered.Count:N0} / {loadedClubs.Count:N0}";
        }

        if (!ReferenceEquals(selectedClub, next))
        {
            selectedClub = next;
            if (next?.Id is { } cid) lastSelectedClubId = cid;
        }
        RenderClubEditor();
        UpdateClubStatusFromDraft();
    }

    async Task CheckSelectedClubAsync()
    {
        if (!clubsActive || selectedClub is not { } club || busy || backend.State.Status != ConnectionStatus.Connected)
            return;
        try
        {
            ulong addr = club.Address;
            bool ok = await Task.Run(() => IsClubObjectValid(addr));
            if (!ok && clubsActive && selectedClub?.Address == addr)
            {
                clubDrafts.Clear();
                clubCache = null;
                rows = [];
                status.Text = "Club no longer available; reloading";
                await LoadClubsScreenAsync(forceReloadPlayers: true);
            }
        }
        catch (Exception ex)
        {
            status.Text = ClubUserMessage(ex);
        }
    }

    bool IsClubObjectValid(ulong clubAddress)
    {
        if (clubAddress == 0 || backend.Session.Profile.Club is not { } clubOffsets) return false;
        Span<byte> vt = stackalloc byte[8];
        return backend.Session.Memory.TryRead(clubAddress, vt)
               && BitConverter.ToUInt64(vt) == backend.Session.ModuleBase + clubOffsets.ClubVtableRva;
    }

    TeamEntry? ResolveSelectedTeam(ClubEntry club)
    {
        var ordered = ClubCatalog.OrderedTeams(club);
        if (clubSelectedTeam.TryGetValue(club.Address, out ulong savedAddr)
            && ordered.FirstOrDefault(t => t.Address == savedAddr) is { } match)
        {
            return match;
        }
        return ClubCatalog.DefaultTeam(club);
    }

    void UpdateClubButtonsAndStatus(ClubDraft draft)
    {
        if (clubSaveButton is not null) clubSaveButton.IsEnabled = draft.CanSave;
        if (clubDiscardButton is not null) clubDiscardButton.IsEnabled = draft.Count > 0 || draft.InvalidCount > 0;
        UpdateClubStatusFromDraft();
    }

    void UpdateClubStatusFromDraft()
    {
        if (!clubsActive) return;
        if (selectedClub is not { } club || !clubDrafts.TryGetValue(club.Address, out var draft))
        {
            status.Text = "";
            return;
        }
        if (draft.InvalidCount > 0)
            status.Text = $"{draft.InvalidCount} invalid value(s)";
        else if (draft.Count > 0)
            status.Text = $"{draft.Count} unsaved change(s)";
        else
            status.Text = "";
    }

    void RenderClubEditor()
    {
        clubRenderGeneration++;
        clubEditorPanel.Children.Clear();
        clubSaveButton = null;
        clubDiscardButton = null;

        if (selectedClub is not { } club || backend.State.Status != ConnectionStatus.Connected)
            return;

        var session = backend.Session;
        var profile = session.Profile;
        var access = session.ClubFields;
        var orderedTeams = ClubCatalog.OrderedTeams(club);
        var activeTeam = ResolveSelectedTeam(club);
        ulong teamAddr = activeTeam?.Address ?? 0;

        var values = access.ReadAll(club.Address, teamAddr);
        if (!clubDrafts.TryGetValue(club.Address, out var draft))
            clubDrafts[club.Address] = draft = new ClubDraft(club.Address);
        draft.AbsorbSnapshot(club.Address, teamAddr, profile, values);

        var headerDock = new DockPanel { LastChildFill = true };
        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        clubSaveButton = new Button
        {
            Content = "Save changes",
            IsEnabled = draft.CanSave,
            Classes = { "accent" },
        };
        clubSaveButton.Click += (_, _) => SaveClubDraft();

        clubDiscardButton = new Button
        {
            Content = "Discard",
            IsEnabled = draft.Count > 0 || draft.InvalidCount > 0,
        };
        clubDiscardButton.Click += (_, _) =>
        {
            clubDrafts.Remove(club.Address);
            RenderClubEditor();
            status.Text = "Changes discarded";
        };
        commands.Children.Add(clubSaveButton);
        commands.Children.Add(clubDiscardButton);
        if (backend.Freezer is not null)
        {
            var freeze = new Button { Content = "Freezer" };
            freeze.Click += async (_, _) => await ShowClubFreezerAsync(club);
            commands.Children.Add(freeze);
        }
        DockPanel.SetDock(commands, Dock.Right);
        headerDock.Children.Add(commands);

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleRow.Children.Add(new TextBlock
        {
            Text = club.Name ?? club.DisplayName,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = Brush("#F8FAFC"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var metaParts = new List<string>(2);
        if (club.Id is { } id) metaParts.Add($"ID {id.ToString(Inv)}");
        if (!string.IsNullOrWhiteSpace(club.ShortName)
            && !string.Equals(club.ShortName, club.Name, StringComparison.Ordinal))
        {
            metaParts.Add(club.ShortName);
        }
        if (metaParts.Count > 0)
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", metaParts),
                FontSize = 12.5,
                Foreground = Brush("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        headerDock.Children.Add(titleRow);

        clubEditorPanel.Children.Add(new Border
        {
            Background = Brush("#151926"),
            BorderBrush = Brush("#7C3AED"),
            BorderThickness = new Thickness(3, 1, 1, 1),
            Padding = new Thickness(14, 8),
            Child = headerDock,
        });

        if (HookClubSquadsView(club, orderedTeams, activeTeam, teamAddr)) return;

        if (selectedClubSection == ClubSection.Finances)
        {
            RenderClubFinancesSection(session, club, orderedTeams, activeTeam, teamAddr, profile, access, values, draft);
            return;
        }

        if (ClubFieldInfo.IsListSection(selectedClubSection))
        {
            RenderClubListSection(selectedClubSection, session, club, orderedTeams, activeTeam, teamAddr);
            return;
        }

        RenderClubScalarSection(selectedClubSection, club, orderedTeams, teamAddr, profile, access, values, draft);
    }

    void RenderClubScalarSection(
        ClubSection section,
        ClubEntry club,
        IReadOnlyList<TeamEntry> orderedTeams,
        ulong teamAddr,
        Fm27Editor.Core.Offsets.OffsetProfile profile,
        ClubFieldAccess access,
        IReadOnlyDictionary<ClubField, long> values,
        ClubDraft draft)
    {
        var groups = ClubSectionLayout.BuildGroups(section, profile, access, club.Address, teamAddr, values);
        if (groups.Count == 0)
            return;

        if (orderedTeams.Count > 1 && groups.Any(g => g.UsesTeamRoot))
            clubEditorPanel.Children.Add(BuildClubTeamSelector(club, orderedTeams, teamAddr));

        var cards = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var group in groups)
            cards.Children.Add(BuildClubGroupCard(group, draft));
        clubEditorPanel.Children.Add(cards);
    }

    void RenderClubFinancesSection(
        GameSession session,
        ClubEntry club,
        IReadOnlyList<TeamEntry> orderedTeams,
        TeamEntry? activeTeam,
        ulong teamAddr,
        Fm27Editor.Core.Offsets.OffsetProfile profile,
        ClubFieldAccess access,
        IReadOnlyDictionary<ClubField, long> values,
        ClubDraft draft)
    {
        var provider = GameSessionClubListProvider.Default;
        bool hasSponsors = provider.HasReader(session, ClubSection.Sponsors);
        bool hasDebts = provider.HasReader(session, ClubSection.Debts);

        if ((selectedFinanceSubSection == ClubSection.Sponsors && !hasSponsors)
            || (selectedFinanceSubSection == ClubSection.Debts && !hasDebts))
        {
            selectedFinanceSubSection = ClubSection.Finances;
        }

        if (hasSponsors || hasDebts)
        {
            var tabStrip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };

            void AddFinanceSubTab(ClubSection subSection, string title)
            {
                bool active = selectedFinanceSubSection == subSection;
                var btn = new Button
                {
                    Content = title,
                };
                if (active)
                    btn.Classes.Add("accent");
                btn.Click += (_, _) =>
                {
                    if (selectedFinanceSubSection == subSection) return;
                    selectedFinanceSubSection = subSection;
                    RenderClubEditor();
                    UpdateClubStatusFromDraft();
                };
                tabStrip.Children.Add(btn);
            }

            AddFinanceSubTab(ClubSection.Finances, "Overview");
            if (hasSponsors)
                AddFinanceSubTab(ClubSection.Sponsors, ClubFieldInfo.SectionTitle(ClubSection.Sponsors));
            if (hasDebts)
                AddFinanceSubTab(ClubSection.Debts, ClubFieldInfo.SectionTitle(ClubSection.Debts));

            clubEditorPanel.Children.Add(tabStrip);
        }

        if (selectedFinanceSubSection == ClubSection.Finances)
        {
            RenderClubScalarSection(ClubSection.Finances, club, orderedTeams, teamAddr, profile, access, values, draft);
        }
        else
        {
            RenderClubListSection(selectedFinanceSubSection, session, club, orderedTeams, activeTeam, teamAddr);
        }
    }

    Control BuildClubTeamSelector(ClubEntry club, IReadOnlyList<TeamEntry> orderedTeams, ulong teamAddr)
    {
        var teamOptions = orderedTeams.Select((t, i) => new TeamOption(t, i)).ToArray();
        var selectedOpt = teamOptions.FirstOrDefault(o => o.Team.Address == teamAddr) ?? teamOptions[0];
        var teamCombo = new ComboBox
        {
            Width = 240,
            ItemsSource = teamOptions,
            SelectedItem = selectedOpt,
        };
        teamCombo.SelectionChanged += (_, _) =>
        {
            if (teamCombo.SelectedItem is TeamOption chosen && chosen.Team.Address != teamAddr)
            {
                clubSelectedTeam[club.Address] = chosen.Team.Address;
                RenderClubEditor();
                UpdateClubStatusFromDraft();
            }
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Team",
                    FontSize = 12.5,
                    Foreground = Brush("#CBD5E1"),
                    VerticalAlignment = VerticalAlignment.Center,
                },
                teamCombo,
            },
        };
    }

    async void RenderClubListSection(
        ClubSection listSection,
        GameSession session,
        ClubEntry club,
        IReadOnlyList<TeamEntry> orderedTeams,
        TeamEntry? activeTeam,
        ulong teamAddr)
    {
        var version = clubRenderGeneration;
        if (listSection == ClubSection.Relationships)
        {
            RenderClubRelationships(session, club);
            return;
        }
        if (listSection == ClubSection.ClubVision && session.Profile.Club?.Lists?.VisionImportanceEditable == true)
        {
            if (teamAddr != 0) RenderClubVision(session, club, orderedTeams, teamAddr);
            return;
        }
        if (listSection == ClubSection.Affiliations && session.Profile.Club?.Lists?.AffiliationAnnualFee is not null)
        {
            RenderClubAffiliations(session, club);
            return;
        }
        if (listSection == ClubSection.Debts && session.Profile.Club?.FinanceLists is { MonthlyRepayment: not null, MonthlyInterestRepayment: not null })
        {
            RenderClubDebts(session, club);
            return;
        }
        if (listSection == ClubSection.Staff)
        {
            ClubListTableModel staffTable;
            Fm27Editor.Core.Staff.StaffRecord[] staffMembers;
            try
            {
                var result = await Task.Run(() =>
                {
                    var members = session.Staff.LoadAll().Where(s => s.Club?.Address == club.Address).ToArray();
                    return (Members: members, Table: ClubStaffLabels.BuildTable(members, session.ReadGameDate()));
                });
                staffMembers = result.Members;
                staffTable = result.Table;
            }
            catch (Exception ex) { if (version == clubRenderGeneration && clubsActive) status.Text = UserMessage.For(ex); return; }
            if (version != clubRenderGeneration || !clubsActive || selectedClub?.Address != club.Address || selectedClubSection != listSection) return;
            if (staffTable.Rows.Count > 0)
            {
                int selected = -1;
                var open = new Button { Content = "Open Selected", IsEnabled = false };
                open.Click += (_, _) =>
                {
                    if (selected < 0 || selected >= staffMembers.Length) return;
                    var original = staffMembers[selected];
                    try
                    {
                        var fresh = session.Staff.Reload(original.Address);
                        if (fresh is null || fresh.Id != original.Id) { status.Text = "Staff member no longer available."; return; }
                        OpenClubStaff(fresh);
                    }
                    catch (Exception ex) { status.Text = UserMessage.For(ex); }
                };
                clubEditorPanel.Children.Add(BuildClubListTable(staffTable, -1, i => { selected = i; open.IsEnabled = true; }));
                clubEditorPanel.Children.Add(open);
            }
            return;
        }
        var provider = GameSessionClubListProvider.Default;
        var playerRecords = rows.OfType<PlayerRecord>().ToArray();
        ClubListTableModel table;
        IReadOnlyList<ClubTransferOffer>? transferOffers = null;
        if (listSection == ClubSection.TransferCenter)
        {
            try
            {
                transferOffers = await Task.Run(() =>
                {
                    var staff = session.Staff.LoadAll();
                    return session.Proposals.ReadForClub(club.Address, playerRecords, staff);
                });
                table = ClubListBuilder.BuildTransferCenter(transferOffers.Select(o => new ClubTransferOfferRow(
                    o.Proposal.Id, o.PersonName, o.PersonId, o.Proposal.From, BuyerClub: o.Proposal.To,
                    Fee: o.Proposal.Value, StatusCode: (byte)o.Proposal.Status, TypeName: o.Proposal.Type.ToString())));
            }
            catch (Exception ex) { if (version == clubRenderGeneration && clubsActive) status.Text = UserMessage.For(ex); return; }
            if (version != clubRenderGeneration || !clubsActive || selectedClub?.Address != club.Address || selectedClubSection != listSection) return;
        }
        else
        {
            if (!provider.HasReader(session, listSection)) return;
            if (competitionCache is null && playerRecords.Length > 0 && provider.NeedsCompetitions(session, listSection))
                competitionCache = session.LoadCompetitions(playerRecords);
            table = provider.ReadSection(listSection, session, club, activeTeam, loadedClubs, competitionCache, playerRecords);
        }

        if (!table.HasReader)
            return;

        if (orderedTeams.Count > 1 && table.UsesTeamRoot)
            clubEditorPanel.Children.Add(BuildClubTeamSelector(club, orderedTeams, teamAddr));

        if (table.Rows.Count == 0)
            return;

        var selKey = (club.Address, listSection);
        int selectedIdx = clubListSelectedRow.TryGetValue(selKey, out int savedIdx)
            ? Math.Clamp(savedIdx, 0, table.Rows.Count - 1)
            : 0;
        clubListSelectedRow[selKey] = selectedIdx;

        Control? Detail(int index) => transferOffers is null ? BuildClubListDetailCard(table.Rows[index]) : BuildClubTransferDetail(session, transferOffers[index]);
        var detailHost = new Border
        {
            Child = Detail(selectedIdx),
        };

        clubEditorPanel.Children.Add(BuildClubListTable(table, selectedIdx, newIdx =>
        {
            clubListSelectedRow[selKey] = newIdx;
            detailHost.Child = Detail(newIdx);
        }));
        clubEditorPanel.Children.Add(detailHost);
    }

    void RenderClubVision(GameSession session, ClubEntry club, IReadOnlyList<TeamEntry> orderedTeams, ulong teamAddr)
    {
        IReadOnlyList<ClubVisionEntry> entries;
        try { entries = session.ReadClubVision(teamAddr, loadedClubs); }
        catch (Exception ex) { status.Text = UserMessage.For(ex); return; }
        if (orderedTeams.Count > 1) clubEditorPanel.Children.Add(BuildClubTeamSelector(club, orderedTeams, teamAddr));
        if (entries.Count == 0) return;
        var table = ClubListBuilder.BuildVision(entries.Select(r => new ClubVisionRow(
            r.TypeCode, r.Importance, r.StartDate, r.EndDate, r.ClubItem?.DisplayName, r.ItemIndex)), usesTeamRoot: true);
        var key = (club.Address, ClubSection.ClubVision);
        int selected = clubListSelectedRow.TryGetValue(key, out int saved) ? Math.Clamp(saved, 0, entries.Count - 1) : 0;
        clubListSelectedRow[key] = selected;
        Control Detail(int index)
        {
            var panel = new StackPanel { Spacing = 8 };
            if (BuildClubListDetailCard(table.Rows[index]) is { } existing) panel.Children.Add(existing);
            var box = new TextBox { Text = entries[index].Importance.ToString(Inv), Width = 100, HorizontalContentAlignment = HorizontalAlignment.Right };
            var apply = new Button { Content = "Apply" };
            apply.Click += (_, _) =>
            {
                if (!int.TryParse(box.Text, out int value)) { status.Text = "Enter a whole number for importance."; return; }
                try
                {
                    backend.Edits.Execute(() => session.SetClubVisionImportance(teamAddr, entries[index], value));
                    RenderClubEditor();
                    status.Text = "Saved. Continue in FM to see updates.";
                }
                catch (Exception ex) { status.Text = UserMessage.For(ex); }
            };
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                new TextBlock { Text = "Importance", VerticalAlignment = VerticalAlignment.Center }, box, apply
            } });
            return panel;
        }
        var detail = new Border { Child = Detail(selected) };
        clubEditorPanel.Children.Add(BuildClubListTable(table, selected, index => { clubListSelectedRow[key] = index; detail.Child = Detail(index); }));
        clubEditorPanel.Children.Add(detail);
    }

    void RenderClubAffiliations(GameSession session, ClubEntry club)
    {
        IReadOnlyList<ClubAffiliationEntry> entries;
        try { entries = session.ReadAffiliations(club.Address); }
        catch (Exception ex) { status.Text = UserMessage.For(ex); return; }
        if (entries.Count == 0) return;
        var table = ClubListBuilder.BuildAffiliations(entries.Select(r => new ClubAffiliationRow(
            r.ParentClub?.DisplayName, r.FeederClub?.DisplayName, r.TypeCode, r.StartDate,
            r.ParentClub?.Id, r.FeederClub?.Id, EndDate: r.EndDate, AnnualFee: r.AnnualFee)));
        var key = (club.Address, ClubSection.Affiliations);
        int selected = clubListSelectedRow.TryGetValue(key, out int saved) ? Math.Clamp(saved, 0, entries.Count - 1) : 0;
        clubListSelectedRow[key] = selected;
        Control Detail(int index)
        {
            var panel = new StackPanel { Spacing = 8 };
            if (BuildClubListDetailCard(table.Rows[index]) is { } existing) panel.Children.Add(existing);
            if (entries[index].ScoutingShared is { } scoutingShared)
            {
                var scoutingBox = new CheckBox { Content = "Scouting Knowledge Shared", IsChecked = scoutingShared, VerticalAlignment = VerticalAlignment.Center };
                var applyScouting = new Button { Content = "Apply" };
                applyScouting.Click += (_, _) =>
                {
                    try
                    {
                        backend.Edits.Execute(() => session.SetAffiliationScoutingShared(club.Address, entries[index], scoutingBox.IsChecked == true));
                        RenderClubEditor();
                        status.Text = "Saved. Continue in FM to see updates.";
                    }
                    catch (Exception ex) { status.Text = UserMessage.For(ex); }
                };
                panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { scoutingBox, applyScouting } });
            }
            if (entries[index].PlayersOnLoan is { } playersOnLoan)
            {
                var loanBox = new CheckBox { Content = "Players Go On Loan", IsChecked = playersOnLoan, VerticalAlignment = VerticalAlignment.Center };
                var applyLoan = new Button { Content = "Apply" };
                applyLoan.Click += (_, _) =>
                {
                    try
                    {
                        backend.Edits.Execute(() => session.SetAffiliationPlayersOnLoan(club.Address, entries[index], loanBox.IsChecked == true));
                        RenderClubEditor();
                        status.Text = "Saved. Continue in FM to see updates.";
                    }
                    catch (Exception ex) { status.Text = UserMessage.For(ex); }
                };
                panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { loanBox, applyLoan } });
            }
            if (entries[index].FirstOption is { } firstOption)
            {
                var firstOptionBox = new CheckBox { Content = "First Option To Buy", IsChecked = firstOption, VerticalAlignment = VerticalAlignment.Center };
                var applyOption = new Button { Content = "Apply" };
                applyOption.Click += (_, _) =>
                {
                    try
                    {
                        backend.Edits.Execute(() => session.SetAffiliationFirstOption(club.Address, entries[index], firstOptionBox.IsChecked == true));
                        RenderClubEditor();
                        status.Text = "Saved. Continue in FM to see updates.";
                    }
                    catch (Exception ex) { status.Text = UserMessage.For(ex); }
                };
                panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { firstOptionBox, applyOption } });
            }
            if (entries[index].PlayFriendlies is { } playFriendlies)
            {
                var box = new CheckBox { Content = "Play Friendlies", IsChecked = playFriendlies, VerticalAlignment = VerticalAlignment.Center };
                var applyFriendlies = new Button { Content = "Apply" };
                applyFriendlies.Click += (_, _) =>
                {
                    try
                    {
                        backend.Edits.Execute(() => session.SetAffiliationPlayFriendlies(club.Address, entries[index], box.IsChecked == true));
                        RenderClubEditor();
                        status.Text = "Saved. Continue in FM to see updates.";
                    }
                    catch (Exception ex) { status.Text = UserMessage.For(ex); }
                };
                panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { box, applyFriendlies } });
            }
            if (entries[index].ParentWagePercent is { } wage)
            {
                var wageBox = new TextBox { Text = wage.ToString(Inv), Width = 130, HorizontalContentAlignment = HorizontalAlignment.Right };
                var applyWage = new Button { Content = "Apply" };
                applyWage.Click += (_, _) =>
                {
                    if (!int.TryParse(wageBox.Text, out int value)) { status.Text = "Enter a whole number for parent wage percentage."; return; }
                    try
                    {
                        backend.Edits.Execute(() => session.SetAffiliationParentWagePercent(club.Address, entries[index], value));
                        RenderClubEditor();
                        status.Text = "Saved. Continue in FM to see updates.";
                    }
                    catch (Exception ex) { status.Text = UserMessage.For(ex); }
                };
                panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
                {
                    new TextBlock { Text = "% Loan Wage Paid By Parent", VerticalAlignment = VerticalAlignment.Center }, wageBox, applyWage
                } });
            }
            if (entries[index].FriendlyChance is { } chance)
            {
                var chanceBox = new TextBox { Text = chance.ToString(Inv), Width = 130, HorizontalContentAlignment = HorizontalAlignment.Right };
                var applyChance = new Button { Content = "Apply" };
                applyChance.Click += (_, _) =>
                {
                    if (!int.TryParse(chanceBox.Text, out int value)) { status.Text = "Enter a whole number for chance of friendly."; return; }
                    try
                    {
                        backend.Edits.Execute(() => session.SetAffiliationFriendlyChance(club.Address, entries[index], value));
                        RenderClubEditor();
                        status.Text = "Saved. Continue in FM to see updates.";
                    }
                    catch (Exception ex) { status.Text = UserMessage.For(ex); }
                };
                panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
                {
                    new TextBlock { Text = "% Chance Of Friendly", VerticalAlignment = VerticalAlignment.Center }, chanceBox, applyChance
                } });
            }
            if (session.Profile.Club?.Lists?.AffiliationStartEditable == true && entries[index].StartDate is { } start)
            {
                var startBox = new TextBox { Text = start.ToString("yyyy-MM-dd", Inv), Width = 130 };
                var applyStart = new Button { Content = "Apply" };
                applyStart.Click += (_, _) =>
                {
                    if (!DateOnly.TryParseExact(startBox.Text, "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var value))
                    { status.Text = "Enter date as yyyy-MM-dd."; return; }
                    try
                    {
                        backend.Edits.Execute(() => session.SetAffiliationStartDate(club.Address, entries[index], value));
                        RenderClubEditor();
                        status.Text = "Saved. Continue in FM to see updates.";
                    }
                    catch (Exception ex) { status.Text = UserMessage.For(ex); }
                };
                panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
                {
                    new TextBlock { Text = "Start Date", VerticalAlignment = VerticalAlignment.Center }, startBox, applyStart
                } });
            }
            if (entries[index].EndDate is { } end)
            {
                var endBox = new TextBox { Text = end.ToString("yyyy-MM-dd", Inv), Width = 130 };
                var applyEnd = new Button { Content = "Apply" };
                applyEnd.Click += (_, _) =>
                {
                    if (!DateOnly.TryParseExact(endBox.Text, "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var value))
                    { status.Text = "Enter date as yyyy-MM-dd."; return; }
                    try
                    {
                        backend.Edits.Execute(() => session.SetAffiliationEndDate(club.Address, entries[index], value));
                        RenderClubEditor();
                        status.Text = "Saved. Continue in FM to see updates.";
                    }
                    catch (Exception ex) { status.Text = UserMessage.For(ex); }
                };
                panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
                {
                    new TextBlock { Text = "End Date", VerticalAlignment = VerticalAlignment.Center }, endBox, applyEnd
                } });
            }
            if (entries[index].AnnualFee is not { } fee) return panel;
            var feeBox = new TextBox { Text = fee.ToString(Inv), Width = 130, HorizontalContentAlignment = HorizontalAlignment.Right };
            var apply = new Button { Content = "Apply" };
            apply.Click += (_, _) =>
            {
                if (!int.TryParse(feeBox.Text, out int value)) { status.Text = "Enter a whole number for annual fee."; return; }
                try
                {
                    backend.Edits.Execute(() => session.SetAffiliationAnnualFee(club.Address, entries[index], value));
                    RenderClubEditor();
                    status.Text = "Saved. Continue in FM to see updates.";
                }
                catch (Exception ex) { status.Text = UserMessage.For(ex); }
            };
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                new TextBlock { Text = "Annual Fee", VerticalAlignment = VerticalAlignment.Center }, feeBox, apply
            } });
            return panel;
        }
        var detail = new Border { Child = Detail(selected) };
        clubEditorPanel.Children.Add(BuildClubListTable(table, selected, index =>
        {
            clubListSelectedRow[key] = index;
            detail.Child = Detail(index);
        }));
        clubEditorPanel.Children.Add(detail);
    }

    void RenderClubDebts(GameSession session, ClubEntry club)
    {
        IReadOnlyList<ClubDebtEntry> debts;
        try { debts = session.ReadDebts(club.Address); }
        catch (Exception ex) { status.Text = UserMessage.For(ex); return; }
        if (debts.Count == 0) return;
        var table = ClubListBuilder.BuildDebts(debts.Select(d => new ClubDebtRow(d.SourceCode, d.OriginalDebt, d.StartDate, d.EndDate)));
        var key = (club.Address, ClubSection.Debts);
        int selected = clubListSelectedRow.TryGetValue(key, out int saved) ? Math.Clamp(saved, 0, debts.Count - 1) : 0;
        clubListSelectedRow[key] = selected;
        var detail = new Border { Child = BuildClubDebtDetail(session, club, debts[selected]) };
        clubEditorPanel.Children.Add(BuildClubListTable(table, selected, index =>
        {
            clubListSelectedRow[key] = index;
            detail.Child = BuildClubDebtDetail(session, club, debts[index]);
        }));
        clubEditorPanel.Children.Add(detail);
    }

    sealed record DebtSourceOption(byte Code, string Label)
    {
        public override string ToString() => Label;
    }

    Control BuildClubDebtDetail(GameSession session, ClubEntry club, ClubDebtEntry debt)
    {
        if (debt.MonthlyRepayment is not { } amount || debt.MonthlyInterestRepayment is not { } interest) return new Border();
        var repaymentBox = new TextBox { Text = amount.ToString(Inv), Width = 130, HorizontalContentAlignment = HorizontalAlignment.Right };
        var interestBox = new TextBox { Text = interest.ToString(Inv), Width = 130, HorizontalContentAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Apply" };
        save.Click += (_, _) =>
        {
            if (!int.TryParse(repaymentBox.Text, out int repayment) || !int.TryParse(interestBox.Text, out int interestValue))
            { status.Text = "Enter whole numbers for debt payments."; return; }
            try
            {
                backend.Edits.Execute(() => session.SetDebtPayments(club.Address, debt, repayment, interestValue));
                RenderClubEditor();
                status.Text = "Saved. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        var panel = new StackPanel { Spacing = 8 };
        if (session.Profile.Club?.FinanceLists?.DebtOriginalEditable == true)
        {
            var originalBox = new TextBox { Text = debt.OriginalDebt.ToString(Inv), Width = 130, HorizontalContentAlignment = HorizontalAlignment.Right };
            var applyOriginal = new Button { Content = "Apply" };
            applyOriginal.Click += (_, _) =>
            {
                if (!int.TryParse(originalBox.Text, out int value)) { status.Text = "Enter a whole number for original debt."; return; }
                try
                {
                    backend.Edits.Execute(() => session.SetOriginalDebt(club.Address, debt, value));
                    RenderClubEditor();
                    status.Text = "Saved. Continue in FM to see updates.";
                }
                catch (Exception ex) { status.Text = UserMessage.For(ex); }
            };
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                new TextBlock { Text = "Original Debt", VerticalAlignment = VerticalAlignment.Center }, originalBox, applyOriginal
            } });
        }
        if (session.Profile.Club?.FinanceLists?.DebtDatesEditable == true && debt.StartDate is { } started && debt.EndDate is { } ended)
        {
            var startBox = new TextBox { Text = started.ToString("yyyy-MM-dd", Inv), Width = 120 };
            var endBox = new TextBox { Text = ended.ToString("yyyy-MM-dd", Inv), Width = 120 };
            var applyDates = new Button { Content = "Apply" };
            applyDates.Click += (_, _) =>
            {
                if (!DateOnly.TryParseExact(startBox.Text, "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var start)
                    || !DateOnly.TryParseExact(endBox.Text, "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var end))
                { status.Text = "Enter dates as yyyy-MM-dd."; return; }
                try
                {
                    backend.Edits.Execute(() => session.SetDebtDates(club.Address, debt, start, end));
                    RenderClubEditor();
                    status.Text = "Saved. Continue in FM to see updates.";
                }
                catch (Exception ex) { status.Text = UserMessage.For(ex); }
            };
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                new TextBlock { Text = "Start", VerticalAlignment = VerticalAlignment.Center }, startBox,
                new TextBlock { Text = "End", VerticalAlignment = VerticalAlignment.Center }, endBox, applyDates
            } });
        }
        if (session.Profile.Club?.FinanceLists?.DebtSourceEditable == true && ClubDebtWriter.SupportedSource(debt.SourceCode))
        {
            var options = ClubListLabels.DebtSources.Select(pair => new DebtSourceOption(pair.Key, pair.Value)).ToArray();
            var source = new ComboBox { ItemsSource = options, SelectedItem = options.First(o => o.Code == debt.SourceCode), Width = 300 };
            var applySource = new Button { Content = "Apply" };
            applySource.Click += (_, _) =>
            {
                if (source.SelectedItem is not DebtSourceOption chosen) return;
                try
                {
                    backend.Edits.Execute(() => session.SetDebtSource(club.Address, debt, chosen.Code));
                    RenderClubEditor();
                    status.Text = "Saved. Continue in FM to see updates.";
                }
                catch (Exception ex) { status.Text = UserMessage.For(ex); }
            };
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                new TextBlock { Text = "Source", VerticalAlignment = VerticalAlignment.Center }, source, applySource
            } });
        }
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
        {
            new TextBlock { Text = "Monthly Repayment", VerticalAlignment = VerticalAlignment.Center }, repaymentBox,
            new TextBlock { Text = "Monthly Interest Repayment", VerticalAlignment = VerticalAlignment.Center }, interestBox, save
        } });
        if (debt.InterestOnly is { } interestOnly)
        {
            var check = new CheckBox { Content = "Interest-only payment", IsChecked = interestOnly, VerticalAlignment = VerticalAlignment.Center };
            var apply = new Button { Content = "Apply" };
            apply.Click += (_, _) =>
            {
                try
                {
                    backend.Edits.Execute(() => session.SetDebtInterestOnly(club.Address, debt, check.IsChecked == true));
                    RenderClubEditor();
                    status.Text = "Saved. Continue in FM to see updates.";
                }
                catch (Exception ex) { status.Text = UserMessage.For(ex); }
            };
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { check, apply } });
        }
        return panel;
    }

    void RenderClubRelationships(GameSession session, ClubEntry club)
    {
        IReadOnlyList<ClubRelationship> ordered;
        try { ordered = ClubRelationshipLabels.Ordered(session.ReadClubRelationships(club.Address)); }
        catch (Exception ex) { status.Text = UserMessage.For(ex); return; }
        if (ordered.Count == 0) return;
        var key = (club.Address, ClubSection.Relationships);
        int selected = clubListSelectedRow.TryGetValue(key, out int saved) ? Math.Clamp(saved, 0, ordered.Count - 1) : 0;
        clubListSelectedRow[key] = selected;
        var detailHost = new Border { Child = BuildClubRelationshipDetail(session, club, ordered[selected]) };
        clubEditorPanel.Children.Add(BuildClubListTable(ClubRelationshipLabels.BuildTable(ordered), selected, index =>
        {
            clubListSelectedRow[key] = index;
            detailHost.Child = BuildClubRelationshipDetail(session, club, ordered[index]);
        }));
        clubEditorPanel.Children.Add(detailHost);
    }

    Control BuildClubRelationshipDetail(GameSession session, ClubEntry club, ClubRelationship row)
    {
        if (row.Kind == ClubRelationshipKind.Other) return new Border();
        var value = new TextBox { Text = row.Value.ToString(Inv), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = row.Name ?? "", VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 });
        panel.Children.Add(new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(value);
        ComboBox? reason = null;
        if (row.Kind != ClubRelationshipKind.DislikesPerson)
        {
            reason = new ComboBox { ItemsSource = ClubRelationshipLabels.Reasons(row.Kind), SelectedIndex = Math.Min(row.Reason, ClubRelationshipLabels.Reasons(row.Kind).Count - 1), Width = 170 };
            panel.Children.Add(new TextBlock { Text = "Reason", VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(reason);
        }
        CheckBox? permanent = null;
        if (row.Kind != ClubRelationshipKind.Rival)
        {
            permanent = new CheckBox { Content = "Permanent", IsChecked = row.Permanent, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(permanent);
        }
        var save = new Button { Content = "Save changes" };
        save.Click += (_, _) =>
        {
            if (!int.TryParse(value.Text, out int v) || v is < 1 or > 100) { status.Text = "Value must be 1 to 100"; return; }
            try
            {
                backend.Edits.Execute(() => session.SetClubRelationship(club.Address, row,
                    reason is { SelectedIndex: >= 0 } r ? r.SelectedIndex : null, permanent?.IsChecked, v));
                RenderClubEditor();
                status.Text = "Saved. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        panel.Children.Add(save);
        return panel;
    }

    Control BuildClubTransferDetail(GameSession session, ClubTransferOffer offer)
    {
        var value = new TextBox { Text = offer.Proposal.Value.ToString(Inv), Width = 150, HorizontalContentAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Save changes" };
        save.Click += (_, _) =>
        {
            if (!int.TryParse(value.Text, out int fee) || fee < 0) { status.Text = "Enter a valid transfer fee"; return; }
            try
            {
                backend.Edits.Execute(() => session.ProposalWriter.SetValue(offer.Proposal, fee));
                RenderClubEditor();
                status.Text = "Saved. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
        {
            new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center }, value, save
        } };
    }

    Control BuildClubListTable(ClubListTableModel table, int selectedIdx, Action<int> onRowSelected)
    {
        var headerGrid = new Grid
        {
            ColumnDefinitions = CreateClubListColumns(table.Columns),
        };
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            var colHeader = new TextBlock
            {
                Text = col.Header,
                FontSize = 11.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#CBD5E1"),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = col.RightAlign ? TextAlignment.Right : TextAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
            };
            Grid.SetColumn(colHeader, i);
            headerGrid.Children.Add(colHeader);
        }

        // ListBoxItem has Padding(8,4,8,4); row Margin(4,0,16,0) matches header Padding(12,6,24,6) and clears the vertical ScrollBar.
        var listBox = new ListBox
        {
            Background = Brush("#151926"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MaxHeight = 300,
            SelectionMode = SelectionMode.Single,
            ItemsSource = table.Rows,
            ItemTemplate = new FuncDataTemplate<ClubListRowModel>((row, _) =>
            {
                if (row is null) return null;
                var grid = new Grid
                {
                    ColumnDefinitions = CreateClubListColumns(table.Columns),
                    Margin = new Thickness(4, 0, 16, 0),
                };
                Avalonia.Automation.AutomationProperties.SetName(grid, row.AutomationName);
                int count = Math.Min(table.Columns.Count, row.Cells.Count);
                for (int i = 0; i < count; i++)
                {
                    var cell = row.Cells[i];
                    var cellView = BuildClubListCellView(cell, i == 0 ? 0 : 8);
                    Grid.SetColumn(cellView, i);
                    grid.Children.Add(cellView);
                }
                return grid;
            }),
        };
        listBox.SelectedIndex = selectedIdx;
        listBox.SelectionChanged += (_, _) =>
        {
            if (clubUpdating) return;
            int idx = listBox.SelectedIndex;
            if (idx >= 0 && idx < table.Rows.Count)
                onRowSelected(idx);
        };

        return new Border
        {
            Background = Brush("#151926"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    new Border
                    {
                        Background = Brush("#1C2032"),
                        BorderBrush = Brush("#7C3AED"),
                        BorderThickness = new Thickness(0, 0, 0, 2),
                        Padding = new Thickness(12, 6, 24, 6),
                        Child = headerGrid,
                    },
                    listBox,
                },
            },
        };
    }

    Control BuildClubListCellView(ClubListCell cell, double leftMargin)
    {
        if (cell.SwatchArgb is { } argb)
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(leftMargin, 0, 0, 0),
                Children =
                {
                    new Border
                    {
                        Width = 14,
                        Height = 14,
                        BorderBrush = Brush("#282E42"),
                        BorderThickness = new Thickness(1),
                        Background = SwatchBrush(argb),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = cell.Text,
                        FontSize = 12.5,
                        Foreground = Brush("#F1F5F9"),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                },
            };
        }

        return new TextBlock
        {
            Text = cell.Text,
            FontSize = 12.5,
            Foreground = Brush("#F1F5F9"),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = cell.RightAlign ? TextAlignment.Right : TextAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(leftMargin, 0, 0, 0),
        };
    }

    Control? BuildClubListDetailCard(ClubListRowModel row)
    {
        if (row.Details.Count == 0)
            return null;

        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(new Border
        {
            Background = Brush("#1C2032"),
            BorderBrush = Brush("#7C3AED"),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(12, 7),
            Child = new TextBlock
            {
                Text = "DETAILS",
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Brush("#F1F5F9"),
            },
        });

        int rowIndex = 0;
        foreach (var field in row.Details)
        {
            var line = new DockPanel { Height = 28, LastChildFill = true };
            Control rightView = field.SwatchArgb is { } argb
                ? new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new Border
                        {
                            Width = 14,
                            Height = 14,
                            BorderBrush = Brush("#282E42"),
                            BorderThickness = new Thickness(1),
                            Background = SwatchBrush(argb),
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        new TextBlock
                        {
                            Text = field.Value,
                            FontSize = 12.5,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Brush("#F8FAFC"),
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    },
                }
                : new TextBlock
                {
                    Text = field.Value,
                    FontSize = 12.5,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush("#F8FAFC"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Right,
                };

            DockPanel.SetDock(rightView, Dock.Right);
            line.Children.Add(rightView);
            line.Children.Add(new TextBlock
            {
                Text = field.Label,
                FontSize = 12.5,
                Foreground = Brush("#CBD5E1"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 12, 0),
            });

            panel.Children.Add(new Border
            {
                Background = rowIndex++ % 2 == 1 ? Brush("#121520") : Brushes.Transparent,
                Padding = new Thickness(12, 2, 12, 2),
                Child = line,
            });
        }

        return new Border
        {
            MaxWidth = 540,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brush("#151926"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(1),
            Child = panel,
        };
    }

    static ColumnDefinitions CreateClubListColumns(IReadOnlyList<ClubListColumn> columns)
    {
        var defs = new ColumnDefinitions();
        foreach (var col in columns)
            defs.Add(new ColumnDefinition(new GridLength(col.Width, col.IsStar ? GridUnitType.Star : GridUnitType.Pixel)));
        return defs;
    }

    Control BuildClubGroupCard(VisibleClubGroup group, ClubDraft draft)
    {
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(new Border
        {
            Background = Brush("#1C2032"),
            BorderBrush = Brush("#7C3AED"),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(12, 7),
            Margin = new Thickness(0, 0, 0, 2),
            Child = new TextBlock
            {
                Text = group.Title.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Brush("#F1F5F9"),
            },
        });

        TextBlock? distributionTotalText = null;
        var distFields = group.Title == "Distribution"
            ? group.Fields.Where(f => ClubSectionLayout.SupporterDistributionFields.Contains(f.Field)).ToArray()
            : [];

        void RefreshDistributionTotal()
        {
            if (distributionTotalText is null) return;
            long sum = distFields.Sum(f => draft.Value(f.Target));
            distributionTotalText.Text = $"{sum.ToString(Inv)}%";
            distributionTotalText.Foreground = Brush(sum is 100 or 0 ? "#F1F5F9" : "#F59E0B");
        }

        int rowIndex = 0;
        foreach (var field in group.Fields)
        {
            var rowControl = BuildClubFieldRow(field, draft, RefreshDistributionTotal);
            panel.Children.Add(new Border
            {
                Background = rowIndex++ % 2 == 1 ? Brush("#121520") : Brushes.Transparent,
                Padding = new Thickness(12, 2, 10, 2),
                Child = rowControl,
            });
        }

        if (distFields.Length == ClubSectionLayout.SupporterDistributionFields.Count)
        {
            distributionTotalText = new TextBlock
            {
                FontSize = 12.5,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
            };
            RefreshDistributionTotal();

            var totalLine = new DockPanel { Height = 28, LastChildFill = true };
            DockPanel.SetDock(distributionTotalText, Dock.Right);
            totalLine.Children.Add(distributionTotalText);
            totalLine.Children.Add(new TextBlock
            {
                Text = "Total",
                FontSize = 12.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            panel.Children.Add(new Border
            {
                BorderBrush = Brush("#252A3D"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 2, 10, 2),
                Child = totalLine,
            });
        }

        return new Border
        {
            Width = 420,
            Margin = new Thickness(0, 0, 12, 12),
            Background = Brush("#151926"),
            BorderBrush = Brush("#252A3D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = panel,
        };
    }

    Control BuildClubFieldRow(VisibleClubField field, ClubDraft draft, Action? onChanged)
    {
        var line = new DockPanel { Height = 29, LastChildFill = true };
        var label = new TextBlock
        {
            Text = field.Meta.Label,
            FontSize = 12.5,
            Foreground = Brush("#CBD5E1"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 10, 0),
        };

        var target = field.Target;
        long currentVal = draft.Value(target);

        void StageValid(long val)
        {
            draft.Stage(field, val);
            onChanged?.Invoke();
            UpdateClubButtonsAndStatus(draft);
        }

        void MarkInvalid(bool invalid)
        {
            draft.SetInvalid(target, invalid);
            UpdateClubButtonsAndStatus(draft);
        }

        if (field.Kind == ClubDisplayKind.Bool)
        {
            var check = new CheckBox
            {
                IsChecked = currentVal != 0,
                MinWidth = 0,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            check.IsCheckedChanged += (_, _) =>
            {
                if (!clubUpdating)
                    StageValid(check.IsChecked == true ? 1 : 0);
            };
            DockPanel.SetDock(check, Dock.Right);
            line.Children.Add(check);
            line.Children.Add(label);
            return line;
        }

        if (field.Kind == ClubDisplayKind.Date)
        {
            var box = new TextBox
            {
                Text = ClubFieldInfo.FormatValue(currentVal, ClubDisplayKind.Date),
                Width = 106,
                FontSize = 12.5,
                FontWeight = FontWeight.Bold,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                Background = Brush("#0E1018"),
                BorderBrush = Brush("#282E42"),
                Foreground = Brush("#F8FAFC"),
            };
            box.TextChanged += (_, _) =>
            {
                if (clubUpdating) return;
                bool ok = ClubFieldInfo.TryParseDate(box.Text, out long parsed);
                box.BorderBrush = Brush(ok ? "#282E42" : "#B91C1C");
                if (ok) StageValid(parsed);
                else MarkInvalid(true);
            };
            DockPanel.SetDock(box, Dock.Right);
            line.Children.Add(box);
            line.Children.Add(label);
            return line;
        }

        if (field.Kind == ClubDisplayKind.Colour)
        {
            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(3),
                BorderBrush = Brush("#282E42"),
                BorderThickness = new Thickness(1),
                Background = SwatchBrush(currentVal),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var box = new TextBox
            {
                Text = ClubFieldInfo.FormatValue(currentVal, ClubDisplayKind.Colour),
                Width = 96,
                FontSize = 12.5,
                FontWeight = FontWeight.Bold,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                Background = Brush("#0E1018"),
                BorderBrush = Brush("#282E42"),
                Foreground = Brush("#F8FAFC"),
            };
            long originalVal = draft.OriginalValue(target);
            box.TextChanged += (_, _) =>
            {
                if (clubUpdating) return;
                bool ok = ClubFieldInfo.TryParseColour(box.Text, originalVal, out long parsed);
                box.BorderBrush = Brush(ok ? "#282E42" : "#B91C1C");
                if (ok)
                {
                    swatch.Background = SwatchBrush(parsed);
                    StageValid(parsed);
                }
                else
                {
                    MarkInvalid(true);
                }
            };
            var colourPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { swatch, box },
            };
            DockPanel.SetDock(colourPanel, Dock.Right);
            line.Children.Add(colourPanel);
            line.Children.Add(label);
            return line;
        }

        if (field.Meta.Options is { Count: > 0 })
        {
            long originalVal = draft.OriginalValue(target);
            var items = ClubFieldInfo.OptionsFor(field.Meta, originalVal, currentVal);
            var combo = new ComboBox
            {
                Width = 224,
                FontSize = 12.5,
                ItemsSource = items,
                SelectedItem = items.FirstOrDefault(i => i.Value == currentVal),
            };
            combo.SelectionChanged += (_, _) =>
            {
                if (clubUpdating) return;
                if (combo.SelectedItem is ClubCodeOption chosen)
                    StageValid(chosen.Value);
            };
            DockPanel.SetDock(combo, Dock.Right);
            line.Children.Add(combo);
            line.Children.Add(label);
            return line;
        }

        double inputWidth = field.Kind switch
        {
            ClubDisplayKind.Rating20 or ClubDisplayKind.Percent or ClubDisplayKind.Rating100 => 56,
            ClubDisplayKind.PlainInt => 78,
            ClubDisplayKind.FormattedInt => 118,
            _ => 136,
        };
        string format = field.Kind is ClubDisplayKind.FormattedInt or ClubDisplayKind.Money ? "N0" : "0";
        decimal increment = field.Kind == ClubDisplayKind.Money ? 10_000 : 1;

        var numeric = new NumericUpDown
        {
            Minimum = field.Min,
            Maximum = field.Max,
            Increment = increment,
            Value = currentVal,
            FormatString = format,
            ShowButtonSpinner = false,
            Width = inputWidth,
            FontSize = 12.5,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Background = Brush("#0E1018"),
            BorderBrush = Brush("#282E42"),
            Foreground = field.Kind == ClubDisplayKind.Rating20
                ? RatingColor((int)Math.Clamp(currentVal, 1, 20))
                : Brush("#F8FAFC"),
        };
        numeric.ValueChanged += (_, e) =>
        {
            if (clubUpdating || e.OldValue == e.NewValue) return;
            if (e.NewValue is null)
            {
                numeric.BorderBrush = Brush("#B91C1C");
                MarkInvalid(true);
                return;
            }
            numeric.BorderBrush = Brush("#282E42");
            long value = Math.Clamp((long)Math.Round(e.NewValue.Value), field.Min, field.Max);
            StageValid(value);
            if (field.Kind == ClubDisplayKind.Rating20)
                numeric.Foreground = RatingColor((int)Math.Clamp(value, 1, 20));
        };

        Control rightControl = field.Kind == ClubDisplayKind.Percent
            ? new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    numeric,
                    new TextBlock
                    {
                        Text = "%",
                        Width = 12,
                        FontSize = 12,
                        Foreground = Brush("#94A3B8"),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            }
            : numeric;

        DockPanel.SetDock(rightControl, Dock.Right);
        line.Children.Add(rightControl);
        line.Children.Add(label);
        return line;
    }

    void SaveClubDraft()
    {
        if (selectedClub is not { } club || !clubDrafts.TryGetValue(club.Address, out var draft) || !draft.CanSave)
            return;

        ulong teamAddr = ResolveSelectedTeam(club)?.Address ?? 0;
        int total = draft.Count;
        var access = backend.Session.ClubFields;
        try
        {
            int saved = draft.Save(backend.Edits, access, target => backend.AcceptFrozenClubEdit(club, target.Field));
            clubDrafts.Remove(club.Address);
            RenderClubEditor();
            status.Text = $"Saved {saved} change(s). Continue in FM to see updates.";
        }
        catch (Exception ex)
        {
            int saved = total - draft.Count;
            try
            {
                var fresh = access.ReadAll(club.Address, teamAddr);
                if (fresh.Count == 0 && !IsClubObjectValid(club.Address))
                {
                    clubDrafts.Remove(club.Address);
                    status.Text = "Club no longer available; reloading";
                    _ = LoadClubsScreenAsync(forceReloadPlayers: true);
                    return;
                }
                draft.Rebase(club.Address, teamAddr, backend.Session.Profile, fresh);
                RenderClubEditor();
                status.Text = saved > 0
                    ? $"{saved} saved. {ClubUserMessage(ex)} Review remaining changes."
                    : ClubUserMessage(ex);
            }
            catch (Exception reloadError)
            {
                status.Text = $"{saved} saved. {ClubUserMessage(reloadError)}";
            }
        }
    }

    static IBrush SwatchBrush(long raw)
    {
        var (a, r, g, b) = ClubFieldInfo.SwatchArgb(raw);
        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }

    static string ClubUserMessage(Exception error) => error switch
    {
        StaleObjectException => "Club data changed in game; reloaded",
        _ => UserMessage.For(error),
    };
}

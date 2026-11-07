using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Offsets;
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

    public sealed class ClubPickerItem(ClubEntry club)
    {
        public ClubEntry Club { get; } = club;
        public int? Id => Club.Id;
        public string Name => Club.Name ?? Club.DisplayName;
        public long? Balance { get; set; }
        public long? Reputation { get; set; }
        public override string ToString() => $"{Club.DisplayName}, {Id}";
    }

    readonly ClubsNavItem clubsNavItem = new();
    readonly Border clubsViewRoot = new() { IsVisible = false };
    readonly TextBox clubSearchBox = new() { Watermark = "Club or ID" };
    readonly DataGrid clubPickerList = new()
    {
        IsReadOnly = true,
        AutoGenerateColumns = false,
        CanUserSortColumns = true,
        CanUserResizeColumns = true,
        SelectionMode = DataGridSelectionMode.Single,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Brush("#1D2130"),
        RowHeight = 26,
        Background = Brush("#11131C"),
        BorderThickness = new Thickness(0),
    };
    readonly ListBox clubSectionList = SectionHost.CreateRailListBox(stretchVertically: false);
    readonly ContentControl clubHeaderHost = new() { Margin = new Thickness(12, 9, 12, 8) };
    readonly StackPanel clubEditorPanel = new() { Spacing = 10, Margin = new Thickness(12, 0, 12, 12) };
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
    Control clubDetailRoot = new Panel();
    IReadOnlyList<ClubPickerItem> clubPickerSource = [];
    IReadOnlyDictionary<ulong, (long? Balance, long? Reputation)> clubPickerStats = new Dictionary<ulong, (long?, long?)>();

    /// <summary>Selects a club in the full list (no filter) so the module screen and the tab agree.</summary>
    void SelectClubInList(ClubEntry club)
    {
        var item = clubPickerSource.FirstOrDefault(i => i.Club.Address == club.Address || ReferenceEquals(i.Club, club));
        if (item is not null)
        {
            clubUpdating = true;
            clubPickerList.SelectedItem = item;
            clubPickerList.ScrollIntoView(item, null);
            clubUpdating = false;
            selectedClub = item.Club;
        }
        else
        {
            // The list may not be loaded yet; the tab still has to show the club the search picked.
            selectedClub = club;
        }
        if (selectedClub?.Id is { } id) lastSelectedClubId = id;
        RenderClubEditor();
        UpdateClubStatusFromDraft();
    }


    void RegisterClubsView()
    {
        ParseClubStartupArgs(out bool startOnClubs, out int? startupClubId);
        if (startupClubId is not null)
            lastSelectedClubId = startupClubId;

        BuildClubsLayout();

        AddModuleTab("Clubs", clubsViewRoot, clubsNavItem);

        modules.ItemsSource = (object[])[.. Modules.Registered, clubsNavItem];

        modules.SelectionChanged += async (_, _) =>
        {
            bool toClubs = ReferenceEquals(modules.SelectedItem, clubsNavItem);
            if (toClubs == clubsActive) return;

            clubsActive = toClubs;
            status.Text = "";

            if (clubsActive)
            {
                if (backend.State.Status == ConnectionStatus.Connected)
                {
                    if (loadedClubs.Count > 0) FilterClubPicker();
                    else await LoadClubsScreenAsync(forceReloadPlayers: false);
                }
                else
                    ClearClubsScreen();
            }
        };

        backend.StateChanged += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            loadedClubs = [];
            clubPickerStats = new Dictionary<ulong, (long?, long?)>();
            clubDrafts.Clear();
            kitDrafts.Clear();
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
        var sectionOptions = ClubFieldInfo.Sections
            .Select(s => new ClubSectionOption(s, ClubFieldInfo.SectionTitle(s)))
            .ToArray();
        clubSectionList.ItemTemplate = new FuncDataTemplate<ClubSectionOption>((opt, _) =>
            opt is null
                ? null
                : new TextBlock
                {
                    Text = opt.Title,
                    FontSize = 11.5,
                    FontWeight = FontWeight.Medium,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 2, 14, 2),
                });
        clubSectionList.ItemsSource = sectionOptions;
        clubSectionList.SelectedIndex = 0;

        clubPickerList.SelectionChanged += (_, _) =>
        {
            if (clubUpdating) return;
            selectedClub = (clubPickerList.SelectedItem as ClubPickerItem)?.Club;
            if (selectedClub?.Id is { } id) lastSelectedClubId = id;
        };
        clubPickerList.DoubleTapped += (_, _) =>
        {
            if (selectedClub is not { } club) return;
            RenderClubEditor();
            UpdateClubStatusFromDraft();
            OpenKindTab("club", club.DisplayName, clubDetailRoot);
        };
        clubPickerList.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter || selectedClub is not { } club) return;
            RenderClubEditor();
            UpdateClubStatusFromDraft();
            OpenKindTab("club", club.DisplayName, clubDetailRoot);
            e.Handled = true;
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

        var listGrid = new Grid
        {
            Background = UiKit.WindowBg,
            RowDefinitions = new RowDefinitions
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };

        var idCol = new DataGridTextColumn { Header = "ID", Binding = new Binding(nameof(ClubPickerItem.Id)), Width = new DataGridLength(96), SortMemberPath = nameof(ClubPickerItem.Id) };
        idCol.CellStyleClasses.Add("numeric");
        clubPickerList.Columns.Add(idCol);
        clubPickerList.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(ClubPickerItem.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), SortMemberPath = nameof(ClubPickerItem.Name) });
        var balCol = new DataGridTextColumn { Header = "Balance", Binding = new Binding(nameof(ClubPickerItem.Balance)) { StringFormat = "N0" }, Width = new DataGridLength(150), SortMemberPath = nameof(ClubPickerItem.Balance) };
        balCol.CellStyleClasses.Add("numeric");
        clubPickerList.Columns.Add(balCol);
        var repCol = new DataGridTextColumn { Header = "Reputation", Binding = new Binding(nameof(ClubPickerItem.Reputation)), Width = new DataGridLength(110), SortMemberPath = nameof(ClubPickerItem.Reputation) };
        repCol.CellStyleClasses.Add("numeric");
        clubPickerList.Columns.Add(repCol);
        clubSearchBox.TextChanged += (_, _) => FilterClubPicker();
        var searchBar = new Border
        {
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 7),
            Child = clubSearchBox,
        };
        Grid.SetRow(searchBar, 0);
        listGrid.Children.Add(searchBar);
        Grid.SetRow(clubPickerList, 1);
        listGrid.Children.Add(clubPickerList);

        clubsViewRoot.Child = listGrid;

        clubSectionList.Margin = new Thickness(12, 0, 0, 10);
        var mainSplit = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions
            {
                new ColumnDefinition(new GridLength(180)) { MinWidth = 156 },
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 460 },
            },
        };
        Grid.SetColumn(clubSectionList, 0);
        mainSplit.Children.Add(clubSectionList);

        var rightScroll = new ScrollViewer
        {
            Content = clubEditorPanel,
            Background = UiKit.WindowBg,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetColumn(rightScroll, 1);
        mainSplit.Children.Add(rightScroll);

        var detailDock = new DockPanel { Background = UiKit.WindowBg };
        DockPanel.SetDock(clubHeaderHost, Dock.Top);
        detailDock.Children.Add(clubHeaderHost);
        detailDock.Children.Add(mainSplit);
        clubDetailRoot = detailDock;
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

            var (clubs, loadedPlayers, gameDate, stats) = await Task.Run(() =>
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
                var access = session.ClubFields;
                var values = new Dictionary<ulong, (long? Balance, long? Reputation)>(clubEntries.Count);
                foreach (var club in clubEntries)
                {
                    try
                    {
                        long? balance = access.Read(club.Address, ClubField.Balance);
                        long? reputation = club.Teams.Count > 0 ? access.Read(club.Teams[0].Address, ClubField.TeamReputation) : null;
                        values[club.Address] = (balance, reputation);
                    }
                    catch (Exception) { }
                }
                return (clubEntries, freshPlayers, gd, values);
            });

            if (current != clubLoadGeneration || !clubsActive) return;

            if (loadedPlayers is not null && rows.Count == 0)
                rows = loadedPlayers.OrderByDescending(p => p.CurrentAbility).Cast<object>().ToArray();

            clubCache = clubs;
            clubPickerStats = stats;
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

        var items = filtered.Select(c =>
        {
            var item = new ClubPickerItem(c);
            if (clubPickerStats.TryGetValue(c.Address, out var values))
            {
                item.Balance = values.Balance;
                item.Reputation = values.Reputation;
            }
            return item;
        }).ToArray();
        var nextItem = next is null ? null : items.FirstOrDefault(i => ReferenceEquals(i.Club, next));

        clubUpdating = true;
        clubPickerSource = items;
        clubPickerList.ItemsSource = items;
        clubPickerList.SelectedItem = nextItem;
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
        if (selectedClub is { } club) UpdateKitSaveButtons(club.Address);
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

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        clubSaveButton = new Button
        {
            Content = "Save changes",
            IsEnabled = draft.CanSave,
            Classes = { "accent" },
        };
        clubSaveButton.Click += (_, _) => SaveClubWithKits(club);

        clubDiscardButton = new Button
        {
            Content = "Discard",
            IsEnabled = draft.Count > 0 || draft.InvalidCount > 0,
        };
        clubDiscardButton.Click += (_, _) =>
        {
            clubDrafts.Remove(club.Address);
            kitDrafts.Remove(club.Address);
            clubFamiliarityDrafts.Remove(club.Address);
            pendingAffiliationRemovals.Remove(club.Address);
            RenderClubEditor();
            status.Text = "Changes discarded";
        };
        UpdateKitSaveButtons(club.Address);

        NationRecord? clubNation = null;
        if (profile.Club?.Nation is { } nationOffset)
        {
            Span<byte> nationPtrBuf = stackalloc byte[8];
            if (session.Memory.TryRead(club.Address + (ulong)nationOffset, nationPtrBuf))
            {
                ulong nationAddr = BitConverter.ToUInt64(nationPtrBuf);
                if (nationAddr != 0)
                {
                    nationCatalogue ??= session.Nations.LoadAll().Where(n => n.DisplayName.Length > 0).OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
                    clubNation = nationCatalogue.FirstOrDefault(n => n.Address == nationAddr);
                }
            }
        }

        string? clubDivisionName = null;
        ulong clubDivisionAddr = 0;
        var playerOfClub = rows.OfType<PlayerRecord>().FirstOrDefault(p => p.Club?.Address == club.Address && p.Division is not null);
        if (playerOfClub?.Division is { } div && !string.IsNullOrEmpty(div.DisplayName))
        {
            clubDivisionName = div.DisplayName;
            clubDivisionAddr = div.Address;
        }
        else if (profile.Competition is { } compOffsets)
        {
            ulong teamAddress = club.FirstTeam?.Address ?? (orderedTeams.Count > 0 ? orderedTeams[0].Address : 0);
            if (teamAddress != 0)
            {
                Span<byte> ptrBuf = stackalloc byte[8];
                if (session.Memory.TryRead(teamAddress + (ulong)compOffsets.TeamCompetition, ptrBuf))
                {
                    ulong compAddr = BitConverter.ToUInt64(ptrBuf);
                    if (compAddr != 0)
                    {
                        try
                        {
                            if (session.Competitions.Reload(compAddr) is { } c)
                            {
                                clubDivisionName = c.DisplayName;
                                clubDivisionAddr = c.Address;
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        var badges = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (club.Id is { } id)
            badges.Children.Add(UiKit.MetaText($"ID {id.ToString(Inv)}"));

        if (!string.IsNullOrWhiteSpace(club.ShortName)
            && !string.Equals(club.ShortName, club.Name, StringComparison.Ordinal))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText(club.ShortName, highlight: true));
        }

        if (clubNation is not null)
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            var flag = graphics.Image("nationflag", clubNation.Id, 15);
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { flag, new TextBlock { Text = clubNation.DisplayName, FontSize = 12, VerticalAlignment = VerticalAlignment.Center } },
            };
            var nationBtn = UiKit.MetaLinkButton(content);
            nationBtn.Click += async (_, _) => await OpenPlayerNationAsync(clubNation.Address);
            badges.Children.Add(nationBtn);
        }

        if (!string.IsNullOrEmpty(clubDivisionName))
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            var divBtn = UiKit.MetaLinkButton(clubDivisionName);
            if (clubDivisionAddr != 0)
                divBtn.Click += (_, _) => OpenPlayerCompetition(clubDivisionAddr);
            badges.Children.Add(divBtn);
        }

        if (orderedTeams.Count > 0)
        {
            if (badges.Children.Count > 0) badges.Children.Add(UiKit.MetaDot());
            badges.Children.Add(UiKit.MetaText($"{orderedTeams[0].SquadCount} players"));
        }

        var crestImg = graphics.Image("club", club.Id, 72);
        clubHeaderHost.Content = UiKit.EntityBanner(club.Name ?? club.DisplayName, badges, null, crestImg);

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

        var items = groups.Select(g => (BuildClubGroupCard(g, draft), g.Fields.Count)).ToList();
        clubEditorPanel.Children.Add(UiKit.TwoColumnMasonry(items, gap: 10));
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
        bool hasStatements = session.Profile.Club?.FinanceLists?.Statements is not null;

        if ((selectedFinanceSubSection == ClubSection.Sponsors && !hasSponsors)
            || (selectedFinanceSubSection == ClubSection.Debts && !hasDebts)
            || (selectedFinanceSubSection == ClubSection.IncomeStatement && !hasStatements))
        {
            selectedFinanceSubSection = ClubSection.Finances;
        }

        if (hasSponsors || hasDebts || hasStatements)
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
            if (hasStatements)
                AddFinanceSubTab(ClubSection.IncomeStatement, "Income Statement");

            clubEditorPanel.Children.Add(tabStrip);
        }

        if (selectedFinanceSubSection == ClubSection.Finances)
        {
            RenderClubScalarSection(ClubSection.Finances, club, orderedTeams, teamAddr, profile, access, values, draft);
        }
        else if (selectedFinanceSubSection == ClubSection.IncomeStatement)
        {
            RenderClubIncomeStatement(session, club);
        }
        else
        {
            RenderClubListSection(selectedFinanceSubSection, session, club, orderedTeams, activeTeam, teamAddr);
        }
    }

    /// <summary>Finances → Income Statement: the monthly blocks the game keeps (this month, last month, this season).</summary>
    void RenderClubIncomeStatement(GameSession session, ClubEntry club)
    {
        IReadOnlyList<ClubFinancialPeriod> periods;
        try { periods = session.FinancialStatements.Read(club.Address); }
        catch (Exception ex) { clubEditorPanel.Children.Add(new TextBlock { Text = UserMessage.For(ex) }); return; }
        if (periods.Count == 0) return;

        string[] headers = ["This Month", "Last Month", "This Season"];
        int columns = 1 + Math.Min(headers.Length, periods.Count);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("300,150,150,150"), RowSpacing = 1, Margin = new Thickness(0, 4) };

        void Add(string label, Func<ClubFinancialPeriod, long> pick, bool bold = false, bool money = true)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock
            {
                Text = label,
                FontSize = 11.5,
                FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = Brush(bold ? "#F8FAFC" : "#CBD5E1"),
            };
            Grid.SetRow(name, row);
            Grid.SetColumn(name, 0);
            grid.Children.Add(name);
            for (int i = 0; i < columns - 1 && i < periods.Count; i++)
            {
                var value = new TextBlock
                {
                    Text = money ? pick(periods[i]).ToString("N0", Inv) : pick(periods[i]).ToString(Inv),
                    FontSize = 11.5,
                    FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
                    TextAlignment = TextAlignment.Right,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 12, 0),
                    Foreground = Brush(pick(periods[i]) < 0 ? "#F87171" : "#F8FAFC"),
                };
                Grid.SetRow(value, row);
                Grid.SetColumn(value, i + 1);
                grid.Children.Add(value);
            }
        }

        int headerRow = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int i = 0; i < columns; i++)
        {
            var head = new TextBlock
            {
                Text = i == 0 ? "Item" : headers[i - 1],
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = Brush("#94A3B8"),
                Margin = new Thickness(i == 0 ? 8 : 0, 0, i == 0 ? 0 : 12, 4),
                HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            };
            Grid.SetRow(head, headerRow);
            Grid.SetColumn(head, i);
            grid.Children.Add(head);
        }

        Add("Total Income", p => p.TotalIncome, bold: true);
        Add("Gate Receipts", p => p.GateReceipts);
        Add("Other", p => p.OtherIncome);
        Add("Season Tickets", p => p.SeasonTickets);
        Add("Interest", p => p.Interest);
        Add("Investments", p => p.Investments);
        Add("Merchandising", p => p.Merchandising);
        Add("Players Sold", p => p.PlayersSold);
        Add("Sponsorship", p => p.Sponsorship);
        Add("Match Day Income", p => p.MatchDayIncome);
        Add("Grants", p => p.Grants);
        Add("Total Expenditure", p => p.TotalExpenditure, bold: true);
        Add("Other", p => p.Other);
        Add("Loan Repayments and Interest", p => p.LoanRepayment);
        Add("League Fines", p => p.LeagueFines);
        Add("Ground Maintenance", p => p.GroundMaintenance);
        Add("Player Wages", p => p.PlayerWages);
        Add("Staff Wages", p => p.StaffWages);
        Add("Bonuses", p => p.Bonuses);
        Add("Loyalty Bonuses", p => p.LoyaltyBonuses);
        Add("Transfer Expenditure", p => p.TransferExpenditure);
        Add("VAT", p => p.Vat);
        Add("NI Employer Tax", p => p.NiEmployerTax);
        Add("Profit", p => p.TotalIncome - p.TotalExpenditure, bold: true);

        clubEditorPanel.Children.Add(grid);

        // FMRTE only shows these figures; here a single figure can be written for one period, guarded by a snapshot check.
        var periodOptions = periods.Select(p => new StatementPeriodOption(p.Index, headers.Length > p.Index ? headers[p.Index] : $"Period {p.Index}")).ToArray();
        var periodBox = new ComboBox { ItemsSource = periodOptions, SelectedIndex = 0, Width = 150 };
        var fields = new List<StatementFieldOption>();
        foreach (var key in ClubStatementOffsets.IncomeKeys)
            fields.Add(new StatementFieldOption(true, key, StatementFieldLabel(key, true)));
        foreach (var key in ClubStatementOffsets.ExpenditureKeys)
            fields.Add(new StatementFieldOption(false, key, StatementFieldLabel(key, false)));
        var fieldBox = new ComboBox { ItemsSource = fields, SelectedIndex = 0, Width = 240 };
        var valueBox = new TextBox { Text = "0", Width = 140, HorizontalContentAlignment = HorizontalAlignment.Right };
        var apply = new Button { Content = "Write value" };
        apply.Click += (_, _) =>
        {
            if (periodBox.SelectedItem is not StatementPeriodOption periodOption
                || fieldBox.SelectedItem is not StatementFieldOption field)
            { status.Text = "Choose a period and a field."; return; }
            if (!long.TryParse(valueBox.Text, out long value)) { status.Text = "Enter a whole number."; return; }
            var period = periods.FirstOrDefault(p => p.Index == periodOption.Index);
            if (period is null) { status.Text = "That period is no longer listed."; return; }
            long expected = field.Income ? IncomeValue(period, field.Key) : ExpenditureValue(period, field.Key);
            try
            {
                backend.Edits.Execute(() => session.FinancialStatements.Set(club.Address, period.Index, field.Income, field.Key, expected, value));
                RenderClubEditor();
                status.Text = "Saved. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        var editor = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        editor.Children.Add(new TextBlock { Text = "Period", VerticalAlignment = VerticalAlignment.Center });
        editor.Children.Add(periodBox);
        editor.Children.Add(new TextBlock { Text = "Field", VerticalAlignment = VerticalAlignment.Center });
        editor.Children.Add(fieldBox);
        editor.Children.Add(new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center });
        editor.Children.Add(valueBox);
        editor.Children.Add(apply);
        clubEditorPanel.Children.Add(editor);
    }

    sealed record StatementPeriodOption(int Index, string Label)
    {
        public override string ToString() => Label;
    }

    sealed record StatementFieldOption(bool Income, string Key, string Label)
    {
        public override string ToString() => Label;
    }

    static string StatementFieldLabel(string key, bool income) => key switch
    {
        "gateReceipts" => "Gate Receipts",
        "other" => income ? "Other income" : "Other expenditure",
        "seasonTickets" => "Season Tickets",
        "interest" => "Interest",
        "investments" => "Investments",
        "merchandising" => "Merchandising",
        "playersSold" => "Players Sold",
        "sponsorship" => "Sponsorship",
        "matchDayIncome" => "Match Day Income",
        "grants" => "Grants",
        "loanRepayment" => "Loan Repayments and Interest",
        "leagueFines" => "League Fines",
        "groundMaintenance" => "Ground Maintenance",
        "playerWages" => "Player Wages",
        "staffWages" => "Staff Wages",
        "bonuses" => "Bonuses",
        "loyaltyBonuses" => "Loyalty Bonuses",
        "transferExpenditure" => "Transfer Expenditure",
        "vat" => "VAT",
        "niEmployerTax" => "NI Employer Tax",
        _ => key,
    };

    static long IncomeValue(ClubFinancialPeriod p, string key) => key switch
    {
        "gateReceipts" => p.GateReceipts,
        "other" => p.OtherIncome,
        "seasonTickets" => p.SeasonTickets,
        "interest" => p.Interest,
        "investments" => p.Investments,
        "merchandising" => p.Merchandising,
        "playersSold" => p.PlayersSold,
        "sponsorship" => p.Sponsorship,
        "matchDayIncome" => p.MatchDayIncome,
        "grants" => p.Grants,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    static long ExpenditureValue(ClubFinancialPeriod p, string key) => key switch
    {
        "other" => p.Other,
        "loanRepayment" => p.LoanRepayment,
        "leagueFines" => p.LeagueFines,
        "groundMaintenance" => p.GroundMaintenance,
        "playerWages" => p.PlayerWages,
        "staffWages" => p.StaffWages,
        "bonuses" => p.Bonuses,
        "loyaltyBonuses" => p.LoyaltyBonuses,
        "transferExpenditure" => p.TransferExpenditure,
        "vat" => p.Vat,
        "niEmployerTax" => p.NiEmployerTax,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

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
                    FontSize = 11.5,
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
        if (listSection == ClubSection.Kits)
        {
            RenderClubKits(session, club);
            return;
        }
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
        if (listSection == ClubSection.Sponsors && session.Profile.Club?.FinanceLists is { SponsorTotal: >= 0 })
        {
            RenderClubSponsors(session, club);
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
            var typeBox = new TextBox { Text = entries[index].TypeCode.ToString(Inv), Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };
            var applyType = new Button { Content = "Apply" };
            applyType.Click += (_, _) =>
            {
                if (!byte.TryParse(typeBox.Text, out byte code)) { status.Text = "Enter a whole number for the type code."; return; }
                try
                {
                    backend.Edits.Execute(() => session.ClubVisionWriter.SetType(teamAddr, entries[index], code));
                    RenderClubEditor();
                    status.Text = "Saved. Continue in FM to see updates.";
                }
                catch (Exception ex) { status.Text = UserMessage.For(ex); }
            };
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                new TextBlock { Text = "Type code", VerticalAlignment = VerticalAlignment.Center }, typeBox, applyType
            } });

            var startBox = new TextBox { Text = entries[index].StartDate?.ToString("yyyy-MM-dd", Inv) ?? "", Width = 110 };
            var endBox = new TextBox { Text = entries[index].EndDate?.ToString("yyyy-MM-dd", Inv) ?? "", Width = 110 };
            var applyDates = new Button { Content = "Apply" };
            applyDates.Click += (_, _) =>
            {
                if (!DateOnly.TryParseExact(startBox.Text, "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var start)
                    || !DateOnly.TryParseExact(endBox.Text, "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var end))
                { status.Text = "Enter dates as yyyy-MM-dd."; return; }
                try
                {
                    backend.Edits.Execute(() => session.ClubVisionWriter.SetDates(teamAddr, entries[index], start, end));
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

            var duplicate = new Button { Content = "Duplicate row" };
            duplicate.Click += (_, _) =>
            {
                try
                {
                    backend.Edits.Execute(() => session.ClubVisionWriter.Duplicate(teamAddr, entries[index],
                        (session.Memory as Fm27Editor.Core.Memory.WindowsProcessMemory) is { } wpm ? wpm.Allocate : null));
                    RenderClubEditor();
                    status.Text = "Row added. Continue in FM to see updates.";
                }
                catch (Exception ex) { status.Text = UserMessage.For(ex); }
            };
            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) =>
            {
                try
                {
                    backend.Edits.Execute(() => session.ClubVisionWriter.Remove(teamAddr, entries[index]));
                    RenderClubEditor();
                    status.Text = "Removed. Continue in FM to see updates.";
                }
                catch (Exception ex) { status.Text = UserMessage.For(ex); }
            };
            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { duplicate, remove } });
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
            bool removing = pendingAffiliationRemovals.TryGetValue(club.Address, out var removal) && removal.Address == entries[index].Address;
            var remove = new Button { Content = removing ? "Undo Remove" : "Remove", IsEnabled = removing || !pendingAffiliationRemovals.ContainsKey(club.Address) };
            remove.Click += (_, _) =>
            {
                if (removing) pendingAffiliationRemovals.Remove(club.Address);
                else pendingAffiliationRemovals[club.Address] = entries[index];
                RenderClubEditor();
            };
            panel.Children.Add(remove);
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

    sealed record SponsorIncomeOption(byte Code, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>Sponsors and Other: existing records are editable, and records can be added or removed.</summary>
    void RenderClubSponsors(GameSession session, ClubEntry club)
    {
        IReadOnlyList<ClubSponsorEntry> sponsors;
        try { sponsors = session.ReadSponsors(club.Address); }
        catch (Exception ex) { status.Text = UserMessage.For(ex); return; }
        if (sponsors.Count == 0) return;

        var addBar = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 6 };
        var newTotal = new TextBox { Text = "1000000", Width = 112, HorizontalContentAlignment = HorizontalAlignment.Right };
        var newStart = new TextBox { Text = "2026-07-01", Width = 104 };
        var newEnd = new TextBox { Text = "2027-06-30", Width = 104 };
        var newType = new ComboBox
        {
            Width = 196,
            ItemsSource = ClubListLabels.SponsorIncomeTypes
                .Where(pair => ClubSponsorWriter.SupportedIncomeType(pair.Key))
                .OrderBy(pair => pair.Value, StringComparer.Ordinal)
                .Select(pair => new SponsorIncomeOption(pair.Key, pair.Value)).ToArray(),
        };
        newType.SelectedIndex = 0;
        var add = new Button { Content = "Add sponsor" };
        add.Click += (_, _) =>
        {
            if (newType.SelectedItem is not SponsorIncomeOption chosen) { status.Text = "Choose an income type."; return; }
            if (!int.TryParse(newTotal.Text, out int value)) { status.Text = "Enter a whole number for the sponsor value."; return; }
            if (!DateOnly.TryParseExact(newStart.Text, "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var start)
                || !DateOnly.TryParseExact(newEnd.Text, "yyyy-MM-dd", Inv, System.Globalization.DateTimeStyles.None, out var end))
            { status.Text = "Enter dates as yyyy-MM-dd."; return; }
            try
            {
                backend.Edits.Execute(() => session.SponsorWriter.Add(club.Address, chosen.Code, value, start, end));
                RenderClubEditor();
                status.Text = "Sponsor added. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        var remove = new Button { Content = "Remove selected" };
        remove.Click += (_, _) =>
        {
            int index = clubListSelectedRow.TryGetValue((club.Address, ClubSection.Sponsors), out int saved) ? saved : 0;
            if (index < 0 || index >= sponsors.Count) return;
            try
            {
                var entry = sponsors[index];
                backend.Edits.Execute(() => session.SponsorWriter.Remove(club.Address, entry));
                RenderClubEditor();
                status.Text = "Sponsor removed. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        addBar.Children.Add(new TextBlock { Text = "Income Type", VerticalAlignment = VerticalAlignment.Center });
        addBar.Children.Add(newType);
        addBar.Children.Add(new TextBlock { Text = "Value", VerticalAlignment = VerticalAlignment.Center });
        addBar.Children.Add(newTotal);
        addBar.Children.Add(new TextBlock { Text = "Start", VerticalAlignment = VerticalAlignment.Center });
        addBar.Children.Add(newStart);
        addBar.Children.Add(new TextBlock { Text = "End", VerticalAlignment = VerticalAlignment.Center });
        addBar.Children.Add(newEnd);
        addBar.Children.Add(add);
        addBar.Children.Add(remove);
        clubEditorPanel.Children.Add(addBar);
        var table = ClubListBuilder.BuildSponsors(sponsors.Select(s => new ClubSponsorRow(s.IncomeTypeCode, s.TotalValue, s.StartDate, s.EndDate)));
        var key = (club.Address, ClubSection.Sponsors);
        int selected = clubListSelectedRow.TryGetValue(key, out int saved) ? Math.Clamp(saved, 0, sponsors.Count - 1) : 0;
        clubListSelectedRow[key] = selected;
        Control Detail(int index)
        {
            var sponsor = sponsors[index];
            var panel = new StackPanel { Spacing = 8 };
            if (ClubSponsorWriter.SupportedIncomeType(sponsor.IncomeTypeCode))
            {
                var options = ClubListLabels.SponsorIncomeTypes
                    .Where(pair => ClubSponsorWriter.SupportedIncomeType(pair.Key))
                    .OrderBy(pair => pair.Value, StringComparer.Ordinal)
                    .Select(pair => new SponsorIncomeOption(pair.Key, pair.Value)).ToArray();
                var income = new ComboBox { ItemsSource = options, SelectedItem = options.First(o => o.Code == sponsor.IncomeTypeCode), Width = 230 };
                var applyIncome = new Button { Content = "Apply" };
                applyIncome.Click += (_, _) =>
                {
                    if (income.SelectedItem is not SponsorIncomeOption chosen) return;
                    try
                    {
                        backend.Edits.Execute(() => session.SetSponsorIncomeType(club.Address, sponsor, chosen.Code));
                        RenderClubEditor();
                        status.Text = "Saved. Continue in FM to see updates.";
                    }
                    catch (Exception ex) { status.Text = UserMessage.For(ex); }
                };
                panel.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, Children =
                    {
                        new TextBlock { Text = "Income Type", VerticalAlignment = VerticalAlignment.Center }, income, applyIncome
                    }
                });
            }
            var totalBox = new TextBox { Text = sponsor.TotalValue.ToString(Inv), Width = 130, HorizontalContentAlignment = HorizontalAlignment.Right };
            var applyTotal = new Button { Content = "Apply" };
            applyTotal.Click += (_, _) =>
            {
                if (!int.TryParse(totalBox.Text, out int value)) { status.Text = "Enter a whole number for the sponsor value."; return; }
                try
                {
                    backend.Edits.Execute(() => session.SetSponsorTotal(club.Address, sponsor, value));
                    RenderClubEditor();
                    status.Text = "Saved. Continue in FM to see updates.";
                }
                catch (Exception ex) { status.Text = UserMessage.For(ex); }
            };
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, Children =
                {
                    new TextBlock { Text = "Total Value", VerticalAlignment = VerticalAlignment.Center }, totalBox, applyTotal
                }
            });
            if (sponsor.StartDate is { } started && sponsor.EndDate is { } ended)
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
                        backend.Edits.Execute(() => session.SetSponsorDates(club.Address, sponsor, start, end));
                        RenderClubEditor();
                        status.Text = "Saved. Continue in FM to see updates.";
                    }
                    catch (Exception ex) { status.Text = UserMessage.For(ex); }
                };
                panel.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, Children =
                    {
                        new TextBlock { Text = "Start", VerticalAlignment = VerticalAlignment.Center }, startBox,
                        new TextBlock { Text = "End", VerticalAlignment = VerticalAlignment.Center }, endBox, applyDates
                    }
                });
            }
            if (sponsor.RenewIncome is { } renewIncome && sponsor.FixedValue is { } fixedValue)
            {
                var renewBox = new CheckBox { Content = "Renew Income", IsChecked = renewIncome, VerticalAlignment = VerticalAlignment.Center };
                var fixedBox = new CheckBox { Content = "Fixed Value", IsChecked = fixedValue, VerticalAlignment = VerticalAlignment.Center };
                var applyFlags = new Button { Content = "Apply" };
                applyFlags.Click += (_, _) =>
                {
                    try
                    {
                        backend.Edits.Execute(() => session.SetSponsorFlags(club.Address, sponsor, renewBox.IsChecked == true, fixedBox.IsChecked == true));
                        RenderClubEditor();
                        status.Text = "Saved. Continue in FM to see updates.";
                    }
                    catch (Exception ex) { status.Text = UserMessage.For(ex); }
                };
                panel.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, Children = { renewBox, fixedBox, applyFlags }
                });
            }
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

    void RenderClubDebts(GameSession session, ClubEntry club)    {
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

        var clearAll = new Button { Content = "Clear all debts" };
        clearAll.Click += (_, _) =>
        {
            try
            {
                backend.Edits.Execute(() => session.RemoveAllDebts(club.Address));
                RenderClubEditor();
                status.Text = "All debts cleared. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        clubEditorPanel.Children.Add(clearAll);
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
        clubEditorPanel.Children.Add(BuildClubRelationshipActions(session, club));
    }


    /// <summary>Add a rival club; the guarded writer appends into spare capacity or a new buffer when the vector is full.</summary>
    Control BuildClubRelationshipActions(GameSession session, ClubEntry club)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        IReadOnlyList<ClubEntry> clubs;
        try { clubs = session.LoadClubs([]); } catch { clubs = []; }
        var pick = new ComboBox { Width = 260, ItemsSource = clubs.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray() };
        pick.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ClubEntry>((entry, _) =>
            new TextBlock { Text = entry is null ? "" : $"{entry.Name} ({entry.Id})" });
        var add = new Button { Content = "Add rival" };
        add.Click += (_, _) =>
        {
            if (pick.SelectedItem is not ClubEntry rival || rival.Address == club.Address) { status.Text = "Pick another club"; return; }
            try
            {
                backend.Edits.Execute(() => new Fm27Editor.Core.Clubs.ClubRelationshipWriter(session.Memory, session.Profile, session.Game.ModuleBase).AddRival(club.Address, rival.Address, 50, 2, null));
                RenderClubEditor();
                status.Text = "Added. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        row.Children.Add(new TextBlock { Text = "Rival", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(pick);
        row.Children.Add(add);
        return row;
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
        var remove = new Button { Content = "Remove" };
        remove.Click += (_, _) =>
        {
            try
            {
                backend.Edits.Execute(() => new Fm27Editor.Core.Clubs.ClubRelationshipWriter(session.Memory, session.Profile, session.Game.ModuleBase).Remove(club.Address, row));
                RenderClubEditor();
                status.Text = "Removed. Continue in FM to see updates.";
            }
            catch (Exception ex) { status.Text = UserMessage.For(ex); }
        };
        panel.Children.Add(remove);
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
                TextWrapping = TextWrapping.Wrap,
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
            Background = UiKit.Surface,
            BorderBrush = UiKit.BorderSubtle,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    new Border
                    {
                        Background = UiKit.SurfaceHeader,
                        BorderBrush = UiKit.BorderSubtle,
                        BorderThickness = new Thickness(0, 0, 0, 1),
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
                        FontSize = 11.5,
                        Foreground = Brush("#F1F5F9"),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            };
        }

        return new TextBlock
        {
            Text = cell.Text,
            FontSize = 11.5,
            Foreground = Brush("#F1F5F9"),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = cell.RightAlign ? TextAlignment.Right : TextAlignment.Left,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(leftMargin, 0, 0, 0),
        };
    }

    Control? BuildClubListDetailCard(ClubListRowModel row)
    {
        if (row.Details.Count == 0)
            return null;

        var panel = new StackPanel { Spacing = 0 };

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
                            FontSize = 11.5,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Brush("#F8FAFC"),
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    },
                }
                : new TextBlock
                {
                    Text = field.Value,
                    FontSize = 11.5,
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
                FontSize = 11.5,
                Foreground = Brush("#CBD5E1"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 12, 0),
            });

            panel.Children.Add(new Border
            {
                Background = rowIndex++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                Padding = new Thickness(12, 2, 12, 2),
                Child = line,
            });
        }

        var detailCard = UiKit.Card("Details", panel);
        detailCard.MaxWidth = 540;
        detailCard.HorizontalAlignment = HorizontalAlignment.Left;
        return detailCard;
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
                Background = rowIndex++ % 2 == 1 ? UiKit.SurfaceZebra : Brushes.Transparent,
                Padding = new Thickness(10, 2, 10, 2),
                Child = rowControl,
            });
        }

        if (distFields.Length == ClubSectionLayout.SupporterDistributionFields.Count)
        {
            distributionTotalText = new TextBlock
            {
                FontSize = 11.5,
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
                FontSize = 11.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#94A3B8"),
                VerticalAlignment = VerticalAlignment.Center,
            });

            panel.Children.Add(new Border
            {
                BorderBrush = UiKit.BorderSubtle,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(10, 2, 10, 2),
                Child = totalLine,
            });
        }

        return UiKit.Card(group.Title, panel);
    }

    Control BuildClubFieldRow(VisibleClubField field, ClubDraft draft, Action? onChanged)
    {
        var line = new DockPanel { MinHeight = 29, LastChildFill = true };
        var label = new TextBlock
        {
            Text = field.Meta.Label,
            FontSize = 11.5,
            Foreground = Brush("#CBD5E1"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
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
                FontSize = 11.5,
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
            var box = new TextBox
            {
                Text = ClubFieldInfo.FormatValue(currentVal, ClubDisplayKind.Colour),
                Width = 106,
                FontSize = 11.5,
                FontWeight = FontWeight.Bold,
                HorizontalContentAlignment = HorizontalAlignment.Left,
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
                if (ok) StageValid(parsed);
                else MarkInvalid(true);
            };
            var colourPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { ColourInput(box) },
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
                Width = 176,
                FontSize = 11.5,
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
            FontSize = 11.5,
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

using Avalonia.Controls;
using Fm27Editor.App;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.History;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App.Tests;

public sealed class GenderFilterTests
{
    sealed class NoopMemory : IProcessMemory
    {
        public bool TryRead(ulong address, Span<byte> buffer) { buffer.Clear(); return false; }
        public bool TryWrite(ulong address, ReadOnlySpan<byte> data) => false;
    }

    sealed class FakeScreenHost : IScreenHost
    {
        public EditorBackend Backend { get; } = new();
        public GraphicsCatalog Graphics { get; } = new();
        public string LastStatus { get; private set; } = "";
        public void SetStatus(string text) => LastStatus = text;
        public void AddModule(string title, Control root, object navItem) { }
        public object? SelectedNav => null;
        public event EventHandler? NavChanged { add { } remove { } }
        public bool IsShowing(Control body) => false;
        public void OpenTab(string kind, ulong address, string title, Control body, Action restore) { }
        public void SwitchModule(string title) { }
        public IReadOnlyList<NationRecord> NationCatalogue => [];
        public Task OpenNationAsync(ulong address) => Task.CompletedTask;
        public Task OpenClubAsync(ulong address) => Task.CompletedTask;
        public void OpenCity(ulong address) { }
        public void OpenStadium(ulong address) { }
        public void OpenStaff(StaffRecord staff) { }
        public Avalonia.Controls.Window Window => throw new NotSupportedException();
        public Control BuildStaffTable(IReadOnlyList<StaffListRow> rows, IStaffContractScope scope, Action refresh) => new Control();
        public Control BuildKits(ulong owner, IReadOnlyList<ClubKitItem> items, Action changed) => new Control();
        public IReadOnlyDictionary<ulong, KitDraft>? KitDraftOf(ulong owner) => null;
        public void ClearKitDraft(ulong owner) { }
        public Control BuildSquad(Func<IReadOnlyList<PlayerRecord>> getSquad) => new Control();
        public IReadOnlyList<PlayerRecord> Players() => [];
        public IReadOnlyList<ClubEntry> Clubs() => [];
    }

    sealed class FakeClubPanels : IClubPanels
    {
        public ListBox SectionRail { get; } = new();
        public IReadOnlyList<ClubEntry>? CachedClubs => null;
        public IReadOnlyList<PlayerRecord> ShownPlayers => [];
        public void ClubsLoaded(IReadOnlyList<ClubEntry> clubs, IReadOnlyList<PlayerRecord>? freshPlayers, DateOnly? gameDate) { }
        public void Reconnected() { }
        public void BuildClub(ClubEntry club, ClubDraft draft) { }
        public bool HasPending(ClubEntry club) => false;
        public int SaveClub(ClubEntry club, ClubDraft draft) => 0;
        public void SaveFailed(ClubEntry club, Exception error) { }
        public ulong TeamOf(ClubEntry club) => 0;
        public bool IsValid(ulong clubAddress) => true;
    }

    static PlayerRecord Player(ulong address, string name, Gender gender, string clubName, Gender clubGender, string? division = "Premier League", bool freeAgent = false) => new()
    {
        Address = address,
        VtableRva = 0x4D27228,
        Id = (int)address,
        FirstName = name,
        LastName = "Test",
        CurrentAbility = 150,
        PotentialAbility = 160,
        HomeReputation = 5000,
        CurrentReputation = 5000,
        WorldReputation = 5000,
        Height = 180,
        Gender = gender,
        Contract = freeAgent ? null : new ContractInfo(address + 0x100, 10000, null, null, new ClubInfo(address + 0x200, clubName, clubName, clubGender)),
        Division = division is null ? null : new CompetitionInfo(address + 0x300, division, division),
        RawAttributes = new byte[Enum.GetValues<PlayerAttribute>().Length],
        AttributeScale = 5,
        Positions = new byte[Enum.GetValues<PlayerPosition>().Length],
        Personality = new byte[Enum.GetValues<PersonalityAttribute>().Length],
    };

    [Fact]
    public void PlayersModule_filters_men_women_and_excludes_womens_clubs()
    {
        var module = new PlayersModule();
        var man = Player(1, "Bruno", Gender.Men, "Man Utd", Gender.Men);
        var woman = Player(2, "Ella", Gender.Women, "Man Utd", Gender.Women, "Women's Super League");
        var atWomenClub = Player(3, "Mismatch", Gender.Men, "Chelsea", Gender.Women);
        var freeWoman = Player(4, "Free", Gender.Women, "", Gender.Unknown, division: null, freeAgent: true);
        var all = new[] { man, woman, atWomenClub, freeWoman };

        var menOnly = module.FilterAndSort(all, new PlayerFilter { Gender = Gender.Men, ExcludeWomenClubs = true },
            new PlayerSortSpec(PlayerSortKey.CurrentAbility, true));
        Assert.Equal([man], menOnly);

        var womenOnly = module.FilterAndSort(all, new PlayerFilter { Gender = Gender.Women },
            new PlayerSortSpec(PlayerSortKey.CurrentAbility, true));
        Assert.Equal([woman, freeWoman], womenOnly);
    }

    [Fact]
    public void ClubCatalog_separates_identical_club_names_by_gender()
    {
        var men = new ClubEntry(1, 602, "Arsenal", "Arsenal", 0, [], Gender.Men);
        var women = new ClubEntry(2, 200001, "Arsenal", "Arsenal", 0, [], Gender.Women);
        var both = new[] { men, women };

        Assert.Equal([men], ClubCatalog.Filter(both, "arsenal", Gender.Men));
        Assert.Equal([women], ClubCatalog.Filter(both, "arsenal", Gender.Women));
        Assert.Equal(2, ClubCatalog.Filter(both, "arsenal").Count);
    }

    [Fact]
    public void StaffFilter_filters_person_gender_while_allowing_female_staff_at_mens_club()
    {
        var kind = new StaffKind(0, 0, 0, false);
        StaffRecord Person(ulong id, Gender gender, string club, Gender clubGender) => new()
        {
            Address = id,
            StaffAddress = id + 1,
            Id = (int)id,
            Gender = gender,
            Club = new ClubInfo(id + 0x100, club, club, clubGender),
            Attributes = [],
            Personality = [],
            Kind = kind,
        };

        var maleAtMenClub = new StaffRow(Person(1, Gender.Men, "Man Utd", Gender.Men), "Coach Men", "Coach", "Man Utd", "England", 40, 140, 150);
        var femaleAtMenClub = new StaffRow(Person(2, Gender.Women, "Man Utd", Gender.Men), "Doctor Women", "Chief Doctor", "Man Utd", "England", 35, 130, 140);
        var maleAtWomenClub = new StaffRow(Person(3, Gender.Men, "Arsenal", Gender.Women), "Coach WClub", "Coach", "Arsenal", "England", 45, 120, 130);
        StaffRow[] rows = [maleAtMenClub, femaleAtMenClub, maleAtWomenClub];

        Assert.Equal([maleAtMenClub], StaffFilter.Apply(rows, "", null, null, Gender.Men, excludeWomenClubs: true));
        Assert.Equal([femaleAtMenClub], StaffFilter.Apply(rows, "", null, null, Gender.Women, excludeWomenClubs: true));
    }

    [Fact]
    public void AdvancedSearch_panels_show_named_gender_options_and_never_hide_rows_silently()
    {
        AvaloniaTestThread.Run(() =>
        {
            var man = Player(1, "Bruno", Gender.Men, "Man Utd", Gender.Men, "Premier League");
            var woman = Player(2, "Ella", Gender.Women, "Arsenal Women", Gender.Women, "Women's Super League");
            var dialog = new AttributeFilterDialog(
                new PlayerFilter { Gender = Gender.Men, ExcludeWomenClubs = true },
                [], null, [man, woman]);

            Assert.Equal(Gender.Men, dialog.ResultGender);
            Assert.True(dialog.ResultExcludeWomenClubs);
            Assert.Contains("Gender: Men", dialog.SummaryMessage, StringComparison.Ordinal);
            Assert.Contains("1 of 2 players shown", dialog.SummaryMessage, StringComparison.Ordinal);

            var profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-gamepass.json"));
            var menClub = new ClubEntry(1, 602, "Arsenal", "Arsenal", 0, [], Gender.Men);
            var womenClub = new ClubEntry(2, 200001, "Arsenal", "Arsenal", 0, [], Gender.Women);
            var clubPanel = new ClubFilterPanel([menClub, womenClub], new ClubFieldAccess(new NoopMemory(), profile, 0x10000));
            Assert.Equal(Gender.Men, clubPanel.Criteria()?.Gender);
            Assert.Equal(1, clubPanel.FilterCount());
            Assert.Contains("Gender: Men", clubPanel.SummaryStatus(), StringComparison.Ordinal);
            Assert.Contains("1 of 2 clubs shown", clubPanel.SummaryStatus(), StringComparison.Ordinal);
            var matchedClubs = clubPanel.Matched();
            Assert.Single(matchedClubs);
            Assert.Equal(Gender.Men, matchedClubs[0].Gender);

            var staffAttrs = new byte[Enum.GetValues<StaffAttribute>().Length];
            var staffPanel = new StaffFilterPanel(
            [
                new StaffRecord { Address = 1, StaffAddress = 2, Id = 1, FirstName = "Alex", Gender = Gender.Men, Club = new ClubInfo(10, "Man Utd", "Man Utd", Gender.Men), Attributes = staffAttrs, Personality = new byte[8], Kind = new StaffKind(0, 0, 0, false) },
                new StaffRecord { Address = 3, StaffAddress = 4, Id = 2, FirstName = "Jane", Gender = Gender.Women, Club = new ClubInfo(10, "Man Utd", "Man Utd", Gender.Men), Attributes = staffAttrs, Personality = new byte[8], Kind = new StaffKind(0, 0, 0, false) },
                new StaffRecord { Address = 5, StaffAddress = 6, Id = 3, FirstName = "Sam", Gender = Gender.Men, Club = new ClubInfo(20, "Arsenal", "Arsenal", Gender.Women), Attributes = staffAttrs, Personality = new byte[8], Kind = new StaffKind(0, 0, 0, false) },
            ], new DateOnly(2026, 11, 10));
            Assert.Equal(Gender.Men, staffPanel.Criteria()?.Gender);
            Assert.Equal(1, staffPanel.FilterCount());
            Assert.Contains("Gender: Men", staffPanel.SummaryStatus(), StringComparison.Ordinal);
            Assert.Contains("1 of 3 staff shown", staffPanel.SummaryStatus(), StringComparison.Ordinal);
            var matchedStaff = staffPanel.Matched();
            Assert.Single(matchedStaff);
            Assert.Equal(1, matchedStaff[0].Id);

            var resultsWindow = new SearchResultsWindow("Club", [], [menClub], new DateOnly(2026, 11, 10), totalCount: 2, filterNote: "Gender: Men");
            Assert.Contains("1 of 2 clubs shown (1 hidden", resultsWindow.StatusMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ClubScreen_uses_named_gender_dropdown_and_updates_status_when_hiding_rows()
    {
        AvaloniaTestThread.Run(() =>
        {
            var host = new FakeScreenHost();
            var screen = new ClubScreen(host, new FakeClubPanels());
            screen.Register();

            Assert.Equal(new[] { "Men", "Women", "All" }, Assert.IsAssignableFrom<IEnumerable<string>>(screen.GenderFilter.ItemsSource).ToArray());
            Assert.Equal(0, screen.GenderFilter.SelectedIndex);

            var menClub = new ClubEntry(1, 602, "Arsenal", "Arsenal", 0, [], Gender.Men);
            var womenClub = new ClubEntry(2, 200001, "Arsenal", "Arsenal", 0, [], Gender.Women);
            screen.SetLoadedRowsForTest([new MainWindow.ClubPickerItem(menClub), new MainWindow.ClubPickerItem(womenClub)]);

            Assert.Single(screen.Visible);
            Assert.Equal(Gender.Men, screen.Visible[0].Club.Gender);
            Assert.Contains("1 of 2 clubs shown", host.LastStatus, StringComparison.Ordinal);
            Assert.Contains("Men filter", host.LastStatus, StringComparison.Ordinal);

            screen.GenderFilter.SelectedIndex = 1;
            Assert.Single(screen.Visible);
            Assert.Equal(Gender.Women, screen.Visible[0].Club.Gender);
            Assert.Contains("Women filter", host.LastStatus, StringComparison.Ordinal);

            screen.GenderFilter.SelectedIndex = 2;
            Assert.Equal(2, screen.Visible.Count);
            Assert.Equal("2 clubs", host.LastStatus);
        });
    }
}

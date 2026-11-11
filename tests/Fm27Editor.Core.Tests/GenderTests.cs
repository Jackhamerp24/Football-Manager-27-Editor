using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Gender decoding and write-guard tests for person (+0x19 mask 0x10; player +0x288+0x19 = +0x2A1) and club (+0xB9: 0 men, non-zero not men's).
/// Live Steam 27.0.0+2493426 measurements (men-only save attached; `gamecheck` ran 66 features with 0 throws):
/// - Male player: Bruno Fernandes (ID 43124203), raw +0x2A1 = 0x02 (Men).
/// - Female staff person (+0x19): Sandra Rossi (ID 14124783, River Plate [Men]), raw +0x19 = 0x10 (Women); Daniela Gozzi (ID 3802120, Monza [Men]), raw +0x19 = 0x50 (Women).
/// - Men's club: Man Utd (ID 680), raw +0xB9 = 0x00 (Men); 22,197 clubs across Steam heap.
/// - Women's club: Man Utd women's club (ID 2000104574), raw +0xB9 = 0x01 (Women); 3,076 clubs across Steam heap (no other non-zero values).
/// Not measured live: open Steam save had no named female players loaded, and Game Pass 27.0.0+2493426 was not attached.
/// </summary>
public class GenderTests
{
    static readonly DateOnly Today = new(2026, 11, 10);

    static OffsetProfile LoadProfile(string file) =>
        OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", file));

    [Theory]
    [InlineData("27.0.0+2486695-steam.json")]
    [InlineData("27.0.0+2493426-steam.json")]
    [InlineData("27.0.0+2493426-gamepass.json")]
    public void Profiles_map_person_and_club_gender_offsets_and_round_trip(string file)
    {
        var original = LoadProfile(file);
        Assert.Equal(0x19, original.Relations?.PersonGender);
        Assert.Equal(0x10, original.Relations?.PersonGenderMask);
        Assert.Equal(0xB9, original.Club?.Gender);

        var copy = OffsetProfile.Load(original.ToJson());
        Assert.Equal(original.Relations?.PersonGender, copy.Relations?.PersonGender);
        Assert.Equal(original.Relations?.PersonGenderMask, copy.Relations?.PersonGenderMask);
        Assert.Equal(original.Club?.Gender, copy.Club?.Gender);
    }

    [Fact]
    public void Unmapped_person_and_club_gender_offsets_report_unknown()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-gamepass.json"));
        var strippedJson = System.Text.RegularExpressions.Regex.Replace(
            json,
            @"""(?:personGender|personGenderMask|gender)""\s*:\s*""[^""]*""\s*,?\r?\n",
            "");

        var copy = OffsetProfile.Load(strippedJson);
        Assert.Null(copy.Relations?.PersonGender);
        Assert.Null(copy.Club?.Gender);
        Assert.Equal(Gender.Unknown, GenderReader.FromPlayerBytes(new byte[0x3C8], copy.Relations));
        Assert.Equal(Gender.Unknown, GenderReader.FromPersonBytes(new byte[0xB0], copy.Relations));
        Assert.Equal(Gender.Unknown, GenderReader.FromClubBytes(new byte[0xD0], copy.Club?.Gender));
    }

    [Fact]
    public void PlayerReader_decodes_male_and_female_players_and_club_gender()
    {
        var w = new TestWorld();
        const ulong femaleAddr = 0x1002_0000;
        const ulong womenClub = 0x5000_0000, womenTeam = 0x5001_0000;
        const ulong menClub = 0x6000_0000, menTeam = 0x6001_0000;

        // Bruno Fernandes byte at +0x2A1 from PlayerInformationTests is 0x02 (bit 0x10 clear -> Men).
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + 0x2A1, [0x02]));

        // Female player with bit 0x10 set at person+0x19 (+0x2A1 = 0x12).
        var f = w.Memory.Map(femaleAddr, 0x3C8);
        TestWorld.Set64(f, 0, TestWorld.ModuleBase + TestWorld.PlayerVtable);
        BitConverter.GetBytes((short)130).CopyTo(f, w.Profile.Fields.CurrentAbility);
        BitConverter.GetBytes((short)140).CopyTo(f, w.Profile.Fields.PotentialAbility);
        BitConverter.GetBytes(999999).CopyTo(f, w.Profile.Fields.Id!.Value);
        f[w.Profile.Relations!.PlayerPerson + w.Profile.Relations.PersonGender!.Value] = 0x12;

        // Map a men's club (0xB9 = 0) and a women's club (0xB9 = 1) with teams and contracts.
        const ulong menContract = 0x5002_0000, womenContract = 0x5003_0000;
        var mc = w.Memory.Map(menClub, 0x160);
        TestWorld.Set64(mc, 0, TestWorld.ModuleBase + w.Profile.Club!.ClubVtableRva);
        BitConverter.GetBytes(680).CopyTo(mc, w.Profile.Club.Id!.Value);
        var mt = w.Memory.Map(menTeam, 0x50);
        TestWorld.Set64(mt, 0, TestWorld.ModuleBase + w.Profile.Team!.VtableRva);
        TestWorld.Set64(mt, w.Profile.Contract!.TeamClub, menClub);
        var mcon = w.Memory.Map(menContract, 0x60);
        TestWorld.Set64(mcon, 0, TestWorld.ModuleBase + w.Profile.Contract.ContractVtableRva);
        TestWorld.Set64(mcon, w.Profile.Contract.Team, menTeam);
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + (ulong)w.Profile.Team.PlayerTeam, BitConverter.GetBytes(menTeam)));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + (ulong)w.Profile.Contract.PlayerContract, BitConverter.GetBytes(menContract)));

        var wc = w.Memory.Map(womenClub, 0x160);
        TestWorld.Set64(wc, 0, TestWorld.ModuleBase + w.Profile.Club.ClubVtableRva);
        BitConverter.GetBytes(2000108253).CopyTo(wc, w.Profile.Club.Id!.Value);
        wc[w.Profile.Club.Gender!.Value] = 1;
        var wt = w.Memory.Map(womenTeam, 0x50);
        TestWorld.Set64(wt, 0, TestWorld.ModuleBase + w.Profile.Team.VtableRva);
        TestWorld.Set64(wt, w.Profile.Contract.TeamClub, womenClub);
        var wcon = w.Memory.Map(womenContract, 0x60);
        TestWorld.Set64(wcon, 0, TestWorld.ModuleBase + w.Profile.Contract.ContractVtableRva);
        TestWorld.Set64(wcon, w.Profile.Contract.Team, womenTeam);
        TestWorld.Set64(f, w.Profile.Team.PlayerTeam, womenTeam);
        TestWorld.Set64(f, w.Profile.Contract.PlayerContract, womenContract);

        // Register both in a new player list array and update TestWorld's mapped root vector in place.
        const ulong newList = 0x3100_0000;
        var list = w.Memory.Map(newList, 16);
        TestWorld.Set64(list, 0, TestWorld.Mainoo);
        TestWorld.Set64(list, 8, femaleAddr);
        ulong root = TestWorld.ModuleBase + w.Profile.PlayerListRootRva;
        Assert.True(w.Memory.TryWrite(root + (ulong)w.Profile.PlayerListBeginOffset, BitConverter.GetBytes(newList)));
        Assert.True(w.Memory.TryWrite(root + (ulong)w.Profile.PlayerListEndOffset, BitConverter.GetBytes(newList + 16)));

        var players = new PlayerRepository(w.Memory, w.Profile, TestWorld.ModuleBase).LoadAll();
        var mainoo = Assert.Single(players, p => p.Address == TestWorld.Mainoo);
        var woman = Assert.Single(players, p => p.Address == femaleAddr);

        Assert.Equal(Gender.Men, mainoo.Gender);
        Assert.Equal(Gender.Men, mainoo.Club?.Gender);
        Assert.Equal(Gender.Women, woman.Gender);
        Assert.Equal(Gender.Women, woman.Club?.Gender);

        var clubs = ClubDirectory.FromPlayers(w.Memory, players, w.Profile, TestWorld.ModuleBase);
        Assert.Equal(Gender.Men, Assert.Single(clubs, c => c.Address == menClub).Gender);
        Assert.Equal(Gender.Women, Assert.Single(clubs, c => c.Address == womenClub).Gender);
    }

    [Fact]
    public void StaffRepository_reads_person_gender_and_allows_female_staff_at_mens_club()
    {
        var memory = new FakeMemory();
        var profile = LoadProfile("27.0.0+2493426-gamepass.json");
        var S = profile.Staff!;
        var kind = S.Kinds[0]; // ordinary staff: staffPart = 0x100
        const ulong module = TestWorld.ModuleBase;

        const ulong clubAddr = 0x5000_0000, teamAddr = 0x5001_0000;
        var club = memory.Map(clubAddr, 0x160);
        TestWorld.Set64(club, 0, module + profile.Club!.ClubVtableRva);
        club[profile.Club.Gender!.Value] = 0; // Men's club
        var team = memory.Map(teamAddr, 0x50);
        TestWorld.Set64(team, 0, module + profile.Team!.VtableRva);
        TestWorld.Set64(team, profile.Contract!.TeamClub, clubAddr);

        ulong MakeStaff(ulong baseAddr, int id, byte genderByte)
        {
            ulong person = baseAddr + (ulong)kind.StaffPart;
            var block = memory.Map(baseAddr, kind.StaffPart + 0xB0);
            TestWorld.Set64(block, 0, module + kind.StaffVtableRva);
            TestWorld.Set64(block, kind.StaffPart, module + kind.PersonVtableRva);
            BitConverter.GetBytes(id).CopyTo(block, kind.StaffPart + S.Id);
            block[kind.StaffPart + profile.Relations!.PersonGender!.Value] = genderByte;
            BitConverter.GetBytes((short)140).CopyTo(block, S.CurrentAbility);
            BitConverter.GetBytes((short)150).CopyTo(block, S.PotentialAbility);

            ulong jobAddr = baseAddr + 0x2000;
            var job = memory.Map(jobAddr, 0x30);
            TestWorld.Set64(job, S.JobPerson, person);
            TestWorld.Set64(job, S.JobTeam, teamAddr);
            BitConverter.GetBytes((short)38).CopyTo(job, S.JobCode); // Chief Doctor
            TestWorld.Set64(block, kind.StaffPart + S.Job, jobAddr);
            return S.DirectStaffVector ? baseAddr : person;
        }

        ulong maleEntry = MakeStaff(0x2000_0000, 1001, 0x00);
        ulong femaleEntry = MakeStaff(0x2001_0000, 1002, 0x10);
        var list = memory.Map(0x3000_0000, 16);
        TestWorld.Set64(list, 0, maleEntry);
        TestWorld.Set64(list, 8, femaleEntry);
        if (S.DirectStaffVector)
        {
            var rootVec = memory.Map(module + S.PersonListRootRva, S.PersonListOffset + 16);
            TestWorld.Set64(rootVec, S.PersonListOffset, 0x3000_0000);
            TestWorld.Set64(rootVec, S.PersonListOffset + 8, 0x3000_0010);
        }
        else
        {
            const ulong indirectRoot = 0x6000_0000;
            TestWorld.Set64(memory.Map(module + S.PersonListRootRva, 8), 0, indirectRoot);
            var rootVec = memory.Map(indirectRoot, S.PersonListOffset + 16);
            TestWorld.Set64(rootVec, S.PersonListOffset, 0x3000_0000);
            TestWorld.Set64(rootVec, S.PersonListOffset + 8, 0x3000_0010);
        }

        var loaded = new StaffRepository(memory, profile, module).LoadAll();
        var m = Assert.Single(loaded, s => s.Id == 1001);
        var f = Assert.Single(loaded, s => s.Id == 1002);

        Assert.Equal(Gender.Men, m.Gender);
        Assert.Equal(Gender.Women, f.Gender);
        Assert.Equal(Gender.Men, f.Club?.Gender); // Female staff at a men's club

        Assert.True(StaffSearch.Matches(m, new StaffSearchCriteria(Gender: Gender.Men, ExcludeWomenClubs: true), Today));
        Assert.False(StaffSearch.Matches(f, new StaffSearchCriteria(Gender: Gender.Men, ExcludeWomenClubs: true), Today));
        Assert.True(StaffSearch.Matches(f, new StaffSearchCriteria(Gender: Gender.Women, ExcludeWomenClubs: true), Today));
    }

    [Fact]
    public void ClubSearch_filters_men_and_women_clubs()
    {
        var men = new ClubEntry(0x1000, 602, "Arsenal", "Arsenal", 0, [], Gender.Men);
        var women = new ClubEntry(0x2000, 200001, "Arsenal", "Arsenal Women", 0, [], Gender.Women);

        Assert.True(ClubSearch.Matches(men, new ClubSearchCriteria(Gender: Gender.Men), _ => null));
        Assert.False(ClubSearch.Matches(women, new ClubSearchCriteria(Gender: Gender.Men), _ => null));
        Assert.True(ClubSearch.Matches(women, new ClubSearchCriteria(Gender: Gender.Women), _ => null));
    }

    [Fact]
    public void Transfer_loan_future_and_swap_reject_female_players_and_womens_clubs_before_writing()
    {
        var w = new TestWorld();
        var profile = LoadProfile("27.0.0+2493426-gamepass.json");
        const ulong module = TestWorld.ModuleBase;

        const ulong sellerClub = 0x5000_0000, sellerFin = 0x5001_0000, sellerTeam = 0x5002_0000, sellerSquad = 0x5003_0000;
        const ulong buyerClub = 0x6000_0000, buyerFin = 0x6001_0000, buyerTeam = 0x6002_0000, buyerSquad = 0x6003_0000;
        const ulong mainooContract = 0x7000_0000, yamalContract = 0x7000_1000;

        void MapClub(ulong clubAddr, ulong finAddr, int id, byte genderByte)
        {
            var c = w.Memory.Map(clubAddr, 0x160);
            TestWorld.Set64(c, 0, module + profile.Club!.ClubVtableRva);
            BitConverter.GetBytes(id).CopyTo(c, profile.Club.Id!.Value);
            TestWorld.Set64(c, profile.Club.Finance!.Pointer, finAddr);
            c[profile.Club.Gender!.Value] = genderByte;
            var f = w.Memory.Map(finAddr, 0x820);
            TestWorld.Set64(f, 0, module + profile.Club.Finance.VtableRva!.Value);
        }

        void MapTeam(ulong teamAddr, ulong clubAddr, ulong squadAddr, ulong playerAddr)
        {
            var t = w.Memory.Map(teamAddr, 0x50);
            TestWorld.Set64(t, 0, module + profile.Team!.VtableRva);
            TestWorld.Set64(t, profile.Contract!.TeamClub, clubAddr);
            TestWorld.Set64(t, profile.Team.Squad, squadAddr);
            TestWorld.Set64(t, profile.Team.Squad + 8, squadAddr + 8);
            TestWorld.Set64(t, profile.Team.Squad + 16, squadAddr + 16);
            TestWorld.Set64(w.Memory.Map(squadAddr, 16), 0, playerAddr);
        }

        void MapContract(ulong contractAddr, ulong teamAddr)
        {
            var c = w.Memory.Map(contractAddr, 0x60);
            TestWorld.Set64(c, 0, module + profile.Contract!.ContractVtableRva);
            TestWorld.Set64(c, profile.Contract.Team, teamAddr);
            BitConverter.GetBytes(10000).CopyTo(c, profile.Contract.Wage);
        }

        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo, BitConverter.GetBytes(module + profile.PlayerVtableRvas[0])));
        Assert.True(w.Memory.TryWrite(TestWorld.Yamal, BitConverter.GetBytes(module + profile.PlayerVtableRvas[0])));
        MapClub(sellerClub, sellerFin, 680, 0);
        MapClub(buyerClub, buyerFin, 868, 1); // Buyer is a women's club
        MapTeam(sellerTeam, sellerClub, sellerSquad, TestWorld.Mainoo);
        MapTeam(buyerTeam, buyerClub, buyerSquad, TestWorld.Yamal);
        MapContract(mainooContract, sellerTeam);
        MapContract(yamalContract, buyerTeam);
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + (ulong)profile.Contract!.PlayerContract, BitConverter.GetBytes(mainooContract)));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + (ulong)profile.Team!.PlayerTeam, BitConverter.GetBytes(sellerTeam)));
        Assert.True(w.Memory.TryWrite(TestWorld.Yamal + (ulong)profile.Contract.PlayerContract, BitConverter.GetBytes(yamalContract)));
        Assert.True(w.Memory.TryWrite(TestWorld.Yamal + (ulong)profile.Team.PlayerTeam, BitConverter.GetBytes(buyerTeam)));

        var reader = new PlayerReader(w.Memory, profile) { ModuleBase = module };
        var mainoo = reader.Decode(TestWorld.Mainoo, w.Memory.Read(TestWorld.Mainoo, profile.PlayerObjectSize), profile.PlayerVtableRvas[0])!;
        var clubs = new[]
        {
            new ClubEntry(sellerClub, 680, "Man Utd", "Man Utd", sellerFin, [new TeamEntry(sellerTeam, sellerClub, sellerSquad, 1, 2, true)], Gender.Men),
            new ClubEntry(buyerClub, 868, "Buyer Women", "Buyer", buyerFin, [new TeamEntry(buyerTeam, buyerClub, buyerSquad, 1, 2, true)], Gender.Women),
        };

        var transfers = new TransferService(w.Memory, profile, module);
        Assert.Throws<InvalidOperationException>(() => transfers.Plan(mainoo, clubs, clubs[1], 0));

        var loans = new LoanCreator(w.Memory, profile, module);
        var offer = new LoanOffer(5000, new(2026, 8, 1), new(2027, 6, 30), 0, 0, 0, 0);
        Assert.Throws<InvalidOperationException>(() => loans.Plan(mainoo, buyerTeam, offer, [mainoo]));

        var future = new FutureTransferCreator(w.Memory, profile, module);
        Assert.Throws<InvalidOperationException>(() => future.Prepare(mainoo, buyerTeam, 0, new(2026, 8, 1), new(2027, 1, 1)));

        var swap = new PlayerSwapService(w.Memory, profile, module);
        Assert.Throws<InvalidOperationException>(() => swap.Plan(mainoo, new SwapTarget(TestWorld.Yamal, buyerClub, buyerTeam), clubs));

        // Now mark buyer men's, but flag player as female (+0x2A1 |= 0x10).
        Assert.True(w.Memory.TryWrite(buyerClub + (ulong)profile.Club!.Gender!.Value, [0]));
        Assert.True(w.Memory.TryWrite(TestWorld.Mainoo + (ulong)(profile.Relations!.PlayerPerson + profile.Relations.PersonGender!.Value), [0x10]));

        Assert.Throws<InvalidOperationException>(() => transfers.Plan(mainoo, clubs, clubs[1] with { Gender = Gender.Men }, 0));
        Assert.Throws<InvalidOperationException>(() => loans.Plan(mainoo, buyerTeam, offer, [mainoo]));
        Assert.Throws<InvalidOperationException>(() => future.Prepare(mainoo, buyerTeam, 0, new(2026, 8, 1), new(2027, 1, 1)));
        Assert.Throws<InvalidOperationException>(() => swap.Plan(mainoo, new SwapTarget(TestWorld.Yamal, buyerClub, buyerTeam), clubs));
    }

    [Fact]
    public void EnsureMalePlayer_and_EnsureMenClub_fail_closed_when_unreadable()
    {
        var profile = LoadProfile("27.0.0+2493426-steam.json");
        var memory = new FakeMemory();
        const ulong playerAddr = 0x1000_0000;
        const ulong clubAddr = 0x2000_0000;

        // Unreadable memory / address 0 must fail closed once offsets are mapped.
        Assert.Throws<MemoryAccessException>(() => GenderGuard.EnsureMalePlayer(memory, profile, playerAddr));
        Assert.Throws<MemoryAccessException>(() => GenderGuard.EnsureMalePlayer(memory, profile, 0));
        Assert.Throws<MemoryAccessException>(() => GenderGuard.EnsureMenClub(memory, profile, clubAddr));

        // Readable male player passes; readable female player is rejected.
        var p = memory.Map(playerAddr, profile.PlayerObjectSize);
        int gOff = profile.Relations!.PlayerPerson + profile.Relations.PersonGender!.Value;
        p[gOff] = 0x02;
        GenderGuard.EnsureMalePlayer(memory, profile, playerAddr);

        p[gOff] = 0x12;
        Assert.Throws<InvalidOperationException>(() => GenderGuard.EnsureMalePlayer(memory, profile, playerAddr));

        // When person gender offsets are not mapped in the profile, EnsureMalePlayer has nothing to check.
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-steam.json"));
        var unmappedJson = System.Text.RegularExpressions.Regex.Replace(
            json,
            @"""(?:personGender|personGenderMask)""\s*:\s*""[^""]*""\s*,?\r?\n",
            "");
        var unmapped = OffsetProfile.Load(unmappedJson);
        GenderGuard.EnsureMalePlayer(new FakeMemory(), unmapped, playerAddr);
    }

    [Fact]
    public void Club_gender_treats_zero_as_men_and_any_non_zero_as_not_mens_club()
    {
        var profile = LoadProfile("27.0.0+2493426-gamepass.json");
        Assert.Equal(Gender.Men, GenderReader.FromClubByte(0));
        Assert.Equal(Gender.Women, GenderReader.FromClubByte(1));
        Assert.Equal(Gender.Women, GenderReader.FromClubByte(0x7F));

        var memory = new FakeMemory();
        const ulong clubAddr = 0x3000_0000;
        var c = memory.Map(clubAddr, 0xD0);
        int genderOff = profile.Club!.Gender!.Value;

        c[genderOff] = 0;
        GenderGuard.EnsureMenClub(memory, profile, clubAddr);

        c[genderOff] = 1;
        Assert.Throws<InvalidOperationException>(() => GenderGuard.EnsureMenClub(memory, profile, clubAddr));

        c[genderOff] = 0x7F;
        Assert.Throws<InvalidOperationException>(() => GenderGuard.EnsureMenClub(memory, profile, clubAddr));
    }
}

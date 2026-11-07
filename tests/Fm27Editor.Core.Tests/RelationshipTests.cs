using Fm27Editor.Core.Players;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Likes / dislikes from Kylian Mbappé's relation records as dumped on 22/11/2026 (player + 0x300 → holder; inline 16-byte
/// records, bytes +0x8..+0xF as dumped; targets re-pointed at fake persons and clubs). Expected values are FMRTE 27's
/// Relationships tab: Ethan Mbappé 100 Sibling Permanent, Neymar 50 Teammate not Permanent, Thierry Henry (staff) 25 Idol,
/// Real Madrid 100 Supporter, Dean Huijsen disliked 20. Ethan's full name is "Ethan Mbappé Lottin"; FMRTE shows first + last.
/// </summary>
public class RelationshipTests
{
    const uint PersonVt = 0x4D27758, StaffVt = 0x4D8BE78, ClubVt = 0x4CDA1F8;
    const ulong Holder = 0x5000_0000, Records = 0x5001_0000, Names = 0x5100_0000;
    readonly TestWorld _w = new();
    ulong _next = Names;

    ulong Text(string s) { ulong at = _next; _next += 0x100; _w.Memory.MapString(at, s); return at; }
    ulong Entry(string s) { ulong at = _next; _next += 0x100; TestWorld.Set64(_w.Memory.Map(at, 8), 0, Text(s)); return at; }

    ulong Person(uint vt, string? full, string? first, string? last, string? common)
    {
        ulong at = _next; _next += 0x100;
        var p = _w.Memory.Map(at, 0x70);
        TestWorld.Set64(p, 0, TestWorld.ModuleBase + vt);
        if (full != null) TestWorld.Set64(p, 0x40, Text(full));
        if (first != null) TestWorld.Set64(p, 0x50, Entry(first));
        if (last != null) TestWorld.Set64(p, 0x58, Entry(last));
        if (common != null) TestWorld.Set64(p, 0x60, Entry(common));
        return at;
    }

    public RelationshipTests()
    {
        ulong club = _next; _next += 0x100;
        var c = _w.Memory.Map(club, 0xD0);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + ClubVt);
        TestWorld.Set64(c, 0xC0, Text("Real Madrid Club de Fútbol")); TestWorld.Set64(c, 0xC8, Text("Real Madrid"));
        (ulong Target, string Tail)[] rows =
        [
            (Person(PersonVt, "Ethan Mbappé Lottin", "Ethan", "Mbappé", null), "02000301644F00FF"),
            (Person(PersonVt, null, null, null, "Neymar"), "05000301320000FF"),
            (Person(StaffVt, null, "Thierry", "Henry", null), "09000301194F00FF"),
            (club, "01000103644E00FF"),
            (Person(PersonVt, "Dean Donny Huijsen", "Dean", "Huijsen", null), "00000302140000FF"),
            (club, "0000041913 0000FF".Replace(" ", "")), // registration: not a relationship
        ];
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x300, BitConverter.GetBytes(Holder)));
        var h = _w.Memory.Map(Holder, 0x18);
        ulong end = Records + (ulong)rows.Length * 16;
        TestWorld.Set64(h, 0, Records); TestWorld.Set64(h, 8, end); TestWorld.Set64(h, 0x10, end);
        var r = _w.Memory.Map(Records, rows.Length * 16);
        for (int i = 0; i < rows.Length; i++)
        {
            TestWorld.Set64(r, i * 16, rows[i].Target);
            Convert.FromHexString(rows[i].Tail).CopyTo(r, i * 16 + 8);
        }
    }

    PlayerRecord Player() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    IReadOnlyList<PlayerRelationship> Read() => new PlayerStatusReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Player()).Relationships;

    [Fact]
    public void Reads_likes_and_dislikes_as_FMRTE_shows_them()
    {
        var r = Read();
        Assert.Equal(["Ethan Mbappé", "Neymar", "Thierry Henry", "Real Madrid", "Dean Huijsen"], r.Select(x => x.Name));
        Assert.Equal([RelationshipKind.LikesPerson, RelationshipKind.LikesPerson, RelationshipKind.LikesPerson, RelationshipKind.LikesTeam, RelationshipKind.DislikesPerson], r.Select(x => x.Kind));
        Assert.Equal([100, 50, 25, 100, 20], r.Select(x => x.Value));
        Assert.Equal([2, 5, 9, 1, 0], r.Select(x => x.Reason)); // Sibling, Teammate, Idol, Supporter
        Assert.Equal([0x4F, 0x00, 0x4F, 0x4E, 0x00], r.Select(x => x.Flags));
    }

    [Fact]
    public void SearchRelationshipUsesTargetAndValueAndReportsUnreadableTarget()
    {
        var player = Player();
        ulong ethan = BitConverter.ToUInt64(_w.Bytes(Records, 8));
        var search = new PlayerRelationSearch(new PlayerStatusReader(_w.Memory, _w.Profile, TestWorld.ModuleBase));
        Assert.Equal(new[] { player.Address }, search.Find([player], new(Relationship: RelationshipKind.LikesPerson, RelationshipTarget: ethan, MinimumValue: 100)).Matches.Select(p => p.Address));
        Assert.Empty(search.Find([player], new(Relationship: RelationshipKind.DislikesPerson, RelationshipTarget: ethan)).Matches);
        Assert.True(_w.Memory.TryWrite(ethan, new byte[8]));
        var unknown = search.Find([player], new(Relationship: RelationshipKind.LikesPerson, RelationshipTarget: ethan));
        Assert.Empty(unknown.Matches);
        Assert.Equal(new[] { player.Address }, unknown.Unavailable.Select(p => p.Address));
    }

    [Fact]
    public void Value_writes_one_byte_and_refuses_stale_rows()
    {
        var writer = new RelationshipWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var before = _w.Bytes(Records, 6 * 16);
        var r = Read();
        writer.SetValue(Player(), r[0], 99); // FMRTE showed Ethan 99 after this write
        before[0xC] = 99;
        Assert.Equal(before, _w.Bytes(Records, 6 * 16));
        Assert.Throws<StaleObjectException>(() => writer.SetValue(Player(), r[0], 98));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.SetValue(Player(), Read()[1], 101));
        var olderHolder = Read()[1];
        Assert.True(_w.Memory.TryWrite(Holder + 8, BitConverter.GetBytes(Records + 16)));
        Assert.Throws<StaleObjectException>(() => writer.SetValue(Player(), olderHolder, 40));
    }

    // Expected bytes below are FMRTE 27's own writes on Harry Kane (2/11/2026, licensed): adding Bukayo Saka as Likes Person,
    // Teammate, 77, Permanent gave "05 00 03 01 4D 4F 00 FF"; then Backroom Staff, not Permanent gave "08 00 03 01 4D 00 00 FF";
    // Tottenham (Likes Team, 01 00 01 03 64 4E) Supporter → Manager gave "03 00 01 03 64 4E"; adding Arsenal Likes Team 50
    // No Reason gave "00 00 01 03 32 4F 00 FF"; removing a row moved only the end pointer back 16 (cap unchanged).

    const ulong Pool = 0x7000_0000;
    ulong _pool = Pool;
    readonly List<int> _allocations = [];
    ulong Allocate(int bytes) { _allocations.Add(bytes); ulong at = _pool; _w.Memory.Map(at, bytes); _pool += 0x1_0000; return at; }
    RelationshipWriter Writer() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);
    PlayerStatuses Statuses() => new PlayerStatusReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Player());
    byte[] Row(ulong target, string tail) => [.. BitConverter.GetBytes(target), .. Convert.FromHexString(tail)];

    [Fact]
    public void NationalitySearchIncludesObservedOtherNationalityAndReportsInvalidTargets()
    {
        const ulong portugal = 0x7200_0000, france = 0x7200_0100;
        TestWorld.Set64(_w.Memory.Map(portugal, 0x30), 0, TestWorld.ModuleBase + 0x4CDF658);
        Assert.True(_w.Memory.TryWrite(Records + 5 * 16, Row(portugal, "00000809645000FF")));
        var original = Player();
        var player = new PlayerRecord { Address = original.Address, VtableRva = original.VtableRva,
            CurrentAbility = original.CurrentAbility, PotentialAbility = original.PotentialAbility,
            HomeReputation = original.HomeReputation, CurrentReputation = original.CurrentReputation,
            WorldReputation = original.WorldReputation, Height = original.Height,
            RawAttributes = original.RawAttributes, AttributeScale = original.AttributeScale,
            Positions = original.Positions, Personality = original.Personality,
            Nationality = new NationInfo(france, "France", "France") };
        var search = new PlayerNationalitySearch(new PlayerStatusReader(_w.Memory, _w.Profile, TestWorld.ModuleBase));
        var pt = new NationRecord(portugal, 788, "Portugal", null);
        var fr = new NationRecord(france, 769, "France", null);

        Assert.Single(search.Find([player], pt, true).Matches);
        Assert.Empty(search.Find([player], pt, false).Matches);
        Assert.Single(search.Find([player], fr, false).Matches);
        Assert.Empty(search.Find([player], new NationRecord(0x72000200, 765, "England", null), true).Unavailable);
        Assert.True(_w.Memory.TryWrite(portugal, new byte[8]));
        var invalid = search.Find([player], pt, true);
        Assert.Empty(invalid.Matches);
        Assert.Single(invalid.Unavailable);
    }

    [Fact]
    public void Add_other_nationality_for_a_player_writes_the_observed_record()
    {
        const ulong nation = 0x7200_0000;
        TestWorld.Set64(_w.Memory.Map(nation, 0x30), 0, TestWorld.ModuleBase + 0x4CDF658);
        var before = _w.Bytes(Records, 6 * 16);
        Writer().AddOtherNationality(PersonOwner.Of(Player(), _w.Profile), Statuses(), nation, 80, Allocate);
        Assert.Equal([.. before, .. Row(nation, "00000809645000FF")], _w.Bytes(Pool, 7 * 16));
        Assert.Equal(before, _w.Bytes(Records, 6 * 16));
    }

    [Fact]
    public void Add_person_to_a_full_vector_writes_FMRTEs_record_into_a_new_buffer()
    {
        var old = _w.Bytes(Records, 6 * 16);
        ulong saka = Person(PersonVt, "Bukayo Ayoyinka Temidayo Saka", "Bukayo", "Saka", null);
        Writer().Add(Player(), Statuses(), RelationshipKind.LikesPerson, saka, 77, 5, permanent: true, Allocate);

        Assert.Equal([12 * 16], _allocations);
        Assert.Equal([.. old, .. Row(saka, "050003014D4F00FF")], _w.Bytes(Pool, 7 * 16));
        Assert.Equal([.. BitConverter.GetBytes(Pool), .. BitConverter.GetBytes(Pool + 7 * 16), .. BitConverter.GetBytes(Pool + 12 * 16)], _w.Bytes(Holder, 24));
        Assert.Equal(old, _w.Bytes(Records, 6 * 16));
        var added = Read()[^1];
        Assert.Equal(("Bukayo Saka", 77, 5, 0x4F), (added.Name, added.Value, added.Reason, added.Flags));
    }

    [Fact]
    public void Add_team_with_spare_capacity_appends_in_place()
    {
        ulong arsenal = _next; _next += 0x100;
        var c = _w.Memory.Map(arsenal, 0xD0);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + ClubVt);
        TestWorld.Set64(c, 0xC8, Text("Arsenal"));
        const ulong Roomy = 0x5002_0000; // same six records in a buffer with room for a seventh
        _w.Memory.Map(Roomy, 7 * 16);
        Assert.True(_w.Memory.TryWrite(Roomy, _w.Bytes(Records, 6 * 16)));
        Assert.True(_w.Memory.TryWrite(Holder, [.. BitConverter.GetBytes(Roomy), .. BitConverter.GetBytes(Roomy + 6 * 16), .. BitConverter.GetBytes(Roomy + 7 * 16)]));
        Writer().Add(Player(), Statuses(), RelationshipKind.LikesTeam, arsenal, 50, 0, permanent: false, Allocate);

        Assert.Empty(_allocations);
        Assert.Equal(Row(arsenal, "00000103324F00FF"), _w.Bytes(Roomy + 6 * 16, 16));
        Assert.Equal(Roomy + 7 * 16, BitConverter.ToUInt64(_w.Bytes(Holder + 8, 8)));
        Assert.Equal("Arsenal", Read()[^1].Name);
    }

    [Fact]
    public void Details_write_reason_and_permanent_like_FMRTE()
    {
        var all = _w.Bytes(Records, 6 * 16);
        var r = Read();
        Writer().SetDetails(Player(), r[0], 8, permanent: false);           // Ethan: Sibling, Permanent → Backroom Staff
        Writer().SetDetails(Player(), Read()[3], 3, permanent: false);      // Real Madrid: Supporter → Manager, flags kept
        Convert.FromHexString("08000301640000FF").CopyTo(all, 8);
        Convert.FromHexString("03000103644E00FF").CopyTo(all, 3 * 16 + 8);
        Assert.Equal(all, _w.Bytes(Records, 6 * 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetDetails(Player(), Read()[3], 5, false)); // teams: 0–4
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetDetails(Player(), Read()[1], 12, false)); // people: 0–11
        Assert.Throws<StaleObjectException>(() => Writer().SetDetails(Player(), r[0], 6, true)); // Ethan's row changed since r was read
    }

    [Fact]
    public void Remove_moves_later_records_up_and_the_end_back()
    {
        var all = _w.Bytes(Records, 6 * 16);
        Writer().Remove(Player(), Read()[1]); // Neymar
        Assert.Equal([.. all[..16], .. all[32..]], _w.Bytes(Records, 5 * 16));
        Assert.Equal(Records + 5 * 16, BitConverter.ToUInt64(_w.Bytes(Holder + 8, 8)));
        Assert.Equal(Records + 6 * 16, BitConverter.ToUInt64(_w.Bytes(Holder + 16, 8)));
        Assert.Equal(["Ethan Mbappé", "Thierry Henry", "Real Madrid", "Dean Huijsen"], Read().Select(x => x.Name));
    }

    [Fact]
    public void Add_refuses_duplicates_wrong_targets_and_stale_vectors()
    {
        var s = Statuses();
        ulong ethan = BitConverter.ToUInt64(_w.Bytes(Records, 8));
        ulong club = BitConverter.ToUInt64(_w.Bytes(Records + 3 * 16, 8));
        Assert.Throws<InvalidOperationException>(() => Writer().Add(Player(), s, RelationshipKind.LikesPerson, ethan, 50, 0, false, Allocate));
        Assert.Throws<ArgumentException>(() => Writer().Add(Player(), s, RelationshipKind.LikesPerson, club, 50, 0, false, Allocate));
        Assert.Throws<ArgumentException>(() => Writer().Add(Player(), s, RelationshipKind.LikesTeam, ethan, 50, 0, false, Allocate));
        Writer().Remove(Player(), Read()[4]);
        var header = _w.Bytes(Holder, 24);
        Assert.Throws<StaleObjectException>(() => Writer().Add(Player(), s, RelationshipKind.DislikesPerson, ethan, 50, 0, false, Allocate));
        Assert.Equal(header, _w.Bytes(Holder, 24));
        Assert.Empty(_allocations);
    }

    // "Retired From Int. Foot.": every one of the 88 game-made type-0xA records on the 15/8/2026 save is
    // 00000000000000000000000A64FF00FF (Toni Micevski, Kazuyoshi Miura …); FMRTE 27 wrote the same bytes (over row 0).
    const string Retirement = "00000000000000000000000A64FF00FF";

    [Fact]
    public void International_retirement_is_read_apart_from_relationships()
    {
        Assert.Null(Statuses().InternationalRetirement);
        Assert.True(_w.Memory.TryWrite(Records + 5 * 16, Convert.FromHexString(Retirement)));
        var s = Statuses();
        Assert.Equal(Records + 5 * 16, s.InternationalRetirement?.Address);
        Assert.Equal(5, s.Relationships.Count);
    }

    [Fact]
    public void Ticking_retirement_appends_the_game_record_and_unticking_removes_it()
    {
        var old = _w.Bytes(Records, 6 * 16);
        Writer().AddInternationalRetirement(Player(), Statuses(), Allocate);
        Assert.Equal([.. old, .. Convert.FromHexString(Retirement)], _w.Bytes(Pool, 7 * 16));
        Assert.Equal(old, _w.Bytes(Records, 6 * 16));
        var s = Statuses();
        Assert.Equal(Pool + 6 * 16, s.InternationalRetirement?.Address);
        Assert.Throws<InvalidOperationException>(() => Writer().AddInternationalRetirement(Player(), s, Allocate));

        Writer().Remove(Player(), s.InternationalRetirement!);
        Assert.Null(Statuses().InternationalRetirement);
        Assert.Equal(Pool + 6 * 16, BitConverter.ToUInt64(_w.Bytes(Holder + 8, 8)));
        Assert.Equal(old, _w.Bytes(Pool, 6 * 16));
    }

    [Fact]
    public void Ticking_retirement_refuses_a_stale_vector()
    {
        var s = Statuses();
        Assert.True(_w.Memory.TryWrite(Holder + 8, BitConverter.GetBytes(Records + 5 * 16)));
        var header = _w.Bytes(Holder, 24);
        Assert.Throws<StaleObjectException>(() => Writer().AddInternationalRetirement(Player(), s, Allocate));
        Assert.Equal(header, _w.Bytes(Holder, 24));
        Assert.Empty(_allocations);
    }

    // Presets. Expected rows: FMRTE 27's own "Register for UEFA" write on Bruno Fernandes (team, 00 00 04 19 00 00 00 FF),
    // and the game's most common formed-at rows (club 00 00 01 48 01 FF 00 FF, nation 00 00 08 46 01 FF 00 FF) on 15/8/2026.
    const uint NationVt = 0x4CDF658, TeamVt = 0x4CE1108;

    ulong Obj(uint vt) { ulong at = _next; _next += 0x100; TestWorld.Set64(_w.Memory.Map(at, 0x100), 0, TestWorld.ModuleBase + vt); return at; }

    [Fact]
    public void Formed_at_and_UEFA_presets_append_the_game_rows_once()
    {
        var old = _w.Bytes(Records, 6 * 16);
        ulong club = Obj(ClubVt), nation = Obj(NationVt), team = Obj(TeamVt);
        Assert.True(Writer().AddTrainedAtClub(Player(), Statuses(), club, Allocate));
        Assert.True(Writer().AddTrainedAtNation(Player(), Statuses(), nation, Allocate));
        Assert.True(Writer().AddUefaRegistration(Player(), Statuses(), team, Allocate));
        Assert.False(Writer().AddTrainedAtClub(Player(), Statuses(), club, Allocate));
        Assert.False(Writer().AddTrainedAtNation(Player(), Statuses(), nation, Allocate));
        Assert.False(Writer().AddUefaRegistration(Player(), Statuses(), team, Allocate));

        Assert.Equal([.. old, .. Row(club, "0000014801FF00FF"), .. Row(nation, "0000084601FF00FF"), .. Row(team, "00000419000000FF")], _w.Bytes(Pool, 9 * 16));
        Assert.Equal(Pool + 9 * 16, BitConverter.ToUInt64(_w.Bytes(Holder + 8, 8)));
        Assert.Equal(old, _w.Bytes(Records, 6 * 16));
        Assert.Equal([12 * 16], _allocations);
    }

    [Fact]
    public void UEFA_registration_is_added_next_to_a_domestic_one_for_the_same_team()
    {
        ulong team = Obj(TeamVt);
        // Bruno's domestic row: same team, code 8.
        Assert.True(_w.Memory.TryWrite(Records + 5 * 16, Row(team, "00000419080000FF")));
        Assert.True(Writer().AddUefaRegistration(Player(), Statuses(), team, Allocate));
        Assert.Equal(Row(team, "00000419000000FF"), _w.Bytes(Pool + 6 * 16, 16));
    }

    [Fact]
    public void Presets_refuse_wrong_targets_and_stale_vectors()
    {
        ulong club = Obj(ClubVt), nation = Obj(NationVt);
        var s = Statuses();
        Assert.Throws<ArgumentException>(() => Writer().AddTrainedAtClub(Player(), s, nation, Allocate));
        Assert.Throws<ArgumentException>(() => Writer().AddTrainedAtNation(Player(), s, club, Allocate));
        Assert.Throws<ArgumentException>(() => Writer().AddUefaRegistration(Player(), s, club, Allocate));
        Assert.True(_w.Memory.TryWrite(Holder + 8, BitConverter.GetBytes(Records + 5 * 16)));
        Assert.Throws<StaleObjectException>(() => Writer().AddTrainedAtClub(Player(), s, club, Allocate));
        Assert.Empty(_allocations);
    }

    // Team cohesion: Kobbie Mainoo's entries as dumped (Man Utd index 0x199 kind 1 value 87, England 0x8B kind 2 value 67);
    // FMRTE 27's Improve Team Cohesion wrote 87 → 100 and left the England entry alone.
    [Fact]
    public void Team_cohesion_sets_only_the_clubs_entry()
    {
        const ulong Cohesion = 0x5002_0000;
        ulong club = Obj(ClubVt);
        Assert.True(_w.Memory.TryWrite(club + 8, BitConverter.GetBytes(0x199)));
        _w.Memory.Map(Holder + 0x18, 0x18);
        Assert.True(_w.Memory.TryWrite(Holder + 0x18, [.. BitConverter.GetBytes(Cohesion), .. BitConverter.GetBytes(Cohesion + 16), .. BitConverter.GetBytes(Cohesion + 16)]));
        _w.Memory.Map(Cohesion, 16);
        Assert.True(_w.Memory.TryWrite(Cohesion, Convert.FromHexString("99010000015700008B00000002430000")));
        var writer = new TeamCohesionWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        Assert.Equal(87, writer.Read(Player(), club));
        Assert.True(writer.Set(Player(), club, TeamCohesionWriter.Improve));
        Assert.Equal(Convert.FromHexString("99010000016400008B00000002430000"), _w.Bytes(Cohesion, 16));
        Assert.True(writer.Set(Player(), club, TeamCohesionWriter.Destroy));
        Assert.Equal(1, writer.Read(Player(), club));

        // England's index with kind 2 is not a club entry; a club with no entry changes nothing.
        Assert.True(_w.Memory.TryWrite(club + 8, BitConverter.GetBytes(0x8B)));
        Assert.False(writer.Set(Player(), club, TeamCohesionWriter.Improve));
        Assert.Equal(Convert.FromHexString("99010000010100008B00000002430000"), _w.Bytes(Cohesion, 16));
    }

    [Fact]
    public void Team_cohesion_rejects_changed_row_and_vector_without_writing()
    {
        const ulong cohesion = 0x5002_0000;
        ulong club = Obj(ClubVt);
        Assert.True(_w.Memory.TryWrite(club + 8, BitConverter.GetBytes(0x199)));
        _w.Memory.Map(Holder + 0x18, 0x18);
        Assert.True(_w.Memory.TryWrite(Holder + 0x18, [.. BitConverter.GetBytes(cohesion), .. BitConverter.GetBytes(cohesion + 16), .. BitConverter.GetBytes(cohesion + 16)]));
        _w.Memory.Map(cohesion, 16);
        Assert.True(_w.Memory.TryWrite(cohesion, Convert.FromHexString("99010000015700008B00000002430000")));
        var writer = new TeamCohesionWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var snapshot = Assert.IsType<TeamCohesionSnapshot>(writer.Capture(Player(), club));

        Assert.True(_w.Memory.TryWrite(cohesion + 5, [88]));
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.Set(Player(), club, snapshot, 100));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(88, _w.Bytes(cohesion + 5, 1)[0]);

        Assert.True(_w.Memory.TryWrite(cohesion + 5, [87]));
        Assert.True(_w.Memory.TryWrite(Holder + 0x20, BitConverter.GetBytes(cohesion + 8)));
        writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.Set(Player(), club, snapshot, 100));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(87, _w.Bytes(cohesion + 5, 1)[0]);
    }

    [Fact]
    public void Team_cohesion_rejects_changed_vector_capacity_without_writing()
    {
        const ulong cohesion = 0x5002_0000;
        ulong club = Obj(ClubVt);
        Assert.True(_w.Memory.TryWrite(club + 8, BitConverter.GetBytes(0x199)));
        _w.Memory.Map(Holder + 0x18, 0x18);
        Assert.True(_w.Memory.TryWrite(Holder + 0x18, [.. BitConverter.GetBytes(cohesion), .. BitConverter.GetBytes(cohesion + 16), .. BitConverter.GetBytes(cohesion + 16)]));
        _w.Memory.Map(cohesion, 16);
        Assert.True(_w.Memory.TryWrite(cohesion, Convert.FromHexString("99010000015700008B00000002430000")));
        var writer = new TeamCohesionWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var snapshot = Assert.IsType<TeamCohesionSnapshot>(writer.Capture(Player(), club));
        Assert.True(_w.Memory.TryWrite(Holder + 0x28, BitConverter.GetBytes(cohesion + 24)));
        int writes = _w.Memory.Writes;

        Assert.Throws<StaleObjectException>(() => writer.Set(Player(), club, snapshot, 100));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(Convert.FromHexString("99010000015700008B00000002430000"), _w.Bytes(cohesion, 16));
    }
}

using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Club → Relationships. Record tails (last 8 bytes of each 16-byte record) are dumped from FM27 27.0.0 RAM (PID 21248,
/// 2026-11-03; vector inline at club+0x48). Expected names, values, reasons and Permanent ticks are the FMRTE 27
/// Relationships screens for Sheffield United (16 rows) and Manchester United (37 rows); Reason names were read from
/// FMRTE's detail panel (Local, Competitive, Historic; Player, Manager) and Chairperson 4 / Founder 8 from FMRTE saves.
/// </summary>
public class ClubRelationshipTests
{
    const uint ClubVt = 0x4CDA1F8, TeamVt = 0x4CE1108, PersonVt = 0x4D27758, OtherVt = 0x4F75F38;
    const ulong Club = 0x5000_0000, Records = 0x5100_0000, Targets = 0x5200_0000, Strings = 0x5300_0000;

    // Sheffield United, FMRTE order is Unknown, Rival x7, Dislikes x3, Likes x5; RAM order is the list below.
    static readonly (string Tail, string? Name, char Target)[] Sheffield =
    [
        ("01000105644F00FF", "Sheffield Wednesday", 'c'), ("010001055A4F00FF", "Leeds United", 'c'),
        ("010001054B4F00FF", "Barnsley", 'c'), ("01000105320000FF", "Rotherham United", 'c'),
        ("01000105320000FF", "Doncaster Rovers", 'c'), ("030001054B4F00FF", "Nottingham Forest", 'c'),
        ("030001053C0000FF", "West Ham United", 'c'),
        ("01000301644F00FF", null, 'o'), ("02000301644F00FF", "Chris Wilder", 'p'),
        ("000003024B4F00FF", "Steve Bruce", 'p'), ("00000302504F00FF", "Gareth Ainsworth", 'p'),
        ("000003024B4F00FF", "Carlos Tevez", 'p'), ("01000301324F00FF", "Alan Kelly", 'p'),
        ("01000301320000FF", "David McGoldrick", 'p'), ("01000301324F00FF", "Chris Basham", 'p'),
        ("0000041D080000FF", "Sheffield United", 't'),
    ];

    // Manchester United rivals, first likes and the Unknown (68) rows.
    static readonly (string Tail, string? Name, char Target)[] ManUtd =
    [
        ("01000105644F02FF", "Liverpool", 'c'), ("01000105644F00FF", "Manchester City", 'c'),
        ("030001055F4F00FF", "Leeds United", 'c'), ("020001053C0000FF", "Arsenal", 'c'),
        ("02000105320000FF", "Chelsea", 'c'), ("010001052D4F00FF", "Bolton Wanderers", 'c'),
        ("01000105284F00FF", "Everton", 'c'), ("01000105194F00FF", "Blackburn Rovers", 'c'),
        ("02000105280000FF", "Real Madrid", 'c'),
        ("02000301644F01FF", "Alex Ferguson", 'p'), ("01000301414F00FF", "Bruno Fernandes", 'p'),
        ("000003024B0000FF", "Carlos Tevez", 'p'),
        ("4200034464FF00FF", "Michael Carrick", 'p'),
    ];

    readonly FakeMemory _m = new();
    readonly TestWorld _w = new();
    ulong _next = Targets;

    ClubRelationship[] Build((string Tail, string? Name, char Target)[] rows)
    {
        var club = _m.Map(Club, 0x170);
        TestWorld.Set64(club, 0, TestWorld.ModuleBase + ClubVt);
        TestWorld.Set64(club, 0x48, Records);
        TestWorld.Set64(club, 0x50, Records + (ulong)rows.Length * 16);
        TestWorld.Set64(club, 0x58, Records + (ulong)rows.Length * 16);
        var records = _m.Map(Records, rows.Length * 16);
        for (int i = 0; i < rows.Length; i++)
        {
            TestWorld.Set64(records, i * 16, Target(rows[i]));
            Convert.FromHexString(rows[i].Tail).CopyTo(records, i * 16 + 8);
        }
        return [.. Reader().Read(Club)];
    }

    ulong Target((string Tail, string? Name, char Target) row)
    {
        ulong at = _next; _next += 0x200;
        var o = _m.Map(at, 0x100);
        ulong str = Strings + (at - Targets);
        switch (row.Target)
        {
            case 'c':
                TestWorld.Set64(o, 0, TestWorld.ModuleBase + ClubVt);
                TestWorld.Set64(o, 0xC0, str);
                _m.MapString(str, row.Name!);
                break;
            case 'p':
                TestWorld.Set64(o, 0, TestWorld.ModuleBase + PersonVt);
                TestWorld.Set64(o, 0x60, str);
                _m.MapNameEntry(str, str + 0x80, row.Name!);
                break;
            case 't':
                TestWorld.Set64(o, 0, TestWorld.ModuleBase + TeamVt);
                ulong club = at + 0x100;
                TestWorld.Set64(o, 0x30, club); // team → club, profile contract.teamClub (club +0x30 in AGENTS §7)
                var c = _m.Map(club, 0x100);
                TestWorld.Set64(c, 0, TestWorld.ModuleBase + ClubVt);
                TestWorld.Set64(c, 0xC0, str);
                _m.MapString(str, row.Name!);
                break;
            default: // a person class the reader does not know (Billy Sharp, the human manager in the real save)
                TestWorld.Set64(o, 0, TestWorld.ModuleBase + OtherVt);
                break;
        }
        return at;
    }

    ClubRelationshipReader Reader() => new(_m, _w.Profile, TestWorld.ModuleBase);
    ClubRelationshipWriter Writer() => new(_m, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Sheffield_rows_match_FMRTE_names_values_and_reasons()
    {
        var rows = Build(Sheffield);
        Assert.Equal(16, rows.Length);
        Assert.Equal(Sheffield.Select(r => r.Name), rows.Select(r => r.Name));
        Assert.Equal(new[] { 100, 90, 75, 50, 50, 75, 60, 100, 100, 75, 80, 75, 50, 50, 50, 8 }, rows.Select(r => r.Value));
        // FMRTE: Sheffield Wednesday / Leeds Local, Nottm Forest and West Ham Historic; Sharp Player, Wilder Manager.
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 3, 3 }, rows.Take(7).Select(r => r.Reason));
        Assert.Equal(1, rows[7].Reason);
        Assert.Equal(2, rows[8].Reason);
        Assert.Equal(1, rows[12].Reason);
        Assert.Equal(new[] { ClubRelationshipKind.Rival, ClubRelationshipKind.Rival, ClubRelationshipKind.Rival, ClubRelationshipKind.Rival,
            ClubRelationshipKind.Rival, ClubRelationshipKind.Rival, ClubRelationshipKind.Rival, ClubRelationshipKind.LikesPerson,
            ClubRelationshipKind.LikesPerson, ClubRelationshipKind.DislikesPerson, ClubRelationshipKind.DislikesPerson,
            ClubRelationshipKind.DislikesPerson, ClubRelationshipKind.LikesPerson, ClubRelationshipKind.LikesPerson,
            ClubRelationshipKind.LikesPerson, ClubRelationshipKind.Other }, rows.Select(r => r.Kind));
        Assert.Equal(29, rows[15].TypeCode);
        // Permanent ticked in FMRTE for Sharp, Wilder, Bruce, Ainsworth and Kelly.
        Assert.All(new[] { 7, 8, 9, 10, 12 }, i => Assert.True(rows[i].Permanent));
    }

    [Fact]
    public void ManUtd_rival_reasons_cover_local_competitive_and_historic()
    {
        var rows = Build(ManUtd);
        Assert.Equal(new[] { 100, 100, 95, 60, 50, 45, 40, 25, 40 }, rows.Take(9).Select(r => r.Value));
        // FMRTE: Liverpool and Bolton Local, Arsenal and Real Madrid Competitive, Leeds Historic.
        Assert.Equal(new[] { 1, 1, 3, 2, 2, 1, 1, 1, 2 }, rows.Take(9).Select(r => r.Reason));
        Assert.Equal(new[] { "Alex Ferguson", "Bruno Fernandes", "Carlos Tevez", "Michael Carrick" }, rows.Skip(9).Select(r => r.Name));
        Assert.Equal(68, rows[12].TypeCode);
        Assert.Equal(ClubRelationshipKind.Other, rows[12].Kind);
        Assert.Equal(ClubRelationshipKind.DislikesPerson, rows[11].Kind);
        Assert.Equal(75, rows[11].Value);
    }

    [Fact]
    public void Reading_never_writes_and_a_non_club_or_missing_profile_gives_nothing()
    {
        var rows = Build(Sheffield);
        int writes = _m.Writes;
        _ = Reader().Read(Club);
        Assert.Equal(writes, _m.Writes);
        Assert.True(_m.TryWrite(Club, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1234560UL)));
        Assert.Empty(Reader().Read(Club));
        Assert.NotEmpty(rows);
    }

    [Fact]
    public void SetValue_changes_exactly_the_value_byte()
    {
        var rows = Build(Sheffield);
        var before = _m.Read(Records, 16 * 16);
        Writer().SetValue(Club, rows[0], 99);
        var after = _m.Read(Records, 16 * 16);
        Assert.Equal(99, after[0xC]);
        after[0xC] = before[0xC];
        Assert.Equal(before, after);
        Assert.Equal(99, Reader().Read(Club)[0].Value);
    }

    [Fact]
    public void SetDetails_writes_reason_value_and_permanent_into_their_bytes_only()
    {
        var rows = Build(Sheffield);
        // Row 12 Alan Kelly, Player 1, permanent. FMRTE 27 stored Chairperson as 4 and Founder as 8 in the same byte.
        Writer().SetDetails(Club, rows[12], 8, false, 77);
        var kelly = _m.Read(Records + 12 * 16 + 8, 8);
        Assert.Equal("080003014D0000FF", Convert.ToHexString(kelly));
        var read = Reader().Read(Club)[12];
        Assert.Equal((8, 77, false), (read.Reason, read.Value, read.Permanent));
        // Neighbours untouched.
        Assert.Equal("01000301320000FF", Convert.ToHexString(_m.Read(Records + 13 * 16 + 8, 8)));
        Writer().SetDetails(Club, Reader().Read(Club)[12], 4, true, null);
        Assert.Equal("040003014D4F00FF", Convert.ToHexString(_m.Read(Records + 12 * 16 + 8, 8)));
    }

    [Fact]
    public void Changed_vector_capacity_rejects_the_original_relationship_snapshot()
    {
        var rows = Build(Sheffield);
        byte[] before = _m.Read(Records, 16);
        Assert.True(_m.TryWrite(Club + 0x58, BitConverter.GetBytes(Records + 17 * 16)));
        int writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetValue(Club, rows[0], 99));
        Assert.Equal(writes, _m.Writes);
        Assert.Equal(before, _m.Read(Records, 16));
    }

    [Fact]
    public void Misaligned_vector_capacity_is_not_a_valid_relationship_list()
    {
        Build(Sheffield);
        Assert.True(_m.TryWrite(Club + 0x58, BitConverter.GetBytes(Records + 16 * 16 + 1)));
        Assert.Empty(Reader().Read(Club));
    }

    [Fact]
    public void Guards_reject_stale_snapshots_other_rows_and_bad_values_without_writing()
    {
        var rows = Build(Sheffield);
        int writes = _m.Writes;
        Assert.Throws<ArgumentException>(() => Writer().SetValue(Club, rows[15], 5));            // Unknown (29)
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetValue(Club, rows[0], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetValue(Club, rows[0], 101));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetDetails(Club, rows[0], 4, null, null));  // rival reasons 0-3
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetDetails(Club, rows[8], 9, null, null)); // person reasons 0-8
        Assert.Throws<ArgumentException>(() => Writer().SetDetails(Club, rows[0], null, true, null));         // rivals have no Permanent
        // Game moved on: the value changed behind our back.
        Assert.True(_m.TryWrite(Records + 0xC, [50]));
        writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetValue(Club, rows[0], 99));
        // Vector replaced by the game.
        Assert.True(_m.TryWrite(Club + 0x50, BitConverter.GetBytes(Records + 15 * 16)));
        Assert.Throws<StaleObjectException>(() => Writer().SetValue(Club, rows[1], 99));
        Assert.Equal(writes + 1, _m.Writes);
        // Not a club any more, with the vector and record exactly as read so only the vtable guard can refuse.
        Assert.True(_m.TryWrite(Club + 0x50, BitConverter.GetBytes(Records + 16 * 16)));
        Assert.True(_m.TryWrite(Club, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1234560UL)));
        writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetValue(Club, rows[1], 99));
        Assert.Equal(writes, _m.Writes);
    }
}

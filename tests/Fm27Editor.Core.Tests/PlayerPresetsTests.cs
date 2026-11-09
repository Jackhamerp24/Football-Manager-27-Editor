using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// FMRTE 27's player presets, with the two measured cases as the oracle: Baptiste 28129876 had one unhappiness motive
/// ending 18/10/2026 and Cooper 29170995 two ending 27/7/2026 and 18/10/2026; after Inspire FMRTE shows 25/11/2026 on
/// all three and the motives are still listed, so the end date moves and nothing is deleted.
/// </summary>
public class PlayerPresetsTests
{
    const ulong Contract = 0x4000_0000, Team = 0x4100_0000, Header = 0x4200_0000, Records = 0x4200_1000;
    const ulong Club = 0x4300_0000, CohesionHolder = 0x4800_0000, Cohesion = 0x5002_0000;
    const ulong BanHolder = 0x4500_0000, BanPointers = 0x4600_0000, BanRecord = 0x4700_0000;
    const uint ContractVtable = 0x4B00AE8, ClubVtable = 0x4CDA1F8;
    const int RecordSize = 0x1C;
    static readonly DateOnly GameDate = new(2026, 11, 25);
    readonly TestWorld _w = new();

    public PlayerPresetsTests()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));

        var contract = _w.Memory.Map(Contract, 0x100);
        TestWorld.Set64(contract, 0x00, TestWorld.ModuleBase + ContractVtable);
        TestWorld.Set64(contract, 0x10, Team);      // contract.team
        TestWorld.Set64(contract, 0x30, Header);    // contract.unhappiness.pointer
        contract[0x5A] = 40;                        // happiness
        contract[0x5C] = 60;                        // playing-time happiness
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x330, BitConverter.GetBytes(Contract)));

        var team = _w.Memory.Map(Team, 0x40);
        TestWorld.Set64(team, 0x30, Club);          // contract.teamClub on the team object
        var club = _w.Memory.Map(Club, 0xD0);
        TestWorld.Set64(club, 0x00, TestWorld.ModuleBase + ClubVtable);
        BitConverter.GetBytes(0x199).CopyTo(club, 0x08);   // Man Utd's cohesion index

        var header = _w.Memory.Map(Header, 0x10);
        BitConverter.GetBytes(3).CopyTo(header, 0);
        TestWorld.Set64(header, 8, Records);
        byte[] all =
        [
            .. Record(new(2026, 7, 1), new(2026, 10, 18), endFlags: 0x5E00),   // Baptiste, ended 18/10/2026
            .. Record(new(2026, 7, 1), new(2026, 7, 27), endFlags: 0x1200),    // Cooper, ended 27/7/2026
            .. Record(new(2026, 7, 1), new(2026, 10, 18), endFlags: 0x1200),   // Cooper, ended 18/10/2026
        ];
        all.CopyTo(_w.Memory.Map(Records, all.Length), 0);

        // Cohesion: relations holder at player + 0x300, its vector at +0x18.
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x300, BitConverter.GetBytes(CohesionHolder)));
        var holder = _w.Memory.Map(CohesionHolder, 0x40);
        TestWorld.Set64(holder, 0x18, Cohesion);
        TestWorld.Set64(holder, 0x20, Cohesion + 24);
        TestWorld.Set64(holder, 0x28, Cohesion + 24);
        // Man Utd (index 0x199, kind 1) 87, England (0x8B, kind 2) 67 — Mainoo's dumped rows — and the club of a loan
        // (index 0x1A0, kind 1) 40, which only the loan test points the playing team at.
        Convert.FromHexString("99010000015700008B00000002430000A001000001280000").CopyTo(_w.Memory.Map(Cohesion, 24), 0);

        // One match ban (matches 1, days -1) in the ban vector at holder + 0x18; the injury vector at +0 stays empty.
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0xF8, BitConverter.GetBytes(BanHolder)));
        var banHolder = _w.Memory.Map(BanHolder, 0x40);
        TestWorld.Set64(banHolder, 0x18, BanPointers);
        TestWorld.Set64(banHolder, 0x20, BanPointers + 8);
        TestWorld.Set64(banHolder, 0x28, BanPointers + 8);
        TestWorld.Set64(_w.Memory.Map(BanPointers, 8), 0, BanRecord);
        var ban = _w.Memory.Map(BanRecord, 0x20);
        BitConverter.GetBytes(-1).CopyTo(ban, 0x00);   // item
        BitConverter.GetBytes((short)-1).CopyTo(ban, 0x16); // days
        ban[0x1C] = 1;                                  // matches
    }

    static byte[] Record(DateOnly start, DateOnly? end, ushort endFlags = 0, short motive = 600, byte type = 1)
    {
        var b = new byte[RecordSize];
        DateBytes(start, 0).CopyTo(b, 0x00);
        DateBytes(end, endFlags).CopyTo(b, 0x04);
        BitConverter.GetBytes(-1).CopyTo(b, 0x08);
        BitConverter.GetBytes(-1).CopyTo(b, 0x0C);
        BitConverter.GetBytes(motive).CopyTo(b, 0x16);
        b[0x18] = type;
        return b;
    }

    /// <summary>No date is day 1 of 1900, which the reader reports as null; <paramref name="flags"/> are the bits above the 9-bit day.</summary>
    static byte[] DateBytes(DateOnly? date, ushort flags = 0) =>
        [.. BitConverter.GetBytes((ushort)(flags | (date?.DayOfYear ?? 1))), .. BitConverter.GetBytes((short)(date?.Year ?? 1900))];

    PlayerRecord Player() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    IReadOnlyList<PlayerUnhappiness> Motives() => new UnhappinessReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Player());
    UnhappinessWriter Writer() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void End_motives_moves_every_end_date_and_keeps_the_records()
    {
        var before = _w.Bytes(Records, 3 * RecordSize);
        Assert.Equal(3, Writer().EndMotives(Player(), GameDate));

        var after = _w.Bytes(Records, 3 * RecordSize);
        Assert.All(Motives(), m => Assert.Equal(GameDate, m.End));
        Assert.Equal(3, BitConverter.ToInt32(_w.Bytes(Header, 4)));
        Assert.Equal(Records, BitConverter.ToUInt64(_w.Bytes(Header + 8, 8)));
        // 25/11/2026 is day 329 (0x149) of 2026 (0x07EA); the flags above the day survive (0x5E00 and 0x1200 stay).
        Assert.Equal(Convert.FromHexString("495FEA07"), after[0x04..0x08]);
        Assert.Equal(Convert.FromHexString("4913EA07"), after[0x20..0x24]);
        Assert.Equal(Convert.FromHexString("4913EA07"), after[0x3C..0x40]);
        for (int i = 0; i < 3; i++)
        {
            int at = i * RecordSize;
            Assert.Equal(before[at..(at + 4)], after[at..(at + 4)]);        // start untouched
            Assert.Equal(before[(at + 8)..(at + RecordSize)], after[(at + 8)..(at + RecordSize)]); // rest of the record untouched
        }
    }

    [Fact]
    public void End_motives_writes_nothing_when_every_end_already_is_the_game_date()
    {
        var writer = Writer();
        Assert.Equal(3, writer.EndMotives(Player(), GameDate));
        int writes = _w.Memory.Writes;
        Assert.Equal(0, writer.EndMotives(Player(), GameDate));
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Fact]
    public void End_motives_needs_a_list_and_a_real_date()
    {
        Assert.True(_w.Memory.TryWrite(Header, BitConverter.GetBytes(-605633096)));
        int writes = _w.Memory.Writes;
        Assert.Equal(0, Writer().EndMotives(Player(), GameDate));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().EndMotives(Player(), new DateOnly(1800, 1, 1)));
    }

    /// <summary>
    /// A motive that starts after the game date keeps its end: writing the game date would make End earlier than Start.
    /// Baptiste's row is reused with a start of 1/12/2026 (day 335) and no end.
    /// </summary>
    [Fact]
    public void End_motives_leaves_a_motive_that_starts_after_the_game_date_alone()
    {
        Assert.True(_w.Memory.TryWrite(Records, DateBytes(new DateOnly(2026, 12, 1))));
        Assert.True(_w.Memory.TryWrite(Records + 0x04, DateBytes(null)));
        Assert.Equal(2, Writer().EndMotives(Player(), GameDate));
        Assert.Equal(Convert.FromHexString("01006C07"), _w.Bytes(Records + 0x04, 4));         // 1/1/1900, untouched
        Assert.Equal(Convert.FromHexString("4913EA07"), _w.Bytes(Records + 0x04 + RecordSize, 4));
        Assert.Null(Motives()[0].End);
        Assert.Equal(new DateOnly(2026, 12, 1), Motives()[0].Start);
    }

    [Fact]
    public void Inspire_does_every_step_of_FMRTEs_preset()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x26C, [5]));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x258, Convert.FromHexString("BC1B4EFF3421")));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x190, [40]));

        new PlayerInspirer(_w.Memory, _w.Profile, TestWorld.ModuleBase).Inspire(Player(), GameDate);

        var player = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        Assert.Equal(20, player[0x26C]);                                        // morale
        Assert.Equal(0, BitConverter.ToInt16(player, 0x25A));                   // jadedness
        Assert.All(Motives(), m => Assert.Equal(GameDate, m.End));              // motive ended, not removed
        Assert.Equal(3, BitConverter.ToInt32(_w.Bytes(Header, 4)));
        Assert.Equal(100, _w.Bytes(Contract + 0x5A, 1)[0]);                     // contract happiness
        Assert.Equal(100, _w.Bytes(Contract + 0x5C, 1)[0]);                     // playing-time happiness
        Assert.Equal(Convert.FromHexString("99010000016400008B00000002430000"), _w.Bytes(Cohesion, 16)); // club 87 -> 100, England kept
    }

    [Fact]
    public void Destroy_lowers_cohesion_and_happiness_but_leaves_tactical_familiarity_alone()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x190, [88]));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x258, Convert.FromHexString("102700001027")));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x26C, [20]));

        new PlayerDestroyer(_w.Memory, _w.Profile, TestWorld.ModuleBase).Destroy(Player());

        var player = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        Assert.Equal(88, player[0x190]);                                        // tactical familiarity is FMRTE's other preset
        Assert.Equal(0, player[0x26C]);
        Assert.Equal(Convert.FromHexString("8813E8038813"), player[0x258..0x25E]);
        Assert.Equal(0, _w.Bytes(Contract + 0x5A, 1)[0]);
        Assert.Equal(0, _w.Bytes(Contract + 0x5C, 1)[0]);
        Assert.Equal(Convert.FromHexString("99010000010100008B00000002430000"), _w.Bytes(Cohesion, 16));
    }

    [Fact]
    public void Tactical_understanding_sets_familiarity_and_cohesion_together()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x190, [40]));
        new PlayerPresetRunner(_w.Memory, _w.Profile, TestWorld.ModuleBase).TacticalUnderstanding(Player());
        Assert.Equal(100, _w.Bytes(TestWorld.Mainoo + 0x190, 1)[0]);
        Assert.Equal(Convert.FromHexString("99010000016400008B00000002430000"), _w.Bytes(Cohesion, 16));
    }

    /// <summary>
    /// Mark Shelton's measured case: contract club York, on loan at Grimsby Town. FMRTE's Improve Team Cohesion left York
    /// alone and set the Grimsby row to 100, so the presets act on the playing club.
    /// </summary>
    [Fact]
    public void Cohesion_uses_the_playing_club_of_a_loan()
    {
        const ulong PlayingTeam = 0x4900_0000, Grimsby = 0x4A00_0000;
        var team = _w.Memory.Map(PlayingTeam, 0x40);
        TestWorld.Set64(team, 0x00, TestWorld.ModuleBase + 0x4CE1108);   // team.vtable
        TestWorld.Set64(team, 0x30, Grimsby);                             // contract.teamClub on the team object
        var grimsby = _w.Memory.Map(Grimsby, 0xD0);
        TestWorld.Set64(grimsby, 0x00, TestWorld.ModuleBase + ClubVtable);
        BitConverter.GetBytes(0x1A0).CopyTo(grimsby, 0x08);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x130, BitConverter.GetBytes(PlayingTeam)));

        new PlayerInspirer(_w.Memory, _w.Profile, TestWorld.ModuleBase).Inspire(Player(), GameDate);

        // The third row (index 0x1A0, kind 1) 40 -> 100; the contract club's row and England's stay as they were.
        Assert.Equal(Convert.FromHexString("9901000001570000"), _w.Bytes(Cohesion, 8));
        Assert.Equal(Convert.FromHexString("8B00000002430000"), _w.Bytes(Cohesion + 8, 8));
        Assert.Equal(Convert.FromHexString("A001000001640000"), _w.Bytes(Cohesion + 16, 8));
    }
    [Fact]
    public void All_in_one_runs_heal_inspire_bans_and_familiarity()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x26C, [5]));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x258, Convert.FromHexString("BC1B4EFF3421")));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x190, [40]));

        new PlayerPresetRunner(_w.Memory, _w.Profile, TestWorld.ModuleBase).AllInOne(Player(), GameDate);

        var player = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        Assert.Equal(Convert.FromHexString("102700001027"), player[0x258..0x25E]); // heal
        Assert.Equal(20, player[0x26C]);                                          // inspire
        Assert.All(Motives(), m => Assert.Equal(GameDate, m.End));
        Assert.Equal(100, _w.Bytes(Contract + 0x5A, 1)[0]);
        Assert.Equal(100, _w.Bytes(Contract + 0x5C, 1)[0]);
        Assert.Equal(100, player[0x190]);                                         // tactical familiarity
        Assert.Equal(Convert.FromHexString("99010000016400008B00000002430000"), _w.Bytes(Cohesion, 16));
        Assert.Equal(BanPointers, BitConverter.ToUInt64(_w.Bytes(BanHolder + 0x20, 8))); // the match ban is gone
    }
}

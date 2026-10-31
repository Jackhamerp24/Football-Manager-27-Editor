using Fm27Editor.Core.Clubs;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Almería's clause list as found in FM27 27.0.0 RAM (club +0xB0 → holder, vector of pointers at holder+0).
/// Record bodies are the bytes dumped from the game; expected values are what FMRTE 27's clause screen shows
/// for Almería: Arribas sell-on 50% to Real Madrid, Marcos Luna £42,951 after 5 appearances (0 so far) to
/// Zaragoza, André Horta £429,515 to Braga until 30/6/2029.
/// </summary>
public class TransferClauseTests
{
    const uint ClubVt = 0x4CDA1F8, SellOnVt = 0x4C8F1E8, AppearanceVt = 0x4DC41E8, CompetitionVt = 0x4DC4088, OtherVt = 0x4DC5E88;
    const ulong Almeria = 0x5000_0000, Holder = 0x5001_0000, List = 0x5002_0000;
    const ulong Real = 0x5100_0000, Zaragoza = 0x5200_0000, Braga = 0x5300_0000;
    const ulong Arribas = 0x6000_0000, Luna = 0x6000_1000, Horta = 0x6000_2000, Link = 0x6000_3000;
    readonly TestWorld _w = new();

    public TransferClauseTests()
    {
        Club(Almeria, "Unión Deportiva Almería", "Almería", 0x5000_8000);
        Club(Real, "Real Madrid Club de Fútbol", "Real Madrid", 0x5100_8000);
        Club(Zaragoza, "Real Zaragoza", "Zaragoza", 0x5200_8000);
        Club(Braga, "Sporting Clube de Braga", "Braga", 0x5300_8000);
        TestWorld.Set64(_w.Memory.Map(Almeria + 0xB0, 8), 0, Holder);
        var holder = _w.Memory.Map(Holder, 0x18);
        TestWorld.Set64(holder, 0, List);
        TestWorld.Set64(holder, 8, List + 4 * 8);
        TestWorld.Set64(holder, 0x10, List + 4 * 8);
        var list = _w.Memory.Map(List, 4 * 8);
        TestWorld.Set64(list, 0, Arribas); TestWorld.Set64(list, 8, Link); TestWorld.Set64(list, 16, Luna); TestWorld.Set64(list, 24, Horta);

        // Sell-on record is 0x28 bytes; mapping only that much also checks the reader does not need more.
        Record(Arribas, 0x28, SellOnVt, 67277465, Real, 0x10, "FF FF FF FF 00 00 00 00 FF 00 32 00 00 00 00 00", 0x18);
        Record(Luna, 0x28, AppearanceVt, 2000111004, Zaragoza, 0x10, "C7 A7 00 00 05 00 00 00 03 00 00 00 00 00 00 00", 0x18);
        Record(Horta, 0x38, CompetitionVt, 55082342, Braga, 0x30,
            "B5 00 ED 07 FF FF FF FF 00 00 00 00 FF FF FF FF 03 00 00 00 34 00 00 00 CB 8D 06 00 00 00 00 00", 0x10);
        // Another kind of entry in the same vector (seen as links to other clubs), not a clause.
        var link = _w.Memory.Map(Link, 0x38);
        TestWorld.Set64(link, 0, TestWorld.ModuleBase + OtherVt);
        BitConverter.GetBytes(1952).CopyTo(link, 8);
    }

    void Club(ulong club, string name, string shortName, ulong strings)
    {
        var c = _w.Memory.Map(club, 0xB0);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + ClubVt);
        var names = _w.Memory.Map(club + 0xC0, 0x10);
        TestWorld.Set64(names, 0, strings);
        TestWorld.Set64(names, 8, strings + 0x100);
        _w.Memory.MapString(strings, name);
        _w.Memory.MapString(strings + 0x100, shortName);
    }

    void Record(ulong at, int size, uint vt, int playerId, ulong club, int clubOffset, string hex, int hexOffset)
    {
        var r = _w.Memory.Map(at, size);
        TestWorld.Set64(r, 0, TestWorld.ModuleBase + vt);
        BitConverter.GetBytes(playerId).CopyTo(r, 8);
        BitConverter.GetBytes(playerId).CopyTo(r, 0xC);
        Convert.FromHexString(hex.Replace(" ", "")).CopyTo(r, hexOffset);
        TestWorld.Set64(r, clubOffset, club);
    }

    IReadOnlyList<TransferClause> Read(ulong club) => new TransferClauseReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(club);

    [Fact]
    public void Reads_the_clauses_fmrte_shows_for_almeria_and_skips_other_entries()
    {
        var c = Read(Almeria);

        Assert.Equal([TransferClauseKind.SellOn, TransferClauseKind.AppearanceFee, TransferClauseKind.CompetitionBonus], c.Select(x => x.Kind));

        Assert.Equal(67277465, c[0].PlayerId);
        Assert.Equal("Real Madrid", c[0].Beneficiary?.DisplayName);
        Assert.Equal((short)50, c[0].Percentage);
        Assert.Null(c[0].Amount);
        Assert.Null(c[0].SignedFor);

        Assert.Equal(2000111004, c[1].PlayerId);
        Assert.Equal("Zaragoza", c[1].Beneficiary?.DisplayName);
        Assert.Equal(42951, c[1].Amount);
        Assert.Equal(5, c[1].Appearances);
        Assert.Equal(0, c[1].AppearancesSoFar);

        Assert.Equal(55082342, c[2].PlayerId);
        Assert.Equal("Braga", c[2].Beneficiary?.DisplayName);
        Assert.Equal(429515, c[2].Amount);
        Assert.Equal(new DateOnly(2029, 6, 30), c[2].Ends);
        Assert.Equal(52, c[2].CompetitionIndex);
        Assert.Null(c[2].Competition);
    }

    /// <summary>
    /// Getafe: Uche's sell-on to Ceuta, FMRTE shows 15% and "Signed For £429,515" (+0x18 = CB 8D 06 00).
    /// Sheffield United: Tanganga's appearance fee to Millwall, +0x18 = 40 42 0F 00 32 00 28 00 (1,000,000, 50, 40 so far).
    /// LaLiga Hypermotion's competition object has index 52 at +0x8, and FMRTE ID 68 at +0xC.
    /// </summary>
    [Fact]
    public void Signed_for_appearances_so_far_and_competition_name()
    {
        Assert.True(_w.Memory.TryWrite(Arribas + 0x18, Convert.FromHexString("CB8D060000000000FF000F00")));
        Assert.True(_w.Memory.TryWrite(Luna + 0x18, Convert.FromHexString("40420F0032002800")));
        var hypermotion = new Players.CompetitionInfo(0x5400_0000, "LALIGA HYPERMOTION", null);

        var c = new TransferClauseReader(_w.Memory, _w.Profile, TestWorld.ModuleBase, new Dictionary<int, Players.CompetitionInfo> { [52] = hypermotion }).Read(Almeria);

        Assert.Equal(429515, c[0].SignedFor);
        Assert.Equal((short)15, c[0].Percentage);
        Assert.Equal(1_000_000, c[1].Amount);
        Assert.Equal(50, c[1].Appearances);
        Assert.Equal(40, c[1].AppearancesSoFar);
        Assert.Same(hypermotion, c[2].Competition);
    }

    [Fact]
    public void Competition_directory_maps_the_index_at_plus_8_of_each_division()
    {
        var obj = _w.Memory.Map(0x5400_0000, 0x50);
        BitConverter.GetBytes(52).CopyTo(obj, 0x8);
        BitConverter.GetBytes(68).CopyTo(obj, 0xC);
        var division = new Players.CompetitionInfo(0x5400_0000, "LALIGA HYPERMOTION", null);
        var player = new Players.PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
        var withDivision = new Players.PlayerRecord
        {
            Address = player.Address, VtableRva = player.VtableRva, CurrentAbility = 1, PotentialAbility = 1, HomeReputation = 0,
            CurrentReputation = 0, WorldReputation = 0, Height = 0, RawAttributes = player.RawAttributes, AttributeScale = 5,
            Positions = player.Positions, Personality = player.Personality, Division = division
        };

        var map = CompetitionDirectory.FromPlayers(_w.Memory, _w.Profile, [withDivision, withDivision]);

        Assert.Equal([52], map.Keys);
        Assert.Same(division, map[52]);
    }

    [Fact]
    public void No_end_date_is_read_as_null()
    {
        // Bonini's bonus: day 1 of 1900.
        Assert.True(_w.Memory.TryWrite(Horta + 0x10, Convert.FromHexString("01006C07")));
        Assert.Null(Read(Almeria)[2].Ends);
    }

    [Fact]
    public void Object_without_the_club_vtable_gives_no_clauses()
    {
        Assert.True(_w.Memory.TryWrite(Almeria, BitConverter.GetBytes(TestWorld.ModuleBase + OtherVt)));
        Assert.Empty(Read(Almeria));
    }

    TransferClauseWriter Writer() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);
    ulong _next = 0x7000_0000;
    ulong Allocate(int bytes) { ulong a = _next; _w.Memory.Map(a, bytes); _next += 0x1000; return a; }

    [Fact]
    public void Value_edits_write_only_their_field()
    {
        var c = Read(Almeria);
        var arribas = _w.Bytes(Arribas, 0x28);
        var horta = _w.Bytes(Horta, 0x38);
        Writer().SetPercentage(c[0], 40);
        Writer().SetAmount(c[1], 50000);
        Writer().SetAppearances(c[1], 10);
        Writer().SetAmount(c[2], 1_000_000);
        Writer().SetEnds(c[2], new DateOnly(2030, 6, 30));

        // 40 = 0x28 at +0x22; 50000 = 0xC350 at +0x18, 10 at +0x1C; 1,000,000 = 0x0F4240 at +0x28;
        // 30/6/2030 is day 181 (non-leap), 2030 = 0x07EE.
        arribas[0x22] = 40;
        Assert.Equal(arribas, _w.Bytes(Arribas, 0x28));
        Assert.Equal(new byte[] { 0x50, 0xC3, 0, 0, 10, 0, 0, 0 }, _w.Bytes(Luna + 0x18, 8));
        Convert.FromHexString("B500EE07").CopyTo(horta, 0x10);
        Convert.FromHexString("40420F00").CopyTo(horta, 0x28);
        Assert.Equal(horta, _w.Bytes(Horta, 0x38));

        Writer().SetEnds(Read(Almeria)[2], null);
        Assert.Equal(new byte[] { 0x01, 0x00, 0x6C, 0x07 }, _w.Bytes(Horta + 0x10, 4));
    }

    [Fact]
    public void Appearances_signed_for_and_competition_keep_their_neighbours()
    {
        // 40 appearances so far (+0x1E) must survive a change of the total (+0x1C).
        Assert.True(_w.Memory.TryWrite(Luna + 0x1E, BitConverter.GetBytes((short)40)));
        var c = Read(Almeria);
        Writer().SetAppearances(c[1], 60);
        Writer().SetSignedFor(c[0], 2_500_000);
        Writer().SetCompetition(c[2], 8);

        Assert.Equal(new byte[] { 60, 0, 40, 0, 3, 0 }, _w.Bytes(Luna + 0x1C, 6));
        // 2,500,000 = 0x2625A0 (Cooper's sell-on to Plymouth stores A0 25 26 00).
        Assert.Equal(Convert.FromHexString("A025260000000000FF003200"), _w.Bytes(Arribas + 0x18, 12));
        Assert.Equal(8, BitConverter.ToInt32(_w.Bytes(Horta + 0x24, 4)));
        Assert.Equal(429515, BitConverter.ToInt32(_w.Bytes(Horta + 0x28, 4)));

        Writer().SetSignedFor(Read(Almeria)[0], null);
        Assert.Equal(-1, BitConverter.ToInt32(_w.Bytes(Arribas + 0x18, 4)));
    }

    [Fact]
    public void Value_edit_is_refused_when_the_game_changed_it()
    {
        var snapshot = Read(Almeria)[0];
        Assert.True(_w.Memory.TryWrite(Arribas + 0x22, BitConverter.GetBytes((short)25)));
        int writes = _w.Memory.Writes;
        Assert.Throws<Players.StaleObjectException>(() => Writer().SetPercentage(snapshot, 40));
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetPercentage(Read(Almeria)[0], 101));
    }

    [Fact]
    public void Remove_shifts_the_vector_and_leaves_the_others()
    {
        Writer().RemoveSellOn(Almeria, Read(Almeria)[0]);

        Assert.Equal(List + 3 * 8, BitConverter.ToUInt64(_w.Bytes(Holder + 8, 8)));
        Assert.Equal([Link, Luna, Horta], Enumerable.Range(0, 3).Select(i => BitConverter.ToUInt64(_w.Bytes(List + (ulong)i * 8, 8))));
        Assert.Equal([TransferClauseKind.AppearanceFee, TransferClauseKind.CompetitionBonus], Read(Almeria).Select(c => c.Kind));
    }

    [Fact]
    public void Add_to_a_full_vector_moves_it_to_a_bigger_buffer()
    {
        ulong record = Writer().AddSellOn(Almeria, 2000221580, Real, 20, Allocate);

        // Record laid out like the game's (Thalys to Palmeiras, 30%): FF FF FF FF 00 00 00 00 FF 00 <pct> 00 at +0x18.
        var r = _w.Bytes(record, 0x28);
        Assert.Equal(TestWorld.ModuleBase + SellOnVt, BitConverter.ToUInt64(r, 0));
        Assert.Equal(Convert.FromHexString("FFFFFFFF00000000FF00140000000000"), r[0x18..0x28]);
        ulong begin = BitConverter.ToUInt64(_w.Bytes(Holder, 8));
        Assert.NotEqual(List, begin);
        Assert.Equal(begin + 5 * 8, BitConverter.ToUInt64(_w.Bytes(Holder + 8, 8)));
        Assert.True(BitConverter.ToUInt64(_w.Bytes(Holder + 16, 8)) > begin + 5 * 8);

        var c = Read(Almeria);
        Assert.Equal(4, c.Count);
        Assert.Equal(TransferClauseKind.SellOn, c[3].Kind);
        Assert.Equal(2000221580, c[3].PlayerId);
        Assert.Equal("Real Madrid", c[3].Beneficiary?.DisplayName);
        Assert.Equal((short)20, c[3].Percentage);
        Assert.Null(c[3].Amount);
    }

    [Fact]
    public void Add_with_room_appends_in_place()
    {
        var bigger = _w.Memory.Map(0x5003_0000, 6 * 8);
        _w.Bytes(List, 4 * 8).CopyTo(bigger, 0);
        Assert.True(_w.Memory.TryWrite(Holder, [.. BitConverter.GetBytes(0x5003_0000UL), .. BitConverter.GetBytes(0x5003_0000UL + 4 * 8), .. BitConverter.GetBytes(0x5003_0000UL + 6 * 8)]));

        ulong record = Writer().AddSellOn(Almeria, 2000221580, Braga, 15, Allocate, signedFor: 2_500_000);

        Assert.Equal(0x5003_0000UL, BitConverter.ToUInt64(_w.Bytes(Holder, 8)));
        Assert.Equal(0x5003_0000UL + 5 * 8, BitConverter.ToUInt64(_w.Bytes(Holder + 8, 8)));
        Assert.Equal(record, BitConverter.ToUInt64(_w.Bytes(0x5003_0000UL + 4 * 8, 8)));
        Assert.Equal(Convert.FromHexString("A025260000000000FF000F00"), _w.Bytes(record + 0x18, 12));
        Assert.Equal(2_500_000, Read(Almeria)[^1].SignedFor);
        Assert.Throws<InvalidOperationException>(() => Writer().AddSellOn(Almeria, 1, Almeria, 15, Allocate));
    }
}

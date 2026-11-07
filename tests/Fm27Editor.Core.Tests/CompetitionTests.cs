using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Competition Information page (FMRTE 27, 3/11/2026). Tails are the 0x60 bytes at +0x130 of the objects, dumped from FM27 27.0.0
/// RAM (PID 21248). Expected values are the FMRTE screens: Premier League (ID 11, Reputation 189, 10/25/2026 - 3/28/2027, dark
/// purple background, black trim), Sky Bet Championship (12, 136, same dates, gold background), Trendyol Süper Lig (130286, 155,
/// 11/15/2015 - 3/15/2016, red background and trim, white foreground, yellow winter ball). FMRTE refused Reputation 201 on Save and
/// saved 200 (RAM C800 C800); saving 156 on the Süper Lig changed both int16 at +0x188 and +0x18A.
/// </summary>
public class CompetitionTests
{
    const ulong PremierLeague = 0x7000_0000, Championship = 0x7000_1000, SuperLig = 0x7000_2000, NotACompetition = 0x7000_3000;

    const string PremierTail = "F8F8F8FF380030FF000000FF000000FF" + "000000FF01000000E900EA079600EB07" + "2A01EA075700EB07E86E5C0600F0F8FF"
        + "00009A1C1A04A8021A04A802FFFF0000" + "3BB72A5D000000000043073800000000" + "FFFFFFFFFFFFFFFFBD00BD0001000100";
    const string ChampionshipTail = "F8F8F8FF4098B0FF000000FF000000FF" + "000000FF01000000E200EA078F00EB07" + "2A01EA075700EB0710CE670048E8F8FF"
        + "00000E17ED0380024904DB02FFFF0000" + "466B770C0000000080E7BD0200000000" + "FFFFFFFFFFFFFFFF8800880013001300";
    const string SuperLigTail = "F8F8F8FF2020D0FF2020D0FF000000FF" + "000000FF01000000E200EA078F00EB07" + "3F01DF074B00E0072F76FF0000F0F8FF"
        + "00003818E80380021A04A802FFFF0000" + "2ED1F40300000000FFFFFFFFFFFFFFFF" + "FFFFFFFFFFFFFFFF9B009B0009000900";

    readonly FakeMemory _m = new();
    readonly TestWorld _w = new();

    void Competition(ulong at, int index, int id, string name, string shortName, string tail)
    {
        var o = _m.Map(at, 0x1B0);
        TestWorld.Set64(o, 0, TestWorld.ModuleBase + _w.Profile.Competition!.VtableRva);
        BitConverter.GetBytes(index).CopyTo(o, 8);
        BitConverter.GetBytes(id).CopyTo(o, 0xC);
        TestWorld.Set64(o, 0x40, at + 0x800); _m.MapString(at + 0x800, name);
        TestWorld.Set64(o, 0x48, at + 0x900); _m.MapString(at + 0x900, shortName);
        Convert.FromHexString(tail).CopyTo(o, 0x130);
    }

    CompetitionRecord[] Build()
    {
        Competition(PremierLeague, 7, 11, "Premier League", "Premier League", PremierTail);
        Competition(Championship, 8, 12, "Sky Bet Championship", "EFL Championship", ChampionshipTail);
        Competition(SuperLig, 224, 130286, "Trendyol Süper Lig", "Süper Lig", SuperLigTail);
        var repo = new CompetitionRepository(_m, _w.Profile, TestWorld.ModuleBase);
        return [repo.Reload(PremierLeague)!, repo.Reload(Championship)!, repo.Reload(SuperLig)!];
    }

    static (int R, int G, int B) Rgb(uint argb) => ((int)(argb >> 16 & 0xFF), (int)(argb >> 8 & 0xFF), (int)(argb & 0xFF));

    [Fact]
    public void Three_competitions_match_FMRTE_ids_reputation_names_and_dates()
    {
        var c = Build();
        Assert.Equal(["Premier League", "Sky Bet Championship", "Trendyol Süper Lig"], c.Select(x => x.Name));
        Assert.Equal(["Premier League", "EFL Championship", "Süper Lig"], c.Select(x => x.ShortName));
        Assert.Equal(new[] { 7, 8, 224 }, c.Select(x => x.Index));
        Assert.Equal(new[] { 11, 12, 130286 }, c.Select(x => x.Id));
        Assert.Equal(new short[] { 189, 136, 155 }, c.Select(x => x.Reputation));
        Assert.Equal(new DateOnly?[] { new(2026, 10, 25), new(2026, 10, 25), new(2015, 11, 15) }, c.Select(x => x.WinterBallStart));
        Assert.Equal(new DateOnly?[] { new(2027, 3, 28), new(2027, 3, 28), new(2016, 3, 15) }, c.Select(x => x.WinterBallEnd));
    }

    [Fact]
    public void Colours_read_with_the_right_channel_order_for_the_swatches_FMRTE_shows()
    {
        var c = Build();
        // FMRTE swatches: Süper Lig background and trim red, Championship background gold, Premier League dark purple; the
        // checks are hue classes so the byte order (B, G, R, A) is judged by what FMRTE displays, not by this reader.
        var red = Rgb(c[2].Background);
        Assert.True(red.R > 0xC0 && red.G < 0x40 && red.B < 0x40);
        Assert.Equal(c[2].Background, c[2].Trim);
        var gold = Rgb(c[1].Background);
        Assert.True(gold.R > gold.G && gold.G > gold.B && gold.R >= 0xA0 && gold.B <= 0x60);
        var purple = Rgb(c[0].Background);
        Assert.True(purple.R < 0x60 && purple.G < 0x10 && purple.B > 0x20 && purple.B < 0x80);
        var yellow = Rgb(c[2].WinterBallColour);
        Assert.True(yellow.R > 0xE0 && yellow.G > 0xE0 && yellow.B < 0x40);
        Assert.All(c, x => Assert.Equal(0xFFu, x.Foreground >> 24));
        Assert.True(Rgb(c[0].Foreground).R > 0xE0 && Rgb(c[0].Foreground).B > 0xE0);   // white
        Assert.True(Rgb(c[0].Trim) == (0, 0, 0));                                      // black
    }

    [Fact]
    public void Missing_winter_ball_dates_read_as_null()
    {
        Competition(PremierLeague, 7, 11, "Premier League", "Premier League", PremierTail);
        _m.TryWrite(PremierLeague + 0x150, Convert.FromHexString("01006C0701006C07")); // the game's "none": day 1 of 1900 (7,995 competitions)
        var c = new CompetitionRepository(_m, _w.Profile, TestWorld.ModuleBase).Reload(PremierLeague)!;
        Assert.Null(c.WinterBallStart);
        Assert.Null(c.WinterBallEnd);
    }

    [Fact]
    public void SetReputation_writes_both_int16_and_nothing_else()
    {
        var c = Build();
        var before = _m.Read(SuperLig, 0x1B0);
        new CompetitionWriter(_m, _w.Profile, TestWorld.ModuleBase).SetReputation(c[2], 200);
        var after = _m.Read(SuperLig, 0x1B0);
        Assert.Equal("C800C800", Convert.ToHexString(after, 0x188, 4));
        before[0x188] = before[0x18A] = 200; before[0x189] = before[0x18B] = 0;
        Assert.Equal(before, after);
        Assert.Equal((short)200, new CompetitionRepository(_m, _w.Profile, TestWorld.ModuleBase).Reload(SuperLig)!.Reputation);
    }

    [Fact]
    public void Guards_refuse_201_stale_values_and_non_competitions_without_writing()
    {
        var c = Build();
        var writer = new CompetitionWriter(_m, _w.Profile, TestWorld.ModuleBase);
        int writes = _m.Writes;
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.SetReputation(c[0], 201));  // FMRTE refused 201
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.SetReputation(c[0], -1));
        Assert.Equal(writes, _m.Writes);

        Assert.True(_m.TryWrite(PremierLeague + 0x18A, [190, 0]));   // only the mirror moved: still stale
        writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => writer.SetReputation(c[0], 150));
        Assert.True(_m.TryWrite(Championship + 0x188, [137, 0]));
        writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => writer.SetReputation(c[1], 150));
        Assert.Equal(writes, _m.Writes);

        var notOne = _m.Map(NotACompetition, 0x1B0);
        notOne[0x188] = notOne[0x18A] = 155;   // same bytes as the snapshot: only the vtable guard can refuse
        Assert.Throws<StaleObjectException>(() => writer.SetReputation(c[2] with { Address = NotACompetition }, 150));
        Assert.Equal(writes, _m.Writes);
    }

    [Fact]
    public void Writers_change_substitution_rules_colour_and_winter_date_and_refuse_a_changed_object()
    {
        // The 2493426 Game Pass profile is the one that carries the substitution rules offset measured on 5/11/2026.
        const ulong module = 0x7100_0000_0000, at = 0x7200_0000;
        var profile = Fm27Editor.Core.Game.GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        var o = memory.Map(at, 0x1B0);
        TestWorld.Set64(o, 0, module + profile.Competition!.VtableRva);
        BitConverter.GetBytes(11).CopyTo(o, 0xC);
        BitConverter.GetBytes(189).CopyTo(o, 0x188);
        BitConverter.GetBytes(189).CopyTo(o, 0x18A);
        o[0xFD] = 0x11;                                             // FMRTE's "5 from 9"
        Convert.FromHexString("000000FF").CopyTo(o, 0x138);         // black trim
        Convert.FromHexString("2A01EA075700EB07").CopyTo(o, 0x150); // 25/10/2026 - 28/3/2027
        var repo = new CompetitionRepository(memory, profile, module);
        var writer = new CompetitionWriter(memory, profile, module);
        var c = repo.Reload(at)!;
        Assert.Equal((byte)0x11, c.SubstitutionRules);

        writer.SetSubstitutionRules(c, CompetitionWriter.SubstitutionRulesNone);
        Assert.Equal(0xFF, memory.Read(at + 0xFD, 1)[0]);
        var afterSubs = repo.Reload(at)!;
        Assert.Equal(CompetitionWriter.SubstitutionRulesNone, afterSubs.SubstitutionRules);

        writer.SetColour(afterSubs, CompetitionColour.Trim, 0xFF00FF00);
        Assert.Equal("00FF00FF", Convert.ToHexString(memory.Read(at + 0x138, 4)));   // B, G, R, A
        var afterColour = repo.Reload(at)!;
        Assert.Equal(0xFF00FF00u, afterColour.Trim);

        // A flag above the 9-bit day survives the write, as it does for contract dates.
        Assert.True(memory.TryWrite(at + 0x150, Convert.FromHexString("2A03EA075700EB07")));
        writer.SetWinterBallDate(repo.Reload(at)!, start: true, new DateOnly(2026, 10, 20));
        Assert.Equal("2503EA07", Convert.ToHexString(memory.Read(at + 0x150, 4)));   // day 293 kept the 0x300 flag
        Assert.Equal(new DateOnly(2026, 10, 20), repo.Reload(at)!.WinterBallStart);
        Assert.Equal(new DateOnly(2027, 3, 28), repo.Reload(at)!.WinterBallEnd);

        Assert.True(memory.TryWrite(at + 0xFD, [0x22]));            // the game (or FMRTE) changed it behind our back
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.SetSubstitutionRules(afterColour, 0x11));
        Assert.Equal(writes, memory.Writes);
    }
}

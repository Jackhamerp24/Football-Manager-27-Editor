using Fm27Editor.Core.Awards;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Award page (FMRTE 27, 3/11/2026). Tails are the 0x20 bytes at +0x80 of the objects, dumped from FM27 27.0.0 RAM (PID 21248).
/// Sky Bet Championship Top Goalscorer (ID 150768) read 13 / 125 / 60 on FMRTE's screen before anything was written; the Player of
/// the Season (ID 29086487) read 14 / 130 / 0 and was then set to 14 / 130 / 33 through FMRTE's own Save, which is the state dumped here.
/// FMRTE's Save of world reputation 33 changed only +0x96; Winner home reputation 256 was refused, 255 saved; Reputation 30 saved
/// and 40 refused.
/// </summary>
public class AwardTests
{
    const ulong TopGoalscorer = 0x8000_0000, PlayerOfTheSeason = 0x8000_1000, NotAnAward = 0x8000_2000;
    const string TopGoalscorerTail = "0D00FFFF010403020001000003010001" + "00000000007D3C00FFFF000000000000";
    const string PlayerOfTheSeasonTail = "0E00FFFF012503020000000103010002" + "0000000000822146FFFF000000000000";

    readonly FakeMemory _m = new();
    readonly TestWorld _w = new();

    void Award(ulong at, int index, int id, string name, string shortName, string tail)
    {
        var o = _m.Map(at, 0xA0);
        TestWorld.Set64(o, 0, TestWorld.ModuleBase + _w.Profile.Award!.VtableRva);
        BitConverter.GetBytes(index).CopyTo(o, 8);
        BitConverter.GetBytes(id).CopyTo(o, 0xC);
        TestWorld.Set64(o, 0x30, at + 0x400); _m.MapString(at + 0x400, name);
        TestWorld.Set64(o, 0x38, at + 0x500); _m.MapString(at + 0x500, shortName);
        Convert.FromHexString(tail).CopyTo(o, 0x80);
    }

    AwardRecord[] Build()
    {
        Award(TopGoalscorer, 370, 150768, "Sky Bet Championship Top Goalscorer", "Top Goalscorer", TopGoalscorerTail);
        Award(PlayerOfTheSeason, 2190, 29086487, "Sky Bet Championship Player of the Season", "Championship Player of the Season", PlayerOfTheSeasonTail);
        var repo = new AwardRepository(_m, _w.Profile, TestWorld.ModuleBase);
        return [repo.Reload(TopGoalscorer)!, repo.Reload(PlayerOfTheSeason)!];
    }

    AwardWriter Writer() => new(_m, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Two_awards_match_FMRTE_page()
    {
        var a = Build();
        Assert.Equal(["Sky Bet Championship Top Goalscorer", "Sky Bet Championship Player of the Season"], a.Select(x => x.Name));
        Assert.Equal(["Top Goalscorer", "Championship Player of the Season"], a.Select(x => x.ShortName));
        Assert.Equal(new[] { 150768, 29086487 }, a.Select(x => x.Id));
        Assert.Equal(new[] { 370, 2190 }, a.Select(x => x.Index));   // the internal index of the same objects in the game-wide list
        Assert.Equal(new short[] { 13, 14 }, a.Select(x => x.Reputation));
        Assert.Equal(new byte[] { 125, 130 }, a.Select(x => x.WinnerHomeReputation));
        Assert.Equal(new byte[] { 60, 33 }, a.Select(x => x.WinnerWorldReputation));
    }

    [Fact]
    public void Each_write_changes_only_its_own_bytes()
    {
        var a = Build();
        var before = _m.Read(PlayerOfTheSeason, 0xA0);
        Writer().Set(a[1], AwardField.WinnerWorldReputation, 70);
        var after = _m.Read(PlayerOfTheSeason, 0xA0);
        Assert.Equal(70, after[0x96]);
        after[0x96] = before[0x96];
        Assert.Equal(before, after);                       // +0x97 (a different byte, 0x46) is untouched

        Writer().Set(a[1], AwardField.WinnerHomeReputation, 255);
        Writer().Set(a[1], AwardField.Reputation, 30);
        var now = new AwardRepository(_m, _w.Profile, TestWorld.ModuleBase).Reload(PlayerOfTheSeason)!;
        Assert.Equal((short)30, now.Reputation);
        Assert.Equal((byte)255, now.WinnerHomeReputation);
        Assert.Equal((byte)70, now.WinnerWorldReputation);
        Assert.Equal("1E00FFFF", Convert.ToHexString(_m.Read(PlayerOfTheSeason + 0x80, 4)));
    }

    [Fact]
    public void Guards_refuse_out_of_range_values_stale_snapshots_and_non_awards_without_writing()
    {
        var a = Build();
        int writes = _m.Writes;
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(a[0], AwardField.Reputation, 40));   // FMRTE refused 40
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(a[0], AwardField.WinnerHomeReputation, 256)); // FMRTE refused 256
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().Set(a[0], AwardField.WinnerWorldReputation, -1));
        Assert.Equal(writes, _m.Writes);

        Assert.True(_m.TryWrite(TopGoalscorer + 0x95, [126]));
        writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().Set(a[0], AwardField.WinnerHomeReputation, 100));
        Assert.Equal(writes, _m.Writes);

        var notOne = _m.Map(NotAnAward, 0xA0);
        notOne[0x96] = a[1].WinnerWorldReputation;   // same byte as the snapshot, so only the vtable guard can refuse
        Assert.Throws<StaleObjectException>(() => Writer().Set(a[1] with { Address = NotAnAward }, AwardField.WinnerWorldReputation, 5));
        Assert.Equal(writes, _m.Writes);
    }
}

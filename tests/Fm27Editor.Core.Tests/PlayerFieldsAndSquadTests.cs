using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Player fields at the offsets lane C1 measured (re/squad/player-scalars.md). The bytes at +0x258..+0x26C are Harrison
/// Burrows' as dumped from FM27 RAM; the expected values are FMRTE 27's: sharpness 7100, jadedness −178, condition 8500,
/// morale 5, asking price £9,018,000.
/// </summary>
public class PlayerFieldsAndSquadTests
{
    const uint TeamVt = 0x4CE1108;
    const ulong Team = 0x5000_0000, Squad = 0x5001_0000;
    readonly TestWorld _w = new();

    public PlayerFieldsAndSquadTests()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x258, Convert.FromHexString("BC1B4EFF3421D417E017A60E7A008C000000")));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x26C, [5]));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x234, BitConverter.GetBytes(9_018_000)));
        // Jordan Ayew's international block: 120 caps, 33 goals, 1 youth cap, 1 youth goal.
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x3BC, Convert.FromHexString("780021000101")));
    }

    PlayerRecord Mainoo() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    PlayerFieldAccess Access() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Reads_the_values_FMRTE_shows()
    {
        var v = Access().ReadAll(Mainoo());
        Assert.Equal(7100, v[PlayerField.MatchSharpness]);
        Assert.Equal(-178, v[PlayerField.Jadedness]);
        Assert.Equal(8500, v[PlayerField.Condition]);
        Assert.Equal(5, v[PlayerField.Morale]);
        Assert.Equal(9_018_000, v[PlayerField.AskingPrice]);
        Assert.Equal(120, v[PlayerField.Caps]);
        Assert.Equal(33, v[PlayerField.InternationalGoals]);
        Assert.Equal(1, v[PlayerField.SubCaps]);
        Assert.Equal(1, v[PlayerField.SubGoals]);
        // Not separate fields in FM27, so never offered.
        Assert.False(v.ContainsKey(PlayerField.MatchExercise));
        Assert.False(v.ContainsKey(PlayerField.DaysInCountry));
    }

    [Fact]
    public void Writes_touch_only_their_bytes_and_refuse_stale_or_non_players()
    {
        var before = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        Access().Set(Mainoo(), PlayerField.Morale, 5, 20);
        Access().Set(Mainoo(), PlayerField.Condition, 8500, 10000);
        var after = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        Assert.Equal(20, after[0x26C]);
        Assert.Equal(new byte[] { 0x10, 0x27 }, after[0x25C..0x25E]); // 10000 = 0x2710
        after[0x26C] = before[0x26C]; after[0x25C..0x25E].CopyTo(before, 0x25C);
        Assert.Equal(before, after);

        var snapshot = Mainoo();
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x26C, [9]));
        Assert.Throws<StaleObjectException>(() => Access().Set(snapshot, PlayerField.Morale, 20, 1));
        Assert.Equal(9, _w.Bytes(TestWorld.Mainoo + 0x26C, 1)[0]);
        Assert.Throws<InvalidOperationException>(() => Access().Set(snapshot, PlayerField.MatchExercise, 0, 1));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo, BitConverter.GetBytes(TestWorld.ModuleBase + TestWorld.OtherVtable)));
        Assert.Null(Access().Read(snapshot, PlayerField.Morale));
        Assert.Throws<StaleObjectException>(() => Access().Set(snapshot, PlayerField.Morale, 9, 1));
    }

    [Fact]
    public void Squad_reader_returns_players_in_vector_order_and_skips_non_players()
    {
        var team = _w.Memory.Map(Team, 0x50);
        TestWorld.Set64(team, 0, TestWorld.ModuleBase + TeamVt);
        TestWorld.Set64(team, 0x38, Squad);
        TestWorld.Set64(team, 0x40, Squad + 3 * 8);
        TestWorld.Set64(team, 0x48, Squad + 4 * 8);
        var squad = _w.Memory.Map(Squad, 4 * 8);
        TestWorld.Set64(squad, 0, TestWorld.Yamal);
        TestWorld.Set64(squad, 8, TestWorld.NotAPlayer);
        TestWorld.Set64(squad, 16, TestWorld.Mainoo);

        var players = new SquadReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Team);
        Assert.Equal([TestWorld.Yamal, TestWorld.Mainoo], players.Select(p => p.Address));

        Assert.True(_w.Memory.TryWrite(Team, BitConverter.GetBytes(TestWorld.ModuleBase + TestWorld.OtherVtable)));
        Assert.Empty(new SquadReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Team));
    }

    [Fact]
    public void Scalar_mass_edit_uses_FMRTE_editor_limits()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + (ulong)_w.Profile.Fields.Id!.Value, BitConverter.GetBytes(1)));
        var players = new[] { Mainoo() };
        var access = Access();
        Assert.Single(PlayerScalarMassEdit.Plan(players, access, PlayerField.Condition, MassEditMode.Set, 5000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayerScalarMassEdit.Plan(players, access, PlayerField.Condition, MassEditMode.Set, 4999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayerScalarMassEdit.Plan(players, access, PlayerField.MatchSharpness, MassEditMode.Set, 4999));
    }
}

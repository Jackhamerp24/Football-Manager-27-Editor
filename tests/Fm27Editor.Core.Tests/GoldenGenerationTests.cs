using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Golden Generation as FM Studio 1.1.1 computes it (CIL read 8/11/2026): PA in [min, max] (defaults 150–180), CA in
/// [min(90, hi), hi] with hi = min(125, PA − 20) kept to 1..PA, Professionalism 19–20, Ambition 17–19. The bounds below are
/// worked by hand from that formula, not taken from the code.
/// </summary>
public class GoldenGenerationTests
{
    readonly TestWorld _w = new();

    public GoldenGenerationTests() =>
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));

    GoldenGenerationWriter Writer(int seed = 1) => new(_w.Memory, _w.Profile, TestWorld.ModuleBase, new Random(seed));

    PlayerRecord Mainoo() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).LoadAll().First(p => p.Address == TestWorld.Mainoo);

    [Fact]
    public void Default_roll_stays_inside_the_studio_bounds_and_reaches_every_edge()
    {
        var writer = Writer();
        var rolls = Enumerable.Range(0, 20000).Select(_ => writer.Roll(GoldenGenerationWriter.DefaultMinPa, GoldenGenerationWriter.DefaultMaxPa)).ToList();
        Assert.Equal(150, rolls.Min(r => r.PotentialAbility));
        Assert.Equal(180, rolls.Max(r => r.PotentialAbility));
        Assert.Equal(90, rolls.Min(r => r.CurrentAbility));
        Assert.Equal(125, rolls.Max(r => r.CurrentAbility));
        Assert.Equal([19, 20], rolls.Select(r => r.Professionalism).Distinct().Order());
        Assert.Equal([17, 18, 19], rolls.Select(r => r.Ambition).Distinct().Order());
    }

    [Theory]
    [InlineData(100, 80, 80)]     // PA 100: hi = 80, lo = 80
    [InlineData(105, 85, 85)]
    [InlineData(145, 90, 125)]    // hi reaches the 125 cap at PA 145, lo is still min(90, hi) = 90
    [InlineData(110, 90, 90)]     // PA 110: hi = 90, lo = 90
    [InlineData(200, 90, 125)]    // hi 125, lo 90
    public void Current_ability_range_follows_the_potential(int pa, int lowest, int highest)
    {
        var writer = Writer();
        var cas = Enumerable.Range(0, 3000).Select(_ => writer.Roll(pa, pa).CurrentAbility).ToList();
        Assert.Equal(lowest, cas.Min());
        Assert.Equal(highest, cas.Max());
    }

    [Theory]
    [InlineData(250, 100, 100, 200)]   // kept to 1..200 first, then the reversed pair is swapped
    [InlineData(0, 0, 1, 1)]           // below 1 is kept to 1
    [InlineData(150, 999, 150, 200)]   // above 200 is kept to 200
    public void Potential_bounds_are_kept_to_1_200_and_ordered(int min, int max, int expectedMin, int expectedMax) =>
        Assert.Equal((expectedMin, expectedMax), GoldenGenerationWriter.NormalisePotential(min, max));

    [Fact]
    public void A_tiny_potential_keeps_current_ability_at_least_1_and_never_above_potential()
    {
        var roll = Writer().Roll(1, 1);
        Assert.Equal(1, roll.PotentialAbility);
        Assert.Equal(1, roll.CurrentAbility);
    }

    [Fact]
    public void Apply_writes_exactly_the_four_fields()
    {
        var player = Mainoo();
        byte[] before = _w.Memory.Read(TestWorld.Mainoo, 0x3C8);
        var values = Writer(7).Apply(player, 160, 170);
        byte[] after = _w.Memory.Read(TestWorld.Mainoo, 0x3C8);
        Assert.Equal(values.CurrentAbility, BitConverter.ToInt16(after, 0x264));
        Assert.Equal(values.PotentialAbility, BitConverter.ToInt16(after, 0x266));
        Assert.Equal(values.Professionalism, after[0x2FC]);
        Assert.Equal(values.Ambition, after[0x2F9]);
        Assert.InRange(values.PotentialAbility, 160, 170);
        var changed = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToList();
        Assert.All(changed, i => Assert.True(i is >= 0x264 and <= 0x267 || i == 0x2FC || i == 0x2F9, $"unexpected byte 0x{i:X} changed"));
    }

    [Fact]
    public void A_player_changed_since_the_snapshot_is_refused()
    {
        var player = Mainoo();
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x266, BitConverter.GetBytes((short)199)));
        Assert.Throws<StaleObjectException>(() => Writer().Apply(player));
    }

    [Fact]
    public void A_change_in_any_of_the_four_fields_is_refused_before_writing()
    {
        var player = Mainoo();
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x2F9, [3]));   // Ambition
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().Apply(player));
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Fact]
    public void Undo_puts_the_four_values_back()
    {
        var player = Mainoo();
        var writer = Writer(11);
        var plan = writer.Plan(player, 150, 180);
        byte[] before = _w.Memory.Read(TestWorld.Mainoo, 0x3C8);
        writer.Apply(plan);
        Assert.NotEqual(before[0x264..0x268], _w.Memory.Read(TestWorld.Mainoo + 0x264, 4));
        writer.Undo(plan);
        Assert.Equal(before, _w.Memory.Read(TestWorld.Mainoo, 0x3C8));
    }

    [Fact]
    public void Undo_refuses_a_player_changed_after_the_apply()
    {
        var player = Mainoo();
        var writer = Writer(11);
        var plan = writer.Plan(player, 150, 180);
        writer.Apply(plan);
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x2FC, [1]));
        int writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.Undo(plan));
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Fact]
    public void The_same_seed_replays_the_same_roll()
    {
        var first = new GoldenGenerationWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase, new Random(2026)).Roll(150, 180);
        var replay = new GoldenGenerationWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase, new Random(2026)).Roll(150, 180);
        var other = new GoldenGenerationWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase, new Random(2027)).Roll(150, 180);
        Assert.Equal(first, replay);
        Assert.NotEqual(first, other);
    }
}

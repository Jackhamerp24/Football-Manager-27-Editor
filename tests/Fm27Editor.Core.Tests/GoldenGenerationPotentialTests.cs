using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The potential windows and the wonderkid lift. Every expected value here is worked by hand from the specification:
/// the band list is written out one by one, and the lift is the gap at the band's share with the floor taken by division.
/// </summary>
public class GoldenGenerationPotentialTests
{
    /// <summary>Start at 10, width 30, step 10, seventeen times — written out rather than computed.</summary>
    static readonly (int Min, int Max)[] Bands =
    [
        (10, 40), (20, 50), (30, 60), (40, 70), (50, 80), (60, 90), (70, 100), (80, 110), (90, 120),
        (100, 130), (110, 140), (120, 150), (130, 160), (140, 170), (150, 180), (160, 190), (170, 200),
    ];

    [Fact]
    public void The_presets_are_seventeen_overlapping_windows_of_thirty_stepping_by_ten()
    {
        Assert.Equal(17, GoldenGenerationBands.Count);
        Assert.Equal(30, GoldenGenerationBands.Width);
        Assert.Equal(10, GoldenGenerationBands.Step);
        Assert.Equal(Bands, GoldenGenerationBands.All.Select(b => (b.Min, b.Max)));
        // Neighbours overlap by twenty, which is what makes the presets a ladder rather than a set of boxes.
        Assert.Equal(20, Bands[0].Max - Bands[1].Min);
    }

    [Fact]
    public void Every_roll_stays_inside_its_own_window()
    {
        var rng = new Random(4242);
        foreach (var band in GoldenGenerationBands.All)
            Assert.All(Enumerable.Range(0, 2000).Select(_ => band.Roll(rng)),
                roll => Assert.InRange(roll, band.Min, band.Max));
    }

    [Fact]
    public void Both_ends_of_a_window_are_reachable()
    {
        var band = GoldenGenerationBands.Band(0);
        // The window asks for a value below 41, so 40 is inside it and a roll answering 40 must reach the top end.
        Assert.Equal(10, band.Roll(new StubRandom(10)));
        Assert.Equal(40, band.Roll(new StubRandom(40)));
    }

    [Fact]
    public void A_band_outside_the_ladder_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GoldenGenerationBands.Band(-1));

    [Theory]
    [InlineData(0, 0, 1, 1)]              // below one is kept to one
    [InlineData(150, 999, 150, 200)]      // above two hundred is kept to two hundred
    [InlineData(250, 300, 200, 200)]      // both ends above the cap meet at it
    [InlineData(150, 160, 150, 160)]      // a window inside the range is left alone
    public void A_custom_window_keeps_its_ends_inside_one_and_two_hundred(int min, int max, int expectedMin, int expectedMax)
    {
        var window = GoldenGenerationPotential.Custom(min, max);
        Assert.Equal((expectedMin, expectedMax), (window.Min, window.Max));
    }

    [Theory]
    [InlineData(250, 100)]   // clamped to 200–100, so the minimum is still above the maximum
    [InlineData(160, 150)]
    [InlineData(5, 3)]
    public void A_custom_window_whose_minimum_is_above_its_maximum_is_refused(int min, int max) =>
        Assert.Throws<ArgumentException>(() => GoldenGenerationPotential.Custom(min, max));

    [Fact]
    public void The_max_potential_branch_takes_the_top_value_only_on_one_roll_in_sixty_four()
    {
        var window = GoldenGenerationBands.Band(0);   // 10–40
        Assert.Equal(40, GoldenGenerationRoll.Potential(window, true, new StubRandom(0)));   // 0 out of 0–64: the hit
        Assert.Equal(15, GoldenGenerationRoll.Potential(window, true, new StubRandom(1, 15)));
        Assert.Equal(15, GoldenGenerationRoll.Potential(window, true, new StubRandom(63, 15)));
        var off = new StubRandom(15);
        Assert.Equal(15, GoldenGenerationRoll.Potential(window, false, off));
        Assert.Equal(1, off.Calls);   // with the branch off the roll in 0–64 is never asked for
    }

    [Theory]
    [InlineData(119, 30)]
    [InlineData(120, 40)]   // the band changes exactly at 120
    [InlineData(149, 40)]
    [InlineData(150, 55)]   // and again at 150
    [InlineData(179, 55)]
    [InlineData(180, 70)]   // and again at 180
    [InlineData(200, 70)]
    public void The_share_of_the_gap_a_hit_closes_follows_the_potential(int potential, int expected) =>
        Assert.Equal(expected, WonderkidRoll.GapPercent(potential));

    [Theory]
    [InlineData(180, 100, 56)]   // gap 80, 70% of 80 is 56
    [InlineData(200, 101, 69)]   // gap 99, 70% of 99 is 69.3, floored to 69
    [InlineData(100, 50, 15)]    // gap 50, 30% of 50 is 15
    [InlineData(149, 100, 19)]   // gap 49, 40% of 49 is 19.6, floored to 19
    [InlineData(190, 101, 62)]   // gap 89, 70% of 89 is 62.3, floored to 62
    public void A_hit_lifts_the_ability_by_the_floored_share_of_the_gap(int potential, int current, int expected) =>
        Assert.Equal(expected, WonderkidRoll.Gain(potential, current));

    [Theory]
    [InlineData(180, 100, true)]    // below 180 and below 126
    [InlineData(150, 110, false)]   // below 150, but 110 is not below 105
    [InlineData(100, 70, false)]    // 70 is not below 70% of 100
    [InlineData(100, 69, true)]     // 69 is
    [InlineData(100, 100, false)]   // no room at all
    public void A_player_only_lifts_when_it_is_below_the_potential_and_below_70_percent(int potential, int current, bool expected) =>
        Assert.Equal(expected, WonderkidRoll.Eligible(potential, current));

    [Fact]
    public void A_hit_lifts_towards_the_potential_and_a_miss_or_no_chance_leaves_the_ability_alone()
    {
        Assert.Equal(156, WonderkidRoll.CurrentAfter(180, 100, WonderkidChance.NinetyNine, new StubRandom(0)));    // 100 + 56
        Assert.Equal(100, WonderkidRoll.CurrentAfter(180, 100, WonderkidChance.NinetyNine, new StubRandom(99)));   // 99 is not below 99
        Assert.Equal(100, WonderkidRoll.CurrentAfter(180, 100, WonderkidChance.Off, new StubRandom(0)));            // no chance was asked for
        Assert.Equal(110, WonderkidRoll.CurrentAfter(150, 110, WonderkidChance.NinetyNine, new StubRandom(0)));     // no room to grow
    }

    [Fact]
    public void The_offered_chance_decides_whether_the_roll_hits()
    {
        Assert.Equal(156, WonderkidRoll.CurrentAfter(180, 100, WonderkidChance.Five, new StubRandom(4)));      // 4 is below 5
        Assert.Equal(100, WonderkidRoll.CurrentAfter(180, 100, WonderkidChance.Five, new StubRandom(5)));      // 5 is not
        Assert.Equal(156, WonderkidRoll.CurrentAfter(180, 100, WonderkidChance.NinetyNine, new StubRandom(98)));
        Assert.Equal(100, WonderkidRoll.CurrentAfter(180, 100, WonderkidChance.NinetyNine, new StubRandom(99)));
    }

    [Fact]
    public void Only_off_and_the_six_offered_chances_exist()
    {
        Assert.True(WonderkidRoll.IsOffered(WonderkidChance.Off));
        Assert.Equal(new[] { 5, 10, 25, 50, 75, 99 }, WonderkidRoll.Offered.Select(chance => (int)chance));
        Assert.False(WonderkidRoll.IsOffered((WonderkidChance)7));
        Assert.False(WonderkidRoll.IsOffered((WonderkidChance)100));
    }
}

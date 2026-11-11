using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Finding the new generations and never rolling the same player twice. The diff compares ids only, and a player
/// whose id cannot be read is reported instead of being called new or old.
/// </summary>
public class GoldenGenerationRegensTests
{
    [Fact]
    public void Only_ids_the_later_snapshot_has_are_new_generations()
    {
        var before = new[] { GoldenGenerationFixtures.Player(1), GoldenGenerationFixtures.Player(2), GoldenGenerationFixtures.Player(3) };
        var after = new[] { GoldenGenerationFixtures.Player(2), GoldenGenerationFixtures.Player(3), GoldenGenerationFixtures.Player(4) };
        var diff = GoldenGenerationRegens.Diff(before, after);
        Assert.Equal([4], diff.New.Select(player => player.Id));
        Assert.Empty(diff.UnknownIdentity);
    }

    [Fact]
    public void A_player_whose_id_cannot_be_read_is_reported_rather_than_called_new_or_old()
    {
        var diff = GoldenGenerationRegens.Diff(
            [GoldenGenerationFixtures.Player(1)],
            [GoldenGenerationFixtures.Player(1), GoldenGenerationFixtures.Player(null)]);
        Assert.Empty(diff.New);
        Assert.Single(diff.UnknownIdentity);
    }

    [Fact]
    public void The_ids_of_a_snapshot_are_the_readable_ones_only()
    {
        var ids = GoldenGenerationRegens.Ids([GoldenGenerationFixtures.Player(7), GoldenGenerationFixtures.Player(null), GoldenGenerationFixtures.Player(9)]);
        Assert.Equal([7, 9], ids.Order());
    }

    [Fact]
    public void The_ledger_refuses_a_second_roll_for_the_same_player()
    {
        var ledger = new GoldenGenerationLedger();
        Assert.True(ledger.Add(42));
        Assert.False(ledger.Add(42));
        Assert.True(ledger.Contains(42));
        Assert.False(ledger.Contains(41));
        Assert.Equal([42], ledger.Ids);
    }

    [Fact]
    public void The_ledger_round_trips_through_a_file_sorted_one_id_per_line()
    {
        var path = Path.Combine(Path.GetTempPath(), "fm27-golden-ledger-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var ledger = new GoldenGenerationLedger([9, 3, 5]);
            ledger.Save(path);
            Assert.Equal(["3", "5", "9"], File.ReadAllLines(path));

            var loaded = GoldenGenerationLedger.Load(path);
            Assert.Equal([3, 5, 9], loaded.Ids);
            Assert.True(loaded.Contains(5));
            Assert.False(loaded.Add(5));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Saving_over_an_existing_ledger_replaces_it_without_leaving_a_partial_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "fm27-golden-overwrite-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "1\n2\n");
            new GoldenGenerationLedger([7, 8]).Save(path);

            Assert.Equal(["7", "8"], File.ReadAllLines(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_missing_ledger_file_is_an_empty_ledger_and_a_damaged_one_is_refused()
    {
        var missing = Path.Combine(Path.GetTempPath(), "fm27-golden-absent-" + Guid.NewGuid().ToString("N") + ".txt");
        Assert.Equal(0, GoldenGenerationLedger.Load(missing).Count);

        var damaged = Path.Combine(Path.GetTempPath(), "fm27-golden-damaged-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(damaged, "42\nnot-an-id\n");
            Assert.Throws<FormatException>(() => GoldenGenerationLedger.Load(damaged));
        }
        finally { File.Delete(damaged); }
    }

    [Fact]
    public void A_negative_maximum_age_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GoldenGenerationRegenOptions { MaxAge = -1 });

    [Fact]
    public void Every_core_filter_is_off_when_it_is_not_given()
    {
        var options = new GoldenGenerationRegenOptions();
        Assert.Null(options.MaxAge);
        Assert.Null(options.BornAfter);
        Assert.Null(options.RequireYouthContract);
    }
}

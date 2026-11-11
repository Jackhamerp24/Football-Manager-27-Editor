using Fm27Editor.App;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Tests;

namespace Fm27Editor.App.Tests;

public sealed class SaveChangedTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.1", "1.2.3")]
    [InlineData("0.4.0+abc123", "0.4.0")]
    [InlineData("V10.0.1", "10.0.1")]
    public void ReleaseTagsAreReadAsVersions(string text, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateCheck.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    public void TextThatIsNoVersionIsIgnored(string? text) => Assert.Null(UpdateCheck.Parse(text));

    [Theory]
    [InlineData("v0.4.0", "0.3.0", true)]
    [InlineData("v0.3.1", "0.3.0", true)]
    [InlineData("v0.3.0", "0.3.0", false)]
    [InlineData("v0.2.9", "0.3.0", false)]
    [InlineData("v1.0.0", "0.9.9", true)]
    [InlineData("nonsense", "0.3.0", false)]
    public void OnlyAHigherTagIsAnUpdate(string tag, string current, bool expected) =>
        Assert.Equal(expected, UpdateCheck.IsNewer(tag, Version.Parse(current)));

    [Fact]
    public void LocalBuildsNeverAskForUpdates() => Assert.Null(UpdateCheck.CurrentVersion());

    [Fact]
    public void EveryWriteIsRefusedWhileTheGameHoldsAnotherSave()
    {
        var world = new TestWorld();
        var repo = new PlayerRepository(world.Memory, world.Profile, TestWorld.ModuleBase);
        bool changed = false;
        var edits = new EditService(new PlayerWriter(world.Memory, world.Profile, TestWorld.ModuleBase), repo) { Blocked = () => changed };
        var player = repo.Reload(TestWorld.Mainoo)!;
        var field = PlayerFields.All.Single(f => f.Group == "Positions" && f.Id == "Striker");
        int before = world.Memory.Writes;

        changed = true;
        Assert.Throws<SaveChangedException>(() => edits.Apply(player, field, 14));
        Assert.Throws<SaveChangedException>(() => edits.Execute(() => world.Memory.TryWrite(TestWorld.Mainoo + 0x15C, new byte[] { 7 })));
        Assert.Throws<SaveChangedException>(() => edits.ExecuteUnrecorded(() => world.Memory.TryWrite(TestWorld.Mainoo + 0x15C, new byte[] { 7 })));
        Assert.Equal(before, world.Memory.Writes);
        Assert.Equal(20, world.Bytes(TestWorld.Mainoo + 0x15C, 1)[0]);

        changed = false;                                     // Load Game read the new save: writes work again
        edits.Apply(player, field, 14);
        Assert.Equal(14, world.Bytes(TestWorld.Mainoo + 0x15C, 1)[0]);
    }
}

/// <summary>
/// Which save is loaded is told by player objects, not by the list's shape. Each case is a hand-built game: a map of address to
/// the player id living there (absent = no player object any more).
/// </summary>
public sealed class SaveFingerprintTests
{
    // A save of 1,000 players at 0x10000 + 0x700 * i with ids 5000 + i.
    static readonly List<ulong> List = Enumerable.Range(0, 1000).Select(i => 0x10000UL + 0x700UL * (ulong)i).ToList();
    static Dictionary<ulong, int> Game() => List.Select((a, i) => (a, i)).ToDictionary(x => x.a, x => 5000 + x.i);
    static Func<ulong, int?> Reader(Dictionary<ulong, int> game) => a => game.TryGetValue(a, out int id) ? id : null;

    static SaveFingerprint Read(Dictionary<ulong, int> game) => SaveFingerprint.Take(List, Reader(game))!;

    [Fact]
    public void Samples_are_spread_over_the_list()
    {
        var f = Read(Game());
        Assert.Equal(SaveFingerprint.Size, f.Samples.Count);
        Assert.Equal([0, 125, 250, 375, 500, 625, 750, 875], f.Samples.Select(s => s.Id - 5000));
    }

    [Fact]
    public void Playing_on_is_the_same_save_even_when_the_list_moves_grows_or_loses_players()
    {
        var game = Game();
        var f = Read(game);
        // Regens added (list grows and the game copies it to a new buffer: neither touches the player objects) and two of
        // the sampled players retire, so their objects are gone.
        for (int i = 0; i < 300; i++) game[0x900000UL + 0x700UL * (ulong)i] = 9000 + i;
        game.Remove(f.Samples[1].Address);
        game.Remove(f.Samples[6].Address);
        Assert.True(f.StillHolds(Reader(game)));
    }

    [Fact]
    public void Another_save_frees_the_objects_and_is_seen()
    {
        var f = Read(Game());
        var other = Enumerable.Range(0, 1000).ToDictionary(i => 0x800000UL + 0x700UL * (ulong)i, i => 7000 + i);
        Assert.False(f.StillHolds(Reader(other)));
    }

    [Fact]
    public void Another_save_reusing_the_same_addresses_is_seen_by_the_ids()
    {
        var f = Read(Game());
        var other = List.Select((a, i) => (a, i)).ToDictionary(x => x.a, x => 70000 + x.i);
        Assert.False(f.StillHolds(Reader(other)));
    }

    [Fact]
    public void An_empty_list_gives_no_fingerprint() => Assert.Null(SaveFingerprint.Take([], _ => 1));
}

public sealed class ClubVisionItemNameTests
{
    static Fm27Editor.Core.Competitions.CompetitionRecord Competition(int index, int id, string name) =>
        new(0, index, id, name, null, 0, 0, 0, 0, 0, null, null);

    [Fact]
    public void CompetitionItemsAreNamedByIndexNotById()
    {
        // FMRTE 27 build8, Real Madrid, Club Vision: item 637 is UEFA Champions League, 51 LALIGA EA SPORTS, 658 Copa de Su Majestad el Rey.
        // The game ids are different numbers (1301394, 67, 1301422); looking the item up by id left the bare numbers on screen.
        var names = MainWindow.VisionCompetitionNames([
            Competition(637, 1301394, "UEFA Champions League"), Competition(51, 67, "LALIGA EA SPORTS"),
            Competition(658, 1301422, "Copa de Su Majestad el Rey"), Competition(67, 5, "Some other league")]);
        Assert.Equal("UEFA Champions League", names[637]);
        Assert.Equal("LALIGA EA SPORTS", names[51]);
        Assert.Equal("Copa de Su Majestad el Rey", names[658]);
        Assert.Equal("Some other league", names[67]);          // index 67 is another competition: ids never leak in
        Assert.False(names.ContainsKey(1301394));
    }
}

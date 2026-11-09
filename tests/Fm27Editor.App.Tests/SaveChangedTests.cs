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

public sealed class PlayerListMarkTests
{
    static readonly PlayerListMark Saved = new(0x1000, 0x1000 + 8 * 50_000, 0xAAAA);

    [Fact] public void TheSameListIsTheSameSave() => Assert.False(Saved.IsAnotherSaveThan(Saved));

    [Fact] public void ANewListBufferIsAnotherSave() => Assert.True((Saved with { Begin = 0x2000 }).IsAnotherSaveThan(Saved));

    [Fact] public void ANewFirstPlayerIsAnotherSaveEvenInTheSameBuffer() => Assert.True((Saved with { First = 0xBBBB }).IsAnotherSaveThan(Saved));

    [Fact] public void AShorterListInTheSameBufferIsAnotherSave() => Assert.True((Saved with { End = Saved.End - 8 * 26_000 }).IsAnotherSaveThan(Saved));

    [Fact] public void NewPlayersGeneratedWhilePlayingAreStillTheSameSave() => Assert.False((Saved with { End = Saved.End + 8 * 300 }).IsAnotherSaveThan(Saved));
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

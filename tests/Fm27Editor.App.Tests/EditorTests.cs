using Fm27Editor.App;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Tests;

namespace Fm27Editor.App.Tests;

public sealed class EditorTests
{
    [Fact]
    public void PitchIncludesEachPositionOnceWithRightAboveLeftAndGoalkeeperAtLeft()
    {
        var positions = PitchLayout.Positions;
        Assert.Equal(14, positions.Count);
        Assert.Equal(Enum.GetValues<PlayerPosition>().Order(), positions.Select(p => p.Position).Order());
        var goalkeeper = positions.Single(p => p.Position == PlayerPosition.Goalkeeper);
        var striker = positions.Single(p => p.Position == PlayerPosition.Striker);
        Assert.True(goalkeeper.X < striker.X);
        foreach (var (right, left) in new[] { (PlayerPosition.DefenderRight, PlayerPosition.DefenderLeft), (PlayerPosition.WingBackRight, PlayerPosition.WingBackLeft), (PlayerPosition.MidfielderRight, PlayerPosition.MidfielderLeft), (PlayerPosition.AttackingMidRight, PlayerPosition.AttackingMidLeft) })
        {
            Assert.True(positions.Single(p => p.Position == right).Y < positions.Single(p => p.Position == left).Y);
        }
    }

    [Fact]
    public void PositionDraftEditsOnlyChosenPositionWhenSaved()
    {
        var (world, _, edits, player) = Setup();
        var draft = new PlayerDraft(player);
        var striker = PlayerFields.All.Single(f => f.Group == "Positions" && f.Id == "Striker");
        draft.Stage(striker, 14);
        Assert.Equal(20, world.Bytes(TestWorld.Mainoo + 0x15C, 1)[0]);
        draft.Save(edits);
        Assert.Equal(14, world.Bytes(TestWorld.Mainoo + 0x15C, 1)[0]);
        Assert.Equal(0, world.Bytes(TestWorld.Mainoo + 0x15B, 1)[0]);
    }
    [Fact]
    public void MemoryFailureShowsUsefulMessageWithoutInternalAddress()
    {
        var error = new MemoryAccessException("Read failed at 0x12345678");
        Assert.Equal("Couldn't access game data. Check that a save is loaded and retry.", UserMessage.For(error));
    }
    static (TestWorld world, PlayerRepository repo, EditService edits, PlayerRecord player) Setup()
    {
        var world = new TestWorld();
        var repo = new PlayerRepository(world.Memory, world.Profile, TestWorld.ModuleBase);
        return (world, repo, new EditService(new PlayerWriter(world.Memory, world.Profile, TestWorld.ModuleBase), repo), repo.Reload(TestWorld.Mainoo)!);
    }
    static FieldDescriptor<PlayerRecord> Crossing => PlayerFields.All.Single(f => f.Id == "Crossing");

    [Fact]
    public void DraftDoesNotWriteUntilSaveAndThenWritesAllChangedFields()
    {
        var (world, _, edits, player) = Setup();
        var draft = new PlayerDraft(player);
        var pace = PlayerFields.All.Single(f => f.Id == "Pace");
        draft.Stage(Crossing, 12);
        draft.Stage(pace, 16);
        Assert.Equal(0, world.Memory.Writes);
        Assert.Equal(45, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        Assert.Equal(66, world.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
        var updated = draft.Save(edits);
        Assert.Equal(60, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        Assert.Equal(80, world.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
        Assert.Equal(2, world.Memory.Writes);
        Assert.Equal(12, updated.Attribute(PlayerAttribute.Crossing));
        Assert.Equal(16, updated.Attribute(PlayerAttribute.Pace));
        Assert.Equal(0, draft.Count);
    }

    [Fact]
    public void DraftStopsWhenAnotherPendingFieldChangedInGame()
    {
        var (world, repo, edits, player) = Setup();
        var draft = new PlayerDraft(player);
        var pace = PlayerFields.All.Single(f => f.Id == "Pace");
        draft.Stage(Crossing, 12);
        draft.Stage(pace, 16);
        Assert.True(world.Memory.TryWrite(TestWorld.Mainoo + 0x185, new byte[] { 75 }));
        Assert.Throws<StaleObjectException>(() => draft.Save(edits));
        Assert.Equal(60, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        Assert.Equal(75, world.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
        Assert.Equal(1, draft.Count);
        var rebased = draft.Rebase(repo.Reload(player.Address)!);
        Assert.Equal(16, rebased.Value(pace));
        Assert.Equal(1, rebased.Count);
        Assert.Equal(75, world.Bytes(TestWorld.Mainoo + 0x185, 1)[0]);
    }

    [Fact]
    public void SearchMatchesBothNamesWithoutCaseOrAccents()
    {
        var (_, _, _, original) = Setup();
        var player = new PlayerRecord { Address = original.Address, VtableRva = original.VtableRva, FirstName = "Benjamin", LastName = "Šeško", FullName = "Benjamin Šeško", CommonName = "Striker", CurrentAbility = 153, PotentialAbility = 177, HomeReputation = 0, CurrentReputation = 0, WorldReputation = 0, Height = 0, RawAttributes = original.RawAttributes, AttributeScale = 5, Positions = original.Positions, Personality = original.Personality };
        var module = new PlayersModule();
        Assert.True(module.Matches(player, "sesko"));
        Assert.True(module.Matches(player, "STRIKER"));
        Assert.False(module.Matches(player, "Yamal"));
    }
    [Fact]
    public void EditWritesOnlyCrossingAndUndoRestoresIt()
    {
        var (world, _, edits, player) = Setup();
        var before = world.Bytes(TestWorld.Mainoo, 0x3C8);
        var updated = edits.Apply(player, Crossing, 12);
        var after = world.Bytes(TestWorld.Mainoo, 0x3C8);
        Assert.Equal(60, after[0x15F]);
        Assert.Equal(Enumerable.Range(0, before.Length).Where(i => i != 0x15F).Select(i => before[i]), Enumerable.Range(0, after.Length).Where(i => i != 0x15F).Select(i => after[i]));
        var record = Assert.Single(edits.History);
        Assert.Equal((9, 12, "Crossing", true), (record.OldValue, record.NewValue, record.Field, record.Succeeded));
        Assert.Equal(9, edits.Undo(updated, Crossing, record).Attribute(PlayerAttribute.Crossing));
        Assert.Equal(45, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
    }
    [Fact]
    public void EditReloadsGameChangedAbility()
    {
        var (world, _, edits, player) = Setup();
        Assert.True(world.Memory.TryWrite(TestWorld.Mainoo + 0x264, BitConverter.GetBytes((short)154)));
        Assert.Equal(154, edits.Apply(player, Crossing, 12).CurrentAbility);
    }
    [Fact]
    public void StaleEditDoesNotOverwriteGameValue()
    {
        var (world, repo, edits, player) = Setup();
        Assert.True(world.Memory.TryWrite(TestWorld.Mainoo + 0x15F, new byte[] { 50 }));
        Assert.Throws<StaleObjectException>(() => edits.Apply(player, Crossing, 12));
        Assert.Equal(10, repo.Reload(player.Address)!.Attribute(PlayerAttribute.Crossing));
        Assert.Equal(50, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        Assert.False(Assert.Single(edits.History).Succeeded);
    }
    [Fact]
    public void RegistryContainsOnlyAvailablePlayersModuleAndNoActions()
    {
        Assert.Equal("Players", Assert.Single(Modules.Registered).Title);
        Assert.Empty(Modules.Actions);
    }
}

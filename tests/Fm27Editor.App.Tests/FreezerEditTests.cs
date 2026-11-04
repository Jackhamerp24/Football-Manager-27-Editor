using Fm27Editor.App;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Tests;

namespace Fm27Editor.App.Tests;

public class FreezerEditTests
{
    [Fact]
    public void Saving_a_frozen_attribute_updates_the_freeze_target()
    {
        var world = new TestWorld();
        Assert.True(world.Memory.TryWrite(TestWorld.Mainoo + 0x294, BitConverter.GetBytes(2000123586)));
        var repo = new PlayerRepository(world.Memory, world.Profile, TestWorld.ModuleBase);
        var writer = new PlayerWriter(world.Memory, world.Profile, TestWorld.ModuleBase);
        var player = repo.Reload(TestWorld.Mainoo)!;
        var frozen = FrozenPlayer.Capture(player, [(PlayerAttribute.Crossing, FreezeMode.Fixed)]);
        var edits = new EditService(writer, repo, (p, field) => frozen.AcceptEdit(p, Enum.Parse<PlayerAttribute>(field)));
        var crossing = PlayerFields.All.Single(f => f.Id == "Crossing");
        edits.Apply(player, crossing, 12);
        Assert.Equal(60, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
        Assert.Equal(0, frozen.Reconcile(repo, writer));
        Assert.True(world.Memory.TryWrite(TestWorld.Mainoo + 0x15F, [40]));
        Assert.Equal(1, frozen.Reconcile(repo, writer));
        Assert.Equal(60, world.Bytes(TestWorld.Mainoo + 0x15F, 1)[0]);
    }
}

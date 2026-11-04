using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class GamePassAttributeWriterTests
{
    [Fact]
    public void Attributes_write_one_byte_and_reject_changed_identity()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000, cunha = 0x10000000, mbeumo = 0x10001000;
        void MapPlayer(ulong address, int id, string bytes)
        {
            var obj = memory.Map(address, 0x3C8);
            BitConverter.GetBytes(module + 0x4CD6318u).CopyTo(obj, 0);
            BitConverter.GetBytes(id).CopyTo(obj, 0x294);
            Convert.FromHexString(bytes).CopyTo(obj, 0x150);
        }
        // Real Game Pass bytes; independent FMRTE snapshots: Dribbling Cunha 18, Mbeumo 15.
        MapPlayer(cunha, 19334410, "0101010101010C010C11140F120101415A4A374B134646401E410F050F0F050A3B370518053C4F46645541374A190F050A4B403C464A3C32464B4B325050461E463C50463100000000000000000000");
        MapPlayer(mbeumo, 48042658, "0101010101010D010E111214110101414B4628411E4B41551E4114050F050A0A41460A230A554B642D4B4B46551E0F050F4B3C414B4B2828234141423C414B144255554B4100000000000000000000");
        var repo = new PlayerRepository(memory, profile, module);
        var first = repo.Reload(cunha)!;
        var second = repo.Reload(mbeumo)!;
        Assert.Equal(18, first.Attribute(PlayerAttribute.Dribbling));
        Assert.Equal(15, second.Attribute(PlayerAttribute.Dribbling));
        var writer = new PlayerWriter(memory, profile, module);
        var before = memory.Read(mbeumo, 0x3C8);
        writer.SetAttribute(second, PlayerAttribute.Dribbling, 17);
        var after = memory.Read(mbeumo, 0x3C8);
        Assert.Equal(85, after[0x160]);
        after[0x160] = before[0x160];
        Assert.Equal(before, after);
        writer.SetCurrentAbility(first, 150);
        Assert.Equal(150, BitConverter.ToInt16(memory.Read(cunha + (ulong)profile.Fields.CurrentAbility, 2)));
        writer.SetAttribute(first, PlayerAttribute.Handling, 15);
        Assert.Equal(75, memory.Read(cunha + (ulong)profile.Attributes[PlayerAttribute.Handling], 1)[0]);
        Assert.True(memory.TryWrite(cunha + 0x294, BitConverter.GetBytes(19334411)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.SetAttribute(first, PlayerAttribute.Dribbling, 16));
        Assert.Equal(writes, memory.Writes);
    }
}

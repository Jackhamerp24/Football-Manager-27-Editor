using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Tactical familiarity layout measured against FM Studio 27 v1.1.1's "Tactical Effectiveness" on Arsenal (6/11):
/// +0x190 tactical 0..100, +0x155..+0x15C eight role ratings 0..20, +0x25C condition 0..10000, +0x26C morale,
/// +0x2F8 one more 0..20 value. Saliba 35→100 and Havertz 81→100 changed exactly these fields.
/// </summary>
public class PlayerFamiliarityTests
{
    const uint PlayerVtable = 0x4D27228;
    const ulong Player = 0x7900_0000;
    readonly TestWorld _w = new();

    byte[] MapPlayer()
    {
        var bytes = _w.Memory.Map(Player, 0x300);
        TestWorld.Set64(bytes, 0, TestWorld.ModuleBase + PlayerVtable);
        bytes[Pretend(0x190)] = 35;
        for (int i = 0; i < 8; i++) bytes[Pretend(0x155 + i)] = 1;
        BitConverter.GetBytes((ushort)9324).CopyTo(bytes, Pretend(0x25C));
        bytes[Pretend(0x26C)] = 17;
        bytes[Pretend(0x2F8)] = 16;
        return bytes;
    }

    // PlayerFamiliarity offsets are absolute in the object; the mapped array starts at Player.
    static int Pretend(int offset) => offset;

    PlayerFamiliarityAccess Access() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Reads_the_measured_fields_and_writes_only_the_tactical_byte()
    {
        var bytes = MapPlayer();
        var snapshot = Access().Read(Player);
        Assert.NotNull(snapshot);
        Assert.Equal(35, snapshot!.Tactical);
        Assert.Equal(8, snapshot.Roles.Length);
        Assert.All(snapshot.Roles, role => Assert.Equal(1, role));
        Assert.Equal(9324, snapshot.Condition);
        Assert.Equal(17, snapshot.Morale);

        Access().SetTactical(snapshot, 100);
        Assert.Equal(100, bytes[0x190]);
        Assert.Equal(1, bytes[0x155]);
        Assert.Equal(9324, BitConverter.ToUInt16(bytes, 0x25C));
        Assert.Equal(17, bytes[0x26C]);
        Assert.Equal(16, bytes[0x2F8]);
        Assert.Equal(100, Access().Read(Player)!.Tactical);

        Assert.Throws<ArgumentOutOfRangeException>(() => Access().SetTactical(snapshot, 101));
        Assert.Throws<StaleObjectException>(() => Access().SetTactical(snapshot, 50));
    }

    [Fact]
    public void Writes_one_role_slot_and_rejects_out_of_range_values()
    {
        MapPlayer();
        var snapshot = Access().Read(Player)!;
        Access().SetRole(snapshot, 3, 20);
        Assert.Equal(20, Access().Read(Player)!.Roles[3]);
        Assert.Equal(1, Access().Read(Player)!.Roles[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Access().SetRole(snapshot, 8, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => Access().SetRole(snapshot, 0, 21));
        Assert.Null(Access().Read(0));
    }
}

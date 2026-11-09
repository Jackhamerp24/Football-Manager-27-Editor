using System.Text.Json.Nodes;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// FMRTE 27's "Tactical Understanding" preset, pressed on Declan Rice (Game Pass 27.0.0+2493426, 9/11/2026, Frida on WriteProcessMemory) wrote
/// 10000 into 16 dwords (kept their flag bits: after the preset they read 0x4E710, 0x2A710, 0x32710, 0x46710, 0x2A710, 0x3A710, 0x52710,
/// 0x2A710, 0x12710, 0x2A710, 0xE710, 0x52710, 0x2A710, 0x1E710, 0x2A710, 0x36710, in the six lists of sizes 3, 4, 1, 3, 2, 3) and 10000 into the
/// u16 at +0xE of seven 16-byte role entries. The test builds that shape with other low bits and expects exactly those after-values.
/// </summary>
public class PlayerTacticalUnderstandingTests : IDisposable
{
    const ulong Holder = 0x5100_0000, Lists = 0x5110_0000, Roles = 0x5120_0000;
    static readonly int[] Sizes = [3, 4, 1, 3, 2, 3];
    static readonly uint[] After =
    [
        0x4E710, 0x2A710, 0x32710,
        0x46710, 0x2A710, 0x3A710, 0x52710,
        0x2A710,
        0x12710, 0x2A710, 0xE710,
        0x52710, 0x2A710,
        0x1E710, 0x2A710, 0x36710,
    ];
    readonly TestWorld _w = new();
    readonly string _profilePath = Path.Combine(Path.GetTempPath(), $"fm27-tu-{Guid.NewGuid():N}.json");
    readonly OffsetProfile _profile;

    public PlayerTacticalUnderstandingTests()
    {
        var root = JsonNode.Parse(File.ReadAllText(TestWorld.ProfilePath))!.AsObject();
        root["player"]!.AsObject()["tacticalUnderstanding"] = new JsonObject
        {
            ["holder"] = "0x100", ["stride"] = "0x20", ["count"] = "0x6", ["roleVector"] = "0xC0", ["roleEntry"] = "0x10", ["roleValue"] = "0xE",
        };
        File.WriteAllText(_profilePath, root.ToJsonString());
        _profile = OffsetProfile.LoadFile(_profilePath);

        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x100, BitConverter.GetBytes(Holder)));
        var holder = _w.Memory.Map(Holder, 0x100);
        var lists = _w.Memory.Map(Lists, 0x100);
        int at = 0, flat = 0;
        for (int i = 0; i < Sizes.Length; i++)
        {
            TestWorld.Set64(holder, i * 0x20, Lists + (ulong)at);
            TestWorld.Set64(holder, i * 0x20 + 8, Lists + (ulong)(at + Sizes[i] * 4));
            TestWorld.Set64(holder, i * 0x20 + 0x10, Lists + (ulong)(at + Sizes[i] * 4));
            TestWorld.Set64(holder, i * 0x20 + 0x18, (ulong)i);   // area number: 0 Passing Style … 5 Pressing Intensity
            for (int k = 0; k < Sizes[i]; k++, flat++) BitConverter.GetBytes((After[flat] & ~0x3FFFu) | 0x0123u).CopyTo(lists, at + k * 4);   // same flags, a lower value
            at += Sizes[i] * 4;
        }
        var roles = _w.Memory.Map(Roles, 0x70);
        for (int e = 0; e < 7; e++) { roles[e * 0x10] = (byte)(0x79 + e); BitConverter.GetBytes((ushort)0x1234).CopyTo(roles, e * 0x10 + 0xE); }
        TestWorld.Set64(holder, 0xC0, Roles);
        TestWorld.Set64(holder, 0xC8, Roles + 0x70);
        TestWorld.Set64(holder, 0xD0, Roles + 0x70);
    }

    public void Dispose() => File.Delete(_profilePath);

    PlayerRecord Player() => new PlayerRepository(_w.Memory, _profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;
    PlayerTacticalUnderstanding Tactics() => new(_w.Memory, _profile, TestWorld.ModuleBase);

    [Fact]
    public void Maximise_writes_the_values_FMRTE_wrote_and_keeps_every_flag_bit()
    {
        int changed = Tactics().Maximise(Player());

        Assert.Equal(23, changed);
        int flat = 0, at = 0;
        foreach (int size in Sizes)
        {
            for (int k = 0; k < size; k++, flat++)
                Assert.Equal(After[flat], BitConverter.ToUInt32(_w.Bytes(Lists + (ulong)(at + k * 4), 4)));
            at += size * 4;
        }
        for (int e = 0; e < 7; e++)
        {
            Assert.Equal(10000, BitConverter.ToUInt16(_w.Bytes(Roles + (ulong)(e * 0x10 + 0xE), 2)));
            Assert.Equal(0x79 + e, _w.Bytes(Roles + (ulong)(e * 0x10), 1)[0]);   // the rest of the entry is untouched
        }
    }

    [Fact]
    public void Maximise_leaves_a_player_already_at_100_alone()
    {
        var tactics = Tactics();
        tactics.Maximise(Player());
        Assert.Equal(0, tactics.Maximise(Player()));
    }

    [Fact]
    public void Read_reports_the_lowest_value_per_slider()
    {
        var values = Tactics().Read(Player())!;
        Assert.Equal(6, values.Sliders.Count);
        Assert.All(values.Sliders, s => Assert.Equal(2, s));    // low 14 bits 0x0123 = 291 → 2
        Assert.Equal(46, values.PositionRole);                  // 0x1234 = 4660 → 46
    }
}

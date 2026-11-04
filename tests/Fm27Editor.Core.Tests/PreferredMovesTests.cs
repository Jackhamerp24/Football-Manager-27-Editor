using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Preferred moves at player + 0x348, bytes as dumped on 22/11/2026. Expected bits are the boxes FMRTE 27 ticks, counted in
/// row order: Kane 7 Tries Killer Balls Often, 10 Places Shots, 19 Comes Deep To Get Ball, 41 Gets Crowd Going, 43 Tries
/// Long Range Passes (bit 62 is set but has no box); Mbappé 10, 35 Tries Tricks, 51 Runs With Ball Often, 57 Cuts Inside From Left Wing.
/// </summary>
public class PreferredMovesTests
{
    readonly TestWorld _w = new();
    PreferredMovesAccess Access() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);
    PlayerRecord Player() => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;

    static int[] Bits(ulong v) => Enumerable.Range(0, 64).Where(i => (v >> i & 1) != 0).ToArray();

    [Theory]
    [InlineData("80040800000A0040", new[] { 7, 10, 19, 41, 43 })]
    [InlineData("0004000008000802", new[] { 10, 35, 51, 57 })]
    public void Reads_the_boxes_FMRTE_ticks(string bytes, int[] boxes)
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x348, Convert.FromHexString(bytes)));
        Assert.Equal(boxes, Bits(Access().Read(Player())!.Value & PreferredMovesAccess.Editable));
    }

    [Fact]
    public void Writes_only_box_bits_keeps_bit_62_and_refuses_stale_values()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x348, Convert.FromHexString("80040800000A0040")));
        var before = _w.Bytes(TestWorld.Mainoo, 0x3C8);
        ulong current = Access().Read(Player())!.Value;
        Access().Set(Player(), current, (current & PreferredMovesAccess.Editable) | 1); // FMRTE ticked Runs With Ball Down Left
        before[0x348] = 0x81;
        Assert.Equal(before, _w.Bytes(TestWorld.Mainoo, 0x3C8));
        Access().Set(Player(), current | 1, 0);
        Assert.Equal("0000000000000040", Convert.ToHexString(_w.Bytes(TestWorld.Mainoo + 0x348, 8)));
        Assert.Throws<StaleObjectException>(() => Access().Set(Player(), current, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Access().Set(Player(), 1UL << 62, 1UL << 61));
    }
}

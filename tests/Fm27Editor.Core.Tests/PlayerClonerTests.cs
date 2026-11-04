using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Clone Center copies the ticked groups from the source onto the target and nothing else (FMRTE 27's "What to copy" list).
/// Source values are TestWorld's Mainoo (CA 153, PA 177, Crossing raw 45, Pace raw 66, Striker 20, Ambition 10, 184 cm,
/// born 19/4/2005); the target starts as Yamal with 180 cm and 13/7/2007.
/// </summary>
public class PlayerClonerTests
{
    readonly TestWorld _w = new();

    public PlayerClonerTests()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Yamal + 0x22E, BitConverter.GetBytes((short)180)));
        Assert.True(_w.Memory.TryWrite(TestWorld.Yamal + 0x310, [.. BitConverter.GetBytes((short)194), .. BitConverter.GetBytes((short)2007)]));
    }

    PlayerRecord Load(ulong a) => new PlayerRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase).Reload(a)!;
    PlayerCloner Cloner() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Copies_only_the_ticked_groups()
    {
        var before = _w.Bytes(TestWorld.Yamal, 0x3C8);
        int n = Cloner().Clone(Load(TestWorld.Mainoo), Load(TestWorld.Yamal), CloneParts.Ability | CloneParts.Technical | CloneParts.Positions);
        var after = _w.Bytes(TestWorld.Yamal, 0x3C8);

        Assert.Equal(4, n); // CA, PA, Crossing, Striker
        Assert.Equal((short)153, BitConverter.ToInt16(after, 0x264));
        Assert.Equal((short)177, BitConverter.ToInt16(after, 0x266));
        Assert.Equal(45, after[0x15F]);   // raw value copied, not rounded through 1–20
        Assert.Equal(20, after[0x15C]);
        foreach (var at in new[] { 0x264, 0x265, 0x266, 0x267, 0x15F, 0x15C }) before[at] = after[at];
        Assert.Equal(before, after);      // Pace (Physical), Ambition (Personality), height, birth date untouched
    }

    [Fact]
    public void Personal_details_copy_height_and_birth_date()
    {
        Cloner().Clone(Load(TestWorld.Mainoo), Load(TestWorld.Yamal), CloneParts.PersonalDetails | CloneParts.Physical | CloneParts.Personality);
        var after = _w.Bytes(TestWorld.Yamal, 0x3C8);
        Assert.Equal((short)184, BitConverter.ToInt16(after, 0x22E));
        Assert.Equal(Convert.FromHexString("6D00D507"), after[0x310..0x314]); // day 109, 2005
        Assert.Equal(66, after[0x185]);
        Assert.Equal(10, after[0x2F9]);
        Assert.Equal(0, Cloner().Clone(Load(TestWorld.Mainoo), Load(TestWorld.Yamal), CloneParts.PersonalDetails | CloneParts.Physical | CloneParts.Personality));
    }

    [Fact]
    public void Refuses_a_stale_target_and_itself()
    {
        var target = Load(TestWorld.Yamal);
        Assert.True(_w.Memory.TryWrite(TestWorld.Yamal + 0x264, BitConverter.GetBytes((short)50)));
        Assert.Throws<StaleObjectException>(() => Cloner().Clone(Load(TestWorld.Mainoo), target, CloneParts.Ability));
        Assert.Throws<InvalidOperationException>(() => Cloner().Clone(Load(TestWorld.Mainoo), Load(TestWorld.Mainoo), CloneParts.All));
    }
}

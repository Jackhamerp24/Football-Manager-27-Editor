using Fm27Editor.Core.Clubs;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Bytes captured from build 2493426 while FMRTE 27 showed Man Utd (First team) as
/// Passing Style 30, Build Up 76, Chance Creation 91, Line of Engagement 13, Pressing Intensity 61, Pressing Trap 57.
/// The block lives at 0x2723F4F5B00 in that session; only the offsets and encoding are asserted here.
/// </summary>
public class TacticalBlockTests
{
    const ulong Block = 0x5000_0000;

    static FakeMemory Build()
    {
        var memory = new FakeMemory();
        var bytes = memory.Map(Block, TacticalBlock.Size);
        void Put(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
        Put(TacticalBlock.BuildUp, 0x00051DB0);          // 76 × 100, flags 20
        Put(TacticalBlock.ChanceCreation, 0x0003238C);   // 91 × 100, flags 12
        Put(TacticalBlock.PassingStyle, 0x00010BB8);     // 30 × 100, flags 4
        Put(TacticalBlock.PressingTrap, 0x00029644);     // 57 × 100, flags 10
        Put(TacticalBlock.LineOfEngagement, 0x0004C514); // 13 × 100, flags 19
        Put(TacticalBlock.PressingIntensity, 0x000457D4); // 61 × 100, flags 17
        return memory;
    }

    [Fact]
    public void Reads_the_six_mapped_sliders_as_FMRTE_shows_them()
    {
        var values = TacticalBlock.Read(Build(), Block);
        Assert.NotNull(values);
        Assert.Equal(76, values!.BuildUp);
        Assert.Equal(91, values.ChanceCreation);
        Assert.Equal(30, values.PassingStyle);
        Assert.Equal(57, values.PressingTrap);
        Assert.Equal(13, values.LineOfEngagement);
        Assert.Equal(61, values.PressingIntensity);
        Assert.Null(values.PositionRole);
    }

    [Fact]
    public void Refuses_a_block_whose_slider_is_out_of_range_or_unreadable()
    {
        var memory = Build();
        Assert.True(memory.TryWrite(Block + TacticalBlock.BuildUp, BitConverter.GetBytes(0x0000FFFFu))); // 163.83
        Assert.Null(TacticalBlock.Read(memory, Block));
        Assert.Null(TacticalBlock.Read(memory, 0x6000_0000));
        Assert.Null(TacticalBlock.Read(memory, 0));
    }
}

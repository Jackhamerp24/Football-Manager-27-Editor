using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class PromiseDateTests
{
    [Fact]
    public void PromisesTheGameMadeCarryFlagBitsAboveTheDay()
    {
        // Bruno Fernandes' two promises, Steam 27.0.0+2493426: the made / deadline words from RAM, and the dates FMRTE 27 shows for them.
        byte[] made = Convert.FromHexString("E28AEA07");        // day word 0x8AE2: flags above day 226, year 2026
        byte[] deadline = Convert.FromHexString("248BEA07");    // day word 0x8B24: day 292
        Assert.Equal(new DateOnly(2026, 8, 14), PromiseStore.DateAt(made, 0));
        Assert.Equal(new DateOnly(2026, 10, 19), PromiseStore.DateAt(deadline, 0));
    }

    [Theory]
    [InlineData("E200EA07", 2026, 8, 14)]    // without flags, as FMRTE writes them
    [InlineData("0100EA07", 2026, 1, 1)]
    public void PromisesWithoutFlagsStillRead(string hex, int year, int month, int day) =>
        Assert.Equal(new DateOnly(year, month, day), PromiseStore.DateAt(Convert.FromHexString(hex), 0));

    [Theory]
    [InlineData("0000EA07")]    // day 0
    [InlineData("01006B07")]    // year 1899
    [InlineData("0100FC08")]    // year 2300
    public void GarbageIsNotADate(string hex) => Assert.Null(PromiseStore.DateAt(Convert.FromHexString(hex), 0));
}

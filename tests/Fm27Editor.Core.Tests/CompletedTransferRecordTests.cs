using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// FM's record of Cole Palmer's £50M move Chelsea → Man Utd (In-Game Editor, Steam 27.0.1+2503222, 14/11/2026, offer id 142).
/// Writing the same record for an editor move (Saka → Liverpool) changed Liverpool's FFP by the same amounts as Man Utd's.
/// </summary>
public class CompletedTransferRecordTests
{
    const string Palmer =
        "d81cf7ddfa7f00000100000063006200" + "00000000000000000000000000000000" + "00000000000000000000000000000000" +
        "00100000000000002845e95ee9010000" + "90c4f854e901000090c4f854e9010000" + "58a1f854e901000080f0fa028e000000" +
        "4c280000ffffffffffffffff681bea07" + "5e1bea075e1bea070f1aeb07011aeb07" + "01006c07000001000007000000140100" +
        "00000000000000000000000000000000" + "0000000000000000000e475ce9010000" + "00000000000000000000000000000000" +
        "00000000000000000000000000000000";

    [Fact]
    public void Offer_matches_the_game_apart_from_dates_and_two_unknown_words()
    {
        var game = Convert.FromHexString(Palmer);
        var built = CompletedTransferRecord.Offer(0x7FFADDF71CD8, 0x1E95EE94528, 0x1E954F8C490, 0x1E954F8A158, 50_000_000, 142, 10316,
            new DateOnly(2026, 12, 16), 0x1E95C470E00);
        var diff = Enumerable.Range(0, game.Length).Where(i => game[i] != built[i]).ToArray();
        // +0x0C/+0x0E: 0x63 and 0x62 in the game's record, 0 in the staff records (meaning unknown).
        // +0x6C..+0x7F: the dates (flag bits and transfer-window days differ).
        Assert.All(diff, i => Assert.True(i is 0x0C or 0x0E || i is >= 0x6C and < 0x80, $"byte +0x{i:X2} differs"));
        Assert.Equal(50_000_000, BitConverter.ToInt32(built, 0x58));
        Assert.Equal(0x14, built[0x8D]);
    }
}

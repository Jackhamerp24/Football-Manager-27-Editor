using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Bytes FM27's In-Game Editor wrote when it moved free-agent staff (RAM snapshots, Steam 27.0.1+2503222, 14/11/2026, game
/// date 15/12/2026): Daniel Duarte → Man Utd as Coach and Pascal Fonsalas → Man City as Physio.
/// </summary>
public class FreeStaffSigningTests
{
    static readonly DateOnly Today = new(2026, 12, 15);
    const ulong ContractVt = 0x7FFADDB24B08, OfferVt = 0x7FFADDF71CD8;

    const string DuarteContract =
        "084bb2ddfa7f00008026f25ce9010000" + "10660656e901000010660656e9010000" + "2b040000000002000000000000200000" +
        "000000000000000000000000ffffffff" + "ffffffff5d7fea07b600ec075d7fea07" + "01006c0700000040000046e214ff0000" +
        "00000000000000000000000000000000" + "00000000000000000000000000000000" + "00000000000000000000000000000000" +
        "00000000000000000000000000000000" + "00000000000000000000000000000000" + "0000000001006c07000000006c070000" +
        "00000001ff000000";

    [Theory]
    [InlineData(0x1E95CF22680UL, 0x1E956066610UL, 1067, 2, 2028, DuarteContract)]
    public void Contract_matches_the_game(ulong person, ulong team, int wage, byte job, int expiryYear, string game) =>
        Assert.Equal(game, Convert.ToHexString(FreeStaffSigning.Contract(ContractVt, person, team, wage, job, Today, new DateOnly(expiryYear, 6, 30))).ToLowerInvariant());

    /// <summary>Fonsalas: only +0x5A differs (0x44 in the game, 0x46 written), everything else matches.</summary>
    [Fact]
    public void Second_contract_differs_only_in_the_unknown_byte()
    {
        var game = Convert.FromHexString("084bb2ddfa7f00000005af5ce9010000c8660656e9010000c8660656e90100002204000000000c000000000000200000000000000000000000000000ffffffffffffffff5d7fea07b500ee075d7fea0701006c0700000040000044e214ff0000");
        var built = FreeStaffSigning.Contract(ContractVt, 0x1E95CAF0500, 0x1E9560666C8, 1058, 0x0C, Today, new DateOnly(2030, 6, 30));
        var diff = Enumerable.Range(0, game.Length).Where(i => game[i] != built[i]).ToArray();
        Assert.Equal([0x5A], diff);
    }

    [Fact]
    public void Offer_matches_the_game()
    {
        const string game = "d81cf7ddfa7f00000100000000000000" + "00000000000000000000000000000000" + "00000000000000000000000000000000" +
            "00100000000000008026f25ce9010000" + "10660656e901000010660656e9010000" + "00000000000000000000000084000000" +
            "42280000ffffffffffffffff677fea07" + "5d7fea075d7fea0701006c075d7fea07" + "01006c070000010000ff000000140100" +
            "00000000000000000000000000000000" + "00000000000000000000000000000000" + "00000000000000000000000000000000" +
            "00000000000000000000000000000000";
        Assert.Equal(game, Convert.ToHexString(FreeStaffSigning.Offer(OfferVt, 0x1E95CF22680, 0x1E956066610, 132, 10306, Today)).ToLowerInvariant());
    }

    /// <summary>Bytes 4-7 of the game's record were heap leftovers (01e9 high half of a pointer); everything else must match.</summary>
    [Fact]
    public void Link_matches_the_game()
    {
        var game = Convert.FromHexString("01000000e9010000000000000000000000000000000000000000000000000000" +
            "000000000000000084000000816967ff000000000000000001006c07000002010100000000000000");
        var built = FreeStaffSigning.Link(132);
        Assert.Equal(game.Length, built.Length);
        var diff = Enumerable.Range(0, game.Length).Where(i => game[i] != built[i]).ToArray();
        Assert.Equal([4, 5], diff);
    }
}

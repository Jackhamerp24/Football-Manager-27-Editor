using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class TransferTermsLayoutTests
{
    [Fact]
    public void RecordsAndObjectMatchCapturedBrunoOffer()
    {
        var terms = new TransferTerms(14000000, 36, 7654321, 31, 6543210, 11, 543210, 13,
            4321098, 14, 3210987, 19, false, 87000, 12, false);
        var records = TransferTermsLayout.Records(terms);
        string[] expected = ["809FD500247F00000C000000", "B1CB74001F7F0000", "6AD763000B7F0000",
            "EA4908000D7F0000", "4AEF41000E7F0000", "EBFE3000FF7F000001006C0701006C07"];
        Assert.Equal(expected, records.Select(Convert.ToHexString));
        ulong[] pointers = [0x1740CA567F0, 0x1740CA56D60, 0x1740CA568E0,
            0x1740CA56850, 0x173B2E75CF0, 0x173B2E75FC0];
        var expectedObject = Convert.FromHexString("F067A50C74010000FC67A50C74010000FC67A50C74010000606DA50C74010000686DA50C74010000686DA50C74010000E068A50C74010000E868A50C74010000E868A50C740100005068A50C740100005868A50C740100005868A50C74010000F05CE7B273010000F85CE7B273010000F85CE7B273010000C05FE7B273010000D05FE7B273010000D05FE7B273010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000D8530100130000000000000000000000");
        Assert.Equal(expectedObject, TransferTermsLayout.Object(terms, pointers, records));
    }

    [Fact]
    public void InstalmentRecordMatchesDembleAndBellinghamCaptures()
    {
        // Dembélé: 12,000,000 over 24 months, Yearly -> last dword 12.
        var dembele = new TransferTerms(12000000, 24, 2345678, 25, 3456789, 10, 456789, 10,
            5678901, 10, 6789012, 17, true, 69076, 12, true);
        string[] expected =
        [
            "001BB700187F00000C000000",
            "CECA2300197F0000",
            "15BF34000A7F0000",
            "55F806000A7F0000",
            "35A756000A7F0000",
            "94976700FF7F000001006C0701006C07",
        ];
        Assert.Equal(expected, TransferTermsLayout.Records(dembele).Select(Convert.ToHexString));

        // Bellingham: 18,000,000 over 24 months, Every 6 months -> last dword 6, no other terms.
        var bellingham = new TransferTerms(18000000, 24, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0, 6, true);
        var records = TransferTermsLayout.Records(bellingham);
        Assert.Equal("80A81201187F000006000000", Convert.ToHexString(records[0]));
        Assert.All(records.Skip(1), r => Assert.Empty(r));
    }

    static byte[][] EmptyRecords() => Enumerable.Range(0, 6).Select(_ => Array.Empty<byte>()).ToArray();

    [Theory]
    [InlineData(true, false, "00000000001400000000000000000000")]   // Jobe Bellingham: sell-on 20% Of the profit, no friendly (FMRTE 27 build8 trace)
    [InlineData(false, true, "00000000140001000000000000000000")]   // Kobbie Mainoo: sell-on 20%, not of the profit, friendly
    public void SellOnProfitAndFriendlyFlagsMatchTheFmrteCaptures(bool profit, bool friendly, string expectedTail)
    {
        var terms = new TransferTerms(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 20, profit, 0, 12, friendly);
        var data = TransferTermsLayout.Object(terms, new ulong[6], EmptyRecords());
        Assert.Equal(expectedTail, Convert.ToHexString(data.AsSpan(0xC0)).ToLowerInvariant());
        Assert.All(data.AsSpan(0, 0xC0).ToArray(), x => Assert.Equal(0, x));
    }

    [Fact]
    public void SellOnAbove100IsRejected()
    {
        var terms = new TransferTerms(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 101, false, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => TransferTermsLayout.Object(terms, new ulong[6], EmptyRecords()));
    }
}

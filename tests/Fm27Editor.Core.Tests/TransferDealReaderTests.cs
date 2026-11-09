using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Layout confirmed on the live game (FM27 27.0.0+2493426): deal vtable +0x4EE4098, person +0x38,
/// buying team +0x40, selling team +0x50, fee +0x58, day counter +0x5C, accept flag +0x84, status +0x88.
/// The oracle values are the bid shown by FMRTE for Bento (Olympique de Marseille to Manchester United,
/// £1,000,000, status "Offer Made") and the status words FM Studio 1.1.1 decodes in GetStatusString.
/// </summary>
public class TransferDealReaderTests
{
    const ulong Module = 0x7FFB18130000UL;
    const ulong Deal = 0x5000_0000;
    const uint Fm27Vtable = 0x4EE4098;

    // The oracle bytes come from the Game Pass image, whose deal vtable is +0x4EE4098; Steam names its own (see the test at the end).
    static OffsetProfile Profile => OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-gamepass.json"));

    static (FakeMemory Memory, TransferDealReader Reader) Build(uint vtableRva, Action<byte[]> fill, ulong at = Deal)
    {
        var memory = new FakeMemory();
        var raw = memory.Map(at, 0x100);
        BitConverter.GetBytes(Module + vtableRva).CopyTo(raw, 0);
        fill(raw);
        return (memory, new TransferDealReader(memory, Profile, Module));
    }

    [Fact]
    public void Reads_the_live_bento_offer()
    {
        var (_, reader) = Build(Fm27Vtable, raw =>
        {
            BitConverter.GetBytes(0x2244696E8C0UL).CopyTo(raw, 0x38);
            BitConverter.GetBytes(0x224372C22E0UL).CopyTo(raw, 0x40);
            BitConverter.GetBytes(0x224372C7640UL).CopyTo(raw, 0x50);
            BitConverter.GetBytes(1_000_000).CopyTo(raw, 0x58);
            BitConverter.GetBytes((short)8610).CopyTo(raw, 0x5C);
            BitConverter.GetBytes(0x0000FF00).CopyTo(raw, 0x88);
        });

        Assert.True(reader.IsDeal(Deal));
        var deal = reader.Read(Deal);
        Assert.NotNull(deal);
        Assert.Equal(0x2244696E8C0UL, deal!.PersonAddress);
        Assert.Equal(0x224372C22E0UL, deal.BuyingTeam);
        Assert.Equal(0x224372C7640UL, deal.SellingTeam);
        Assert.Equal(1_000_000, deal.Fee);
        Assert.Equal((short)8610, deal.DayCounter);
        Assert.Equal("Offer made", deal.Status);
        Assert.True(deal.IsOpen);
        Assert.False(deal.IsAccepted);
    }

    [Theory]
    [InlineData(0x0000FF00, "Offer made")]
    [InlineData(0x0000FF01, "Offer made")]
    [InlineData(0xFF02, "Offer accepted")]
    [InlineData(0xFF03, "Player accepted")]
    [InlineData(0xFF04, "Rejected")]
    [InlineData(0xFF05, "Cancelled")]
    [InlineData(0x0701, "Confirmed")]
    [InlineData(0x01000002, "Delayed")]
    public void Status_words_use_the_game_labels(int statusRaw, string expected)
    {
        Assert.Equal(expected, TransferDealStatus.Label(statusRaw));
    }

    [Fact]
    public void An_unmapped_status_never_leaks_its_number()
    {
        string label = TransferDealStatus.Label(0x0000FF7B);
        Assert.Equal("In progress", label);
        Assert.DoesNotContain("7", label);
        Assert.DoesNotContain("123", label);
    }

    [Fact]
    public void Accepted_status_is_recognised_from_the_status_word()
    {
        var (_, reader) = Build(Fm27Vtable, raw => BitConverter.GetBytes(0xFF02).CopyTo(raw, 0x88));
        var deal = reader.Read(Deal)!;
        Assert.True(deal.IsAccepted);
        Assert.False(deal.IsOpen);
        Assert.Equal("Offer accepted", deal.Status);
    }

    [Fact]
    public void FindForPlayer_picks_the_object_holding_that_person()
    {
        var memory = new FakeMemory();
        var profile = Profile;
        const ulong second = 0x5000_1000;
        var first = memory.Map(Deal, 0x100);
        var other = memory.Map(second, 0x100);
        BitConverter.GetBytes(Module + Fm27Vtable).CopyTo(first, 0);
        BitConverter.GetBytes(Module + Fm27Vtable).CopyTo(other, 0);
        BitConverter.GetBytes(0xAAAAUL).CopyTo(first, 0x38);
        BitConverter.GetBytes(0xBBBBUL).CopyTo(other, 0x38);
        BitConverter.GetBytes(42).CopyTo(first, 0x58);
        BitConverter.GetBytes(99).CopyTo(other, 0x58);

        var reader = new TransferDealReader(memory, profile, Module);
        var found = reader.FindForPlayer(0xBBBBUL, [Deal, second]);
        Assert.NotNull(found);
        Assert.Equal(second, found!.Address);
        Assert.Equal(99, found.Fee);
        Assert.Null(reader.FindForPlayer(0xCCCCUL, [Deal, second]));
    }

    [Fact]
    public void Reads_a_deal_with_either_fm_studio_vtable()
    {
        foreach (uint rva in new[] { 74_494_248u, 83_071_720u })
        {
            var (_, reader) = Build(rva, raw => BitConverter.GetBytes(5_000_000).CopyTo(raw, 0x58));
            Assert.True(reader.IsDeal(Deal));
            Assert.Equal(5_000_000, reader.Read(Deal)!.Fee);
        }
    }

    [Fact]
    public void Rejects_an_object_with_a_different_vtable()
    {
        var (_, reader) = Build(Fm27Vtable, _ => { });
        var memory = new FakeMemory();
        var other = memory.Map(0x6000_0000, 0x100);
        BitConverter.GetBytes(Module + 0x1234).CopyTo(other, 0);
        var otherReader = new TransferDealReader(memory, Profile, Module);

        Assert.False(otherReader.IsDeal(0x6000_0000));
        Assert.Null(otherReader.Read(0x6000_0000));
        Assert.True(reader.IsDeal(Deal));
    }

    [Fact]
    public void Writer_sets_the_fee_and_reads_it_back()
    {
        var (memory, reader) = Build(Fm27Vtable, raw => BitConverter.GetBytes(1_000_000).CopyTo(raw, 0x58));
        var deal = reader.Read(Deal)!;
        new TransferDealWriter(memory, Profile, Module).SetFee(deal, 1_000_000, 1_234_567);

        Assert.Equal(1_234_567, reader.Read(Deal)!.Fee);
    }

    [Fact]
    public void Writer_refuses_when_the_value_moved_since_the_snapshot()
    {
        var (memory, reader) = Build(Fm27Vtable, raw => BitConverter.GetBytes(1_000_000).CopyTo(raw, 0x58));
        var deal = reader.Read(Deal)!;
        Assert.True(memory.TryWrite(Deal + 0x58, BitConverter.GetBytes(2_000_000)));

        Assert.Throws<StaleObjectException>(() => new TransferDealWriter(memory, Profile, Module).SetFee(deal, 1_000_000, 5));
        Assert.Equal(2_000_000, reader.Read(Deal)!.Fee);
    }

    [Fact]
    public void Writer_refuses_when_the_object_is_gone()
    {
        var (memory, reader) = Build(Fm27Vtable, raw => BitConverter.GetBytes(1_000_000).CopyTo(raw, 0x58));
        var deal = reader.Read(Deal)!;
        Assert.True(memory.TryWrite(Deal, BitConverter.GetBytes(Module + 0x1234)));

        Assert.Throws<StaleObjectException>(() => new TransferDealWriter(memory, Profile, Module).SetFee(deal, 1_000_000, 5));
    }

    [Fact]
    public void AcceptOffer_writes_the_accepted_status_and_the_accept_flag()
    {
        var (memory, reader) = Build(Fm27Vtable, raw =>
        {
            BitConverter.GetBytes(1_000_000).CopyTo(raw, 0x58);
            BitConverter.GetBytes(0x0000FF00).CopyTo(raw, 0x88);
        });
        var deal = reader.Read(Deal)!;
        new TransferDealWriter(memory, Profile, Module).AcceptOffer(deal);

        var after = reader.Read(Deal)!;
        Assert.Equal(TransferDealStatus.OfferAccepted, after.StatusRaw);
        Assert.True(after.IsAccepted);
        Assert.Equal((byte)2, memory.Read(Deal + 0x84, 1)[0]);
    }

    [Fact]
    public void DayCounterToDate_reads_the_day_of_year_and_the_season_offset()
    {
        // 26 November 2026 = day 330; the low 9 bits carry the day, the bits above it the season offset.
        Assert.Equal(new DateOnly(2026, 11, 26), TransferDealReader.DayCounterToDate(330, 2026));
        Assert.Equal(new DateOnly(2026, 1, 1), TransferDealReader.DayCounterToDate(1, 2026));
        Assert.Null(TransferDealReader.DayCounterToDate(0, 2026));
        Assert.Null(TransferDealReader.DayCounterToDate(999, 2026));
    }

    [Fact]
    public void Steam_deals_are_recognised_by_the_steam_profile_vtable_only()
    {
        // Measured on the live Steam game (27.0.0+2493426): the offer class vtable is +0x4F3B2E8 there, +0x4EE4098 on Game Pass.
        var steam = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426.json"));
        var memory = new FakeMemory();
        var good = memory.Map(Deal, 0x100); BitConverter.GetBytes(Module + 0x4F3B2E8).CopyTo(good, 0);
        var other = memory.Map(Deal + 0x1000, 0x100); BitConverter.GetBytes(Module + 0x4EE4098).CopyTo(other, 0);
        var reader = new TransferDealReader(memory, steam, Module);
        Assert.True(reader.IsDeal(Deal));
        Assert.False(reader.IsDeal(Deal + 0x1000));
    }
}

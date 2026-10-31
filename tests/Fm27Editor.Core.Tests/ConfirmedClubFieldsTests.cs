using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Tests;

public class ConfirmedClubFieldsTests
{
    const ulong Club = 0x53000000, Info = 0x53010000, Finance = 0x53020000;

    [Theory]
    [InlineData(1, 1, 3, 100, 0)]        // Sheffield 708 FMRTE
    [InlineData(5, 1, 0, 90, 0)]         // Tottenham 728 FMRTE
    [InlineData(1, 1, 1, 100, 4295159)]   // PSG 868 FMRTE
    public void Six_new_mappings_read_independent_FMRTE_values_on_full_finance(int owner, int title, int benefactor, int revenue, int rental)
    {
        var (memory, access, fin) = World(compact: false, owner, title);
        // Literal measured offsets; never obtain fixture positions from the profile under test.
        Convert.FromHexString("01006C07").CopyTo(fin, 0x24);
        fin[0x34] = (byte)benefactor;
        BitConverter.GetBytes(revenue).CopyTo(fin, 0x7D0);
        BitConverter.GetBytes(rental).CopyTo(fin, 0x7F8);
        var values = access.ReadAll(Club, 0);
        Assert.Equal(owner, values[ClubField.OwnershipType]);
        Assert.Equal(title, values[ClubField.ChairpersonTitle]);
        Assert.Equal(benefactor, values[ClubField.RichBenefactor]);
        Assert.Equal(revenue, values[ClubField.TransferRevenuePercent]);
        Assert.Equal(rental, values[ClubField.StadiumRentalPerYear]);
        Assert.Equal(0, values[ClubField.EmbargoStart]);
        Assert.Equal(0, memory.Writes);
    }

    [Theory]
    [InlineData(1, 1, "F400E6071F00E8070100E8070000000004010000", 20220901, 4)] // Shenzhen day244/2022
    [InlineData(3, 2, "01006C07B600EC076D01EA070F00000400010000", 0, 0)]        // Ismaily
    public void Compact_finance_keeps_prefix_byte_fields_and_omits_full_only_fields(int owner, int title, string prefix, long start, int benefactor)
    {
        var (memory, access, fin) = World(compact: true, owner, title);
        Convert.FromHexString(prefix).CopyTo(fin, 0x24);
        // Readable other objects beyond this 0x38 record must not make its full-only fields appear.
        BitConverter.GetBytes(123456).CopyTo(memory.Map(Finance + 0x7D0, 4), 0);
        BitConverter.GetBytes(654321).CopyTo(memory.Map(Finance + 0x7F8, 4), 0);
        var values = access.ReadAll(Club, 0);
        Assert.Equal(owner, values[ClubField.OwnershipType]);
        Assert.Equal(title, values[ClubField.ChairpersonTitle]);
        Assert.Equal(start, values[ClubField.EmbargoStart]);
        Assert.Equal(benefactor, values[ClubField.RichBenefactor]); // +0x35 flag1 must not become code256
        Assert.False(values.ContainsKey(ClubField.TransferRevenuePercent));
        Assert.False(values.ContainsKey(ClubField.StadiumRentalPerYear));
        Assert.Equal(0, memory.Writes);
    }

    static (FakeMemory Memory, ClubFieldAccess Access, byte[] Finance) World(bool compact, int owner, int title)
    {
        var m = new FakeMemory();
        var c = m.Map(Club, 0x170);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + 0x4CDA1F8);
        TestWorld.Set64(c, 0x100, Info); TestWorld.Set64(c, 0x150, Finance);
        var i = m.Map(Info, 0x130);
        i[0x129] = (byte)owner; i[0x12A] = (byte)title;
        var f = m.Map(Finance, compact ? 0x38 : 0x8B0);
        TestWorld.Set64(f, 0, TestWorld.ModuleBase + (compact ? 0x4CF07F8u : 0x4CF0CC8u));
        return (m, new(m, OffsetProfile.LoadFile(TestWorld.ProfilePath), TestWorld.ModuleBase), f);
    }
}

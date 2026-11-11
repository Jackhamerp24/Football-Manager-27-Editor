using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class ClubAffiliationFeeTests
{
    const ulong List = 0x5000_0000, Record = 0x5001_0000, OtherList = 0x5002_0000;
    // Live FM27 2493426 bytes; FMRTE: Bayern→Unterhaching £322136; Nottingham→Crumlin £50000.
    const string Bayern = "6022D56973020000104CD5697302000058EA040021000000000000001C4200004E01E807B500EE0701006C07D72EEA0701323219FFFF0000";
    const string Nottingham = "705DD46973020000C064EA697302000050C30000010000000000000030020000F400D707B500EE0701006C0701006C0715323201FFFF0000";
    // FMRTE: Hibernian→Stranraer ends 31 May 2027; Bayern→Unterhaching ends 30 June 2030.
    const string Hibernian = "50F8D76973020000A013D8697302000000000000410000000000000004000000A700EA079700EB0701006C0701006C07013232FFFFFF6400";
    // FMRTE: Inverness→Nairn 75%; Bayern→Unterhaching 25%.
    const string Inverness = "C0F9D76973020000A0C7F8697302000000000000210000000000000014000000AA00E80701006C0701006C0701006C070132324BFFFF0000";
    // FMRTE: Livingston→Arbroath Players Go On Loan off; Hibernian→Stranraer on.
    const string Livingston = "A0FCD7697302000030CDD7697302000000000000010000000000000000000000BA00E50701006C0701006C0701006C07103232FFFFFF0000";
    // FMRTE: Metz→Forbach shares scouting knowledge; Hibernian→Stranraer does not.
    const string Metz = "A0D7D4697302000040BAD96973020000000000000108000000000000084000003101DB0701006C0701006C0701006C07033232FFFFFF0000";
    readonly FakeMemory memory = new();
    readonly OffsetProfile profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-steam.json"));
    ulong parent, feeder;

    void Build(string raw, int parentId, int feederId)
    {
        byte[] bytes = Convert.FromHexString(raw);
        parent = BitConverter.ToUInt64(bytes, 0); feeder = BitConverter.ToUInt64(bytes, 8);
        var p = memory.Map(parent, 0x140); var f = memory.Map(feeder, 0x140);
        TestWorld.Set64(p, 0, TestWorld.ModuleBase + 0x4CDC1F8);
        TestWorld.Set64(f, 0, TestWorld.ModuleBase + 0x4CDC1F8);
        BitConverter.GetBytes(parentId).CopyTo(p, 0xC); BitConverter.GetBytes(feederId).CopyTo(f, 0xC);
        TestWorld.Set64(p, 0x118, List); TestWorld.Set64(p, 0x120, List + 8); TestWorld.Set64(p, 0x128, List + 8);
        TestWorld.Set64(memory.Map(List, 8), 0, Record);
        TestWorld.Set64(f, 0x118, OtherList); TestWorld.Set64(f, 0x120, OtherList + 8); TestWorld.Set64(f, 0x128, OtherList + 8);
        TestWorld.Set64(memory.Map(OtherList, 8), 0, Record);
        bytes.CopyTo(memory.Map(Record, 0x38), 0);
    }

    ClubListReader Reader() => new(memory, profile, TestWorld.ModuleBase);
    ClubAffiliationWriter Writer() => new(memory, profile, TestWorld.ModuleBase);

    [Fact]
    public void Duplicate_and_remove_update_inline_header_without_overwriting_original_pointer()
    {
        Build(Bayern, 915, 958);
        var original = Assert.Single(Reader().ReadAffiliations(parent));
        ulong next = 0x5100_0000;
        ulong Allocate(int size) { ulong at = next; next += 0x1000; memory.Map(at, size); return at; }

        var added = Writer().Duplicate(parent, original, Allocate);
        Assert.NotNull(added);
        var header = memory.Read(parent + 0x118, 24);
        ulong begin = BitConverter.ToUInt64(header);
        Assert.Equal(begin + 16, BitConverter.ToUInt64(header, 8));
        Assert.Equal(Record, BitConverter.ToUInt64(memory.Read(begin, 8)));
        Assert.Equal(Record, BitConverter.ToUInt64(memory.Read(List, 8))); // original array untouched
        Assert.Equal(2, Reader().ReadAffiliations(parent).Count);
        Assert.Equal(added!.Address, Reader().ReadAffiliations(feeder)[1].Address);

        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().Remove(parent, original));
        Assert.Equal(writes, memory.Writes);
        Writer().Remove(parent, added!);
        Assert.Equal(begin + 8, BitConverter.ToUInt64(memory.Read(parent + 0x120, 8)));
        Assert.Equal(Record, Assert.Single(Reader().ReadAffiliations(parent)).Address);
        Assert.Equal(Record, Assert.Single(Reader().ReadAffiliations(feeder)).Address);
    }

    [Fact]
    public void Annual_fee_matches_FMRTE_and_writer_preserves_real_record()
    {
        Build(Bayern, 915, 958);
        var row = Assert.Single(Reader().ReadAffiliations(parent));
        Assert.Equal(322136, row.AnnualFee);
        Assert.Equal(915, row.ParentClub!.Id); Assert.Equal(958, row.FeederClub!.Id);
        var other = new ClubAffiliationFeeTests();
        other.Build(Nottingham, 692, 553498);
        Assert.Equal(50000, Assert.Single(other.Reader().ReadAffiliations(other.parent)).AnnualFee);

        Writer().SetAnnualFee(parent, row, 322137);
        Assert.Equal(Convert.FromHexString("6022D56973020000104CD5697302000059EA040021000000000000001C4200004E01E807B500EE0701006C07D72EEA0701323219FFFF0000"), memory.Read(Record, 0x38));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetAnnualFee(parent, row, 1));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Fee_writer_rejects_stale_membership_club_vtable_and_negative_encoding()
    {
        Build(Bayern, 915, 958);
        var row = Assert.Single(Reader().ReadAffiliations(parent));
        int writes = memory.Writes;
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetAnnualFee(parent, row, -1));
        Assert.Equal(writes, memory.Writes);
        Assert.True(memory.TryWrite(parent + 0x120, BitConverter.GetBytes(List)));
        writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetAnnualFee(parent, row, 1));
        Assert.Equal(writes, memory.Writes);
        Assert.True(memory.TryWrite(parent + 0x120, BitConverter.GetBytes(List + 8)));
        Assert.True(memory.TryWrite(feeder, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1234560UL)));
        writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetAnnualFee(parent, row, 1));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void End_date_matches_FMRTE_and_writer_preserves_real_record()
    {
        Build(Hibernian, 1575, 1600);
        var row = Assert.Single(Reader().ReadAffiliations(parent));
        Assert.Equal(new DateOnly(2027, 5, 31), row.EndDate);
        var other = new ClubAffiliationFeeTests();
        other.Build(Bayern, 915, 958);
        Assert.Equal(new DateOnly(2030, 6, 30), Assert.Single(other.Reader().ReadAffiliations(other.parent)).EndDate);
        Writer().SetEndDate(parent, row, new DateOnly(2027, 6, 1));
        Assert.Equal(Convert.FromHexString("50F8D76973020000A013D8697302000000000000410000000000000004000000A700EA079800EB0701006C0701006C07013232FFFFFF6400"), memory.Read(Record, 0x38));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetEndDate(parent, row, new DateOnly(2027, 6, 2)));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void End_date_preserves_high_bits_and_rejects_stale_club()
    {
        Build(Hibernian, 1575, 1600);
        Assert.True(memory.TryWrite(Record + 0x25, new byte[] { 0x32 }));
        var row = Assert.Single(Reader().ReadAffiliations(parent));
        Assert.Equal(new DateOnly(2027, 5, 31), row.EndDate);
        Writer().SetEndDate(parent, row, new DateOnly(2027, 6, 1));
        Assert.Equal(new byte[] { 0x98, 0x32, 0xEB, 0x07 }, memory.Read(Record + 0x24, 4));
        row = Assert.Single(Reader().ReadAffiliations(parent));
        Assert.True(memory.TryWrite(feeder, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1234560UL)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetEndDate(parent, row, new DateOnly(2027, 6, 2)));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Start_date_matches_FMRTE_and_guarded_writer_preserves_record()
    {
        Build(Hibernian, 1575, 1600);
        var row = Assert.Single(Reader().ReadAffiliations(parent));
        Assert.Equal(new DateOnly(2026, 6, 16), row.StartDate);
        var other = new ClubAffiliationFeeTests();
        other.Build(Bayern, 915, 958);
        Assert.Equal(new DateOnly(2024, 11, 29), Assert.Single(other.Reader().ReadAffiliations(other.parent)).StartDate);
        Writer().SetStartDate(parent, row, new DateOnly(2026, 6, 17));
        Assert.Equal(Convert.FromHexString("50F8D76973020000A013D8697302000000000000410000000000000004000000A800EA079700EB0701006C0701006C07013232FFFFFF6400"), memory.Read(Record, 0x38));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetStartDate(parent, row, new DateOnly(2026, 6, 18)));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Start_date_writer_refuses_older_profile_with_valid_clubs_and_record()
    {
        Build(Hibernian, 1575, 1600);
        var oldProfile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2486695-steam.json"));
        Assert.True(memory.TryWrite(parent, BitConverter.GetBytes(TestWorld.ModuleBase + 0x4CDA1F8UL)));
        Assert.True(memory.TryWrite(feeder, BitConverter.GetBytes(TestWorld.ModuleBase + 0x4CDA1F8UL)));
        var row = Assert.Single(new ClubListReader(memory, oldProfile, TestWorld.ModuleBase).ReadAffiliations(parent));
        Assert.Equal(new DateOnly(2026, 6, 16), row.StartDate);
        var original = memory.Read(Record, 0x38);
        int writes = memory.Writes;
        var writer = new ClubAffiliationWriter(memory, oldProfile, TestWorld.ModuleBase);
        Assert.Throws<InvalidOperationException>(() => writer.SetStartDate(parent, row, new DateOnly(2026, 6, 17)));
        Assert.Equal(writes, memory.Writes);
        Assert.Equal(original, memory.Read(Record, 0x38));
    }

    [Fact]
    public void Friendly_chance_matches_FMRTE_and_guarded_writer_preserves_record()
    {
        Build(Inverness, 1577, 5201818);
        var row = Assert.Single(Reader().ReadAffiliations(parent));
        Assert.Equal((byte)75, row.FriendlyChance);
        var other = new ClubAffiliationFeeTests();
        other.Build(Bayern, 915, 958);
        Assert.Equal((byte)25, Assert.Single(other.Reader().ReadAffiliations(other.parent)).FriendlyChance);
        Writer().SetFriendlyChance(parent, row, 76);
        Assert.Equal(Convert.FromHexString("C0F9D76973020000A0C7F8697302000000000000210000000000000014000000AA00E80701006C0701006C0701006C070132324CFFFF0000"), memory.Read(Record, 0x38));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetFriendlyChance(parent, row, 77));
        Assert.Equal(writes, memory.Writes);
        var absent = new ClubAffiliationFeeTests(); absent.Build(Hibernian, 1575, 1600);
        var absentRow = Assert.Single(absent.Reader().ReadAffiliations(absent.parent));
        Assert.Null(absentRow.FriendlyChance);
        Assert.Throws<InvalidOperationException>(() => absent.Writer().SetFriendlyChance(absent.parent, absentRow, 20));
    }

    [Fact]
    public void Parent_wage_percentage_matches_FMRTE_and_guarded_writer_preserves_record()
    {
        Build(Hibernian, 1575, 1600);
        var row = Assert.Single(Reader().ReadAffiliations(parent));
        Assert.Equal((byte)100, row.ParentWagePercent);
        var other = new ClubAffiliationFeeTests(); other.Build(Bayern, 915, 958);
        Assert.Equal((byte)0, Assert.Single(other.Reader().ReadAffiliations(other.parent)).ParentWagePercent);
        Writer().SetParentWagePercent(parent, row, 99);
        Assert.Equal(Convert.FromHexString("50F8D76973020000A013D8697302000000000000410000000000000004000000A700EA079700EB0701006C0701006C07013232FFFFFF6300"), memory.Read(Record, 0x38));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetParentWagePercent(parent, row, 98));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Play_friendlies_matches_FMRTE_and_guarded_writer_preserves_other_bits()
    {
        Build(Hibernian, 1575, 1600);
        var row = Assert.Single(Reader().ReadAffiliations(parent));
        Assert.False(row.PlayFriendlies);
        var other = new ClubAffiliationFeeTests(); other.Build(Inverness, 1577, 5201818);
        Assert.True(Assert.Single(other.Reader().ReadAffiliations(other.parent)).PlayFriendlies);
        Writer().SetPlayFriendlies(parent, row, true);
        Assert.Equal(Convert.FromHexString("50F8D76973020000A013D8697302000000000000410000000000000014000000A700EA079700EB0701006C0701006C07013232FFFFFF6400"), memory.Read(Record, 0x38));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetPlayFriendlies(parent, row, false));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void First_option_matches_FMRTE_and_guarded_writer_preserves_other_bits()
    {
        Build(Hibernian, 1575, 1600);
        var row = Assert.Single(Reader().ReadAffiliations(parent));
        Assert.False(row.FirstOption);
        var other = new ClubAffiliationFeeTests(); other.Build(Bayern, 915, 958);
        Assert.True(Assert.Single(other.Reader().ReadAffiliations(other.parent)).FirstOption);
        Writer().SetFirstOption(parent, row, true);
        Assert.Equal(Convert.FromHexString("50F8D76973020000A013D869730200000000000041000000000000000C000000A700EA079700EB0701006C0701006C07013232FFFFFF6400"), memory.Read(Record, 0x38));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetFirstOption(parent, row, false));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Players_on_loan_matches_FMRTE_and_guarded_writer_preserves_other_bits()
    {
        Build(Livingston, 1581, 1541);
        var row = Assert.Single(Reader().ReadAffiliations(parent));
        Assert.False(row.PlayersOnLoan);
        var other = new ClubAffiliationFeeTests(); other.Build(Hibernian, 1575, 1600);
        Assert.True(Assert.Single(other.Reader().ReadAffiliations(other.parent)).PlayersOnLoan);
        Writer().SetPlayersOnLoan(parent, row, true);
        Assert.Equal(Convert.FromHexString("A0FCD7697302000030CDD7697302000000000000010000000000000004000000BA00E50701006C0701006C0701006C07103232FFFFFF0000"), memory.Read(Record, 0x38));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetPlayersOnLoan(parent, row, false));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Scouting_shared_matches_FMRTE_and_guarded_writer_preserves_other_bits()
    {
        Build(Hibernian, 1575, 1600);
        var row = Assert.Single(Reader().ReadAffiliations(parent));
        Assert.False(row.ScoutingShared);
        var other = new ClubAffiliationFeeTests(); other.Build(Metz, 844, 2014);
        Assert.True(Assert.Single(other.Reader().ReadAffiliations(other.parent)).ScoutingShared);
        Writer().SetScoutingShared(parent, row, true);
        Assert.Equal(Convert.FromHexString("50F8D76973020000A013D8697302000000000000410000000000000004400000A700EA079700EB0701006C0701006C07013232FFFFFF6400"), memory.Read(Record, 0x38));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetScoutingShared(parent, row, false));
        Assert.Equal(writes, memory.Writes);
    }
}

using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>Add/remove go through the pointer vector, so the tests cover appending in place and growing it.</summary>
public class ClubSponsorListTests
{
    const ulong Club = 0x7000_0000, Holder = 0x7001_0000, List = 0x7002_0000, Records = 0x7003_0000;
    const ulong Allocated = 0x7100_0000;

    static readonly string[] Rows =
    [
        "B500EB07B600E907C0C62D00000000000000000116010000",
        "B502EB07B600E90740420F00000000000000000004010000",
    ];

    readonly FakeMemory memory = new();
    readonly OffsetProfile profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426.json"));
    ulong nextAllocation = Allocated;

    ulong Allocate(int bytes)
    {
        ulong address = nextAllocation;
        memory.Map(address, Math.Max(bytes, 8));
        nextAllocation += 0x10000;
        return address;
    }

    void Build(int capacity)
    {
        var club = memory.Map(Club, 0x170);
        TestWorld.Set64(club, 0, TestWorld.ModuleBase + profile.Club!.ClubVtableRva);
        TestWorld.Set64(club, 0x130, Holder);
        var holder = memory.Map(Holder, 0x18);
        TestWorld.Set64(holder, 0, List);
        TestWorld.Set64(holder, 8, List + (ulong)Rows.Length * 8);
        TestWorld.Set64(holder, 0x10, List + (ulong)capacity * 8);
        var list = memory.Map(List, Math.Max(8, capacity * 8));
        var records = memory.Map(Records, Rows.Length * 0x100);
        for (int i = 0; i < Rows.Length; i++)
        {
            TestWorld.Set64(list, i * 8, Records + (ulong)(i * 0x100));
            Convert.FromHexString(Rows[i]).CopyTo(records, i * 0x100);
        }
    }

    ClubListReader Reader() => new(memory, profile, TestWorld.ModuleBase);
    ClubSponsorWriter Writer() => new(memory, profile, TestWorld.ModuleBase, Allocate);

    [Fact]
    public void Add_appends_when_the_vector_has_room()
    {
        Build(capacity: 4);

        var added = Writer().Add(Club, 3, 250_000, new DateOnly(2026, 7, 1), new DateOnly(2027, 6, 30));

        var sponsors = Reader().ReadSponsors(Club);
        Assert.Equal(3, sponsors.Count);
        Assert.Equal(added.Address, sponsors[^1].Address);
        Assert.Equal(250_000, sponsors[^1].TotalValue);
        Assert.Equal(3, sponsors[^1].IncomeTypeCode);
        Assert.Equal(new DateOnly(2026, 7, 1), sponsors[^1].StartDate);
        Assert.Equal(new DateOnly(2027, 6, 30), sponsors[^1].EndDate);
        // The first two records are untouched.
        Assert.Equal(3_000_000, sponsors[0].TotalValue);
        Assert.Equal(1_000_000, sponsors[1].TotalValue);
    }

    [Fact]
    public void Add_grows_a_full_vector_and_keeps_every_pointer()
    {
        Build(capacity: 2);

        var added = Writer().Add(Club, 6, 999, new DateOnly(2026, 1, 1), new DateOnly(2028, 1, 1));

        var sponsors = Reader().ReadSponsors(Club);
        Assert.Equal(3, sponsors.Count);
        Assert.Equal(Records, sponsors[0].Address);
        Assert.Equal(Records + 0x100, sponsors[1].Address);
        Assert.Equal(added.Address, sponsors[2].Address);
    }

    [Fact]
    public void Remove_drops_only_the_selected_entry()
    {
        Build(capacity: 4);
        var sponsors = Reader().ReadSponsors(Club);

        Writer().Remove(Club, sponsors[0]);

        var after = Reader().ReadSponsors(Club);
        Assert.Single(after);
        Assert.Equal(Records + 0x100, after[0].Address);
    }

    [Fact]
    public void Remove_rejects_an_entry_that_is_no_longer_listed()
    {
        Build(capacity: 4);
        var sponsors = Reader().ReadSponsors(Club);
        Writer().Remove(Club, sponsors[1]);

        Assert.Throws<StaleObjectException>(() => Writer().Remove(Club, sponsors[1]));
    }

    [Fact]
    public void Add_rejects_an_unverified_income_type()
    {
        Build(capacity: 4);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Writer().Add(Club, 99, 1000, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)));
    }
}

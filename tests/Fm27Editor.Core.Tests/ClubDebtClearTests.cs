using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>Removing every debt collapses the pointer vector; the records themselves stay readable.</summary>
public class ClubDebtClearTests
{
    const ulong Club = 0x7200_0000, Holder = 0x7201_0000, List = 0x7202_0000, Records = 0x7203_0000;

    // 0x1C-byte debt records: original debt, ..., end date, start date, ..., source.
    static readonly string[] Rows =
    [
        "C0C62D0000000000B600E907B500EB07000000000000000100" + "0A000000",
        "40420F0000000000B600E907B500EB07000000000000000100" + "01000000",
    ];

    readonly FakeMemory memory = new();
    readonly OffsetProfile profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-steam.json"));

    void Build()
    {
        var club = memory.Map(Club, 0x170);
        TestWorld.Set64(club, 0, TestWorld.ModuleBase + profile.Club!.ClubVtableRva);
        TestWorld.Set64(club, 0x100, Holder);
        var holder = memory.Map(Holder, 0x70);
        ulong vector = Holder + 0x50;
        TestWorld.Set64(holder, 0x50, List);
        TestWorld.Set64(holder, 0x58, List + (ulong)Rows.Length * 8);
        TestWorld.Set64(holder, 0x60 - 0x50 + 0x50, List + (ulong)Rows.Length * 8);
        var list = memory.Map(List, Rows.Length * 8);
        var records = memory.Map(Records, Rows.Length * 0x100);
        for (int i = 0; i < Rows.Length; i++)
        {
            TestWorld.Set64(list, i * 8, Records + (ulong)(i * 0x100));
            Convert.FromHexString(Rows[i]).CopyTo(records, i * 0x100);
        }
    }

    ClubListReader Reader() => new(memory, profile, TestWorld.ModuleBase);
    ClubDebtWriter Writer() => new(memory, profile, TestWorld.ModuleBase);

    [Fact]
    public void Clears_every_debt()
    {
        Build();
        Assert.Equal(2, Reader().ReadDebts(Club).Count);

        Writer().RemoveAll(Club);

        Assert.Empty(Reader().ReadDebts(Club));
    }

    [Fact]
    public void Rejects_a_club_without_debts()
    {
        Build();
        Writer().RemoveAll(Club);

        Assert.Throws<StaleObjectException>(() => Writer().RemoveAll(Club));
    }
}

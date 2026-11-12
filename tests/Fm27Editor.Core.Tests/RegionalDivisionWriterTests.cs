using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Record bytes read from Man Utd's first regional division on Steam 27.0.1 (FMRTE: Premier League 2 Division 1, level 1,
/// year 0): competition id 0x05AA370B at +0 and +4, year 0 at +8, level 1 at +0xA.
/// </summary>
public class RegionalDivisionWriterTests
{
    static readonly byte[] Record = [0x0B, 0x37, 0xAA, 0x05, 0x0B, 0x37, 0xAA, 0x05, 0x00, 0x00, 0x01, 0x00];
    const ulong Address = 0x5000_0000;

    static OffsetProfile Profile() =>
        OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.1+2494687-steam.json"));

    static (FakeMemory, ClubRegionalDivisionEntry) Setup()
    {
        var memory = new FakeMemory();
        Record.CopyTo(memory.Map(Address, 0x10), 0);
        return (memory, new(Address, 1, 0x05AA370B, 0, Convert.ToHexString(Record)));
    }

    [Fact]
    public void Set_writes_both_competition_ids_the_year_and_the_level()
    {
        var (memory, entry) = Setup();
        new RegionalDivisionWriter(memory, Profile()).Set(entry, 0x05AA370F, 2, 2027);
        Assert.Equal(new byte[] { 0x0F, 0x37, 0xAA, 0x05, 0x0F, 0x37, 0xAA, 0x05, 0xEB, 0x07, 0x02, 0x00 }, memory.Read(Address, 12));
    }

    [Fact]
    public void A_record_that_changed_since_it_was_read_is_refused()
    {
        var (memory, entry) = Setup();
        memory.TryWrite(Address + 0xA, [3]);
        Assert.Throws<StaleObjectException>(() => new RegionalDivisionWriter(memory, Profile()).Set(entry, 0x05AA370F, 2, 0));
        Assert.Equal(3, memory.Read(Address + 0xA, 1)[0]);
    }
}

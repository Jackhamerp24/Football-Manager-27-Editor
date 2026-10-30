using Fm27Editor.Core.Game;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Game date chains measured on FM27 27.0.0: game_plugin+0x56F1DE0 → +0x268 → date at +0x78, and the
/// cross-check game_plugin+0x57084D0 → +0x0 → date at +0xC. 5 November 2026 is day 309.
/// </summary>
public class GameClockTests
{
    const ulong Obj = 0x5000_0000, Node = 0x5000_1000, Obj2 = 0x5000_2000, Node2 = 0x5000_3000;
    readonly TestWorld _w = new();

    public GameClockTests()
    {
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + 0x56F1DE0, 8), 0, Obj);
        TestWorld.Set64(_w.Memory.Map(Obj, 0x270), 0x268, Node);
        var n = _w.Memory.Map(Node, 0x80);
        TestWorld.Set16(n, 0x78, 309); TestWorld.Set16(n, 0x7A, 2026);

        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + 0x57084D0, 8), 0, Obj2);
        TestWorld.Set64(_w.Memory.Map(Obj2, 8), 0, Node2);
        var n2 = _w.Memory.Map(Node2, 0x10);
        TestWorld.Set16(n2, 0xC, 309); TestWorld.Set16(n2, 0xE, 2026);
    }

    DateOnly? Read() => GameClock.Read(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Reads_the_date_when_both_chains_agree() =>
        Assert.Equal(new DateOnly(2026, 11, 5), Read());

    [Fact]
    public void Unknown_when_the_cross_check_disagrees()
    {
        Assert.True(_w.Memory.TryWrite(Node2 + 0xC, BitConverter.GetBytes((short)310)));
        Assert.Null(Read());
    }

    [Fact]
    public void Unknown_when_no_save_is_loaded()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.ModuleBase + 0x56F1DE0, BitConverter.GetBytes(0UL)));
        Assert.Null(Read());
    }

    [Fact]
    public void Age_on_the_game_date_counts_birthdays_correctly()
    {
        // Elliot Anderson, born 6 Nov 2002: still 23 on 5 Nov 2026, 24 the next day.
        var anderson = new DateOnly(2002, 11, 6);
        Assert.Equal(23, AgeOn(anderson, Read()!.Value));
        Assert.Equal(24, AgeOn(anderson, Read()!.Value.AddDays(1)));
    }

    static int AgeOn(DateOnly dob, DateOnly date)
    {
        var record = new Players.PlayerRecord
        {
            Address = 1, VtableRva = 0, CurrentAbility = 0, PotentialAbility = 0, HomeReputation = 0, CurrentReputation = 0,
            WorldReputation = 0, Height = 0, DateOfBirth = dob, RawAttributes = [], AttributeScale = 5, Positions = [], Personality = [],
        };
        return record.AgeOn(date)!.Value;
    }

    [Fact]
    public void Written_json_keeps_both_date_chains()
    {
        var copy = OffsetProfile.Load(_w.Profile.ToJson());
        Assert.Equal(new DateChain(0x56F1DE0, 0x268, 0x78), copy.GameDate!.Primary);
        Assert.Equal(new DateChain(0x57084D0, 0x0, 0xC), copy.GameDate.CrossCheck);
    }
}

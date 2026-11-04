using System.Text.Json.Nodes;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Game date as measured on FM27 27.0.0 after a full game restart: game_plugin+0x56F1A18 → an object with
/// vtable +0x4C8C858, date at +0x7F4. 7 November 2026 is day 311 (304 days in Jan–Oct, plus 7).
/// </summary>
public class GameClockTests
{
    [Fact]
    public void Game_pass_reads_flagged_direct_date_and_rejects_unset_date()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var copy = OffsetProfile.Load(profile.ToJson());
        Assert.Equal(0x56257C8u, copy.DirectGameDateRva);
        var memory = new FakeMemory();
        var date = memory.Map(TestWorld.ModuleBase + 0x56257C8, 4);
        // FMLibreEditor documented 9 September 2026; direct bytes observed on 0.9.2409.0.
        new byte[] { 0xFC, 0x3A, 0xEA, 0x07 }.CopyTo(date, 0);
        Assert.Equal(new DateOnly(2026, 9, 9), GameClock.Read(memory, copy, TestWorld.ModuleBase));
        // Local save, 15 August 2026, observed 4/11/2026.
        new byte[] { 0xE3, 0x3A, 0xEA, 0x07 }.CopyTo(date, 0);
        Assert.Equal(new DateOnly(2026, 8, 15), GameClock.Read(memory, copy, TestWorld.ModuleBase));
        new byte[] { 0x01, 0x00, 0x6C, 0x07 }.CopyTo(date, 0);
        Assert.Null(GameClock.Read(memory, copy, TestWorld.ModuleBase));
    }

    const ulong Obj = 0x5000_0000;
    readonly TestWorld _w = new();

    public GameClockTests()
    {
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + 0x56F1A18, 8), 0, Obj);
        var o = _w.Memory.Map(Obj, 0x800);
        TestWorld.Set64(o, 0, TestWorld.ModuleBase + 0x4C8C858);
        TestWorld.Set16(o, 0x7F4, 311); TestWorld.Set16(o, 0x7F6, 2026);
    }

    DateOnly? Read() => GameClock.Read(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Reads_the_date_from_the_clock_object() =>
        Assert.Equal(new DateOnly(2026, 11, 7), Read());

    [Fact]
    public void Unknown_when_the_root_points_to_another_kind_of_object()
    {
        Assert.True(_w.Memory.TryWrite(Obj, BitConverter.GetBytes(TestWorld.ModuleBase + 0x4CC4CF8)));
        Assert.Null(Read());
    }

    [Fact]
    public void Unknown_when_no_save_is_loaded()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.ModuleBase + 0x56F1A18, BitConverter.GetBytes(0UL)));
        Assert.Null(Read());
    }

    [Fact]
    public void Unknown_when_the_value_is_not_a_date()
    {
        Assert.True(_w.Memory.TryWrite(Obj + 0x7F4, BitConverter.GetBytes((short)6966)));
        Assert.Null(Read());
    }

    [Fact]
    public void Two_hop_chain_with_cross_check_still_reads()
    {
        // Older profile shape: root → object → [+0x268] → node, date at +0x78, plus a cross-check chain.
        const ulong Obj1 = 0x6000_0000, Node1 = 0x6000_1000, Obj2 = 0x6000_2000, Node2 = 0x6000_3000;
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + 0x56F1DE0, 8), 0, Obj1);
        TestWorld.Set64(_w.Memory.Map(Obj1, 0x270), 0x268, Node1);
        var n1 = _w.Memory.Map(Node1, 0x80);
        TestWorld.Set16(n1, 0x78, 309); TestWorld.Set16(n1, 0x7A, 2026);
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + 0x57084D0, 8), 0, Obj2);
        TestWorld.Set64(_w.Memory.Map(Obj2, 8), 0, Node2);
        var n2 = _w.Memory.Map(Node2, 0x10);
        TestWorld.Set16(n2, 0xC, 309); TestWorld.Set16(n2, 0xE, 2026);
        var profile = Chained(new DateChain(0x56F1DE0, 0x268, 0x78), new DateChain(0x57084D0, 0x0, 0xC));

        Assert.Equal(new DateOnly(2026, 11, 5), GameClock.Read(_w.Memory, profile, TestWorld.ModuleBase));
        Assert.True(_w.Memory.TryWrite(Node2 + 0xC, BitConverter.GetBytes((short)310)));
        Assert.Null(GameClock.Read(_w.Memory, profile, TestWorld.ModuleBase));
    }

    OffsetProfile Chained(DateChain primary, DateChain crossCheck)
    {
        static JsonObject Json(DateChain c) => new()
        {
            ["rootRva"] = $"0x{c.RootRva:X}", ["pointerOffset"] = $"0x{c.PointerOffset:X}", ["dateOffset"] = $"0x{c.DateOffset:X}",
        };
        var root = JsonNode.Parse(_w.Profile.ToJson())!;
        root["gameDate"] = new JsonObject { ["primary"] = Json(primary), ["crossCheck"] = Json(crossCheck) };
        return OffsetProfile.Load(root.ToJsonString());
    }

    [Fact]
    public void Age_on_the_game_date_counts_birthdays_correctly()
    {
        // Elliot Anderson, born 6 Nov 2002: 23 on 5 Nov 2026, 24 on 7 Nov.
        var anderson = new DateOnly(2002, 11, 6);
        Assert.Equal(24, AgeOn(anderson, Read()!.Value));
        Assert.Equal(23, AgeOn(anderson, Read()!.Value.AddDays(-2)));
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
    public void Written_json_keeps_the_date_chain()
    {
        var copy = OffsetProfile.Load(_w.Profile.ToJson());
        Assert.Equal(new DateChain(0x56F1A18, null, 0x7F4, 0x4C8C858), copy.GameDate!.Primary);
        Assert.Null(copy.GameDate.CrossCheck);
    }
}

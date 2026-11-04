using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// A staff member's career plan container sits on the person at +0xC8 (= player + 0x350 for a player's person part). Diego
/// Simeone's one plan on 15/8/2026, dumped: Atlético de Madrid, then "01 01 10 03 02000000"; FMRTE 27 showed it as
/// "Wants to be a / Manager / Club Atlético de Madrid / Club" (2/11/2026).
/// </summary>
public class StaffCareerPlanTests
{
    const ulong Person = 0x6000_0000, Container = 0x6001_0000, Vector = 0x6001_1000, Plan = 0x6002_0000, Club = 0x6003_0000;
    readonly TestWorld _w = new();

    public StaffCareerPlanTests()
    {
        TestWorld.Set64(_w.Memory.Map(Person, 0x100), 0, TestWorld.ModuleBase + 0x4D8BE78);
        var c = _w.Memory.Map(Club, 0xD0);
        TestWorld.Set64(c, 0, TestWorld.ModuleBase + 0x4CDA1F8);
        _w.Memory.MapString(Club + 0x1000, "Club Atlético de Madrid");
        TestWorld.Set64(c, 0xC0, Club + 0x1000);
        var k = _w.Memory.Map(Container, 0x18);
        TestWorld.Set64(k, 0, Vector); TestWorld.Set64(k, 8, Vector + 8); TestWorld.Set64(k, 16, Vector + 8);
        TestWorld.Set64(_w.Memory.Map(Vector, 8), 0, Plan);
        var p = _w.Memory.Map(Plan, 16);
        TestWorld.Set64(p, 0, Club);
        Convert.FromHexString("0101100302000000").CopyTo(p, 8);
        Assert.True(_w.Memory.TryWrite(Person + 0xC8, BitConverter.GetBytes(Container)));
    }

    PersonOwner Simeone() => PersonOwner.Of(new StaffRecord
    {
        Address = Person, StaffAddress = Person - 0x100, Attributes = new byte[64], Personality = new byte[8], Kind = _w.Profile.Staff!.Kinds[0],
    }, _w.Profile);

    [Fact]
    public void Reads_and_sets_a_staff_members_career_plan()
    {
        var reader = new CareerPlanReader(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var plans = reader.Read(Simeone())!;
        var plan = Assert.Single(plans);
        Assert.Equal((1, 1, 16, 3, "Club Atlético de Madrid"), (plan.Condition, plan.Scope, plan.Job, plan.ItemType, plan.ItemName));
        new CareerPlanWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase).Set(Simeone(), plans, plan, new CareerPlanFields(3, 1, 16, reader.Item(Club)!));
        Assert.Equal("0301100302000000", Convert.ToHexString(_w.Bytes(Plan + 8, 8)));
    }
}

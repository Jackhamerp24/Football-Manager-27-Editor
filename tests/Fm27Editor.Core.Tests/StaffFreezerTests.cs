using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class StaffFreezerTests
{
    const ulong Root = 0x6000_0000, Vector = 0x6000_1000, Obj = 0x6001_0000, Person = Obj + 0x100, Job = 0x6002_0000;
    readonly TestWorld _w = new();

    public StaffFreezerTests()
    {
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + 0x5703738, 8), 0, Root);
        var r = _w.Memory.Map(Root, 0xA0);
        TestWorld.Set64(r, 0x88, Vector); TestWorld.Set64(r, 0x90, Vector + 16);
        var v = _w.Memory.Map(Vector, 16);
        TestWorld.Set64(v, 0, TestWorld.Mainoo);
        TestWorld.Set64(v, 8, Person);

        var o = _w.Memory.Map(Obj, 0x240);
        TestWorld.Set64(o, 0x00, TestWorld.ModuleBase + 0x4D8BE08);
        Convert.FromHexString("0F090C08130E0E1111050F0111640F1410130C0F06120E1210124B055A5A5A5F0541553C2D6446462D2D4B4B054B2D0F2D5A5037").CopyTo(o, 0x10);
        Convert.FromHexString("222422242E22BC00C300").CopyTo(o, 0xD4);
        TestWorld.Set64(o, 0x100, TestWorld.ModuleBase + 0x4D8BE78);
        BitConverter.GetBytes(2020287).CopyTo(o, 0x10C);
        Convert.FromHexString("0C141413130B0712").CopyTo(o, 0x170);
        Convert.FromHexString("6D019507").CopyTo(o, 0x188);
        TestWorld.Set64(o, 0x1A8, Job);
        var j = _w.Memory.Map(Job, 0x28);
        TestWorld.Set64(j, 0x8, Person);
        Convert.FromHexString("D495000026000600").CopyTo(j, 0x20);
    }

    [Fact]
    public void StaffFreezer_restores_fixed_values_and_accepts_increases()
    {
        var repo = new StaffRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var writer = new StaffWriter(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var staff = Assert.Single(repo.LoadAll());

        int initialDet = staff.Attribute(StaffAttribute.Determination);
        int initialTac = staff.Attribute(StaffAttribute.TacticalKnowledge);
        Assert.True(initialDet > 0, "Determination should be > 0");
        Assert.True(initialTac > 0, "TacticalKnowledge should be > 0");

        var frozen = FrozenStaff.Capture(staff, [
            (StaffAttribute.Determination, FreezeMode.Fixed),
            (StaffAttribute.TacticalKnowledge, FreezeMode.AllowIncrease)
        ]);

        // Determination offset in staff part:
        var (detOff, detScaled) = _w.Profile.Staff!.Attributes[StaffAttribute.Determination];
        var (tacOff, tacScaled) = _w.Profile.Staff!.Attributes[StaffAttribute.TacticalKnowledge];

        // Deliberately modify Determination to a different value in RAM
        byte modifiedDet = (byte)(detScaled ? (initialDet - 2) * 5 : (initialDet - 2));
        Assert.True(_w.Memory.TryWrite(Obj + (ulong)detOff, [modifiedDet]));

        // Reconcile should detect the change and write back the initial value
        int writes = frozen.Reconcile(repo, writer);
        Assert.Equal(1, writes);
        Assert.Equal(initialDet, repo.Reload(Person)!.Attribute(StaffAttribute.Determination));

        // Now test AllowIncrease: modify TacticalKnowledge to higher
        byte higherTac = (byte)(tacScaled ? (initialTac + 1) * 5 : (initialTac + 1));
        Assert.True(_w.Memory.TryWrite(Obj + (ulong)tacOff, [higherTac]));
        writes = frozen.Reconcile(repo, writer);
        Assert.Equal(0, writes); // Accepted without overwrite
        Assert.Equal(initialTac + 1, repo.Reload(Person)!.Attribute(StaffAttribute.TacticalKnowledge));

        // Now lower TacticalKnowledge: reconcile must revert it to the higher value
        byte lowerTac = (byte)(tacScaled ? (initialTac - 1) * 5 : (initialTac - 1));
        Assert.True(_w.Memory.TryWrite(Obj + (ulong)tacOff, [lowerTac]));
        writes = frozen.Reconcile(repo, writer);
        Assert.Equal(1, writes);
        Assert.Equal(initialTac + 1, repo.Reload(Person)!.Attribute(StaffAttribute.TacticalKnowledge));

        // Reject changed identity
        Assert.True(_w.Memory.TryWrite(Person + 0xC, BitConverter.GetBytes(999999)));
        Assert.Throws<StaleObjectException>(() => frozen.Reconcile(repo, writer));
    }

    [Fact]
    public void AllAttributes_enumerates_valid_staff_attributes()
    {
        var repo = new StaffRepository(_w.Memory, _w.Profile, TestWorld.ModuleBase);
        var staff = Assert.Single(repo.LoadAll());
        var all = FrozenStaff.AllAttributes(staff).ToList();
        Assert.True(all.Count > 10);
        Assert.All(all, a => Assert.Equal(FreezeMode.Fixed, a.Mode));
    }
}

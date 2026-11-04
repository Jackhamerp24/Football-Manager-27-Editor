using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class StaffTendenciesTests
{
    [Theory]
    [InlineData("3F11130F55115B0D4B0D371033134C0D540D150D", 0x3F, 17)]
    [InlineData("4B0D4C0D4E0D4D0A4F11270D2611150D35123111", 0x4B, 13)]
    public void Reads_observed_tendencies_and_guards_level_write(string dump, byte expectedType, byte expectedLevel)
    {
        var w = new TestWorld();
        const ulong staff = 0x60000000, holder = 0x60001000, vector = 0x60002000;
        var o = w.Memory.Map(staff, 0x240);
        TestWorld.Set64(o, 0, TestWorld.ModuleBase + 0x4D8BE08);
        TestWorld.Set64(o, 0x100, TestWorld.ModuleBase + 0x4D8BE78);
        TestWorld.Set64(o, 0xA8, holder);
        byte[] raw = Convert.FromHexString(dump);
        var h = w.Memory.Map(holder, 24); TestWorld.Set64(h, 0, vector); TestWorld.Set64(h, 8, vector + (ulong)raw.Length);
        raw.CopyTo(w.Memory.Map(vector, raw.Length), 0);
        var person = new StaffRecord { Address = staff + 0x100, StaffAddress = staff, Attributes = new byte[64], Personality = new byte[8], Kind = w.Profile.Staff!.Kinds[0] };
        var access = new StaffTendencies(w.Memory, w.Profile, TestWorld.ModuleBase);
        var rows = access.Read(person);
        Assert.Equal((expectedType, expectedLevel), (rows[0].Type, rows[0].Level));
        access.SetLevel(person, rows[0], expectedLevel + 1);
        Assert.Equal(expectedType, w.Bytes(vector, 1)[0]);
        Assert.Equal(expectedLevel + 1, w.Bytes(vector + 1, 1)[0]);
        Assert.Equal(raw[2..], w.Bytes(vector + 2, raw.Length - 2));
        Assert.Throws<StaleObjectException>(() => access.SetLevel(person, rows[0], expectedLevel));
    }

    /// <summary>
    /// FMRTE 27 on Simeone's first row (Allow creative freedom 13, 2/11/2026): choosing "Will use man-marking" wrote 4C, "Will sit
    /// back and defend a lead" 49, "Influenced by supporters" 5E, "Sign domestic-based players" 01, the level byte staying 0D.
    /// </summary>
    [Theory]
    [InlineData("Will use man-marking", 0x4C)]
    [InlineData("Will sit back and defend a lead", 0x49)]
    [InlineData("Influenced by supporters", 0x5E)]
    [InlineData("Sign domestic-based players", 0x01)]
    public void Setting_a_type_writes_the_code_fmrte_writes(string name, byte code)
    {
        var w = new TestWorld();
        const ulong staff = 0x60000000, holder = 0x60001000, vector = 0x60002000;
        var o = w.Memory.Map(staff, 0x240);
        TestWorld.Set64(o, 0, TestWorld.ModuleBase + 0x4D8BE08);
        TestWorld.Set64(o, 0x100, TestWorld.ModuleBase + 0x4D8BE78);
        TestWorld.Set64(o, 0xA8, holder);
        byte[] raw = Convert.FromHexString("4B0D4C0D4E0D");
        var h = w.Memory.Map(holder, 24); TestWorld.Set64(h, 0, vector); TestWorld.Set64(h, 8, vector + (ulong)raw.Length);
        raw.CopyTo(w.Memory.Map(vector, raw.Length), 0);
        var person = new StaffRecord { Address = staff + 0x100, StaffAddress = staff, Attributes = new byte[64], Personality = new byte[8], Kind = w.Profile.Staff!.Kinds[0] };
        var access = new StaffTendencies(w.Memory, w.Profile, TestWorld.ModuleBase);
        var row = access.Read(person)[0];
        Assert.Equal("Allow creative freedom", StaffTendencies.TypeName(row.Type));
        access.SetType(person, row, StaffTendencies.Types.Single(t => t.Name == name).Code);
        Assert.Equal([code, 0x0D, 0x4C, 0x0D, 0x4E, 0x0D], w.Bytes(vector, 6));
        Assert.Equal("Unknown (96)", StaffTendencies.TypeName(96));
        Assert.Throws<ArgumentOutOfRangeException>(() => access.SetType(person, access.Read(person)[0], 96));
    }
}

using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class GamePassStaffReadingTests
{
    [Fact]
    public void Reads_staff_objects_from_direct_vector_and_skips_unknown_vtables()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        Assert.True(profile.Staff!.DirectStaffVector);
        Assert.True(Fm27Editor.Core.Offsets.OffsetProfile.Load(profile.ToJson()).Staff!.DirectStaffVector);
        var memory = new FakeMemory();
        const ulong module = 0x700000000000, vector = 0x30000000;
        void MapStaff(ulong staff, int id, string first, string last, string head, string abilities)
        {
            var s = memory.Map(staff, 0x100);
            Convert.FromHexString(head).CopyTo(s, 0);
            BitConverter.GetBytes(module + 0x4D3B358u).CopyTo(s, 0);
            Convert.FromHexString(abilities).CopyTo(s, 0xDA);
            var p = memory.Map(staff + 0x100, 0xB0);
            BitConverter.GetBytes(module + 0x4D3B3C8u).CopyTo(p, 0);
            BitConverter.GetBytes(id).CopyTo(p, 0xC);
            BitConverter.GetBytes(staff + 0x1000).CopyTo(p, 0x50);
            BitConverter.GetBytes(staff + 0x2000).CopyTo(p, 0x58);
            memory.MapNameEntry(staff + 0x1000, staff + 0x1100, first);
            memory.MapNameEntry(staff + 0x2000, staff + 0x2100, last);
        }
        // Heads and CA/PA bytes from Game Pass RAM; Jim's ID and 96/133 were observed in FMRTE.
        MapStaff(0x10000000, 127973, "Darren", "Fletcher", "58B389DDFA7F0000E82A7CDDFA7F0000090B120903050F0C04080F050F5409100E100C040710090F0A132805363B453B0F3627360A40453B4141373C054B4B0F052C3B19000000000000000000000000", "7D00A000");
        MapStaff(0x20000000, 5209579, "Jim", "O'Brien", "58B389DDFA7F0000E82A7CDDFA7F000007100F130405041013080E041146050A060808120506110A07123C0538334C3C0A2F202F202A2F2F5A4B2341051E4B0F1938201E000000000000000000000000", "60008500");
        var unknown = memory.Map(0x40000000, 8);
        BitConverter.GetBytes(module + 0x1234560u).CopyTo(unknown, 0);
        var root = memory.Map(module + 0x5683E08, 16);
        BitConverter.GetBytes(vector).CopyTo(root, 0);
        BitConverter.GetBytes(vector + 24).CopyTo(root, 8);
        var list = memory.Map(vector, 24);
        BitConverter.GetBytes(0x10000000UL).CopyTo(list, 0);
        BitConverter.GetBytes(0x20000000UL).CopyTo(list, 8);
        BitConverter.GetBytes(0x40000000UL).CopyTo(list, 16);
        var staff = new StaffRepository(memory, profile, module).LoadAll();
        Assert.Equal(2, staff.Count);
        var darren = Assert.Single(staff, s => s.Id == 127973);
        var jim = Assert.Single(staff, s => s.Id == 5209579);
        Assert.Equal("Darren Fletcher", darren.DisplayName);
        Assert.Equal((short)125, darren.CurrentAbility);
        Assert.Equal((short)160, darren.PotentialAbility);
        Assert.Equal("Jim O'Brien", jim.DisplayName);
        Assert.Equal((short)96, jim.CurrentAbility);
        Assert.Equal((short)133, jim.PotentialAbility);
    }

    [Fact]
    public void Reads_player_staff_and_manager_records_with_their_own_person_offsets()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000, vector = 0x30000000;
        void Map(ulong address, uint staffVt, uint personVt, int personOffset, int id, string first, string last, short ca, short pa)
        {
            var s = memory.Map(address, personOffset);
            BitConverter.GetBytes(module + staffVt).CopyTo(s, 0);
            BitConverter.GetBytes(ca).CopyTo(s, 0xDA);
            BitConverter.GetBytes(pa).CopyTo(s, 0xDC);
            var p = memory.Map(address + (ulong)personOffset, 0xB0);
            BitConverter.GetBytes(module + personVt).CopyTo(p, 0);
            BitConverter.GetBytes(id).CopyTo(p, 0xC);
            BitConverter.GetBytes(address + 0x1000).CopyTo(p, 0x50);
            BitConverter.GetBytes(address + 0x2000).CopyTo(p, 0x58);
            memory.MapNameEntry(address + 0x1000, address + 0x1100, first);
            memory.MapNameEntry(address + 0x2000, address + 0x2100, last);
        }
        // Vtables, IDs and CA/PA read from Game Pass. Player-staff IDs/names also match FM databases.
        Map(0x10000000, 0x4F20D0C, 0x4F21258, 0x100, 109160, "Paul", "Ifill", 95, 130);
        Map(0x11000000, 0x4F20D0C, 0x4F21258, 0x100, 215468, "Josh", "Mullin", 56, 154);
        Map(0x20000000, 0x4C701FC, 0x4C70268, 0x440, 2002081896, "", "(Jackhamer)", 100, 200);
        Map(0x21000000, 0x4C701FC, 0x4C70268, 0x440, 2002081897, "Duong", "Vu", 145, 200);
        var root = memory.Map(module + 0x5683E08, 16);
        BitConverter.GetBytes(vector).CopyTo(root, 0);
        BitConverter.GetBytes(vector + 32).CopyTo(root, 8);
        var list = memory.Map(vector, 32);
        for (int i = 0; i < 4; i++) BitConverter.GetBytes(new[] { 0x10000000UL, 0x11000000UL, 0x20000000UL, 0x21000000UL }[i]).CopyTo(list, i * 8);
        var staff = new StaffRepository(memory, profile, module).LoadAll();
        Assert.Equal(4, staff.Count);
        Assert.Equal("Paul Ifill", Assert.Single(staff, s => s.Id == 109160).DisplayName);
        Assert.Equal("Josh Mullin", Assert.Single(staff, s => s.Id == 215468).DisplayName);
        Assert.Equal((short)100, Assert.Single(staff, s => s.Id == 2002081896).CurrentAbility);
        var manager = Assert.Single(staff, s => s.Id == 2002081897);
        Assert.Equal("Duong Vu", manager.DisplayName);
        Assert.Equal((short)145, manager.CurrentAbility);
        Assert.True(manager.Kind.Manager);
    }

    [Fact]
    public void Writes_staff_ability_and_attribute_and_rejects_stale_identity()
    {
        var profile = GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong module = 0x700000000000, alan = 0x10000000, omar = 0x20000000;
        void Map(ulong address, int id, string head, string ability)
        {
            var s = memory.Map(address, 0x100);
            Convert.FromHexString(head).CopyTo(s, 0);
            BitConverter.GetBytes(module + 0x4D3B358u).CopyTo(s, 0);
            Convert.FromHexString(ability).CopyTo(s, 0xDA);
            var p = memory.Map(address + 0x100, 0xB0);
            BitConverter.GetBytes(module + 0x4D3B3C8u).CopyTo(p, 0);
            BitConverter.GetBytes(id).CopyTo(p, 0xC);
        }
        // Game Pass RAM bytes; FMRTE recorded Alan 80/114 and Omar 135/145.
        Map(alan, 2006027, "58B389DDFA7F0000E82A7CDDFA7F0000100B0F0511080807100B0E0810400A0506120B0809030E0A0E114B05222C1A310F40223606222C3B231937050F4B4114411F0619000000000000000000000000", "50007200");
        Map(omar, 2000336892, "58B389DDFA7F0000E82A7CDDFA7F00001209060B0E0B0A0A0B0B0E0212420F0C0C120A0F09111108020E190F605E51380F5C565828515658464B4B4B054B370F1E64511E000000000000000000000000", "87009100");
        var repo = new StaffRepository(memory, profile, module);
        var first = repo.Reload(alan + 0x100)!;
        var second = repo.Reload(omar + 0x100)!;
        Assert.Equal(((short)80, (short)114), (first.CurrentAbility, first.PotentialAbility));
        Assert.Equal(((short)135, (short)145), (second.CurrentAbility, second.PotentialAbility));
        var writer = new StaffWriter(memory, profile, module);
        var before = memory.Read(alan, 0x100);
        writer.SetAbility(first, 81, 114);
        var after = memory.Read(alan, 0x100);
        Assert.Equal(new byte[] { 81, 0, 114, 0 }, after[0xDA..0xDE]);
        before[0xDA..0xDE].CopyTo(after, 0xDA);
        Assert.Equal(before, after);
        writer.SetAttribute(second, StaffAttribute.Authority, 10);
        Assert.Equal(10, memory.Read(omar + (ulong)profile.Staff!.Attributes[StaffAttribute.Authority].Offset, 1)[0]);
        Assert.True(memory.TryWrite(omar + 0x10C, BitConverter.GetBytes(2000336893)));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => writer.SetAbility(second, 136, 145));
        Assert.Equal(writes, memory.Writes);
    }
}

using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class ClubListReaderTests
{
    // Opt in only in fake-memory tests; shipped profiles carry the flag since the live CLI write of 5/11.
    static OffsetProfile VisionTestProfile() => OffsetProfile.Load(File.ReadAllText(TestWorld.ProfilePath)
        .Replace("\"importance\": \"0x2D\"", "\"importance\": \"0x2D\", \"visionImportanceEditable\": true"));

    [Theory]
    [InlineData("C805C41CFB7F0000FFFF7FFF0000000008DD8E9372020000000000000000000001006C0701006C07FFFFFFFF9605000001006C07FFFFFFFF02000000020000000000000000000000D047179600F70590", 150, 5)]
    [InlineData("C805C41CFB7F0000FFFF7FFF0000000008DD8E9372020000000000000000000001006C0701006C07FFFFFFFF7803000001006C070000000001001500020000000000000000000000704A779200170590", 120, 3)]
    public void New_build_reads_captured_vision_and_writes_importance(string raw, byte type, byte importance)
    {
        // FM27 2493426 live RAM, 4/11/2026: Sheffield 708 and Everton 650 first culture records.
        // Writing became allowed after the CLI write of 5/11/2026 (Sheffield 150: 5 -> 6, read back from RAM).
        const ulong module = 0x7FFB18130000, team = 0x50010000, detail = 0x50020000, holder = 0x50030000;
        const ulong header = 0x50040000, vector = 0x60000000, record = 0x70000000;
        var memory = new FakeMemory();
        var profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426.json"));
        var teamBytes = memory.Map(team, 0xB8);
        Put64(teamBytes, 0, module + 0x4CE3108); Put64(teamBytes, 0xA0, detail);
        Put64(memory.Map(detail, 0xD0), 0xC8, holder);
        var holderBytes = memory.Map(holder, 0x80);
        Put64(holderBytes, 0, module + 0x4DBCC98); Put64(holderBytes, 0x78, header);
        var headerBytes = memory.Map(header, 24);
        Put64(headerBytes, 0, vector); Put64(headerBytes, 8, vector + 8); Put64(headerBytes, 16, vector + 8);
        Put64(memory.Map(vector, 8), 0, record);
        Convert.FromHexString(raw).CopyTo(memory.Map(record, 0x50), 0);

        var row = Assert.Single(new ClubListReader(memory, profile, module).ReadClubVision(team));
        Assert.Equal((type, importance), (row.TypeCode, row.Importance));
        new ClubVisionWriter(memory, profile, module).SetImportance(team, row, 6);
        var expected = Convert.FromHexString(raw);
        expected[0x2D] = 6;
        Assert.Equal(expected, memory.Read(record, 0x50));
    }

    [Fact]
    public void Writes_vision_type_and_both_dates()
    {
        // Same captured Sheffield record as the importance test: type 5, importance 150, dates 1/1/1900 markers.
        const string raw = "C805C41CFB7F0000FFFF7FFF0000000008DD8E9372020000000000000000000001006C0701006C07FFFFFFFF9605000001006C07FFFFFFFF02000000020000000000000000000000D047179600F70590";
        const ulong module = 0x7FFB18130000, team = 0x50010000, detail = 0x50020000, holder = 0x50030000;
        const ulong header = 0x50040000, vector = 0x60000000, record = 0x70000000;
        var memory = new FakeMemory();
        var profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426.json"));
        var teamBytes = memory.Map(team, 0xB8);
        Put64(teamBytes, 0, module + 0x4CE3108); Put64(teamBytes, 0xA0, detail);
        Put64(memory.Map(detail, 0xD0), 0xC8, holder);
        var holderBytes = memory.Map(holder, 0x80);
        Put64(holderBytes, 0, module + 0x4DBCC98); Put64(holderBytes, 0x78, header);
        var headerBytes = memory.Map(header, 24);
        Put64(headerBytes, 0, vector); Put64(headerBytes, 8, vector + 8); Put64(headerBytes, 16, vector + 8);
        Put64(memory.Map(vector, 8), 0, record);
        Convert.FromHexString(raw).CopyTo(memory.Map(record, 0x50), 0);

        var layout = profile.Club!.Lists!;
        var reader = new ClubListReader(memory, profile, module);
        var writer = new ClubVisionWriter(memory, profile, module);
        var row = reader.ReadClubVision(team).Single();

        writer.SetType(team, row, 7);
        Assert.Equal((byte)7, reader.ReadClubVision(team).Single().TypeCode);

        row = reader.ReadClubVision(team).Single();
        writer.SetDates(team, row, new DateOnly(2027, 3, 4), new DateOnly(2029, 5, 6));

        var expected = Convert.FromHexString(raw);
        expected[layout.VisionType] = 7;
        PutVisionDate(expected, layout.VisionStart, new DateOnly(2027, 3, 4));
        PutVisionDate(expected, layout.VisionEnd, new DateOnly(2029, 5, 6));
        Assert.Equal(expected, memory.Read(record, 0x50));
    }

    static void PutVisionDate(byte[] bytes, int offset, DateOnly date)
    {
        ushort current = BitConverter.ToUInt16(bytes, offset);
        BitConverter.GetBytes((ushort)((current & 0xFE00) | date.DayOfYear)).CopyTo(bytes, offset);
        BitConverter.GetBytes((short)date.Year).CopyTo(bytes, offset + 2);
    }

    [Fact]
    public void Reads_and_writes_vision_target_and_status()
    {
        const string raw = "C805C41CFB7F0000FFFF7FFF0000000008DD8E9372020000000000000000000001006C0701006C07FFFFFFFF9605000001006C07FFFFFFFF02000000020000000000000000000000D047179600F70590";
        const ulong module = 0x7FFB18130000, team = 0x50010000, detail = 0x50020000, holder = 0x50030000;
        const ulong header = 0x50040000, vector = 0x60000000, record = 0x70000000;
        var memory = new FakeMemory();
        // The 2493426 profile carries the measured visionTarget/visionStatus offsets; the fixture uses its vtables.
        var profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426.json"));
        var teamBytes = memory.Map(team, 0xB8);
        Put64(teamBytes, 0, module + 0x4CE3108); Put64(teamBytes, 0xA0, detail);
        Put64(memory.Map(detail, 0xD0), 0xC8, holder);
        var holderBytes = memory.Map(holder, 0x80);
        Put64(holderBytes, 0, module + 0x4DBCC98); Put64(holderBytes, 0x78, header);
        var headerBytes = memory.Map(header, 24);
        Put64(headerBytes, 0, vector); Put64(headerBytes, 8, vector + 8); Put64(headerBytes, 16, vector + 8);
        Put64(memory.Map(vector, 8), 0, record);
        Convert.FromHexString(raw).CopyTo(memory.Map(record, 0x50), 0);

        var reader = new ClubListReader(memory, profile, module);
        var writer = new ClubVisionWriter(memory, profile, module);
        var before = memory.Read(record, 0x50);
        var row = reader.ReadClubVision(team).Single();
        Assert.Equal(BitConverter.ToInt32(before, 0x28), row.Target);
        Assert.Equal(before[0x38], row.Status);

        writer.SetTarget(team, row, 1234);
        Assert.Equal(1234, reader.ReadClubVision(team).Single().Target);

        row = reader.ReadClubVision(team).Single();
        writer.SetStatus(team, row, 2);
        Assert.Equal((byte)2, reader.ReadClubVision(team).Single().Status);

        var after = memory.Read(record, 0x50);
        Assert.Equal(1234, BitConverter.ToInt32(after, 0x28));
        Assert.Equal((byte)2, after[0x38]);
        // Everything else stays byte-identical.
        for (int i = 0; i < 0x50; i++)
            if (i is not (>= 0x28 and < 0x2C) and not 0x38) Assert.Equal(before[i], after[i]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Culture_fields_match_FMRTE_on_both_clubs(bool everton)
    {
        var f = new Fixture(everton);
        var rows = f.Reader.ReadClubVision(Fixture.Team, f.Clubs);
        // Independent oracle: docs/club-plan.md Sheffield culture rows; STATUS [A21:40] Everton all seven.
        Assert.Equal(everton ? new byte[] { 120, 42, 40, 155, 120, 44, 182 } : [150, 120, 104, 44, 181, 183, 183], rows.Select(r => r.TypeCode));
        Assert.Equal(everton ? new byte[] { 3, 8, 5, 7, 4, 10, 6 } : [5, 5, 4, 10, 7, 8, 1], rows.Select(r => r.Importance));
        DateOnly july13 = new(2026, 7, 13);
        Assert.Equal(everton ? new DateOnly?[] { null, null, july13, null, null, july13, july13 }
            : [null, null, null, july13, july13, july13, july13], rows.Select(r => r.StartDate));
        Assert.Equal(everton ? new DateOnly?[] { null, null, new(2031, 7, 17), null, null, null, null }
            : [null, null, null, null, null, null, null], rows.Select(r => r.EndDate));
        Assert.Equal(everton ? new short?[] { null, null, null, null, null, null, 403 }
            : [null, null, null, null, 436, 398, 419], rows.Select(r => r.ItemIndex));
        Assert.Equal(everton ? new[] { "Liverpool" } : ["Sheffield Wednesday", "Leeds United", "Nottingham Forest"],
            rows.Where(r => r.ClubItem is not null).Select(r => r.ClubItem!.Name));
        Assert.Equal(f.InitialWrites, f.Memory.Writes);
    }

    [Fact]
    public void Vision_importance_writer_uses_FMRTE_values_and_preserves_the_real_record()
    {
        var sheffield = new Fixture(false);
        var everton = new Fixture(true);
        var row = sheffield.Reader.ReadClubVision(Fixture.Team, sheffield.Clubs)[0];
        Assert.Equal((byte)5, row.Importance);
        Assert.Equal((byte)3, everton.Reader.ReadClubVision(Fixture.Team, everton.Clubs)[0].Importance);
        var writer = new ClubVisionWriter(sheffield.Memory, VisionTestProfile(), Fixture.Module);
        writer.SetImportance(Fixture.Team, row, 6);
        var expected = Convert.FromHexString(ClubListRamFixtures.SheffieldVision[0]);
        expected[0x2D] = 6;
        Assert.Equal(expected, sheffield.Memory.Read(row.Address, 0x50));

        var newProfile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426.json"));
        var oldProfile = OffsetProfile.LoadFile(TestWorld.ProfilePath);
        // Changed deliberately: the 2493426 profiles now allow this write after the live CLI verification of 5/11
        // (Sheffield 708 type 150 5 -> 6, re-read from a fresh process). The older 2486695 profile still refuses.
        Assert.False(oldProfile.Club!.Lists!.VisionImportanceEditable);
        Assert.True(newProfile.Club!.Lists!.VisionImportanceEditable);
        int writes = everton.Memory.Writes;
        Assert.Throws<InvalidOperationException>(() => new ClubVisionWriter(everton.Memory, oldProfile, Fixture.Module)
            .SetImportance(Fixture.Team, everton.Reader.ReadClubVision(Fixture.Team)[0], 4));
        Assert.Equal(writes, everton.Memory.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Vision_importance_writer_rejects_a_changed_record_or_vector(bool changeVector)
    {
        var f = new Fixture(false);
        var row = f.Reader.ReadClubVision(Fixture.Team)[0];
        if (changeVector)
        {
            ulong cap = BitConverter.ToUInt64(f.Memory.Read(Fixture.VisionHeader + 16, 8));
            Assert.True(f.Memory.TryWrite(Fixture.VisionHeader + 16, BitConverter.GetBytes(cap + 8)));
        }
        else Assert.True(f.Memory.TryWrite(row.Address + 0x2E, new byte[] { 1 }));
        var record = f.Memory.Read(row.Address, 0x50);
        int writes = f.Memory.Writes;
        var writer = new ClubVisionWriter(f.Memory, VisionTestProfile(), Fixture.Module);

        Assert.Throws<StaleObjectException>(() => writer.SetImportance(Fixture.Team, row, 6));
        Assert.Equal(writes, f.Memory.Writes);
        Assert.Equal(record, f.Memory.Read(row.Address, 0x50));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Affiliations_match_FMRTE_names_types_and_confirmed_start(bool everton)
    {
        var f = new Fixture(everton);
        var rows = f.Reader.ReadAffiliations(f.Club);
        Assert.Equal(everton ? new byte[] { 16, 16, 25 } : [25], rows.Select(r => r.TypeCode));
        Assert.All(rows, r => Assert.Equal(everton ? 650 : 708, r.ParentClub!.Id));
        Assert.Equal(everton ? new DateOnly(2010, 5, 1) : new(2024, 4, 17), rows[0].StartDate);
        if (everton)
            Assert.Equal(new[] { "Everton de Viña del Mar", "Sligo Rovers FC", "Everton" }, rows.Select(r => r.FeederClub!.Name));
        Assert.Equal(f.InitialWrites, f.Memory.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Regional_levels_match_FMRTE_and_ignore_unconfirmed_year_and_id(bool everton)
    {
        var f = new Fixture(everton);
        Assert.Equal(everton ? new byte[] { 6, 1, 2, 3, 4, 1, 2, 3 } : [6, 7, 8],
            f.Reader.ReadRegionalDivisions(f.Club).Select(r => r.Level));
        Assert.Equal(f.InitialWrites, f.Memory.Writes);
    }

    [Theory]
    [InlineData(0)] // end before begin
    [InlineData(1)] // unaligned end
    [InlineData(2)] // capacity before end
    [InlineData(3)] // unaligned capacity
    [InlineData(4)] // unreasonable allocation
    public void Malformed_vectors_are_rejected_before_reading_entries(int kind)
    {
        var f = new Fixture(true);
        foreach (ulong header in new[] { Fixture.VisionHeader, f.Club + 0x118, Fixture.RegionalHeader })
        {
            var h = f.Memory.Read(header, 24);
            ulong begin = BitConverter.ToUInt64(h), end = BitConverter.ToUInt64(h, 8);
            if (kind == 0) Put64(h, 8, begin - 8);
            if (kind == 1) Put64(h, 8, end - 1);
            if (kind == 2) Put64(h, 16, end - 8);
            if (kind == 3) Put64(h, 16, end + 1);
            if (kind == 4) Put64(h, 16, begin + 513 * 8);
            Assert.True(f.Memory.TryWrite(header, h));
        }
        int writes = f.Memory.Writes;
        Assert.Empty(f.Reader.ReadClubVision(Fixture.Team));
        Assert.Empty(f.Reader.ReadAffiliations(f.Club));
        Assert.Empty(f.Reader.ReadRegionalDivisions(f.Club));
        Assert.Equal(writes, f.Memory.Writes);
    }

    [Fact]
    public void Wrong_vtables_short_records_and_unknown_reference_kinds_are_not_decoded()
    {
        var f = new Fixture(true);
        // Unknown item marker must never be resolved to Liverpool merely because its low index is 403.
        Assert.True(f.Memory.TryWrite(Fixture.VisionRecords + 6 * 0x100 + 0xA, new byte[] { 0, 0x19 }));
        Assert.Null(f.Reader.ReadClubVision(Fixture.Team, f.Clubs)[6].ItemIndex);
        Assert.Null(f.Reader.ReadClubVision(Fixture.Team, f.Clubs)[6].ClubItem);
        Assert.True(f.Memory.TryWrite(Fixture.VisionRecords, BitConverter.GetBytes(Fixture.Module + 0x4DC9AF8)));
        Assert.Equal(6, f.Reader.ReadClubVision(Fixture.Team).Count); // objective layout is still candidate
        // A matching record vtable alone is insufficient: the complete 0x50 bytes must be readable.
        Put64(f.Memory.Map(0x7100_0000, 8), 0, Fixture.Module + 0x4B0E5C8);
        Assert.True(f.Memory.TryWrite(Fixture.VisionArray + 8, BitConverter.GetBytes(0x7100_0000UL)));
        Assert.Equal(5, f.Reader.ReadClubVision(Fixture.Team).Count);
        Assert.True(f.Memory.TryWrite(Fixture.Holder, BitConverter.GetBytes(Fixture.Module + 0x1234560)));
        Assert.Empty(f.Reader.ReadClubVision(Fixture.Team));
        Assert.True(f.Memory.TryWrite(Fixture.Holder, BitConverter.GetBytes(Fixture.Module + 0x4DBAC98)));
        Assert.True(f.Memory.TryWrite(Fixture.Team, BitConverter.GetBytes(Fixture.Module + 0x1234560)));
        Assert.Empty(f.Reader.ReadClubVision(Fixture.Team));
        Assert.True(f.Memory.TryWrite(Fixture.Team, BitConverter.GetBytes(Fixture.Module + 0x4CE1108)));
        Assert.True(f.Memory.TryWrite(f.Club, BitConverter.GetBytes(Fixture.Module + 0x1234560)));
        Assert.Empty(f.Reader.ReadAffiliations(f.Club));
        Assert.Empty(f.Reader.ReadRegionalDivisions(f.Club));
    }

    [Fact]
    public void Missing_profile_empty_vectors_and_invalid_layouts_fail_closed()
    {
        var f = new Fixture(true);
        var noLists = OffsetProfile.Load(File.ReadAllText(TestWorld.ProfilePath).Replace("\"lists\": {", "\"unusedLists\": {"));
        var noReader = new ClubListReader(f.Memory, noLists, Fixture.Module);
        Assert.Empty(noReader.ReadClubVision(Fixture.Team));
        Assert.Empty(noReader.ReadAffiliations(f.Club));
        Assert.Empty(noReader.ReadRegionalDivisions(f.Club));
        var bad = OffsetProfile.Load(File.ReadAllText(TestWorld.ProfilePath).Replace("\"cultureSize\": \"0x50\"", "\"cultureSize\": \"0x2D\""));
        Assert.Empty(new ClubListReader(f.Memory, bad, Fixture.Module).ReadClubVision(Fixture.Team));
        foreach (ulong h in new[] { Fixture.VisionHeader, f.Club + 0x118, Fixture.RegionalHeader })
            Assert.True(f.Memory.TryWrite(h, new byte[24]));
        Assert.Empty(f.Reader.ReadClubVision(Fixture.Team));
        Assert.Empty(f.Reader.ReadAffiliations(f.Club));
        Assert.Empty(f.Reader.ReadRegionalDivisions(f.Club));
    }

    static void Put64(byte[] b, int at, ulong value) => BitConverter.GetBytes(value).CopyTo(b, at);

    sealed class Fixture
    {
        internal const ulong Module = 0x7FFAF3E20000, Team = 0x50010000, Holder = 0x50030000,
            VisionHeader = 0x50040000, RegionalHeader = 0x50050000, VisionArray = 0x60000000, VisionRecords = 0x70000000;
        public FakeMemory Memory { get; } = new();
        public List<ClubEntry> Clubs { get; } = [];
        public ulong Club { get; }
        public ClubListReader Reader { get; }
        public int InitialWrites { get; }
        ulong nextName = 0x51000000;

        public Fixture(bool everton)
        {
            Club = everton ? 0x1AB691E7840UL : 0x1AB691E24E0UL;
            var c = MapClub(Club, everton ? 650 : 708, everton ? "Everton" : "Sheffield United");
            var t = Memory.Map(Team, 0xB8);
            Put64(t, 0, Module + 0x4CE1108); Put64(t, 0xA0, 0x50020000);
            Put64(Memory.Map(0x50020000, 0xD0), 0xC8, Holder);
            var holder = Memory.Map(Holder, 0x80);
            Put64(holder, 0, Module + 0x4DBAC98); Put64(holder, 0x78, VisionHeader);
            Memory.Map(VisionHeader, 24); Memory.Map(RegionalHeader, 24);
            Put64(c, 0x138, RegionalHeader);
            var vision = everton ? ClubListRamFixtures.EvertonVision : ClubListRamFixtures.SheffieldVision;
            var aff = everton ? ClubListRamFixtures.EvertonAffiliations : ClubListRamFixtures.SheffieldAffiliations;
            var regional = everton ? ClubListRamFixtures.EvertonRegional : ClubListRamFixtures.SheffieldRegional;
            Vector(VisionHeader, VisionArray, VisionRecords, vision);
            Vector(Club + 0x118, 0x60001000, 0x70001000, aff);
            Vector(RegionalHeader, 0x60002000, 0x70002000, regional);
            for (int i = 0; i < aff.Length; i++)
            {
                ulong feeder = BitConverter.ToUInt64(Convert.FromHexString(aff[i]), 8);
                MapClub(feeder, everton ? new[] { 130782, 598, 2000104568 }[i] : 0x77373ADD,
                    everton ? new[] { "Everton de Viña del Mar", "Sligo Rovers FC", "Everton" }[i] : null);
            }
            if (everton) MapClub(0x52000000, 676, "Liverpool", 403);
            else
            {
                MapClub(0x52000000, 709, "Sheffield Wednesday", 436);
                MapClub(0x52001000, 671, "Leeds United", 398);
                MapClub(0x52002000, 692, "Nottingham Forest", 419);
            }
            Reader = new(Memory, OffsetProfile.LoadFile(TestWorld.ProfilePath), Module);
            InitialWrites = Memory.Writes;
        }

        byte[] MapClub(ulong address, int id, string? name, int index = 0)
        {
            var b = Memory.Map(address, 0x170);
            Put64(b, 0, Module + 0x4CDA1F8);
            BitConverter.GetBytes(index).CopyTo(b, 8); BitConverter.GetBytes(id).CopyTo(b, 0xC);
            if (name is not null)
            {
                Memory.MapString(nextName, name);
                Put64(b, 0xC0, nextName); Put64(b, 0xC8, nextName); nextName += 0x1000;
            }
            Clubs.Add(new(address, id, name, name, 0, []));
            return b;
        }

        void Vector(ulong header, ulong array, ulong records, string[] raw)
        {
            var b = Memory.Map(array, (raw.Length + 1) * 8);
            for (int i = 0; i < raw.Length; i++)
            {
                var bytes = Convert.FromHexString(raw[i]);
                bytes.CopyTo(Memory.Map(records + (ulong)i * 0x100, bytes.Length), 0);
                Put64(b, i * 8, records + (ulong)i * 0x100);
            }
            Put64(b, raw.Length * 8, records); // valid stale duplicate in capacity, beyond end: must not appear
            var h = new byte[24];
            Put64(h, 0, array); Put64(h, 8, array + (ulong)raw.Length * 8); Put64(h, 16, array + (ulong)(raw.Length + 1) * 8);
            Assert.True(Memory.TryWrite(header, h));
        }
    }
}

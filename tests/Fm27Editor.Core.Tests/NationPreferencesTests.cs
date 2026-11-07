using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class NationPreferencesTests
{
    [Fact]
    public void Adding_to_empty_eu_copies_the_observed_agreement_and_leaves_non_foreign_unchanged()
    {
        // Brazil/FMRTE: agreement at +0x20 and 2022-07-01 / No limit at +0x28/+0x2C.
        var profile = Fm27Editor.Core.Game.GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong destination = 0x75000000, sourceNation = 0x75001000, sourceArray = 0x75002000,
            sourceRecord = 0x75003000, organisation = 0x75004000;
        foreach (ulong address in new[] { destination, sourceNation })
            TestWorld.Set64(memory.Map(address, 0x200), 0, TestWorld.ModuleBase + profile.Nation!.VtableRva);
        Assert.True(memory.TryWrite(sourceNation + 0xA8, BitConverter.GetBytes(sourceArray)));
        Assert.True(memory.TryWrite(sourceNation + 0xB0, BitConverter.GetBytes(sourceArray + 8)));
        Assert.True(memory.TryWrite(sourceNation + 0xB8, BitConverter.GetBytes(sourceArray + 8)));
        TestWorld.Set64(memory.Map(sourceArray, 8), 0, sourceRecord);
        TestWorld.Set64(memory.Map(organisation, 0x40), 0, TestWorld.ModuleBase + 0x4C608B8);
        var bytes = memory.Map(sourceRecord, 0x30);
        TestWorld.Set64(bytes, 0x20, organisation);
        Convert.FromHexString("B600E60701006C07").CopyTo(bytes, 0x28);
        var access = new NationAgreements(memory, profile, TestWorld.ModuleBase);
        var source = Assert.Single(access.Read(sourceNation, NationAgreements.EuVector, []));
        byte[] empty = memory.Read(destination + 0xA8, 24);
        ulong next = 0x75010000;
        ulong Allocate(int size) { ulong address = next; next += 0x1000; memory.Map(address, size); return address; }
        var added = access.AddToEmpty(destination, empty, sourceNation, source, Allocate, []);
        Assert.NotNull(added);
        Assert.Equal(AgreementKind.Agreement, added.Kind);
        Assert.Equal(organisation, added.Target);
        Assert.Equal(Convert.FromHexString("B600E60701006C07"), memory.Read(added.Address + 0x28, 8));
        Assert.Equal(new byte[24], memory.Read(destination + 0x90, 24));
    }

    [Fact]
    public void Adding_to_empty_non_foreign_copies_a_valid_row_and_rejects_stale_state()
    {
        var profile = Fm27Editor.Core.Game.GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong destination = 0x76000000, secondDestination = 0x76001000, sourceNation = 0x76002000,
            target = 0x76003000, sourceArray = 0x76004000, sourceRecord = 0x76005000;
        foreach (ulong address in new[] { destination, secondDestination, sourceNation, target })
            TestWorld.Set64(memory.Map(address, 0x200), 0, TestWorld.ModuleBase + profile.Nation!.VtableRva);
        Assert.True(memory.TryWrite(sourceNation + 0x90, BitConverter.GetBytes(sourceArray)));
        Assert.True(memory.TryWrite(sourceNation + 0x98, BitConverter.GetBytes(sourceArray + 8)));
        Assert.True(memory.TryWrite(sourceNation + 0xA0, BitConverter.GetBytes(sourceArray + 8)));
        TestWorld.Set64(memory.Map(sourceArray, 8), 0, sourceRecord);
        var bytes = memory.Map(sourceRecord, 0x30);
        TestWorld.Set64(bytes, 0, target);
        Convert.FromHexString("01006C0701006C07").CopyTo(bytes, 0x28);
        var access = new NationAgreements(memory, profile, TestWorld.ModuleBase);
        var source = Assert.Single(access.Read(sourceNation, NationAgreements.NonForeignVector, []));
        byte[] empty = memory.Read(destination + 0x90, 24);
        ulong next = 0x76010000;
        ulong Allocate(int size) { ulong address = next; next += 0x1000; memory.Map(address, size); return address; }
        var added = access.AddToEmpty(destination, empty, sourceNation, source, Allocate, []);
        Assert.NotNull(added);
        Assert.Equal(AgreementKind.Nation, added.Kind);
        Assert.Equal(target, added.Target);
        Assert.Equal(Convert.FromHexString("01006C0701006C07"), memory.Read(added.Address + 0x28, 8));
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => access.AddToEmpty(destination, empty, sourceNation, source, Allocate, []));
        Assert.Equal(writes, memory.Writes);
        Assert.True(memory.TryWrite(sourceRecord + 0x28, Convert.FromHexString("02006C07")));
        writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => access.AddToEmpty(secondDestination, new byte[24], sourceNation, source, Allocate, []));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void Region_catalogue_and_guarded_target_preserve_the_dates()
    {
        var profile = Fm27Editor.Core.Game.GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong nation = 0x77000000, array = 0x77010000, record = 0x77020000, region14 = 0x77030000, region23 = 0x77031000, holder = 0x77040000, catalogue = 0x77050000;
        var n = memory.Map(nation, 0x200);
        TestWorld.Set64(n, 0, TestWorld.ModuleBase + profile.Nation!.VtableRva);
        TestWorld.Set64(n, 0x90, array); TestWorld.Set64(n, 0x98, array + 8); TestWorld.Set64(n, 0xA0, array + 8);
        TestWorld.Set64(memory.Map(array, 8), 0, record);
        var b = memory.Map(record, 0x30);
        TestWorld.Set64(b, 0x18, region14);
        Convert.FromHexString("0100E4076D01EE07").CopyTo(b, 0x28);
        foreach (var (address, id) in new[] { (region14, 14), (region23, 23) })
        {
            var region = memory.Map(address, 24);
            TestWorld.Set64(region, 0, TestWorld.ModuleBase + 0x4D78508);
            BitConverter.GetBytes(id).CopyTo(region, 0x10);
        }
        TestWorld.Set64(memory.Map(TestWorld.ModuleBase + profile.Nation.ListRootRva!.Value, 8), 0, holder);
        var container = memory.Map(holder, 0x810);
        TestWorld.Set64(container, 0x740, catalogue); TestWorld.Set64(container, 0x748, catalogue + 16); TestWorld.Set64(container, 0x750, catalogue + 16);
        var entries = memory.Map(catalogue, 16);
        TestWorld.Set64(entries, 0, region14); TestWorld.Set64(entries, 8, region23);
        var access = new NationAgreements(memory, profile, TestWorld.ModuleBase);
        Assert.Equal(["Uk And Ireland", "23"], access.Regions().Select(x => x.Name));
        var row = Assert.Single(access.Read(nation, NationAgreements.NonForeignVector, []));
        access.SetTarget(nation, row, AgreementKind.Region, region23);
        Assert.Equal(region23, BitConverter.ToUInt64(memory.Read(record + 0x18, 8)));
        Assert.Equal(Convert.FromHexString("0100E4076D01EE07"), memory.Read(record + 0x28, 8));
        Assert.Throws<StaleObjectException>(() => access.SetTarget(nation, row, AgreementKind.Region, region14));
    }

    [Fact]
    public void Region_names_use_fmrte_enum_values()
    {
        // FMRTE NativeFormat enum constants: EasternEurope=6, CentralAsia=15; 29 has no named enum member.
        var profile = Fm27Editor.Core.Game.GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong holder = 0x78000000, catalogue = 0x78001000;
        TestWorld.Set64(memory.Map(TestWorld.ModuleBase + profile.Nation!.ListRootRva!.Value, 8), 0, holder);
        var container = memory.Map(holder, 0x810);
        TestWorld.Set64(container, 0x740, catalogue); TestWorld.Set64(container, 0x748, catalogue + 24); TestWorld.Set64(container, 0x750, catalogue + 24);
        var entries = memory.Map(catalogue, 24);
        int[] ids = [6, 15, 29];
        for (int i = 0; i < ids.Length; i++)
        {
            ulong address = 0x78010000UL + (ulong)i * 0x1000;
            TestWorld.Set64(entries, i * 8, address);
            var region = memory.Map(address, 24);
            TestWorld.Set64(region, 0, TestWorld.ModuleBase + 0x4D78508);
            BitConverter.GetBytes(ids[i]).CopyTo(region, 0x10);
        }
        Assert.Equal(["Eastern Europe", "Central Asia", "29"], new NationAgreements(memory, profile, TestWorld.ModuleBase).Regions().Select(x => x.Name));
    }

    [Fact]
    public void Agreement_target_change_uses_the_gamepass_organisation_and_preserves_dates()
    {
        var profile = Fm27Editor.Core.Game.GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.0+2493426 Game Pass");
        var memory = new FakeMemory();
        const ulong nation = 0x77000000, array = 0x77010000, record = 0x77020000, organisation = 0x77030000;
        var n = memory.Map(nation, 0x200);
        TestWorld.Set64(n, 0, TestWorld.ModuleBase + profile.Nation!.VtableRva);
        TestWorld.Set64(n, 0xA8, array); TestWorld.Set64(n, 0xB0, array + 8); TestWorld.Set64(n, 0xB8, array + 8);
        TestWorld.Set64(memory.Map(array, 8), 0, record);
        var b = memory.Map(record, 0x30);
        Convert.FromHexString("0100E4076D01EE07").CopyTo(b, 0x28); // FMRTE England 2020-01-01 / 2030-12-31
        TestWorld.Set64(memory.Map(organisation, 0x40), 0, TestWorld.ModuleBase + 0x4C608B8);
        var access = new NationAgreements(memory, profile, TestWorld.ModuleBase);
        var row = Assert.Single(access.Read(nation, NationAgreements.EuVector, []));
        access.SetTarget(nation, row, AgreementKind.Agreement, organisation);
        Assert.Equal(organisation, BitConverter.ToUInt64(memory.Read(record + 0x20, 8)));
        Assert.Equal(Convert.FromHexString("0100E4076D01EE07"), memory.Read(record + 0x28, 8));
        Assert.Throws<StaleObjectException>(() => access.SetTarget(nation, row, AgreementKind.Agreement, organisation));
    }

    [Fact]
    public void Rejects_changed_capacity_and_misaligned_capacity()
    {
        // Spain–Austria bytes and values observed in FMRTE; fake pointers identify this test's objects.
        var w = new TestWorld();
        const ulong spain = 0x71000000, austria = 0x71002000, st = 0x72000000, at = 0x72002000, sr = 0x73000000;
        var nations = new[] { new NationRecord(spain, 796, "Spain", null), new NationRecord(austria, null, "Austria", null) };
        foreach (var (nation, team) in new[] { (spain, st), (austria, at) })
        {
            var n = w.Memory.Map(nation, 0x200);
            TestWorld.Set64(n, 0, TestWorld.ModuleBase + 0x4CDF658);
            TestWorld.Set64(n, 0x108, team);
            TestWorld.Set64(w.Memory.Map(team, 0x900), 0, TestWorld.ModuleBase + 0x4CEC0B8);
        }
        Assert.True(w.Memory.TryWrite(st + 0xF0, BitConverter.GetBytes(sr)));
        Assert.True(w.Memory.TryWrite(st + 0xF8, BitConverter.GetBytes(sr + 16)));
        Assert.True(w.Memory.TryWrite(st + 0x100, BitConverter.GetBytes(sr + 16)));
        var bytes = w.Memory.Map(sr, 16);
        Convert.FromHexString("0619120000000000D8E4C52633010000").CopyTo(bytes, 0);
        TestWorld.Set64(bytes, 8, at);
        var access = new NationPreferences(w.Memory, w.Profile, TestWorld.ModuleBase);
        var row = Assert.Single(access.Read(nations[0], nations));
        var original = w.Bytes(sr, 16);
        Assert.True(w.Memory.TryWrite(st + 0x100, BitConverter.GetBytes(sr + 32)));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => access.Set(row, 7, 19, 26, 1));
        Assert.Equal(writes, w.Memory.Writes);
        Assert.Equal(original, w.Bytes(sr, 16));
        Assert.True(w.Memory.TryWrite(st + 0x100, BitConverter.GetBytes(sr + 17)));
        Assert.Empty(access.Read(nations[0], nations));
    }

    [Fact]
    public void Reads_observed_rows_writes_four_values_and_preserves_remaining_bytes()
    {
        // FMRTE 27: Spain–Austria 6/0/18/25; France–Belgium 12/12/14/45.
        var w = new TestWorld();
        const ulong spain = 0x71000000, france = 0x71001000, austria = 0x71002000, belgium = 0x71003000;
        const ulong st = 0x72000000, ft = 0x72001000, at = 0x72002000, bt = 0x72003000;
        const ulong sr = 0x73000000, fr = 0x73001000;
        var nations = new[] { new NationRecord(spain, 796, "Spain", null), new NationRecord(france, 769, "France", null),
            new NationRecord(austria, null, "Austria", null), new NationRecord(belgium, null, "Belgium", null) };
        foreach (var (nation, team) in new[] { (spain, st), (france, ft), (austria, at), (belgium, bt) })
        {
            var n = w.Memory.Map(nation, 0x200);
            TestWorld.Set64(n, 0, TestWorld.ModuleBase + 0x4CDF658);
            TestWorld.Set64(n, 0x108, team);
            TestWorld.Set64(w.Memory.Map(team, 0x900), 0, TestWorld.ModuleBase + 0x4CEC0B8);
        }
        void Vector(ulong team, ulong rows, ulong target, string raw)
        {
            Assert.True(w.Memory.TryWrite(team + 0xF0, BitConverter.GetBytes(rows)));
            Assert.True(w.Memory.TryWrite(team + 0xF8, BitConverter.GetBytes(rows + 16)));
            Assert.True(w.Memory.TryWrite(team + 0x100, BitConverter.GetBytes(rows + 16)));
            var bytes = w.Memory.Map(rows, 16);
            Convert.FromHexString(raw).CopyTo(bytes, 0);
            TestWorld.Set64(bytes, 8, target);
        }
        Vector(st, sr, at, "0619120000000000D8E4C52633010000");
        Vector(ft, fr, bt, "0C2D0E030C00000008F6C52633010000");
        var access = new NationPreferences(w.Memory, w.Profile, TestWorld.ModuleBase);
        var a = Assert.Single(access.Read(nations[0], nations));
        var b = Assert.Single(access.Read(nations[1], nations));
        Assert.Equal(("Austria", (sbyte)6, (byte)0, (byte)18, (byte)25), (a.Target, a.Rating, a.Knowledge, a.MinAge, a.MaxAge));
        Assert.Equal(("Belgium", (sbyte)12, (byte)12, (byte)14, (byte)45), (b.Target, b.Rating, b.Knowledge, b.MinAge, b.MaxAge));
        access.Set(a, 7, 19, 26, 1);
        Assert.Equal("071A130001000000", Convert.ToHexString(w.Bytes(sr, 8)));
        access.Set(b, 13, 15, 44, 13);
        Assert.Equal("0D2C0F030D000000", Convert.ToHexString(w.Bytes(fr, 8)));
        Assert.Throws<StaleObjectException>(() => access.Set(b, 12, 14, 45, 12));
        var fresh = Assert.Single(access.Read(nations[1], nations));
        Assert.True(w.Memory.TryWrite(fr + 3, [0x04]));
        Assert.Throws<StaleObjectException>(() => access.Set(fresh, 12, 14, 45, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => access.Set(a, 6, 46, 45, 0));
    }

    [Fact]
    public void Continents_rows_read_from_team_0x108_and_write_four_values()
    {
        // Spain's continents vector as measured on 5/11: Asia / North America / Oceania -1, South America 12/0/16/28.
        var w = new TestWorld();
        const ulong spain = 0x74000000, st = 0x74100000, rows = 0x74200000, asia = 0x74300000;
        var nation = new NationRecord(spain, 796, "Spain", null);
        var n = w.Memory.Map(spain, 0x200);
        TestWorld.Set64(n, 0, TestWorld.ModuleBase + 0x4CDF658);
        TestWorld.Set64(n, 0x108, st);
        TestWorld.Set64(w.Memory.Map(st, 0x900), 0, TestWorld.ModuleBase + 0x4CEC0B8);
        Assert.True(w.Memory.TryWrite(st + 0x108, BitConverter.GetBytes(rows)));
        Assert.True(w.Memory.TryWrite(st + 0x110, BitConverter.GetBytes(rows + 16)));
        Assert.True(w.Memory.TryWrite(st + 0x118, BitConverter.GetBytes(rows + 16)));
        var asiaRow = w.Memory.Map(rows, 16);
        Convert.FromHexString("FF2D0E00000000000000000000000000").CopyTo(asiaRow, 0);
        TestWorld.Set64(asiaRow, 8, asia);
        var continents = new[] { new ContinentRecord(asia, "Asia") };
        var access = new NationPreferences(w.Memory, w.Profile, TestWorld.ModuleBase);
        var row = Assert.Single(access.ReadContinents(nation, continents));
        Assert.Equal((-1, 0, (byte)14, (byte)45), (row.Rating, row.Knowledge, row.MinAge, row.MaxAge));
        access.SetContinent(row, 13, 16, 28, 2);
        var after = Assert.Single(access.ReadContinents(nation, continents));
        Assert.Equal((13, 2, (byte)16, (byte)28), (after.Rating, after.Knowledge, after.MinAge, after.MaxAge));
        Assert.Throws<StaleObjectException>(() => access.SetContinent(row, 1, 1, 2, 1));
    }

    [Fact]
    public void EuroCoefficients_reads_ten_floats_and_labels_them_from_the_current_season()
    {
        var w = new TestWorld();
        const ulong spain = 0x75000000, st = 0x75100000, rows = 0x75200000;
        var n = w.Memory.Map(spain, 0x200);
        TestWorld.Set64(n, 0, TestWorld.ModuleBase + 0x4CDF658);
        TestWorld.Set64(n, 0x108, st);
        TestWorld.Set64(w.Memory.Map(st, 0x900), 0, TestWorld.ModuleBase + 0x4CEC0B8);
        Assert.True(w.Memory.TryWrite(st + 0x190, BitConverter.GetBytes(rows)));
        Assert.True(w.Memory.TryWrite(st + 0x198, BitConverter.GetBytes(rows + 40)));
        Assert.True(w.Memory.TryWrite(st + 0x1A0, BitConverter.GetBytes(rows + 40)));
        var buffer = w.Memory.Map(rows, 40);
        float[] expected = [19.714f, 19.571f, 18.928f, 19.5f, 18.428f, 16.571f, 16.062f, 23.892f, 22.093f, 0f];
        for (int i = 0; i < expected.Length; i++) BitConverter.GetBytes(expected[i]).CopyTo(buffer, i * 4);
        var access = new NationEuroCoefficients(w.Memory, w.Profile, TestWorld.ModuleBase);
        Assert.Equal(expected, access.Read(spain));
        Assert.Equal("Season 2017/2018", NationEuroCoefficients.Labels(2026, 10)[0]);
        Assert.Equal("Season 2026/2027", NationEuroCoefficients.Labels(2026, 10)[9]);
    }

    [Fact]
    public void Agreements_rows_read_duplicate_and_remove_like_fmrtes_plus_and_minus()
    {
        // Layout measured 5/11 with a Frida write trace: vector at nation+0xA8 (EU), 0x30-byte record, target +0x20,
        // From/Until +0x28/+0x2C, adding copies the record into a new array.
        var w = new TestWorld();
        const ulong england = 0x76000000, array = 0x76100000, row = 0x76200000, org = 0x76300000,
            name = 0x76301000, recordCopy = 0x76400000, arrayCopy = 0x76500000;
        var n = w.Memory.Map(england, 0x200);
        TestWorld.Set64(n, 0, TestWorld.ModuleBase + 0x4CDF658);
        Assert.True(w.Memory.TryWrite(england + 0xA8, BitConverter.GetBytes(array)));
        Assert.True(w.Memory.TryWrite(england + 0xB0, BitConverter.GetBytes(array + 8)));
        Assert.True(w.Memory.TryWrite(england + 0xB8, BitConverter.GetBytes(array + 8)));
        w.Memory.Map(array, 16);
        Assert.True(w.Memory.TryWrite(array, BitConverter.GetBytes(row)));
        var record = w.Memory.Map(row, 0x30);
        TestWorld.Set64(record, 0x20, org);
        Convert.FromHexString("01006C0701006C07").CopyTo(record, 0x28);
        var orgBytes = w.Memory.Map(org, 0x40);
        TestWorld.Set64(orgBytes, 0x20, name);
        var label = w.Memory.Map(name, 32);
        BitConverter.GetBytes(14).CopyTo(label, 0);
        System.Text.Encoding.UTF8.GetBytes("European Union").CopyTo(label, 4);
        var access = new NationAgreements(w.Memory, w.Profile, TestWorld.ModuleBase);
        var first = Assert.Single(access.Read(england, NationAgreements.EuVector, []));
        Assert.Equal(AgreementKind.Agreement, first.Kind);
        Assert.Equal("European Union", first.TargetName);
        Assert.Null(first.From);

        // FMRTE trace: 01/01/2020 and 31/12/2030 = 0100e407 6d01ee07.
        access.SetDates(england, first, new DateOnly(2020, 1, 1), new DateOnly(2030, 12, 31));
        Assert.Equal(Convert.FromHexString("0100E4076D01EE07"), w.Memory.Read(row + 0x28, 8));
        Assert.Throws<StaleObjectException>(() => access.SetDates(england, first, null, null));
        first = Assert.Single(access.Read(england, NationAgreements.EuVector, []));

        const ulong targetNation = 0x76800000;
        TestWorld.Set64(w.Memory.Map(targetNation, 0x200), 0, TestWorld.ModuleBase + w.Profile.Nation!.VtableRva);
        access.SetTarget(england, first, AgreementKind.ExcludedNation, targetNation);
        var nationList = new[] { new NationRecord(targetNation, 123, "New Zealand", null) };
        first = Assert.Single(access.Read(england, NationAgreements.EuVector, [], nationList));
        Assert.Equal(AgreementKind.ExcludedNation, first.Kind);
        Assert.Equal("New Zealand", first.TargetName);
        Assert.Equal(targetNation, BitConverter.ToUInt64(w.Memory.Read(row + 0x10, 8)));
        Assert.Equal(0UL, BitConverter.ToUInt64(w.Memory.Read(row + 0x20, 8)));

        w.Memory.Map(recordCopy, 0x30);
        w.Memory.Map(arrayCopy, 32);
        int allocations = 0;
        ulong Allocate(int bytes) => allocations++ == 0 ? recordCopy : arrayCopy;
        var added = access.Duplicate(england, first, Allocate, []);
        Assert.NotNull(added);
        Assert.Equal(2, allocations);
        var afterAdd = access.Read(england, NationAgreements.EuVector, []);
        Assert.Equal(2, afterAdd.Count);
        Assert.Equal(recordCopy, afterAdd[1].Address);

        access.Remove(england, afterAdd[0]);
        var remaining = Assert.Single(access.Read(england, NationAgreements.EuVector, []));
        Assert.Equal(recordCopy, remaining.Address);
        Assert.Throws<StaleObjectException>(() => access.Remove(england, afterAdd[0]));
    }

    [Fact]
    public void AllowLicensedKits_is_bit_0x08_of_team_0x2E_and_preserves_other_bits()
    {
        // Frida trace 6/11: FMRTE wrote 0x2C when unticking and 0x24 when ticking England's senior team at +0x2E.
        var w = new TestWorld();
        const ulong team = 0x76600000;
        var bytes = w.Memory.Map(team, 0x100);
        TestWorld.Set64(bytes, 0, TestWorld.ModuleBase + (w.Profile.Nation?.Teams?.TeamVtable ?? 0x4D71998));
        bytes[0x2C] = 0x64; bytes[0x2D] = 0xFF; bytes[0x2E] = 0x2C; bytes[0x2F] = 0x01;
        var reader = new NationTeamReader(w.Memory, w.Profile, TestWorld.ModuleBase);
        var record = new NationTeamRecord(team, 0, 9592, null);
        Assert.False(reader.AllowLicensedKits(team));

        reader.SetAllowLicensedKits(record, true);
        Assert.Equal((byte)0x24, bytes[0x2E]);
        Assert.Equal((byte)0x64, bytes[0x2C]);
        Assert.Equal((byte)0xFF, bytes[0x2D]);
        Assert.Equal((byte)0x01, bytes[0x2F]);
        Assert.True(reader.AllowLicensedKits(team));

        reader.SetAllowLicensedKits(record, false);
        Assert.Equal((byte)0x2C, bytes[0x2E]);
        Assert.False(reader.AllowLicensedKits(team));

        TestWorld.Set64(bytes, 0, 0);
        Assert.Throws<StaleObjectException>(() => reader.SetAllowLicensedKits(record, true));
        Assert.Equal((byte)0x2C, bytes[0x2E]);
    }

    [Fact]
    public void Nation_colours_read_foreground_and_background_bgra_and_write_one_dword()
    {
        // Frida trace + pointer search 6/11: nation+0x108 (team) +0xB0 (block) +0xA0 foreground, +0xA8 background.
        var w = new TestWorld();
        const ulong england = 0x76700000, team = 0x76800000, block = 0x76900000;
        var n = w.Memory.Map(england, 0x200);
        TestWorld.Set64(n, 0, TestWorld.ModuleBase + (w.Profile.Nation?.VtableRva ?? 0x4C8EFA8));
        TestWorld.Set64(n, 0x108, team);
        var teamBytes = w.Memory.Map(team, 0x900);
        TestWorld.Set64(teamBytes, 0, TestWorld.ModuleBase + (w.Profile.Nation?.Preferences?.TeamVtable ?? 0x4C9B0E8));
        TestWorld.Set64(teamBytes, 0xB0, block);
        var colours = w.Memory.Map(block + 0xA0, 16);
        Convert.FromHexString("B5552FFF00000000FAFAFAFF").CopyTo(colours, 0);
        var access = new NationColourAccess(w.Memory, w.Profile, TestWorld.ModuleBase);
        var snapshot = access.Read(england);
        Assert.NotNull(snapshot);
        Assert.Equal("#2F55B5", NationColours.Format(snapshot!.Foreground));
        Assert.Equal("#FAFAFA", NationColours.Format(snapshot.Background));

        access.Set(england, snapshot, true, 0xFF563412);
        var after = access.Read(england);
        Assert.Equal("#563412", NationColours.Format(after!.Background));
        Assert.Equal("#2F55B5", NationColours.Format(after.Foreground));

        Convert.FromHexString("11223344").CopyTo(colours, 0);
        Assert.Throws<StaleObjectException>(() => access.Set(england, after, false, 0xFF000000));
        Assert.True(NationColours.TryParse("#123456", out uint parsed));
        Assert.Equal(0xFF123456u, parsed);
        Assert.False(NationColours.TryParse("12345", out _));
    }

    [Fact]
    public void Preferences_change_target_add_with_spill_and_remove()
    {
        var w = new TestWorld();
        const ulong england = 0x76A00000, team = 0x76B00000, rows = 0x76C00000, brazil = 0x76D00000,
            buffer = 0x76E00000;
        var n = w.Memory.Map(england, 0x200);
        TestWorld.Set64(n, 0, TestWorld.ModuleBase + (w.Profile.Nation?.VtableRva ?? 0x4C8EFA8));
        TestWorld.Set64(n, 0x108, team);
        var teamBytes = w.Memory.Map(team, 0x400);
        TestWorld.Set64(teamBytes, 0, TestWorld.ModuleBase + (w.Profile.Nation?.Preferences?.TeamVtable ?? 0x4C9B0E8));
        Assert.True(w.Memory.TryWrite(team + 0xF0, BitConverter.GetBytes(rows)));
        Assert.True(w.Memory.TryWrite(team + 0xF8, BitConverter.GetBytes(rows + 16)));
        Assert.True(w.Memory.TryWrite(team + 0x100, BitConverter.GetBytes(rows + 16)));
        var record = w.Memory.Map(rows, 16);
        Convert.FromHexString("FE2D0E0000000000").CopyTo(record, 0);
        TestWorld.Set64(record, 8, 0x76D00000);
        var access = new NationPreferences(w.Memory, w.Profile, TestWorld.ModuleBase);
        var nation = new NationRecord(england, 765, "England", null);
        var row = Assert.Single(access.Read(nation, []));
        Assert.Equal((sbyte)-2, row.Rating);

        access.SetTarget(row, 0xF0, brazil);
        Assert.Equal(brazil, BitConverter.ToUInt64(record, 8));

        w.Memory.Map(buffer, 64);
        access.Add(team, 0xF0, brazil, 5, 18, 30, 7, _ => buffer);
        Assert.Equal(buffer, BitConverter.ToUInt64(teamBytes, 0xF0));
        Assert.Equal(buffer + 32, BitConverter.ToUInt64(teamBytes, 0xF8));
        var added = w.Bytes(buffer + 16, 16);
        Assert.Equal((byte)5, added[0]);
        Assert.Equal((byte)30, added[1]);
        Assert.Equal((byte)18, added[2]);
        Assert.Equal((byte)7, added[4]);

        var fresh = access.Read(nation, []);
        Assert.Equal(2, fresh.Count);
        access.Remove(fresh[0], 0xF0);
        Assert.Equal(1, access.Read(nation, []).Count);
        Assert.Throws<StaleObjectException>(() => access.Remove(fresh[0], 0xF0));

        // Empty vector: England's continents list starts at 0/0/0, so adding must allocate and repoint the header.
        const ulong emptyTeam = 0x76F00000, emptyBuffer = 0x76F80000;
        var emptyBytes = w.Memory.Map(emptyTeam, 0x200);
        TestWorld.Set64(emptyBytes, 0, TestWorld.ModuleBase + (w.Profile.Nation?.Preferences?.TeamVtable ?? 0x4C9B0E8));
        w.Memory.Map(emptyBuffer, 64);
        access.Add(emptyTeam, 0x108, brazil, -1, 14, 45, 0, _ => emptyBuffer);
        Assert.Equal(emptyBuffer, BitConverter.ToUInt64(emptyBytes, 0x108));
        Assert.Equal(emptyBuffer + 16, BitConverter.ToUInt64(emptyBytes, 0x110));
        Assert.Equal(emptyBuffer + 32, BitConverter.ToUInt64(emptyBytes, 0x118));
        Assert.Equal(brazil, BitConverter.ToUInt64(w.Bytes(emptyBuffer + 8, 8), 0));
    }

    [Fact]
    public void Nation_kits_read_the_shared_holder_vector_and_write_rgb_only()
    {
        // England Home shirt as measured 6/11: holder+0x78 vector, colour pointer = record+8, 0x28-byte record,
        // kit 0 type 0x03 style 10 colours 0x002080 / 0xF8F8F8.
        var w = new TestWorld();
        const ulong england = 0x77000000, team = 0x77100000, holder = 0x77200000, slots = 0x77300000, record = 0x77400000;
        var n = w.Memory.Map(england, 0x200);
        TestWorld.Set64(n, 0, TestWorld.ModuleBase + (w.Profile.Nation?.VtableRva ?? 0x4C8EFA8));
        TestWorld.Set64(n, 0x108, team);
        var teamBytes = w.Memory.Map(team, 0x400);
        TestWorld.Set64(teamBytes, 0, TestWorld.ModuleBase + (w.Profile.Nation?.Preferences?.TeamVtable ?? 0x4C9B0E8));
        TestWorld.Set64(teamBytes, 0xB0, holder);
        var holderBytes = w.Memory.Map(holder, 0x200);
        Assert.True(w.Memory.TryWrite(holder + 0x78, BitConverter.GetBytes(slots)));
        Assert.True(w.Memory.TryWrite(holder + 0x80, BitConverter.GetBytes(slots + 8)));
        Assert.True(w.Memory.TryWrite(holder + 0x88, BitConverter.GetBytes(slots + 8)));
        w.Memory.Map(slots, 8);
        Assert.True(w.Memory.TryWrite(slots, BitConverter.GetBytes(record + 8)));
        var bytes = w.Memory.Map(record, 0x28);
        bytes[2] = 0; bytes[3] = 0x03;
        BitConverter.GetBytes(10).CopyTo(bytes, 0x1C);
        Convert.FromHexString("802000FF").CopyTo(bytes, 8);
        Convert.FromHexString("F8F8F8FF").CopyTo(bytes, 12);
        var access = new NationKits(w.Memory, w.Profile, TestWorld.ModuleBase);
        var item = Assert.Single(access.Read(england));
        Assert.Equal((byte)0, item.Kit);
        Assert.Equal((byte)0x03, item.Type);
        Assert.Equal(10, item.Style);
        Assert.Equal(0xFF002080u, item.Colour(0));

        access.SetColour(item, 0, 0xFF123456);
        Assert.Equal(0xFF123456u, BitConverter.ToUInt32(bytes, 8));
        Assert.Equal(0xFFu, bytes[11]);
        access.SetStyle(Assert.Single(access.Read(england)), 7);
        Assert.Equal(7, BitConverter.ToInt32(bytes, 0x1C));

        TestWorld.Set64(holderBytes, 0x78, slots + 8);
        Assert.Empty(access.Read(england));
        Assert.Throws<StaleObjectException>(() => access.SetColour(item, 0, 0xFF000000));
    }
}

using Fm27Editor.Core.Players;
using Fm27Editor.Core.Stadiums;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Stadiums from FM27 RAM: [module + 0x56F5078] + 0x560 → stadium pointers (0xC0-byte objects, vtable 0x4DEDC18); stands
/// are eight 8-byte records behind +0x20. Bytes are Old Trafford's as dumped (pointers replaced); expected values are
/// FMRTE 27's Information and Stadium Details tabs (ID 680, capacity 74,879 … expansion 95,285, built 1/6/1909, rebuilt
/// 1/8/2006, pitch 105×68 / min 90×55 / max 105×68, condition 200, decay 6, recovery 16, Medium deterioration, Standard
/// environment, Club owner type; stands 20000/6600/11844/3300/8000/3300/14000/7000 = 74,044, tunnel only on the first).
/// The enum codes and flag bits were mapped by writing them and reading FMRTE back.
/// </summary>
public class StadiumTests
{
    const ulong Root = 0x7100_0000, Vector = 0x7100_1000, Ot = 0x7101_0000, Stands = 0x7102_0000, Name = 0x7103_0000;
    readonly TestWorld _w = new();

    public StadiumTests()
    {
        TestWorld.Set64(_w.Memory.Map(TestWorld.ModuleBase + 0x56F5078, 8), 0, Root);
        var r = _w.Memory.Map(Root, 0x570);
        TestWorld.Set64(r, 0x560, Vector); TestWorld.Set64(r, 0x568, Vector + 16);
        var v = _w.Memory.Map(Vector, 16);
        TestWorld.Set64(v, 0, TestWorld.Mainoo); // not a stadium: skipped
        TestWorld.Set64(v, 8, Ot);
        var o = _w.Memory.Map(Ot, 0xC0);
        Convert.FromHexString(
            "000000000000000047000000A8020000A80200000000000000000000000000000000000000000000B600E90701006C07" +
            "98007507D500D6079800750701006C07" + new string('0', 64) + new string('0', 32) +
            "2CDA554240A512C00000E1FF7F2401007F240100462201000422010035740100" +
            "00000000000000001A04A802FFFFFFFF840326021A04A802C800064002081003" +
            "0300000001008200FF00000000000000").CopyTo(o, 0);
        TestWorld.Set64(o, 0, TestWorld.ModuleBase + 0x4DEDC18);
        TestWorld.Set64(o, 0x20, Stands);
        TestWorld.Set64(o, 0x40, Name);
        Convert.FromHexString("204E00001E00121BC819000013001113442E00001A001113E40C00004D001013401F00004D000F13E40C00004D001213B03600001A001213581B000013001213")
            .CopyTo(_w.Memory.Map(Stands, 0x40), 0);
        var n = _w.Memory.Map(Name, 0x20);
        BitConverter.GetBytes(12).CopyTo(n, 0); "Old Trafford"u8.ToArray().CopyTo(n, 4);
    }

    StadiumRepository Repo() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);
    StadiumWriter Writer() => new(_w.Memory, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Reads_Old_Trafford_as_FMRTE_shows_it()
    {
        var s = Assert.Single(Repo().LoadAll());
        Assert.Equal((680, "Old Trafford"), (s.Id, s.Name));
        Assert.Equal(53.463060, s.Latitude, 4);
        Assert.Equal(-2.291336, s.Longitude, 4);
        var f = Writer().ReadFields(Ot);
        (StadiumField, long)[] expected =
        [
            (StadiumField.Capacity, 74879), (StadiumField.SeatingCapacity, 74879), (StadiumField.UsedCapacity, 74310),
            (StadiumField.AllSeaterCapacity, 74244), (StadiumField.ExpansionCapacity, 95285),
            (StadiumField.BuildDate, 19090601), (StadiumField.RebuildDate, 20060801), (StadiumField.PitchLastRelaid, 20250701),
            (StadiumField.PitchRelayRequired, 0),
            (StadiumField.PitchLength, 105), (StadiumField.PitchWidth, 68), (StadiumField.MinPitchLength, 90), (StadiumField.MinPitchWidth, 55),
            (StadiumField.MaxPitchLength, 105), (StadiumField.MaxPitchWidth, 68),
            (StadiumField.PitchCondition, 200), (StadiumField.Decay, 6), (StadiumField.PitchRecoveryRate, 16),
            (StadiumField.PitchDeteriorationRate, 2), (StadiumField.Environment, 3), (StadiumField.OwnerType, 1),
            (StadiumField.PitchType, 8), (StadiumField.UsedByNationalTeam, 0), (StadiumField.UsedByNationalU21Team, 0), (StadiumField.UsedByNationalU19Team, 0),
        ];
        Assert.Equal(Enum.GetValues<StadiumField>().Length, expected.Length);
        Assert.All(expected, e => Assert.Equal(e.Item2, f[e.Item1]));
        Assert.Equal(new[] { 20000, 6600, 11844, 3300, 8000, 3300, 14000, 7000 }, s.Stands.Select(t => t.Capacity));
        Assert.Equal(74044, s.StandsTotal);
        Assert.All(s.Stands, t => Assert.True(t.HasRoof && t.HasSeats));
        Assert.Equal([0], s.Stands.Where(t => t.HasTunnel).Select(t => t.Index));
        Assert.Equal(0x82, s.Flags); // Has Undersoil Heating, Not Used In Continental Final
        Assert.Equal(0x40, s.AltFlags); // Digital Advertising Boards ticked; Is Training Ground clear; Retractable Roof (+0xB6 bit 0x40) clear
    }

    /// <summary>Etihad Stadium's pitch/enum bytes (+0xA0..+0xB7) where neighbouring bytes differ (Old Trafford has 3 at
    /// both +0xAF and +0xB0); FMRTE: max 120×90, Decay 1, Recovery 18, Slow, High End, Council.</summary>
    [Fact]
    public void Etihad_enum_bytes_decode_to_FMRTE_values()
    {
        Assert.True(_w.Memory.TryWrite(Ot + 0xA0, Convert.FromHexString("84032602B0048403C80001600308120104000000020002")));
        var f = Writer().ReadFields(Ot);
        Assert.Equal((120L, 90L, 1L, 18L), (f[StadiumField.MaxPitchLength], f[StadiumField.MaxPitchWidth], f[StadiumField.Decay], f[StadiumField.PitchRecoveryRate]));
        Assert.Equal((3L, 4L, 2L), (f[StadiumField.PitchDeteriorationRate], f[StadiumField.Environment], f[StadiumField.OwnerType]));
    }

    [Fact]
    public void Writes_fields_flags_and_stands_and_keep_unknown_bits()
    {
        var s = Repo().LoadAll()[0];
        Writer().SetField(Ot, StadiumField.PitchLength, 105, 110);
        Assert.Equal(BitConverter.GetBytes((short)1100), _w.Bytes(Ot + 0x98, 2));
        Writer().SetFlag(s, StadiumFlag.IsCovered, true);
        Assert.Equal(0x83, _w.Bytes(Ot + 0xB6, 1)[0]);
        Writer().SetStand(s, 0, 21000, roof: false, seats: true, tunnel: true);
        Assert.Equal(BitConverter.GetBytes(21000), _w.Bytes(Stands, 4));
        Assert.Equal(0x0B, _w.Bytes(Stands + 7, 1)[0]); // 0x1B without the roof bit 0x10; bit 0x01 kept
        Assert.Throws<StaleObjectException>(() => Writer().SetStand(s, 0, 20000, true, true, true));
        Assert.Throws<StaleObjectException>(() => Writer().SetFlag(s, StadiumFlag.Extinct, true));
    }

    /// <summary>
    /// Wembley (ID 102969) as FMRTE 27 showed it before anything was written: Environment High End, Used By National Team All Matches
    /// (U21 and U19 No Matches), Owner Type empty, Pitch Type Grass/Synthetic Mix, condition 199, Slow, recovery 15, decay 1, min 90×55,
    /// max 110×73, Has Undersoil Heating only; Is Training Ground, Retractable Roof and Digital Advertising Boards unticked.
    /// </summary>
    [Fact]
    public void Wembley_bytes_decode_to_FMRTE_values()
    {
        Assert.True(_w.Memory.TryWrite(Ot + 0xA0, Convert.FromHexString("84032602" + "4C04DA02" + "C7000120" + "03080F01" + "04010000" + "00000200")));
        var f = Writer().ReadFields(Ot);
        Assert.Equal((8L, 1L, 0L, 0L), (f[StadiumField.PitchType], f[StadiumField.UsedByNationalTeam], f[StadiumField.UsedByNationalU21Team], f[StadiumField.UsedByNationalU19Team]));
        Assert.Equal((4L, 0L, 3L, 15L, 199L, 1L), (f[StadiumField.Environment], f[StadiumField.OwnerType], f[StadiumField.PitchDeteriorationRate],
            f[StadiumField.PitchRecoveryRate], f[StadiumField.PitchCondition], f[StadiumField.Decay]));
        var s = Repo().LoadAll()[0];
        Assert.Equal(0x02, s.Flags);
        Assert.Equal(0x20, s.AltFlags);        // 0x40 (Digital Advertising Boards) and 0x04 (Is Training Ground) are clear
    }

    /// <summary>FMRTE 27 on Old Trafford: ticking Is Training Ground changed +0xAB 0x40 → 0x44, ticking Retractable Roof changed +0xB6
    /// 0x82 → 0xC2, unticking Digital Advertising Boards changed +0xAB 0x44 → 0x04. Pitch Type Sand saved as 7, Grass as 1 (+0xAD only).</summary>
    [Fact]
    public void New_flags_and_pitch_type_write_the_bytes_FMRTE_wrote()
    {
        var s = Repo().LoadAll()[0];
        Writer().SetFlag(s, StadiumFlag.IsTrainingGround, true);
        Assert.Equal(0x44, _w.Bytes(Ot + 0xAB, 1)[0]);
        Assert.Equal(0x82, _w.Bytes(Ot + 0xB6, 1)[0]);
        s = Repo().LoadAll()[0];
        Writer().SetFlag(s, StadiumFlag.RetractableRoof, true);
        Assert.Equal(0xC2, _w.Bytes(Ot + 0xB6, 1)[0]);
        s = Repo().LoadAll()[0];
        Writer().SetFlag(s, StadiumFlag.DigitalBoards, false);
        Assert.Equal(0x04, _w.Bytes(Ot + 0xAB, 1)[0]);
        Assert.Throws<StaleObjectException>(() => Writer().SetFlag(s, StadiumFlag.IsTrainingGround, false)); // snapshot predates the change above

        var before = _w.Bytes(Ot, 0xC0);
        Writer().SetField(Ot, StadiumField.PitchType, 8, 7);
        var after = _w.Bytes(Ot, 0xC0);
        Assert.Equal(7, after[0xAD]);
        after[0xAD] = before[0xAD];
        Assert.Equal(before, after);
        Writer().SetField(Ot, StadiumField.UsedByNationalU21Team, 0, 6);
        Assert.Equal(6, _w.Bytes(Ot + 0xB2, 1)[0]);
        Assert.Equal(0, _w.Bytes(Ot + 0xB1, 1)[0]);
        Assert.Equal(0, _w.Bytes(Ot + 0xB3, 1)[0]);
    }

    /// <summary>
    /// Int. Length / Width: Old Trafford's box is unticked (FFFF FFFF). FMRTE ticking it on Wembley wrote 1A04 A802 (105 m × 68 m, the pitch
    /// size) at +0x9C / +0x9E and unticking restored FFFF FFFF. Seat Colour is red in FMRTE for both Old Trafford (+0x78 bytes 00 00 E1 FF)
    /// and Wembley (00 00 D7 FF), so the bytes are B, G, R, A.
    /// </summary>
    [Fact]
    public void International_size_and_seat_colour_read_like_FMRTE()
    {
        var s = Repo().LoadAll()[0];
        Assert.Null(s.IntLength);
        Assert.Null(s.IntWidth);
        Assert.Equal(0xFFu, s.SeatColour >> 24);
        Assert.True((s.SeatColour >> 16 & 0xFF) > 0xC0 && (s.SeatColour >> 8 & 0xFF) < 0x20 && (s.SeatColour & 0xFF) < 0x20);
        Assert.True(_w.Memory.TryWrite(Ot + 0x9C, Convert.FromHexString("1A04A802")));
        s = Repo().LoadAll()[0];
        Assert.Equal((short)1050, s.IntLength);
        Assert.Equal((short)680, s.IntWidth);
    }

    [Fact]
    public void SetInternationalSize_writes_both_int16_or_FFFF_and_guards()
    {
        var s = Repo().LoadAll()[0];
        var before = _w.Bytes(Ot, 0xC0);
        Writer().SetInternationalSize(s, (105, 68));
        Assert.Equal("1A04A802", Convert.ToHexString(_w.Bytes(Ot + 0x9C, 4)));
        var after = _w.Bytes(Ot, 0xC0);
        Array.Copy(before, 0x9C, after, 0x9C, 4);
        Assert.Equal(before, after);
        s = Repo().LoadAll()[0];
        Writer().SetInternationalSize(s, null);
        Assert.Equal("FFFFFFFF", Convert.ToHexString(_w.Bytes(Ot + 0x9C, 4)));
        s = Repo().LoadAll()[0];
        int writes = _w.Memory.Writes;
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetInternationalSize(s, (79, 68)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Writer().SetInternationalSize(s, (105, 101)));
        Assert.True(_w.Memory.TryWrite(Ot + 0x9E, BitConverter.GetBytes((short)700)));
        writes = _w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => Writer().SetInternationalSize(s, (105, 68)));
        Assert.Equal(writes, _w.Memory.Writes);
    }
}

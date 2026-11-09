using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>Offsets and encodings measured against FMRTE 27 on 9/11/2026.</summary>
public class InjuryTypeAccessTests
{
    const ulong TypeAddress = 0x7300_0000;
    readonly TestWorld w = new();
    readonly InjuryType type = new(TypeAddress, 65, "Concussion");
    readonly InjuryTypeAccess access;

    public InjuryTypeAccessTests()
    {
        var bytes = w.Memory.Map(TypeAddress, 0x78);
        TestWorld.Set64(bytes, 0, TestWorld.ModuleBase + w.Profile.Injuries!.TypeVtable);
        BitConverter.GetBytes(65).CopyTo(bytes, 8);
        access = new InjuryTypeAccess(w.Memory, w.Profile, TestWorld.ModuleBase);
    }

    [Theory]
    [InlineData(InjuryTypeField.MinimumDays, 258, 0x38, "0201")]
    [InlineData(InjuryTypeField.ExtraDays, 513, 0x3A, "0102")]
    [InlineData(InjuryTypeField.OccurrenceRatio, 1.5, 0x30, "0000C03F")]
    [InlineData(InjuryTypeField.MatchInjuryPercentage, 25, 0x68, "19")]
    [InlineData(InjuryTypeField.AgeRule, 2, 0x69, "02")]
    [InlineData(InjuryTypeField.Season, 3, 0x6A, "03")]
    [InlineData(InjuryTypeField.Contagious, 1, 0x6B, "01")]
    [InlineData(InjuryTypeField.Seriousness, 4, 0x6C, "04")]
    [InlineData(InjuryTypeField.GoalkeeperPercentage, -1, 0x6D, "FF")]
    [InlineData(InjuryTypeField.DefenderCentralPercentage, -1, 0x6E, "FF")]
    [InlineData(InjuryTypeField.Treatment, 3, 0x6F, "03")]
    [InlineData(InjuryTypeField.InactiveRatio, 42, 0x70, "2A")]
    [InlineData(InjuryTypeField.Recurring, 1, 0x71, "01")]
    public void EachFieldChangesOnlyTheFmrteBytes(InjuryTypeField field, double value, int offset, string expectedHex)
    {
        var original = w.Bytes(TypeAddress, 0x78);
        var snapshot = access.Read(type)!;
        access.Set(snapshot, field, value);
        Convert.FromHexString(expectedHex).CopyTo(original, offset);
        Assert.Equal(original, w.Bytes(TypeAddress, 0x78));
    }

    [Fact]
    public void RefusesOutOfRangeValuesAndStaleSnapshotsWithoutWriting()
    {
        var snapshot = access.Read(type)!;
        int writes = w.Memory.Writes;
        Assert.Throws<ArgumentOutOfRangeException>(() => access.Set(snapshot, InjuryTypeField.GoalkeeperPercentage, 101));
        Assert.Equal(writes, w.Memory.Writes);
        Assert.True(w.Memory.TryWrite(TypeAddress + 0x71, [1]));
        writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => access.Set(snapshot, InjuryTypeField.MinimumDays, 3));
        Assert.Equal(writes, w.Memory.Writes);
    }

    [Fact]
    public void ReadsFmrteTypeValuesIncludingSignedPercentages()
    {
        Assert.True(w.Memory.TryWrite(TypeAddress + 0x30, Convert.FromHexString("0000C03F")));
        Assert.True(w.Memory.TryWrite(TypeAddress + 0x38, Convert.FromHexString("02010102")));
        Assert.True(w.Memory.TryWrite(TypeAddress + 0x68, Convert.FromHexString("1902030104FFFE032A01")));
        var read = access.Read(type)!;
        Assert.Equal((258, 513, 1.5f, 25, 2, 3, true, 4, -1, -2, InjuryTreatment.Physio | InjuryTreatment.Specialist, 42, true),
            (read.MinimumDays, read.ExtraDays, read.OccurrenceRatio, read.MatchInjuryPercentage, read.AgeRule, read.Season,
             read.Contagious, read.Seriousness, read.GoalkeeperPercentage, read.DefenderCentralPercentage, read.Treatment, read.InactiveRatio, read.Recurring));
    }
}

using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>The fields of an injury type FMRTE 27's injury page edits (Cause is read-only there and so is the name).</summary>
public enum InjuryTypeField
{
    MinimumDays, ExtraDays, InactiveRatio, OccurrenceRatio, MatchInjuryPercentage, GoalkeeperPercentage, DefenderCentralPercentage,
    Seriousness, AgeRule, Season, Recurring, Contagious, Treatment,
}

/// <summary>An injury type with the values FMRTE's Injury page shows, as read from the type object.</summary>
public sealed record InjuryTypeDetails(ulong Address, int Id, int UniqueId, string? Name, int MinimumDays, int ExtraDays, int InactiveRatio,
    float OccurrenceRatio, int MatchInjuryPercentage, int GoalkeeperPercentage, int DefenderCentralPercentage, int Seriousness,
    int AgeRule, int Season, bool Recurring, bool Contagious, InjuryTreatment Treatment, string Snapshot)
{
    public int Value(InjuryTypeField field) => field switch
    {
        InjuryTypeField.MinimumDays => MinimumDays, InjuryTypeField.ExtraDays => ExtraDays, InjuryTypeField.InactiveRatio => InactiveRatio, InjuryTypeField.OccurrenceRatio => (int)OccurrenceRatio,
        InjuryTypeField.MatchInjuryPercentage => MatchInjuryPercentage, InjuryTypeField.GoalkeeperPercentage => GoalkeeperPercentage,
        InjuryTypeField.DefenderCentralPercentage => DefenderCentralPercentage, InjuryTypeField.Seriousness => Seriousness,
        InjuryTypeField.AgeRule => AgeRule, InjuryTypeField.Season => Season, InjuryTypeField.Recurring => Recurring ? 1 : 0,
        InjuryTypeField.Contagious => Contagious ? 1 : 0, InjuryTypeField.Treatment => (int)Treatment,
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };
}

/// <summary>
/// Reads and writes the injury type objects (size 0x78, vtable <c>Injuries.TypeVtable</c>), with the offsets measured against FMRTE 27
/// by changing one control at a time and diffing the object (9/11/2026): minimum days +0x38 and extra days +0x3A (uint16),
/// occurrence ratio float +0x30, match injury percentage +0x68, age rule +0x69 (1 over 28, 2 any), season +0x6A (1 any, 2 summer,
/// 3 winter), contagious +0x6B, seriousness +0x6C (0 invalid, 1 career threatening, 2 serious, 3 moderate, 4 minor, 5 walking wounded),
/// goalkeeper % +0x6D and central defender % +0x6E (signed byte, -1 = none), treatment bits +0x6F, inactive ratio +0x70, recurring +0x71.
/// </summary>
public sealed class InjuryTypeAccess(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int Size = 0x78, UniqueId = 0x0C, Occurrence = 0x30, Minimum = 0x38, Extra = 0x3A, MatchPercent = 0x68, AgeRuleAt = 0x69, SeasonAt = 0x6A,
        ContagiousAt = 0x6B, SeriousnessAt = 0x6C, GoalkeeperAt = 0x6D, DefenderAt = 0x6E, TreatmentAt = 0x6F, InactiveAt = 0x70, RecurringAt = 0x71;

    InjuryOffsets O => profile.Injuries ?? throw new InvalidOperationException("Profile has no injury offsets");

    public InjuryTypeDetails? Read(InjuryType type)
    {
        var o = O;
        var b = new byte[Size];
        if (!memory.TryRead(type.Address, b) || BitConverter.ToUInt64(b) != moduleBase + o.TypeVtable) return null;
        return new(type.Address, BitConverter.ToInt32(b, o.TypeId), BitConverter.ToInt32(b, UniqueId), type.Name, BitConverter.ToUInt16(b, Minimum),
            BitConverter.ToUInt16(b, Extra), b[InactiveAt], BitConverter.ToSingle(b, Occurrence), b[MatchPercent], (sbyte)b[GoalkeeperAt], (sbyte)b[DefenderAt],
            b[SeriousnessAt], b[AgeRuleAt], b[SeasonAt], b[RecurringAt] != 0, b[ContagiousAt] != 0, (InjuryTreatment)(b[TreatmentAt] & 0x0F), Convert.ToHexString(b));
    }

    /// <summary>Writes one field of the type. The whole object must still be what it was when read.</summary>
    public void Set(InjuryTypeDetails type, InjuryTypeField field, double value)
    {
        var o = O;
        var before = Convert.FromHexString(type.Snapshot);
        if (before.Length != Size || !memory.Read(type.Address, Size).AsSpan().SequenceEqual(before) ||
            BitConverter.ToUInt64(before) != moduleBase + o.TypeVtable)
            throw new StaleObjectException($"The injury type '{type.Name}' changed since it was read");
        var after = (byte[])before.Clone();
        int v = (int)Math.Round(value);
        switch (field)
        {
            case InjuryTypeField.MinimumDays: Range(v, 0, 65535, field); BitConverter.GetBytes((ushort)v).CopyTo(after, Minimum); break;
            case InjuryTypeField.ExtraDays: Range(v, 0, 65535, field); BitConverter.GetBytes((ushort)v).CopyTo(after, Extra); break;
            case InjuryTypeField.InactiveRatio: Range(v, 0, 255, field); after[InactiveAt] = (byte)v; break;
            case InjuryTypeField.OccurrenceRatio:
                if (value < 0 || value > 1000 || double.IsNaN(value)) throw new ArgumentOutOfRangeException(nameof(value), "Occurrence ratio is 0 to 1000");
                BitConverter.GetBytes((float)value).CopyTo(after, Occurrence); break;
            case InjuryTypeField.MatchInjuryPercentage: Range(v, 0, 100, field); after[MatchPercent] = (byte)v; break;
            case InjuryTypeField.GoalkeeperPercentage: Range(v, -1, 100, field); after[GoalkeeperAt] = (byte)(sbyte)v; break;
            case InjuryTypeField.DefenderCentralPercentage: Range(v, -1, 100, field); after[DefenderAt] = (byte)(sbyte)v; break;
            case InjuryTypeField.Seriousness: Range(v, 0, 5, field); after[SeriousnessAt] = (byte)v; break;
            case InjuryTypeField.AgeRule: Range(v, 1, 2, field); after[AgeRuleAt] = (byte)v; break;
            case InjuryTypeField.Season: Range(v, 1, 3, field); after[SeasonAt] = (byte)v; break;
            case InjuryTypeField.Recurring: Range(v, 0, 1, field); after[RecurringAt] = (byte)v; break;
            case InjuryTypeField.Contagious: Range(v, 0, 1, field); after[ContagiousAt] = (byte)v; break;
            case InjuryTypeField.Treatment: Range(v, 0, 15, field); after[TreatmentAt] = (byte)((before[TreatmentAt] & ~0x0F) | v); break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
        // Only the changed bytes are written.
        for (int i = 0; i < Size; i++)
        {
            if (after[i] == before[i]) continue;
            int start = i;
            while (i + 1 < Size && after[i + 1] != before[i + 1]) i++;
            var chunk = after[start..(i + 1)];
            if (!memory.TryWrite(type.Address + (ulong)start, chunk) || !memory.Read(type.Address + (ulong)start, chunk.Length).AsSpan().SequenceEqual(chunk))
                throw new MemoryAccessException($"Could not write the injury type at +0x{start:X}");
        }
    }

    static void Range(int value, int min, int max, InjuryTypeField field)
    {
        if (value < min || value > max) throw new ArgumentOutOfRangeException(nameof(value), $"{field} is {min} to {max}");
    }
}

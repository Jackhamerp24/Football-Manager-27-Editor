using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Competitions;

/// <summary>
/// Read-only competition details from the CE table's cmpo layout (FMCET26 update 8, FM26 26.3.2):
/// three-letter name, type, level, trophy, official category, continental positions, flags.
/// Out-of-range values stay null so the UI reads unknown rather than a guess. There are no writes.
/// </summary>
public sealed record CompetitionDetails(
    CompetitionRecord Competition,
    string? ThreeLetterName,
    byte? Type,
    byte? CompetitionLevel,
    byte? Trophy,
    sbyte? LowestOfficialCategory,
    short? ContinentalPosition,
    short? LastContinentalPosition,
    byte? UsesExtraOfficials,
    byte? SeatedStadiumsOnly,
    byte? Flags)
{
    public string? TypeName => Type is { } t ? CompetitionDetailNames.Type(t) : null;
    public string? TrophyName => Trophy is { } t ? CompetitionDetailNames.Trophy(t) : null;
    public string? LowestOfficialCategoryName =>
        LowestOfficialCategory is { } c ? CompetitionDetailNames.LowestOfficialCategory(c) : null;
    // NOTE: CE labels bit 0 "Uses Squad Numbers" and bit 2 "Is Extinct", but on FM27 bit 2 is set on
    // active leagues (Premier League reads 0x06), so the bit positions are unverified and not decoded.
    // Flags stays raw for future measurement; nothing is displayed from it.
}

/// <summary>
/// Names for the detail enums, from the CE table's competition entries (IDs 992, 12593, 1314).
/// Type indices run 0-29 then 31-39 (30 is unmapped); Trophy 0-11; Lowest Official Category -1-5.
/// </summary>
public static class CompetitionDetailNames
{
    public static string? Type(byte value) => value switch
    {
        0 => "Domestic Top Division",
        1 => "Domestic Division",
        2 => "Domestic Main Cup",
        3 => "Domestic League Cup",
        4 => "Domestic Cup",
        5 => "Super Cup",
        6 => "International Finals",
        7 => "International Qualifiers",
        8 => "Reserve Division",
        9 => "Club Friendly",
        10 => "Main Continental International Finals",
        11 => "Main Continental International Qualifiers",
        12 => "Club Finals",
        13 => "Friendly",
        14 => "Domestic Other Division",
        15 => "U21 International Finals",
        16 => "U21 International Qualifiers",
        17 => "U19 International Finals",
        18 => "U19 International Qualifiers",
        19 => "U23 International Finals",
        20 => "U23 International Qualifiers",
        21 => "Club Champions Cup",
        22 => "Cup Winners Cup",
        23 => "Reserve Cup",
        24 => "International Friendly",
        25 => "U20 International Finals",
        26 => "All Stars Cup",
        27 => "U20 International Qualifiers",
        28 => "Inactive Competition",
        29 => "Inactive Other Competition",
        31 => "U22 International Finals",
        32 => "U22 International Qualifiers",
        33 => "U23 Division",
        34 => "U22 Division",
        35 => "U21 Division",
        36 => "U20 Division",
        37 => "U19 Division",
        38 => "U18 Division",
        39 => "Youth Cup",
        _ => null,
    };

    public static string? Trophy(byte value) => value switch
    {
        0 => "Not set",
        1 => "Silver Plate",
        2 => "National Cup 3",
        3 => "National Cup 2",
        4 => "National Cup 1",
        5 => "National Championship 3",
        6 => "National Championship 2",
        7 => "National Championship 1",
        8 => "International Cup 2",
        9 => "International Cup 1",
        10 => "Continental Cup 2",
        11 => "Continental Cup 1",
        _ => null,
    };

    public static string? LowestOfficialCategory(sbyte value) => value switch
    {
        -1 => "Not Available",
        0 => "None",
        1 => "European Elite Group",
        2 => "European Elite Development Group",
        3 => "European First Group",
        4 => "European Second Group",
        5 => "European Third Group",
        _ => null,
    };
}

/// <summary>
/// Reads the detail fields with the same identity checks as the other competition readers, plus a
/// reputation anchor: the int16 at the profile's Reputation offset must equal the record's reputation,
/// which proves the object layout still lines up. Values outside the CE table's ranges stay null so the
/// UI reads unknown; names must decode as short text and positions must fit a ranking.
/// </summary>
public sealed class CompetitionDetailsAccess(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxName = 12;

    public CompetitionDetails? Read(CompetitionRecord competition)
    {
        var c = profile.Competition;
        if (c?.Id is not { } id || c.ThreeLetterName is not { } name || c.Type is not { } type ||
            c.UsesExtraOfficials is not { } extra || c.SeatedStadiumsOnly is not { } seated ||
            c.LastContinentalPosition is not { } last || c.ContinentalPosition is not { } position ||
            c.CompetitionLevel is not { } level || c.Trophy is not { } trophy ||
            c.LowestOfficialCategory is not { } category || c.Flags is not { } flags ||
            c.Reputation is not { } rep)
            return null;
        try
        {
            ulong at = competition.Address;
            if (memory.ReadUInt64(at) != moduleBase + c.VtableRva ||
                BitConverter.ToInt32(memory.Read(at + (ulong)id, 4)) != competition.Id)
                return null;
            if (BitConverter.ToInt16(memory.Read(at + (ulong)rep, 2)) != competition.Reputation)
                return null;
            return new(competition,
                ReadName(at + (ulong)name),
                ReadByte(at + (ulong)type, 0, 39),
                ReadByte(at + (ulong)level, 0, 32),
                ReadByte(at + (ulong)trophy, 0, 11),
                ReadSByte(at + (ulong)category, -1, 5),
                ReadPosition(at + (ulong)position),
                ReadPosition(at + (ulong)last),
                ReadByte(at + (ulong)extra, 0, 1),
                ReadByte(at + (ulong)seated, 0, 1),
                memory.Read(at + (ulong)flags, 1)[0]);
        }
        catch (MemoryAccessException) { return null; }
    }

    byte? ReadByte(ulong at, int min, int max)
    {
        byte value = memory.Read(at, 1)[0];
        return value >= min && value <= max ? value : null;
    }

    sbyte? ReadSByte(ulong at, int min, int max)
    {
        sbyte value = (sbyte)memory.Read(at, 1)[0];
        return value >= min && value <= max ? value : null;
    }

    short? ReadPosition(ulong at)
    {
        short value = BitConverter.ToInt16(memory.Read(at, 2));
        return value is >= -1 and <= 512 ? value : null;
    }

    string? ReadName(ulong field)
    {
        ulong at = memory.ReadUInt64(field);
        if (at == 0) return null;
        int n = BitConverter.ToInt32(memory.Read(at, 4));
        if (n < 1 || n > MaxName) return null;
        string text = Encoding.UTF8.GetString(memory.Read(at + 4, n));
        return text.Length == n && text.All(ch => ch is >= ' ' and not '\u007f') ? text : null;
    }
}

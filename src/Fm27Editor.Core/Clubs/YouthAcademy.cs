using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>
/// Club youth infrastructure, the four 1–20 values FMRTE's Facilities tab shows. They sit behind club +0x100:
/// YouthImportance +0x126, YouthFacilities +0x12B, YouthCoaching +0x12C, YouthRecruitment +0x12D (Man Utd read
/// 19/18/18/18 on 8/11/2026). FM Studio's MaximizeYouthInfrastructure writes 20 to each of the four.
/// </summary>
public sealed record YouthInfrastructure(int Facilities, int Coaching, int Recruitment, int Importance)
{
    public const int Max = 20;
    public bool IsMaximal => Facilities == Max && Coaching == Max && Recruitment == Max && Importance == Max;
}

/// <summary>Reads and writes the four youth values through the guarded club-field mapping.</summary>
public sealed class YouthAcademy(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    static readonly ClubField[] Fields =
        [ClubField.YouthFacilities, ClubField.YouthCoaching, ClubField.YouthRecruitment, ClubField.YouthImportance];

    /// <summary>Null when the club does not carry all four values (e.g. a broken 0x100 pointer or wrong object).</summary>
    public YouthInfrastructure? Read(ulong clubAddress)
    {
        var access = new ClubFieldAccess(memory, profile, moduleBase);
        var v = new int?[4];
        for (int i = 0; i < 4; i++) v[i] = (int?)access.Read(clubAddress, Fields[i]);
        if (v.Any(x => x is null)) return null;
        return new(v[0]!.Value, v[1]!.Value, v[2]!.Value, v[3]!.Value);
    }

    /// <summary>Sets the four values (0–20 each); false when the club already holds them. Each write is guarded and read back.</summary>
    public bool Set(ulong clubAddress, YouthInfrastructure value)
    {
        int[] wanted = [value.Facilities, value.Coaching, value.Recruitment, value.Importance];
        if (wanted.Any(x => x is < 0 or > YouthInfrastructure.Max)) throw new ArgumentOutOfRangeException(nameof(value), "Youth values are 0–20");
        var current = Read(clubAddress) ?? throw new StaleObjectException("The club does not carry the four youth values");
        int[] before = [current.Facilities, current.Coaching, current.Recruitment, current.Importance];
        if (before.SequenceEqual(wanted)) return false;
        var access = new ClubFieldAccess(memory, profile, moduleBase);
        for (int i = 0; i < 4; i++)
            if (before[i] != wanted[i]) access.Set(clubAddress, Fields[i], before[i], wanted[i]);
        return true;
    }

    /// <summary>FM Studio's MaximizeYouthInfrastructure: all four to 20.</summary>
    public bool Maximize(ulong clubAddress) =>
        Set(clubAddress, new(YouthInfrastructure.Max, YouthInfrastructure.Max, YouthInfrastructure.Max, YouthInfrastructure.Max));
}

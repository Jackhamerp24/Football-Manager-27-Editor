using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Nations;

/// <summary>
/// FMRTE 27's EuroCoefficients block on the nation's National Teams page: ten floats per nation, ordered oldest season
/// first. Measured on Spain (796): 19.714 / 19.571 / 18.928 / 19.5 / 18.428 / 16.571 / 16.062 / 23.892 / 22.093 / 0 for
/// seasons 2017/18 … 2026/27, the vector sitting at team+0x190 (nation+0x108 → team).
/// </summary>
public sealed class NationEuroCoefficients(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    /// <summary>Vector offset inside the nation's preferences team object.</summary>
    public const int Vector = 0x190;

    /// <summary>Seasons FMRTE shows; the game keeps the same count for every nation measured.</summary>
    public const int Count = 10;

    public IReadOnlyList<float> Read(ulong nationAddress)
    {
        if (profile.Nation?.Preferences is not { } l) return [];
        if (!IsNation(nationAddress)) return [];
        ulong team = memory.ReadUInt64(nationAddress + (ulong)l.Team);
        if (team == 0 || memory.ReadUInt64(team) != moduleBase + l.TeamVtable) return [];
        ulong begin = memory.ReadUInt64(team + Vector), end = memory.ReadUInt64(team + Vector + 8),
            capacity = memory.ReadUInt64(team + Vector + 16);
        if (begin == 0 || end < begin || capacity < end || (end - begin) % 4 != 0
            || (capacity - begin) % 4 != 0 || (end - begin) / 4 > 32) return [];
        int count = (int)(end - begin) / 4;
        var bytes = memory.Read(begin, count * 4);
        var values = new float[count];
        for (int i = 0; i < count; i++) values[i] = BitConverter.ToSingle(bytes, i * 4);
        return values;
    }

    /// <summary>FMRTE's row labels: the current season last, older seasons before it, e.g. 2017/2018 … 2026/2027.</summary>
    public static IReadOnlyList<string> Labels(int currentSeasonStart, int count)
    {
        var labels = new string[count];
        for (int i = 0; i < count; i++)
        {
            int year = currentSeasonStart - (count - 1 - i);
            labels[i] = $"Season {year}/{year + 1}";
        }
        return labels;
    }

    bool IsNation(ulong address) =>
        address != 0 && profile.Nation is { } n && memory.TryRead(address, new byte[8]) && memory.ReadUInt64(address) == moduleBase + n.VtableRva;
}

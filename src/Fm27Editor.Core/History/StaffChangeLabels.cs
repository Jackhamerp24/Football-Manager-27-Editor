using Fm27Editor.Core.Staff;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.History;

/// <summary>Names the bytes of a staff member for the edit history: attributes, manager attributes, personality, ability, reputation.</summary>
public static class StaffChangeLabels
{
    public static Func<ulong, int, ChangeLabel?>? For(OffsetProfile profile, StaffRecord staff)
    {
        if (profile.Staff is not { } s) return null;
        var map = new Dictionary<ulong, ChangeLabel>();
        foreach (var (attribute, (offset, scaled)) in s.Attributes)
            map[staff.StaffAddress + (ulong)offset] = new(PlayerChangeLabels.Words(attribute.ToString()), scaled ? Math.Max(1, profile.AttributeScale) : 1);
        if (staff.Kind.Manager)
            foreach (var (attribute, offset) in s.ManagerAttributes)
                map[staff.StaffAddress + (ulong)offset] = new(PlayerChangeLabels.Words(attribute.ToString()));
        foreach (var trait in Enum.GetValues<StaffPersonality>())
            map[staff.Address + (ulong)(s.Personality + (int)trait)] = new(PlayerChangeLabels.Words(trait.ToString()));
        map[staff.StaffAddress + (ulong)s.CurrentAbility] = new("Current / Potential Ability", 1, 2);
        map[staff.StaffAddress + (ulong)s.HomeReputation] = new("Home / Current / World Reputation", 1, 3);
        if (s.Formations is { } formations) map[staff.StaffAddress + (ulong)formations] = new("Formations (4 in / 4 out of possession)", 1, 8);
        return (address, _) => map.GetValueOrDefault(address);
    }

    /// <summary>Knowledge rows are 40-byte records; a write inside one is that nation's knowledge.</summary>
    public static Func<ulong, int, ChangeLabel?> ForKnowledge(IEnumerable<StaffKnowledgeRow> rows)
    {
        var list = rows.ToList();
        return (address, _) => list.FirstOrDefault(r => address >= r.Address && address < r.Address + 40) is { } row
            ? new ChangeLabel($"Knowledge of {row.Nation}") : null;
    }

    /// <summary>One tendency row (type byte, level byte): the row is named after the type it holds.</summary>
    public static Func<ulong, int, ChangeLabel?> ForTendencies(IEnumerable<StaffTendency> rows)
    {
        var map = new Dictionary<ulong, ChangeLabel>();
        foreach (var row in rows)
        {
            string name = StaffTendencies.TypeName(row.Type);
            map[row.Address] = new($"Tendency type ({name})");
            map[row.Address + 1] = new($"Tendency level ({name})");
        }
        return (address, _) => map.GetValueOrDefault(address);
    }
}

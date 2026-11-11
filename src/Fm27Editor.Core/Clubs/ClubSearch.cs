using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Clubs;

/// <summary>Criteria for the Club scope of Advanced Search; every set value must match.</summary>
public sealed record ClubSearchCriteria(
    string? NameContains = null,
    IReadOnlyDictionary<ClubField, (long Min, long Max)>? Ranges = null,
    Gender? Gender = null);

public static class ClubSearch
{
    public static bool Matches(ClubEntry club, ClubSearchCriteria criteria, Func<ClubField, long?> read)
    {
        if (criteria.Gender is { } gender && club.Gender != gender) return false;
        if (!string.IsNullOrWhiteSpace(criteria.NameContains))
        {
            string query = criteria.NameContains.Trim();
            bool match = club.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (club.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (club.ShortName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!match) return false;
        }
        foreach (var (field, range) in criteria.Ranges ?? new Dictionary<ClubField, (long, long)>())
        {
            long? value = read(field);
            if (value is not { } v || v < range.Min || v > range.Max) return false;
        }
        return true;
    }
}

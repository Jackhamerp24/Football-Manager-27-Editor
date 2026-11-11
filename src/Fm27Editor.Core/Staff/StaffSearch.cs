using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Staff;

/// <summary>Criteria for the Coach scope of Advanced Search; every set value must match.</summary>
public sealed record StaffSearchCriteria(
    string? NameContains = null,
    string? Nationality = null,
    short? JobCode = null,
    int? MinAge = null,
    int? MaxAge = null,
    int? MinCa = null,
    int? MaxCa = null,
    int? MinPa = null,
    int? MaxPa = null,
    IReadOnlyDictionary<StaffAttribute, (int Min, int Max)>? AttributeRanges = null,
    string? ClubContains = null,
    int? MinHomeReputation = null,
    int? MaxHomeReputation = null,
    int? MinCurrentReputation = null,
    int? MaxCurrentReputation = null,
    int? MinWorldReputation = null,
    int? MaxWorldReputation = null,
    Gender? Gender = null,
    bool ExcludeWomenClubs = false);

public static class StaffSearch
{
    public static bool Matches(StaffRecord staff, StaffSearchCriteria criteria, DateOnly today)
    {
        if (criteria.Gender is { } gender && staff.Gender != gender) return false;
        if (criteria.ExcludeWomenClubs && staff.Club?.Gender == Gender.Women) return false;
        if (!string.IsNullOrWhiteSpace(criteria.NameContains) &&
            !staff.DisplayName.Contains(criteria.NameContains.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(criteria.Nationality) &&
            !string.Equals(staff.Nationality?.DisplayName, criteria.Nationality, StringComparison.OrdinalIgnoreCase)) return false;
        if (criteria.JobCode is { } job && staff.JobCode != job) return false;
        if (criteria.MinAge is not null || criteria.MaxAge is not null)
        {
            if (staff.DateOfBirth is not { } born) return false;
            int age = today.Year - born.Year;
            if (born.AddYears(age) > today) age--;
            if (criteria.MinAge is { } lo && age < lo) return false;
            if (criteria.MaxAge is { } hi && age > hi) return false;
        }
        if (!string.IsNullOrWhiteSpace(criteria.ClubContains))
        {
            string query = criteria.ClubContains.Trim();
            bool match = (staff.Club?.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (staff.Club?.ShortName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
            if (!match) return false;
        }
        if (criteria.MinCa is { } minCa && staff.CurrentAbility < minCa) return false;
        if (criteria.MaxCa is { } maxCa && staff.CurrentAbility > maxCa) return false;
        if (criteria.MinPa is { } minPa && staff.PotentialAbility < minPa) return false;
        if (criteria.MaxPa is { } maxPa && staff.PotentialAbility > maxPa) return false;
        if (criteria.MinHomeReputation is { } minHome && staff.HomeReputation < minHome) return false;
        if (criteria.MaxHomeReputation is { } maxHome && staff.HomeReputation > maxHome) return false;
        if (criteria.MinCurrentReputation is { } minCurrent && staff.CurrentReputation < minCurrent) return false;
        if (criteria.MaxCurrentReputation is { } maxCurrent && staff.CurrentReputation > maxCurrent) return false;
        if (criteria.MinWorldReputation is { } minWorld && staff.WorldReputation < minWorld) return false;
        if (criteria.MaxWorldReputation is { } maxWorld && staff.WorldReputation > maxWorld) return false;
        foreach (var (attribute, range) in criteria.AttributeRanges ?? new Dictionary<StaffAttribute, (int, int)>())
        {
            int value = staff.Attribute(attribute);
            if (value < range.Min || value > range.Max) return false;
        }
        return true;
    }
}

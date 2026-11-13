using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

public sealed record TalentedRegenCriteria(
    int MinPotentialAbility = 150,
    int MaxAge = 23,
    bool FreeAgentsOnly = false,
    Gender? Gender = null,
    int? MinCurrentAbility = null,
    string? Query = null);

public sealed record TalentedRegenMatch(
    PlayerRecord Player,
    PersonalityClassification Personality,
    RoleScore BestRole,
    int Rca);

public sealed record TalentedRegensResult(
    IReadOnlyList<TalentedRegenMatch> Matches,
    bool RegenIdentificationUnavailable,
    int? RegenIdFloor);

/// <summary>
/// Filters loaded players for talented regens/newgens matching FMCET Database / Talented Regens criteria.
/// Uses <see cref="RegenIds.IsRegen(PlayerRecord)"/> so non-regen IDs are never guessed when the floor is unknown.
/// </summary>
public static class TalentedRegensQuery
{
    public static TalentedRegensResult Find(
        IEnumerable<PlayerRecord> players,
        TalentedRegenCriteria criteria,
        int? regenIdFloorOverride = null)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(criteria);

        int? floor = regenIdFloorOverride ?? RegenIds.Current;
        if (floor is not { } validFloor || validFloor <= 0)
            return new TalentedRegensResult([], RegenIdentificationUnavailable: true, RegenIdFloor: null);

        string? needle = string.IsNullOrWhiteSpace(criteria.Query) ? null : criteria.Query.Trim();
        var matches = new List<TalentedRegenMatch>();

        foreach (var p in players)
        {
            if (criteria.Gender is { } g && p.Gender != g) continue;

            bool isRegen = regenIdFloorOverride is { } explicitFloor
                ? p.Id is { } id && id >= explicitFloor
                : RegenIds.IsRegen(p) == true;
            if (!isRegen) continue;

            if (p.PotentialAbility < criteria.MinPotentialAbility) continue;
            if (criteria.MinCurrentAbility is { } minCa && p.CurrentAbility < minCa) continue;
            if (p.Age is not { } age || age > criteria.MaxAge) continue;
            if (criteria.FreeAgentsOnly && p.Contract is not null) continue;

            if (needle is not null)
            {
                bool hit = p.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || p.ClubName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || p.NationalityName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || p.PositionSummary.Contains(needle, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
            }

            var personality = PersonalityClassifier.Classify(p, isRegenOverride: true);
            var bestRole = RoleScores.BestFitRole(p);
            int rca = RcaCalculator.Calculate(p);
            matches.Add(new TalentedRegenMatch(p, personality, bestRole, rca));
        }

        matches.Sort(static (a, b) =>
        {
            int pa = b.Player.PotentialAbility.CompareTo(a.Player.PotentialAbility);
            if (pa != 0) return pa;
            int ca = b.Player.CurrentAbility.CompareTo(a.Player.CurrentAbility);
            if (ca != 0) return ca;
            return (a.Player.Age ?? 99).CompareTo(b.Player.Age ?? 99);
        });

        return new TalentedRegensResult(matches, RegenIdentificationUnavailable: false, RegenIdFloor: validFloor);
    }
}

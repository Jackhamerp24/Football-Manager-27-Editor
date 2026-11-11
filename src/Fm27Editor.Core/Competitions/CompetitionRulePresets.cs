namespace Fm27Editor.Core.Competitions;

/// <summary>One planned permissive edit: the validated snapshot plus the value that lifts the restriction.</summary>
public sealed record RulePresetEdit(int Code, CompetitionSquadRules Snapshot, int Value);

/// <summary>Why a rule code was left out of a preset plan; never silently skipped.</summary>
public sealed record RulePresetSkip(int Code, string Reason);

/// <summary>What a rule preset would do: guarded edits plus the codes it refused with reasons.</summary>
public sealed record RulePresetPlan(IReadOnlyList<RulePresetEdit> Edits, IReadOnlyList<RulePresetSkip> Skipped);

/// <summary>
/// Rule-lifting presets in the spirit of the CE table's unlockables, built on the validated rule engine
/// instead of byte patches: only rules with a single validated snapshot are touched, minimum-type rules
/// are never raised, and every refusal carries a reason. There are no writes here; applying goes through
/// <see cref="CompetitionSquadRulesAccess.SetMany"/>, which re-checks every copy before writing.
/// </summary>
public static class CompetitionRulePresets
{
    /// <summary>Foreign-player maximums (CE selectionRulesTypes): playing 11, bench, match squad and squad.</summary>
    public static readonly int[] ForeignCodes = [0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x12, 0x15];

    /// <summary>Loan rules (CE selectionRulesTypes): season, squad and match-squad limits.</summary>
    public static readonly int[] LoanCodes = [0x0C, 0x0D, 0x19, 0x1A, 0x1B, 0x3E, 0xCF];

    /// <summary>
    /// Plans "Nationality Restrictions Lifted": every foreign-player maximum present in the group goes up
    /// to the squad maximum, so the squad size is the only effective cap.
    /// </summary>
    public static RulePresetPlan LiftNationality(CompetitionRecord competition, CompetitionSquadRulesAccess access,
        IReadOnlyList<CompetitionRecord>? peers = null) => Plan(competition, access, ForeignCodes, peers);

    /// <summary>
    /// Plans "No Loan Restrictions": every loan maximum present in the group goes up to the squad maximum.
    /// </summary>
    public static RulePresetPlan LiftLoans(CompetitionRecord competition, CompetitionSquadRulesAccess access,
        IReadOnlyList<CompetitionRecord>? peers = null) => Plan(competition, access, LoanCodes, peers);

    static RulePresetPlan Plan(CompetitionRecord competition, CompetitionSquadRulesAccess access,
        IReadOnlyList<int> codes, IReadOnlyList<CompetitionRecord>? peers)
    {
        var edits = new List<RulePresetEdit>();
        var skipped = new List<RulePresetSkip>();
        var present = access.RuleCodes(competition).ToHashSet();
        var maximum = access.Read(competition, peers);
        if (maximum is null)
        {
            foreach (int code in codes.Where(present.Contains))
                skipped.Add(new(code, "no validated squad maximum to lift to"));
            foreach (int code in codes.Where(c => !present.Contains(c)))
                skipped.Add(new(code, "competition has no such rule"));
            return new(edits, skipped);
        }
        int lift = maximum.MaximumSquadSize;
        foreach (int code in codes)
        {
            if (!present.Contains(code)) { skipped.Add(new(code, "competition has no such rule")); continue; }
            var snapshot = access.ReadRule(competition, code, null, peers);
            if (snapshot is null) { skipped.Add(new(code, "no single validated value")); continue; }
            if (snapshot.IsMinimum == true) { skipped.Add(new(code, "minimum-type rules are never raised")); continue; }
            if (snapshot.Sizes.Contains(1) && lift > byte.MaxValue) { skipped.Add(new(code, "lift value does not fit the stored size")); continue; }
            if (snapshot.MaximumSquadSize >= lift) { skipped.Add(new(code, "already at or above the squad maximum")); continue; }
            edits.Add(new(code, snapshot, lift));
        }
        return new(edits, skipped);
    }
}

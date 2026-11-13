namespace Fm27Editor.Core.Staff;

public sealed record StaffRcaResult(
    int? Score,
    StaffRole? PrimaryRole,
    bool HasUnavailableDataAnalystInput = false,
    string? UnavailableReason = null);

/// <summary>
/// Staff Recommended Current Ability (RCA) derived from FMCET getNonPlayerRCA.
/// In FM27 the Data Analyst input (FM26 single AnalysingData attribute) was split into JudgingPlayerData,
/// JudgingTeamData and PresentingData; rather than guessing weights, staff with DataAnalyst &gt;= 15 report unavailable.
/// </summary>
public static class StaffRcaCalculator
{
    static readonly double[][] JobWeights =
    [
        /*  0 */ [3, 3, 0, 0, 3, 0, 3, 3, 1, 0, 0, 3, 4, 4, 4, 3, 3, 3, 4, 0, 0, 0, 0, 0, 0, 0],
        /*  1 */ [2, 2, 0, 0, 2, 0, 2, 2, 1, 0, 0, 3, 4, 4, 4, 3, 1, 1, 3, 0, 0, 0, 0, 0, 0, 0],
        /*  2 */ [4, 4, 1, 0, 4, 0, 4, 4, 1, 0, 0, 2, 2, 1, 3, 3, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0],
        /*  3 */ [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 2, 0, 0, 2, 1, 0, 0, 4, 2, 0, 0, 0, 0],
        /*  4 */ [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 4, 4, 2, 1, 4, 0, 0, 0, 0, 0, 0, 0],
        /*  5 */ [0, 0, 0, 4, 2, 0, 0, 0, 1, 0, 0, 2, 3, 2, 2, 2, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0],
        /*  6 */ [0, 0, 4, 0, 0, 0, 0, 0, 1, 0, 0, 2, 3, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        /*  7 */ [0, 0, 0, 0, 0, 4, 2, 1, 1, 0, 0, 2, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        /*  8 */ [3, 3, 0, 0, 3, 0, 3, 3, 1, 0, 0, 3, 4, 4, 4, 3, 3, 3, 4, 0, 0, 0, 0, 0, 0, 0],
        /*  9 */ [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 4, 4, 4, 4, 4, 1, 0, 0, 0, 2, 0, 0, 0],
        /* 10 */ [2, 2, 1, 0, 2, 2, 2, 2, 4, 0, 0, 2, 2, 2, 4, 4, 3, 2, 1, 0, 0, 0, 0, 0, 0, 0],
        /* 11 */ [0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 2, 3, 4, 4, 4, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0],
        /* 12 */ [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 3, 0, 0, 0, 2, 4, 0, 0, 0, 0, 0, 0],
        /* 13 */ [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 2, 0, 0, 0, 0, 0, 0, 2, 4, 0, 0, 0, 0],
        /* 14 */ [2, 2, 1, 0, 2, 0, 2, 2, 1, 0, 0, 2, 2, 3, 3, 3, 4, 4, 1, 0, 0, 0, 0, 0, 0, 0],
    ];

    public static StaffRcaResult Calculate(StaffRecord staff)
    {
        ArgumentNullException.ThrowIfNull(staff);
        if (staff.Roles is null || staff.Roles.Length < 15)
            return new StaffRcaResult(null, null, false, "Role ratings unavailable on this staff record");

        if (staff.Roles[(int)StaffRole.DataAnalyst] >= 15)
        {
            return new StaffRcaResult(
                null,
                StaffRole.DataAnalyst,
                true,
                "Unavailable: Data Analyst RCA requires FM26's single Analysing Data attribute (split into 3 in FM27)");
        }

        double Attr(StaffAttribute a) =>
            (int)a < staff.Attributes.Length ? Scale20(staff.Attributes[(int)a]) : 0;
        double Pers(StaffPersonality p) =>
            (int)p < staff.Personality.Length ? Scale20(staff.Personality[(int)p]) : 0;

        Span<double> attrs = stackalloc double[26];
        attrs[0]  = Attr(StaffAttribute.CoachingAttacking);
        attrs[1]  = Attr(StaffAttribute.CoachingDefending);
        attrs[2]  = Attr(StaffAttribute.CoachingFitness);
        attrs[3]  = Attr(StaffAttribute.CoachingGoalkeeping);
        attrs[4]  = Attr(StaffAttribute.CoachingPossession);
        attrs[5]  = Attr(StaffAttribute.SetPieces);
        attrs[6]  = Attr(StaffAttribute.CoachingTactical);
        attrs[7]  = Attr(StaffAttribute.CoachingTechnical);
        attrs[8]  = Attr(StaffAttribute.WorkingWithYoungsters);
        attrs[9]  = Pers(StaffPersonality.Adaptability);
        attrs[10] = Attr(StaffAttribute.Authority);
        attrs[11] = Attr(StaffAttribute.Determination);
        attrs[12] = Attr(StaffAttribute.Motivating);
        attrs[13] = Attr(StaffAttribute.PeopleManagement);
        attrs[14] = Attr(StaffAttribute.JudgingPlayerAbility);
        attrs[15] = Attr(StaffAttribute.JudgingPlayerPotential);
        attrs[16] = Attr(StaffAttribute.JudgingStaffAbility);
        attrs[17] = Attr(StaffAttribute.Negotiating);
        attrs[18] = Attr(StaffAttribute.TacticalKnowledge);
        attrs[19] = 0; // FM26 AnalysingData (weight 0 for all roles except DataAnalyst)
        attrs[20] = Attr(StaffAttribute.Physiotherapy);
        attrs[21] = Attr(StaffAttribute.SportsScience);
        attrs[22] = Attr(StaffAttribute.Business);
        attrs[23] = Attr(StaffAttribute.Interference);
        attrs[24] = Attr(StaffAttribute.Resources);
        attrs[25] = Attr(StaffAttribute.Patience);

        Span<double> blended = stackalloc double[26];
        double totalRoleWeight = 0;
        StaffRole? primaryRole = null;
        byte bestRoleRating = 0;

        for (int job = 0; job < 15; job++)
        {
            byte rating = staff.Roles[job];
            if (rating < 15) continue;
            if (rating > bestRoleRating)
            {
                bestRoleRating = rating;
                primaryRole = (StaffRole)job;
            }
            double[] jobW = JobWeights[job];
            for (int i = 0; i < 26; i++)
                blended[i] += jobW[i] * rating;
            totalRoleWeight += rating;
        }

        if (totalRoleWeight == 0)
            return new StaffRcaResult(1, primaryRole);

        double weightedSum = 0;
        double weightDenom = 0;
        for (int i = 0; i < 26; i++)
        {
            double w = blended[i] / totalRoleWeight;
            if (w <= 0) continue;
            weightedSum += attrs[i] * w;
            weightDenom += w;
        }

        if (weightDenom == 0)
            return new StaffRcaResult(1, primaryRole);

        int score = Math.Clamp((int)Math.Floor((weightedSum / weightDenom) * 10.0 + 0.5), 1, 200);
        return new StaffRcaResult(score, primaryRole);
    }

    static double Scale20(int value) => value > 20 ? (value / 255.0) * 20.0 : value;
}

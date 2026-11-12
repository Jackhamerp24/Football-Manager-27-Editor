namespace Fm27Editor.Core.Staff;

/// <summary>The eleven training categories, in FMRTE 27's Training Rating order.</summary>
public enum TrainingCategory
{
    GoalkeepingShotStopping, GoalkeepingHandlingAndDistribution, DefendingTechnical, DefendingTactical,
    PossessionTactical, PossessionTechnical, AttackingTactical, AttackingTechnical, Strength, Quickness, SetPieces,
}

/// <summary>
/// A coach's training star ratings. The game does not store them: they are worked out from the staff attributes, as
/// FMRTE 27.0.1-build11 does (its rating getters were read from FMRTE.exe and its game reads traced with Frida on
/// 12/11/2026). Each category is a weighted sum of 1-20 values (weights 9, or 6 + 3; GK Handling only Goalkeeping ×6), plus Determination, Authority
/// and Motivating at weight 2 each; stars = round(sum / 60 × 2, half away from zero) × 0.5, so 0 to 5 in half stars.
/// Checked against FMRTE's Training Rating tab for Paolo Orlandoni (Inter, staff 581): all eleven categories match.
/// </summary>
public static class TrainingRatings
{
    static readonly (StaffAttribute Attribute, int Weight)[] Common =
        [(StaffAttribute.Determination, 2), (StaffAttribute.Authority, 2), (StaffAttribute.Motivating, 2)];

    public static IReadOnlyList<(StaffAttribute Attribute, int Weight)> Weights(TrainingCategory category) => category switch
    {
        TrainingCategory.GoalkeepingShotStopping => [(StaffAttribute.CoachingGoalkeeping, 9)],
        TrainingCategory.GoalkeepingHandlingAndDistribution => [(StaffAttribute.CoachingGoalkeeping, 6)],
        TrainingCategory.DefendingTechnical => [(StaffAttribute.CoachingDefending, 6), (StaffAttribute.CoachingTechnical, 3)],
        TrainingCategory.DefendingTactical => [(StaffAttribute.CoachingDefending, 6), (StaffAttribute.CoachingTactical, 3)],
        TrainingCategory.PossessionTactical => [(StaffAttribute.CoachingPossession, 3), (StaffAttribute.CoachingTactical, 6)],
        TrainingCategory.PossessionTechnical => [(StaffAttribute.CoachingPossession, 3), (StaffAttribute.CoachingTechnical, 6)],
        TrainingCategory.AttackingTactical => [(StaffAttribute.CoachingAttacking, 6), (StaffAttribute.CoachingTactical, 3)],
        TrainingCategory.AttackingTechnical => [(StaffAttribute.CoachingAttacking, 6), (StaffAttribute.CoachingTechnical, 3)],
        TrainingCategory.Strength or TrainingCategory.Quickness => [(StaffAttribute.CoachingFitness, 9)],
        TrainingCategory.SetPieces => [(StaffAttribute.SetPieces, 3), (StaffAttribute.TacticalKnowledge, 6)],
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    /// <summary>Stars (0-5, half steps) from 1-20 attribute values.</summary>
    public static double Stars(TrainingCategory category, Func<StaffAttribute, int> value)
    {
        int sum = Weights(category).Concat(Common).Sum(w => value(w.Attribute) * w.Weight);
        return Math.Round(sum / 60.0 * 2, MidpointRounding.AwayFromZero) * 0.5;
    }

    public static double Stars(StaffRecord staff, TrainingCategory category) => Stars(category, staff.Attribute);

    public static string Label(TrainingCategory category) => category switch
    {
        TrainingCategory.GoalkeepingShotStopping => "GK - Shot Stopping",
        TrainingCategory.GoalkeepingHandlingAndDistribution => "GK - Handling and Distribution",
        TrainingCategory.DefendingTechnical => "Defending - Technical",
        TrainingCategory.DefendingTactical => "Defending - Tactical",
        TrainingCategory.PossessionTactical => "Possession - Tactical",
        TrainingCategory.PossessionTechnical => "Possession - Technical",
        TrainingCategory.AttackingTactical => "Attacking - Tactical",
        TrainingCategory.AttackingTechnical => "Attacking - Technical",
        TrainingCategory.Strength => "Strength",
        TrainingCategory.Quickness => "Quickness",
        TrainingCategory.SetPieces => "Set Pieces - Overall",
        _ => category.ToString(),
    };
}

using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Paolo Orlandoni (Inter, staff 581, Steam 27.0.1 calibration save, 15/12/2026): attributes as the game holds them
/// (Coaching Attacking 9, Defending 9, Fitness 7, Goalkeeping 13, Possession 10, Tactical 10, Technical 5, Set Pieces 1,
/// Tactical Knowledge 12, Determination 16, Authority 17, Motivating 17); expected stars read off FMRTE 27's Training
/// Rating tab for him.
/// </summary>
public class TrainingRatingsTests
{
    static readonly Dictionary<StaffAttribute, int> Orlandoni = new()
    {
        [StaffAttribute.CoachingAttacking] = 9, [StaffAttribute.CoachingDefending] = 9, [StaffAttribute.CoachingFitness] = 7,
        [StaffAttribute.CoachingGoalkeeping] = 13, [StaffAttribute.CoachingPossession] = 10, [StaffAttribute.CoachingTactical] = 10,
        [StaffAttribute.CoachingTechnical] = 5, [StaffAttribute.SetPieces] = 1, [StaffAttribute.TacticalKnowledge] = 12,
        [StaffAttribute.Determination] = 16, [StaffAttribute.Authority] = 17, [StaffAttribute.Motivating] = 17,
    };

    [Theory]
    [InlineData(TrainingCategory.GoalkeepingShotStopping, 3.5)]
    [InlineData(TrainingCategory.GoalkeepingHandlingAndDistribution, 3.0)]
    [InlineData(TrainingCategory.DefendingTechnical, 3.0)]
    [InlineData(TrainingCategory.DefendingTactical, 3.0)]
    [InlineData(TrainingCategory.PossessionTactical, 3.0)]
    [InlineData(TrainingCategory.PossessionTechnical, 2.5)]
    [InlineData(TrainingCategory.AttackingTactical, 3.0)]
    [InlineData(TrainingCategory.AttackingTechnical, 3.0)]
    [InlineData(TrainingCategory.Strength, 2.5)]
    [InlineData(TrainingCategory.Quickness, 2.5)]
    [InlineData(TrainingCategory.SetPieces, 3.0)]
    public void Orlandoni_matches_FMRTE(TrainingCategory category, double stars) =>
        Assert.Equal(stars, TrainingRatings.Stars(category, a => Orlandoni[a]));

    /// <summary>
    /// Weights as FMRTE.exe's rating getters build them (each getter allocates its (value, weight) pairs, then divides
    /// by 60): every category weighs 15 except GK - Handling and Distribution, whose getter has Goalkeeping ×6 and the
    /// three shared attributes only (12), so it tops out at 4 stars.
    /// </summary>
    [Fact]
    public void Weights_match_FMRTE_and_bound_the_stars()
    {
        foreach (var c in Enum.GetValues<TrainingCategory>())
        {
            bool handling = c == TrainingCategory.GoalkeepingHandlingAndDistribution;
            Assert.Equal(handling ? 12 : 15, TrainingRatings.Weights(c).Sum(w => w.Weight) + 6);
            Assert.Equal(handling ? 4.0 : 5.0, TrainingRatings.Stars(c, _ => 20));
            Assert.Equal(handling ? 0.0 : 0.5, TrainingRatings.Stars(c, _ => 1));
        }
    }
}

using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Planning rules of the coach General page's mass edits, on Paolo Orlandoni's values (Steam 27.0.1, 15/12/2026):
/// born 12/8/1972, Coaching Goalkeeping 13, Determination 16, Authority 17, Motivating 17. Expected values are worked by hand.
/// </summary>
public class StaffDetailsMassEditTests
{
    static StaffRecord Orlandoni()
    {
        var attrs = new byte[Enum.GetValues<StaffAttribute>().Length];
        attrs[(int)StaffAttribute.CoachingGoalkeeping] = 13;
        attrs[(int)StaffAttribute.Determination] = 16;
        attrs[(int)StaffAttribute.Authority] = 17;
        attrs[(int)StaffAttribute.Motivating] = 17;
        return new StaffRecord
        {
            Address = 0x1000, StaffAddress = 0x0F00, Id = 581, Attributes = attrs, Personality = new byte[8],
            Kind = new StaffKind(1, 2, 0x100, false), DateOfBirth = new DateOnly(1972, 8, 12), Caps = 0, Goals = 0,
            License = StaffLicense.StudyingToContinentalA,
        };
    }

    /// <summary>
    /// FMRTE 27's "GK - Shot Stopping: Set to 4" on Orlandoni wrote Goalkeeping, Determination, Authority and Motivating
    /// all to 16 (traced with Frida): 4 stars × 4 = 16, and (9 × 16 + 2 × 48) / 30 = 8 half stars, i.e. 4 stars after the edit.
    /// </summary>
    [Fact]
    public void A_training_target_sets_every_formula_attribute_to_four_times_the_stars()
    {
        Assert.Equal(16, StaffDetailsMassEdit.ValueFor(4));
        Assert.Equal(18, StaffDetailsMassEdit.ValueFor(4.5));
        Assert.Equal(20, StaffDetailsMassEdit.ValueFor(5));
        Assert.Equal(1, StaffDetailsMassEdit.ValueFor(0));
        var change = Assert.Single(StaffDetailsMassEdit.Training([Orlandoni()], TrainingCategory.GoalkeepingShotStopping, Players.MassEditMode.Set, 4, null!));
        Assert.Equal((7, 8), (change.Before, change.After)); // half stars: 3.5 before, 4 after
    }

    /// <summary>
    /// Traced with Frida on FMRTE 27's coach Mass Edit (Orlandoni, 13/11/2026): GK - Shot Stopping (3.5 stars) "Change by -1"
    /// wrote 10 to Goalkeeping, Determination, Authority and Motivating (2.5 stars), "Change by +0.5" wrote 16 (4 stars), and Possession - Technical (2.5 stars)
    /// "Set to 2.5" wrote 10 to Possession, Technical, Determination, Authority and Motivating.
    /// </summary>
    [Fact]
    public void Training_change_by_adds_to_the_current_stars_and_set_to_takes_half_stars()
    {
        var down = Assert.Single(StaffDetailsMassEdit.Training([Orlandoni()], TrainingCategory.GoalkeepingShotStopping, Players.MassEditMode.Change, -1, null!));
        Assert.Equal((7, 5), (down.Before, down.After));
        Assert.Equal(10, StaffDetailsMassEdit.ValueFor(2.5));
        var half = Assert.Single(StaffDetailsMassEdit.Training([Orlandoni()], TrainingCategory.GoalkeepingShotStopping, Players.MassEditMode.Change, 0.5, null!));
        Assert.Equal((7, 8), (half.Before, half.After)); // FMRTE "+0.5" wrote 16 to all four: 4 stars
        Assert.Equal(16, StaffDetailsMassEdit.ValueFor(4));
        var up = Assert.Single(StaffDetailsMassEdit.Training([Orlandoni()], TrainingCategory.GoalkeepingShotStopping, Players.MassEditMode.Change, 5, null!));
        Assert.Equal(10, up.After); // kept at 5 stars
    }

    [Fact]
    public void Age_moves_the_birth_year_and_keeps_the_birthday()
    {
        var today = new DateOnly(2026, 12, 15);
        Assert.Equal(54, StaffDetailsMassEdit.AgeOn(new DateOnly(1972, 8, 12), today));
        Assert.Equal(53, StaffDetailsMassEdit.AgeOn(new DateOnly(1972, 12, 16), today));
        var plan = StaffDetailsMassEdit.Age([Orlandoni()], Players.MassEditMode.Set, 50, today, null!);
        var change = Assert.Single(plan);
        Assert.Equal((54, 50), (change.Before, change.After));
    }

    [Fact]
    public void Licence_codes_follow_FMRTE_and_unchanged_values_plan_nothing()
    {
        Assert.Equal(16, StaffDetailsMassEdit.Licenses.Length);
        Assert.Equal(-2, (int)StaffLicense.StudyingToContinentalA);
        Assert.Equal(8, (int)StaffLicense.NoLicence);
        Assert.Empty(StaffDetailsMassEdit.License([Orlandoni()], StaffLicense.StudyingToContinentalA, null!));
        Assert.Single(StaffDetailsMassEdit.License([Orlandoni()], StaffLicense.ContinentalA, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => StaffDetailsMassEdit.Caps([Orlandoni()], Players.MassEditMode.Change, -1, null!));
    }
}

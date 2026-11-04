using System.Globalization;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App;

/// <summary>Staff screen labels and groups (FMRTE 27's order); job codes as measured on Man Utd.</summary>
public static class StaffLabels
{
    static readonly Dictionary<short, string> Jobs = new()
    {
        [2] = "Coach", [4] = "Chairperson", [6] = "Director", [10] = "Director of Football", [16] = "Manager",
        [20] = "Assistant Manager", [26] = "Fitness Coach", [34] = "GK Coach", [60] = "Performance Analyst",
        [64] = "Head of Youth Development",
    };

    /// <summary>Codes outside <see cref="Jobs"/> use FM26's JobType names (CareerPlanLabels): 8 Managing Director (Newcastle's and Sporting's CEOs),
    /// 38 Chief Doctor (Ricard Pruna), 44 Chief Scout (Juni Calafat), 58 Recruitment Analyst, 66 Owner (John Henry), 70 President (Florentino Pérez).</summary>
    public static string Job(short? code) => code is { } c ? Jobs.TryGetValue(c, out var s) ? s : CareerPlanLabels.Name(CareerPlanLabels.Jobs, c).Replace("Code ", "Job ") : "";

    public static readonly (ManagerAttribute Attribute, string Label)[] Manager =
    [
        (ManagerAttribute.HandsOnApproach, "Hands On Approach"), (ManagerAttribute.DomesticPlayerBias, "Domestic Player Bias"),
        (ManagerAttribute.TacticalConsistency, "Tactical Consistency"), (ManagerAttribute.ClubPlayerLoyalty, "Club/Player Loyalty"),
        (ManagerAttribute.SquadDiscipline, "Squad Discipline"), (ManagerAttribute.FinancialControl, "Financial Control"),
        (ManagerAttribute.MediaHandling, "Media Handling"),
    ];

    public static readonly (string Title, (StaffAttribute Attribute, string Label)[] Items)[] Groups =
    [
        ("Coaching", [
            (StaffAttribute.CoachingAttacking, "Attacking"), (StaffAttribute.CoachingDefending, "Defending"),
            (StaffAttribute.CoachingFitness, "Fitness"), (StaffAttribute.CoachingGoalkeeping, "Goalkeeping"),
            (StaffAttribute.CoachingPossession, "Possession"), (StaffAttribute.CoachingPlayer, "Player"),
            (StaffAttribute.CoachingTactical, "Tactical"), (StaffAttribute.CoachingTechnical, "Technical"),
            (StaffAttribute.PeopleManagement, "People Management"), (StaffAttribute.WorkingWithYoungsters, "Working with Youngsters"),
            (StaffAttribute.DirtinessAllowance, "Dirtiness Allowance"), (StaffAttribute.Versatility, "Versatility"),
            (StaffAttribute.SetPieces, "Set Pieces")]),
        ("Tactical", [
            (StaffAttribute.TacticalAttacking, "Attacking"), (StaffAttribute.Depth, "Depth"), (StaffAttribute.Directness, "Directness"),
            (StaffAttribute.Flamboyancy, "Flamboyancy"), (StaffAttribute.Flexibility, "Flexibility"), (StaffAttribute.FreeRoles, "Free Roles"),
            (StaffAttribute.Marking, "Marking"), (StaffAttribute.Offside, "Offside"), (StaffAttribute.Pressing, "Pressing"),
            (StaffAttribute.SittingBack, "Sitting Back"), (StaffAttribute.Tempo, "Tempo"), (StaffAttribute.UseOfPlaymaker, "Use of Playmaker"),
            (StaffAttribute.UseOfSubstitutions, "Use of Substitutions"), (StaffAttribute.Width, "Width")]),
        ("Non Tactical", [
            (StaffAttribute.BuyingPlayers, "Buying Players"), (StaffAttribute.HardnessOfTraining, "Hardness of Training"),
            (StaffAttribute.MindGames, "Mind Games"), (StaffAttribute.SquadRotation, "Squad Rotation")]),
        ("Mental", [
            (StaffAttribute.Determination, "Determination"), (StaffAttribute.JudgingPlayerAbility, "Judging CA"),
            (StaffAttribute.JudgingPlayerPotential, "Judging PA"), (StaffAttribute.JudgingStaffAbility, "Judging Staff Ability"),
            (StaffAttribute.Negotiating, "Negotiating"), (StaffAttribute.Authority, "Authority"), (StaffAttribute.Motivating, "Motivating"),
            (StaffAttribute.TacticalKnowledge, "Tactical Knowledge"), (StaffAttribute.Physiotherapy, "Physiotherapy"),
            (StaffAttribute.SportsScience, "Sports Science")]),
        ("Scouting", [
            (StaffAttribute.JudgingPlayerData, "Judging Player Data"), (StaffAttribute.JudgingTeamData, "Judging Team Data"),
            (StaffAttribute.PresentingData, "Presenting Data")]),
        ("Chairman", [
            (StaffAttribute.Business, "Business"), (StaffAttribute.Interference, "Interference"), (StaffAttribute.Patience, "Patience"),
            (StaffAttribute.Resources, "Resources")]),
    ];
}

/// <summary>Pending staff edits keyed like "attr:CoachingAttacking", "pers:Loyalty", "mgr:MediaHandling", "CA", "PA", "rep:Home".</summary>
public sealed class StaffDraft
{
    public Dictionary<string, string> Changes { get; } = [];

    public static (int Min, int Max) Range(string key) => key switch
    {
        "CA" or "PA" => (1, 200),
        _ when key.StartsWith("mgr:", StringComparison.Ordinal) => (1, 100),
        _ when key.StartsWith("rep:", StringComparison.Ordinal) => (0, 10000),
        _ => (1, 20),
    };

    /// <summary>Parses every change; the first invalid one is returned as an error and nothing is applied.</summary>
    public bool TryParse(out Dictionary<string, int> values, out string? error)
    {
        values = [];
        error = null;
        foreach (var (key, text) in Changes)
        {
            var (min, max) = Range(key);
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || v < min || v > max)
            {
                error = $"{key[(key.IndexOf(':') + 1)..]}: enter a whole number {min}–{max}";
                values = [];
                return false;
            }
            values[key] = v;
        }
        return true;
    }
}

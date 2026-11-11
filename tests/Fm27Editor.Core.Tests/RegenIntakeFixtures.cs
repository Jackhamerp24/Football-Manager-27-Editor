using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The audit's fixtures: save A on timeline A0, baseline S0 = {P100, P101, P102} with club X holding P100 and P101
/// before the intake and P102 at club Y, the intake E1 observed on the day of the after scan, P200 a new youth and
/// P201 another new player with nothing to back it up. Expected results come from the audit's test matrix, not from
/// anything this code decided.
/// </summary>
internal static class RegenIntakeFixtures
{
    public const string Save = "save-a";
    public const string Branch = "A0";
    public const string OtherBranch = "A1";
    public const string OtherSave = "save-b";

    public const string ClubX = "club-x";
    public const string ClubY = "club-y";
    public const string TeamXYouth = "club-x-youth";
    public const string TeamXFirst = "club-x-first";
    public const string TeamYYouth = "club-y-youth";

    public const string Feature = RegenFeature.Default;

    /// <summary>The baseline was scanned the day before the intake; the after scan on the day itself.</summary>
    public static readonly DateOnly BeforeDate = new(2026, 12, 12);
    public static readonly DateOnly IntakeDate = new(2026, 12, 13);

    /// <summary>The intake E1, observed on the day of the after scan and checked against the game.</summary>
    public static readonly RegenIntakeEvent EventE1 = new("E1", ClubX, IntakeDate, "game calendar", Verified: true);

    /// <summary>S0: P100 and P101 already at club X, P102 at club Y.</summary>
    public static readonly RegenObservation P100 = Person("P100", ClubX, TeamXYouth, new(2009, 5, 4), youth: true);
    public static readonly RegenObservation P101 = Person("P101", ClubX, TeamXYouth, new(2008, 9, 1), youth: true);
    public static readonly RegenObservation P102 = Person("P102", ClubY, TeamYYouth, new(2010, 1, 15), youth: true);

    /// <summary>P200: the new youth of club X, born in the intake's age range and at no other club.</summary>
    public static readonly RegenObservation P200 = Person("P200", ClubX, TeamXYouth, new(2010, 2, 2), youth: true);

    /// <summary>P201: another new player, already in the senior squad of club X.</summary>
    public static readonly RegenObservation P201 = Person("P201", ClubX, TeamXFirst, new(2010, 6, 6), youth: false);

    /// <summary>The age window this feature is configured with; the audit asks for configurable, never baked-in, ages.</summary>
    public static RegenIntakeOptions Options() => new(ClubUid: ClubX, MinAge: 14, MaxAge: 19);

    public static RegenObservation Person(string uid, string club, string team, DateOnly born, bool youth) =>
        new(uid, DateOfBirth: born, ClubUid: club, TeamUid: team, IsPlayer: true, IsYouth: youth);

    public static RegenSnapshot Baseline() => Scan([P100, P101, P102], gameDate: BeforeDate, phase: RegenPhase.BeforeIntake);

    /// <summary>S0 plus whatever had arrived by the time of the after scan.</summary>
    public static RegenSnapshot After(params RegenObservation[] extra) =>
        Scan([.. Baseline().Players, .. extra], phase: RegenPhase.AfterIntake);

    /// <summary>An after-scan-shaped snapshot of whatever players are named.</summary>
    public static RegenSnapshot Snapshot(params RegenObservation[] players) =>
        Scan(players, phase: RegenPhase.AfterIntake);

    public static RegenSnapshot Scan(
        RegenObservation[] players,
        DateOnly? gameDate = null,
        bool complete = true,
        string saveId = Save,
        string branchId = Branch,
        string scope = RegenScope.Global,
        string phase = RegenPhase.Unknown) =>
        new(saveId, branchId, gameDate ?? IntakeDate, complete, players, scope, phase);

    /// <summary>A store file in the system temp folder; the caller removes it.</summary>
    public static string NewStorePath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "fm27-regen-intake-tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
    }
}

using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Staff;

/// <summary>One planned staff edit: what it shows in the review and how to write it on a freshly read record.</summary>
public sealed record StaffDetailChange(StaffRecord Staff, string Field, int Before, int After, Func<StaffRecord, int> Read, Action<StaffRecord> Write);

/// <summary>
/// Mass edits of the coach General page that are not plain numbers on the staff part: caps, goals, age, licence,
/// nationality, role ratings and training ratings. Every write re-reads the staff member and refuses when the value no
/// longer equals the planned "before".
/// </summary>
public static class StaffDetailsMassEdit
{
    static int Next(MassEditMode mode, int before, int value) => mode == MassEditMode.Set ? value : checked(before + value);

    static IReadOnlyList<StaffDetailChange> Plan(IEnumerable<StaffRecord> staff, string field, Func<StaffRecord, int?> read,
        Func<StaffRecord, int, int> target, int min, int max, Func<StaffRecord, int, Action<StaffRecord>> write)
    {
        var ids = new HashSet<int>();
        var changes = new List<StaffDetailChange>();
        foreach (var person in staff)
        {
            if (person.Id <= 0 || !ids.Add(person.Id)) throw new ArgumentException("Staff must have unique positive IDs");
            if (read(person) is not { } before) continue;
            int after = target(person, before);
            if (after < min || after > max) throw new ArgumentOutOfRangeException(nameof(staff), after, $"{field} must be between {min} and {max}");
            if (after != before) changes.Add(new(person, field, before, after, r => read(r) ?? int.MinValue, write(person, after)));
        }
        return changes;
    }

    public static IReadOnlyList<StaffDetailChange> Caps(IEnumerable<StaffRecord> staff, MassEditMode mode, int value, StaffWriter writer) =>
        Plan(staff, "Caps", s => s.Caps, (_, b) => Next(mode, b, value), 0, 10000, (_, v) => r => writer.SetCaps(r, v));

    public static IReadOnlyList<StaffDetailChange> Goals(IEnumerable<StaffRecord> staff, MassEditMode mode, int value, StaffWriter writer) =>
        Plan(staff, "Goals", s => s.Goals, (_, b) => Next(mode, b, value), 0, 10000, (_, v) => r => writer.SetGoals(r, v));

    public static IReadOnlyList<StaffDetailChange> License(IEnumerable<StaffRecord> staff, StaffLicense value, StaffWriter writer) =>
        Plan(staff, "License", s => s.License is { } l ? (int)l : null, (_, _) => (int)value, -7, 8, (_, v) => r => writer.SetLicense(r, (StaffLicense)(sbyte)v));

    public static IReadOnlyList<StaffDetailChange> Role(IEnumerable<StaffRecord> staff, StaffRole role, MassEditMode mode, int value, StaffWriter writer) =>
        Plan(staff, RoleLabel(role), s => s.Roles?[(int)role], (_, b) => Next(mode, b, value), 1, 20, (_, v) => r => writer.SetRole(r, role, v));

    public static IReadOnlyList<StaffDetailChange> Nationality(IEnumerable<StaffRecord> staff, int nationId, ulong nationAddress, StaffWriter writer) =>
        Plan(staff, "Nationality", s => s.Nationality?.Id ?? 0, (_, _) => nationId, int.MinValue, int.MaxValue, (_, _) => r => writer.SetNationality(r, nationAddress));

    /// <summary>Age on <paramref name="today"/>; the birthday keeps its day and month and moves by whole years.</summary>
    public static IReadOnlyList<StaffDetailChange> Age(IEnumerable<StaffRecord> staff, MassEditMode mode, int value, DateOnly today, StaffWriter writer) =>
        Plan(staff, "Age", s => s.DateOfBirth is { } d ? AgeOn(d, today) : null, (_, b) => Next(mode, b, value), 14, 100,
            (person, age) => r => writer.SetDateOfBirth(r, Born(person.DateOfBirth!.Value, age, today)));

    public static int AgeOn(DateOnly born, DateOnly today) => today.Year - born.Year - (today < born.AddYears(today.Year - born.Year) ? 1 : 0);

    /// <summary>The birth date that gives <paramref name="age"/> on <paramref name="today"/>, keeping the day and month.</summary>
    public static DateOnly BornForAge(DateOnly born, int age, DateOnly today) => Born(born, age, today);

    static DateOnly Born(DateOnly born, int age, DateOnly today)
    {
        var shifted = born.AddYears(age - AgeOn(born, today));
        return AgeOn(shifted, today) == age ? shifted : shifted.AddYears(age - AgeOn(shifted, today));
    }

    /// <summary>
    /// Training rating "Set to" as FMRTE 27 writes it: every attribute in the category's formula (its coaching
    /// attribute(s), Determination, Authority and Motivating) becomes stars × 4 on the 1-20 scale. Measured with Frida on
    /// Paolo Orlandoni (Steam 27.0.1, 12/11/2026): GK - Shot Stopping "Set to 4" wrote Goalkeeping, Determination, Authority
    /// and Motivating all to 16 and nothing else. "Change by" adds the change to the current stars (kept between 0 and
    /// 5) and applies the same rule: on Orlandoni FMRTE's GK - Shot Stopping (3.5 stars) Change by -1 wrote 10 (2.5 stars) to
    /// all four attributes, and Possession - Technical Set to 2.5 wrote 10 to its five (Frida, 13/11/2026). FMRTE accepts
    /// 0 to 5 for Set to and -5 to 5 for Change by. Before/After are in half stars.
    /// </summary>
    public static IReadOnlyList<StaffDetailChange> Training(IEnumerable<StaffRecord> staff, TrainingCategory category, MassEditMode mode,
        double stars, StaffWriter writer)
    {
        var attributes = TrainingRatings.Weights(category).Select(w => w.Attribute)
            .Concat([StaffAttribute.Determination, StaffAttribute.Authority, StaffAttribute.Motivating]).Distinct().ToArray();
        double Target(StaffRecord s) => mode == MassEditMode.Set ? stars
            : Math.Clamp(TrainingRatings.Stars(s, category) + stars, 0, 5);
        return Plan(staff, TrainingRatings.Label(category), s => (int)Math.Round(TrainingRatings.Stars(s, category) * 2),
            (s, _) => (int)Math.Round(TrainingRatings.Stars(category, _ => ValueFor(Target(s))) * 2),
            0, 10, (s, _) =>
            {
                int value = ValueFor(Target(s));
                return r => { foreach (var a in attributes) if (r.Attribute(a) != value) writer.SetAttribute(r, a, value); };
            });
    }

    /// <summary>The 1-20 value FMRTE writes for a star target: stars × 4, at least 1.</summary>
    public static int ValueFor(double stars) => Math.Clamp((int)Math.Round(stars * 4, MidpointRounding.AwayFromZero), 1, 20);

    /// <summary>Re-reads the staff member, checks the planned "before", then writes.</summary>
    public static void Apply(StaffDetailChange change, StaffRepository repository)
    {
        var current = repository.Reload(change.Staff.Address);
        if (current is null || current.Id != change.Staff.Id || change.Read(current) != change.Before)
            throw new StaleObjectException("Staff member or field changed since the batch was prepared");
        change.Write(current);
    }

    public static string RoleLabel(StaffRole role) => role switch
    {
        StaffRole.AssistantManager => "Assistant Manager",
        StaffRole.GoalkeepingCoach => "Goalkeeping Coach",
        StaffRole.FitnessCoach => "Fitness Coach",
        StaffRole.SetPieceCoach => "Set Piece Coach",
        StaffRole.DirectorOfFootball => "Director of Football",
        StaffRole.HeadOfYouthDevelopment => "Head of Youth Development",
        StaffRole.LoanManager => "Loan Manager",
        StaffRole.DataAnalyst => "Data Analyst",
        StaffRole.SportsScientist => "Sports Scientist",
        StaffRole.TechnicalDirector => "Technical Director",
        _ => role.ToString(),
    };

    /// <summary>FMRTE 27's licence dropdown, in its order.</summary>
    public static readonly (StaffLicense Value, string Name)[] Licenses =
    [
        (StaffLicense.None, "None"), (StaffLicense.ContinentalPro, "Continental Pro Licence"), (StaffLicense.ContinentalA, "Continental A Licence"),
        (StaffLicense.ContinentalB, "Continental B Licence"), (StaffLicense.ContinentalC, "Continental C Licence"),
        (StaffLicense.NationalA, "National A Licence"), (StaffLicense.NationalB, "National B Licence"), (StaffLicense.NationalC, "National C Licence"),
        (StaffLicense.NoLicence, "No Licence"), (StaffLicense.StudyingToNationalC, "Studying To National C"),
        (StaffLicense.StudyingToNationalB, "Studying To National B"), (StaffLicense.StudyingToNationalA, "Studying To National A"),
        (StaffLicense.StudyingToContinentalC, "Studying To Continental C"), (StaffLicense.StudyingToContinentalB, "Studying To Continental B"),
        (StaffLicense.StudyingToContinentalA, "Studying To Continental A"), (StaffLicense.StudyingToContinentalPro, "Studying To Continental Pro"),
    ];
}

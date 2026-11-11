namespace Fm27Editor.App;

/// <summary>
/// One line of a mass edit's review table: who, which field, the value before and after. <see cref="Apply"/> performs just this
/// line (and returns how many values it wrote); a line with a <see cref="Problem"/> cannot be applied and is shown crossed out.
/// </summary>
internal sealed class MassRow
{
    public required string Name { get; init; }
    public required string Field { get; init; }
    public string Before { get; init; } = "";
    public string After { get; init; } = "";
    public string? Problem { get; init; }
    public Func<int>? Apply { get; init; }
    public bool Include { get; set; }

    public bool CanApply => Problem is null && Apply is not null;
    public string Note => Problem ?? "";

    public static MassRow Change(string name, string field, object before, object after, Func<int> apply) =>
        new() { Name = name, Field = field, Before = before.ToString() ?? "", After = after.ToString() ?? "", Apply = apply, Include = true };

    public static MassRow Refused(string name, string field, string problem) => new() { Name = name, Field = field, Problem = problem };

    /// <summary>One line standing for an action on many people (a preset, a clause) that has no single before and after.</summary>
    public static MassRow Action(string name, string field, string after, Func<int> apply) =>
        new() { Name = name, Field = field, Before = "", After = after, Apply = apply, Include = true };
}

/// <summary>What one scope (Player, Coach, Club, Nation) would change; the review window shows one table per plan.</summary>
internal sealed class MassPlan(string scope, int targets, IReadOnlyList<MassRow> rows)
{
    public string Scope { get; } = scope;
    public int Targets { get; } = targets;
    public IReadOnlyList<MassRow> Rows { get; } = rows;

    public int Selected => Rows.Count(r => r.Include && r.CanApply);
    public int Fields => Rows.Select(r => r.Field).Distinct().Count();

    /// <summary>"1,234 players, 3 fields, 3,702 changes" for the table's header line.</summary>
    public string Summary(string noun) => $"{Targets:N0} {noun}, {Fields:N0} field(s), {Selected:N0} change(s) selected";
}

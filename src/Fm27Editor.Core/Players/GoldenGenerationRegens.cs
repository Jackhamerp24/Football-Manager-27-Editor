using System.Globalization;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Which players are new generations: the ids the later snapshot has and the earlier one did not. A player whose id
/// cannot be read is reported separately instead of being guessed at either way.
/// </summary>
public sealed record GoldenGenerationRegenDiff(IReadOnlyList<PlayerRecord> New, IReadOnlyList<PlayerRecord> UnknownIdentity);

public static class GoldenGenerationRegens
{
    /// <summary>The readable ids of a snapshot, for comparing two of them.</summary>
    public static HashSet<int> Ids(IEnumerable<PlayerRecord> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Where(player => player.Id is > 0).Select(player => player.Id!.Value).ToHashSet();
    }

    /// <summary>Players whose id is new since <paramref name="before"/>, plus the ones that could not be compared.</summary>
    public static GoldenGenerationRegenDiff Diff(IEnumerable<PlayerRecord> before, IEnumerable<PlayerRecord> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var seen = Ids(before);
        var fresh = new List<PlayerRecord>();
        var unknown = new List<PlayerRecord>();
        foreach (var player in after)
        {
            if (player.Id is not > 0) unknown.Add(player);
            else if (!seen.Contains(player.Id.Value)) fresh.Add(player);
        }
        return new(fresh, unknown);
    }
}

/// <summary>
/// The core filters on a new generation. Every one of them is nullable: null means the filter is off, never "any
/// value", so a run with none of them set touches every player the snapshot diff brought in.
/// </summary>
public sealed record GoldenGenerationRegenOptions
{
    int? _maxAge;

    /// <summary>Players older than this are left alone.</summary>
    public int? MaxAge
    {
        get => _maxAge;
        init { if (value is < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Maximum age cannot be negative"); _maxAge = value; }
    }

    /// <summary>Players born before this date are left alone.</summary>
    public DateOnly? BornAfter { get; init; }

    /// <summary>When set, only players on (or not on) a youth contract are touched.</summary>
    public bool? RequireYouthContract { get; init; }
}

/// <summary>
/// The players this feature has already rolled, so a second run never rolls the same player twice. Stored one id per
/// line and sorted, so the file stays readable and diffable.
/// </summary>
public sealed class GoldenGenerationLedger
{
    readonly HashSet<int> _ids;

    public GoldenGenerationLedger(IEnumerable<int>? ids = null) => _ids = ids is null ? [] : ids.ToHashSet();

    public int Count => _ids.Count;

    public IReadOnlyList<int> Ids => _ids.Order().ToArray();

    public bool Contains(int id) => _ids.Contains(id);

    /// <summary>False when the id was already there.</summary>
    public bool Add(int id) => _ids.Add(id);

    /// <summary>One id per line; a line that is not an id is refused rather than skipped, so a damaged file is visible.</summary>
    public static GoldenGenerationLedger Load(string path)
    {
        if (!File.Exists(path)) return new GoldenGenerationLedger();
        var ids = new List<int>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (!int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                throw new FormatException($"{path}: '{line}' is not a player id");
            ids.Add(id);
        }
        return new GoldenGenerationLedger(ids);
    }

    public void Save(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        // Write beside the target, then rename: a crash leaves the old ledger whole instead of a half-written file.
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllLines(temp, Ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}

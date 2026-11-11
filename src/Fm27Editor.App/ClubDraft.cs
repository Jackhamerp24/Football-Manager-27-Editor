using System.Globalization;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.App;

public readonly record struct ClubFieldTarget(ulong Root, ClubField Field);

public sealed record VisibleClubField(
    ClubField Field,
    ClubFieldMeta Meta,
    ClubFieldOffset Offset,
    ulong Root,
    ClubDisplayKind Kind,
    long Min,
    long Max)
{
    public ClubFieldTarget Target => new(Root, Field);
}

public sealed record VisibleClubGroup(
    string Title,
    bool UsesTeamRoot,
    IReadOnlyList<VisibleClubField> Fields);

public static class ClubCatalog
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly CompareInfo Compare = Inv.CompareInfo;
    const CompareOptions IgnoreCaseAndAccents = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public static IReadOnlyList<ClubEntry> Filter(IEnumerable<ClubEntry> clubs, string? query, Fm27Editor.Core.Offsets.Gender? gender = null)
    {
        if (gender is { } g) clubs = clubs.Where(c => c.Gender == g);
        string q = query?.Trim() ?? "";
        if (q.Length == 0)
        {
            return clubs
                .OrderBy(c => c.DisplayName, StringComparer.InvariantCultureIgnoreCase)
                .ThenBy(c => c.Id ?? int.MaxValue)
                .ToArray();
        }

        bool digitsOnly = q.All(char.IsAsciiDigit);
        var matches = new List<(ClubEntry Club, int Rank)>();
        foreach (var c in clubs)
        {
            string? idText = c.Id?.ToString(Inv);
            bool exactId = idText is not null && idText == q;
            bool prefixId = !exactId && digitsOnly && idText is not null && idText.StartsWith(q, StringComparison.Ordinal);
            bool nameMatch = ContainsText(c.DisplayName, q)
                             || ContainsText(c.Name ?? "", q)
                             || ContainsText(c.ShortName ?? "", q);
            if (!exactId && !prefixId && !nameMatch) continue;

            int rank = exactId ? 0 : prefixId ? 1 : 2;
            matches.Add((c, rank));
        }

        return matches
            .OrderBy(m => m.Rank)
            .ThenBy(m => m.Rank == 1 ? m.Club.Id ?? int.MaxValue : 0)
            .ThenBy(m => m.Club.DisplayName, StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(m => m.Club.Id ?? int.MaxValue)
            .Select(m => m.Club)
            .ToArray();
    }

    public static IReadOnlyList<TeamEntry> OrderedTeams(ClubEntry club) =>
        club.Teams.OrderByDescending(t => t.IsFirstTeam).ToArray();

    public static TeamEntry? DefaultTeam(ClubEntry club) =>
        club.FirstTeam ?? club.Teams.FirstOrDefault();

    static bool ContainsText(string haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) && Compare.IndexOf(haystack, needle, IgnoreCaseAndAccents) >= 0;
}

public static class ClubSectionLayout
{
    public static IReadOnlyList<ClubField> SupporterDistributionFields { get; } =
    [
        ClubField.SupportersHardcore,
        ClubField.SupportersCore,
        ClubField.SupportersFamily,
        ClubField.SupportersFairWeather,
        ClubField.SupportersCorporate,
        ClubField.SupportersCasual,
    ];

    public static IReadOnlyList<VisibleClubGroup> BuildGroups(
        ClubSection section,
        OffsetProfile profile,
        ClubFieldAccess access,
        ulong clubAddress,
        ulong teamAddress,
        IReadOnlyDictionary<ClubField, long> values)
    {
        var map = profile.Club?.Fields;
        if (map is null || map.Count == 0) return [];

        var groupOrder = new List<string>();
        var byGroup = new Dictionary<string, List<VisibleClubField>>(StringComparer.Ordinal);

        foreach (var baseMeta in ClubFieldInfo.All)
        {
            var field = baseMeta.Field;
            if (!access.IsMapped(field) || !map.TryGetValue(field, out var offset)) continue;

            var meta = ClubFieldInfo.For(field, offset);
            if (meta.Section != section) continue;
            if (!values.TryGetValue(field, out long currentVal)) continue;

            ulong root = offset.Root == ClubFieldRoot.Club ? clubAddress : teamAddress;
            if (root == 0) continue;

            var kind = ClubFieldInfo.EffectiveKind(meta, offset);
            var (min, max) = ClubFieldInfo.EffectiveBounds(meta, offset, currentVal);
            var visible = new VisibleClubField(field, meta, offset, root, kind, min, max);

            if (!byGroup.TryGetValue(meta.Group, out var list))
            {
                byGroup[meta.Group] = list = [];
                groupOrder.Add(meta.Group);
            }
            list.Add(visible);
        }

        return groupOrder
            .Select(title =>
            {
                var fields = byGroup[title];
                bool usesTeam = fields.Any(f => f.Offset.Root == ClubFieldRoot.Team);
                return new VisibleClubGroup(title, usesTeam, fields);
            })
            .ToArray();
    }
}

/// <summary>
/// Stages edits for a club (and its teams) against an initial snapshot, then writes each changed field
/// through <see cref="IEditService.Execute"/> and <see cref="ClubFieldAccess.Set"/>.
/// </summary>
public sealed class ClubDraft(ulong clubAddress)
{
    readonly Dictionary<ClubFieldTarget, long> snapshots = [];
    readonly Dictionary<ClubFieldTarget, long> changes = [];
    readonly HashSet<ClubFieldTarget> invalidInputs = [];

    public ulong ClubAddress { get; } = clubAddress;
    public int Count => changes.Count;
    public int InvalidCount => invalidInputs.Count;
    public bool CanSave => changes.Count > 0 && invalidInputs.Count == 0;

    public void AbsorbSnapshot(
        ulong clubRoot,
        ulong teamRoot,
        OffsetProfile profile,
        IReadOnlyDictionary<ClubField, long> readValues)
    {
        var map = profile.Club?.Fields;
        if (map is null) return;

        foreach (var (field, val) in readValues)
        {
            if (!map.TryGetValue(field, out var offset)) continue;
            ulong root = offset.Root == ClubFieldRoot.Club ? clubRoot : teamRoot;
            if (root == 0) continue;

            var target = new ClubFieldTarget(root, field);
            // Keep the original baseline for pending edits so game-side mutations still trigger StaleObjectException.
            if (!changes.ContainsKey(target))
                snapshots[target] = val;
            else
                snapshots.TryAdd(target, val);
        }
    }

    public long OriginalValue(ClubFieldTarget target) => snapshots[target];

    public long Value(ClubFieldTarget target) =>
        changes.TryGetValue(target, out long value) ? value : snapshots[target];

    public void Stage(VisibleClubField field, long value)
    {
        if (field.Kind == ClubDisplayKind.Bool)
            value = value != 0 ? 1 : 0;

        if (field.Kind == ClubDisplayKind.Date && !IsValidDateValue(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Invalid calendar date");

        if (value < field.Min || value > field.Max)
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Expected {field.Min}..{field.Max}");

        var target = field.Target;
        if (!snapshots.TryGetValue(target, out long original))
            throw new InvalidOperationException($"Missing snapshot for {field.Field}");

        invalidInputs.Remove(target);
        bool unchanged = field.Kind == ClubDisplayKind.Bool
            ? original is 0 or 1 && original == value
            : original == value;

        if (unchanged) changes.Remove(target);
        else changes[target] = value;
    }

    public void SetInvalid(ClubFieldTarget target, bool invalid)
    {
        if (invalid) invalidInputs.Add(target);
        else invalidInputs.Remove(target);
    }

    public int Save(IEditService edits, ClubFieldAccess access, Action<ClubFieldTarget>? onApplied = null)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(access);
        if (invalidInputs.Count > 0)
            throw new FormatException("Fix invalid values before saving");

        int saved = 0;
        foreach (var (target, newValue) in changes.ToArray())
        {
            long expected = snapshots[target];
            edits.Execute(() => { access.Set(target.Root, target.Field, expected, newValue); onApplied?.Invoke(target); });
            snapshots[target] = newValue;
            changes.Remove(target);
            saved++;
        }
        return saved;
    }

    public void Rebase(
        ulong clubRoot,
        ulong teamRoot,
        OffsetProfile profile,
        IReadOnlyDictionary<ClubField, long> freshValues)
    {
        var map = profile.Club?.Fields;
        if (map is null) return;

        foreach (var (field, val) in freshValues)
        {
            if (!map.TryGetValue(field, out var offset)) continue;
            ulong root = offset.Root == ClubFieldRoot.Club ? clubRoot : teamRoot;
            if (root == 0) continue;

            var target = new ClubFieldTarget(root, field);
            snapshots[target] = val;
            if (changes.TryGetValue(target, out long staged) && staged == val)
                changes.Remove(target);
        }
    }

    static bool IsValidDateValue(long yyyymmdd)
    {
        if (yyyymmdd == 0) return true;
        int year = (int)(yyyymmdd / 10000);
        int month = (int)(yyyymmdd / 100 % 100);
        int day = (int)(yyyymmdd % 100);
        return year is >= 1901 and <= 2300
               && month is >= 1 and <= 12
               && day >= 1
               && day <= DateTime.DaysInMonth(year, month);
    }
}

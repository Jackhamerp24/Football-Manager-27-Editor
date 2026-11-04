using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

public enum FieldKind { Rating20, Int, Text, Date, ReadOnly }
public sealed record FieldDescriptor<T>(string Id, string Label, string Group, FieldKind Kind, int Min, int Max, Func<T, int> Read, Action<PlayerWriter, T, int>? Write);
public sealed record ColumnDescriptor(string Title, string Binding);
public interface IEditorModule
{
    string Title { get; }
    IReadOnlyList<ColumnDescriptor> Columns { get; }
    Task<IReadOnlyList<object>> LoadAsync(IEditorBackend backend, CancellationToken ct);
    bool Matches(object row, string query);
}
public interface IEntityAction
{
    string Title { get; }
    Type EntityType { get; }
    bool CanRun(object entity);
    Task RunAsync(IEditService edits, IReadOnlyList<object> entities, CancellationToken ct);
}
public interface IBackgroundService { Task RunAsync(CancellationToken ct); }
public static class Modules
{
    public static IReadOnlyList<IEditorModule> Registered { get; } = [new PlayersModule()];
    public static IReadOnlyList<IEntityAction> Actions { get; } = [];
}
public sealed record AttributeThreshold(PlayerAttribute Attribute, int MinValue, int? MaxValue = null);

public sealed record PlayerFilter
{
    public string? NameQuery { get; init; }
    public string? Club { get; init; }
    public string? Nationality { get; init; }
    public string? Division { get; init; }
    public int? MinAge { get; init; }
    public int? MaxAge { get; init; }
    public int? MinCa { get; init; }
    public int? MaxCa { get; init; }
    public int? MinPa { get; init; }
    public int? MaxPa { get; init; }
    public IReadOnlyList<PlayerPosition> Positions { get; init; } = [];
    public int PositionMinRating { get; init; } = 15;
    public IReadOnlyList<AttributeThreshold> AttributeThresholds { get; init; } = [];
}

public enum PlayerSortKey
{
    CurrentAbility,
    PotentialAbility,
    Age,
    Name,
    Club,
    Nationality,
    Division,
    Attribute
}

public sealed record PlayerSortSpec(PlayerSortKey Key = PlayerSortKey.CurrentAbility, bool Descending = true, PlayerAttribute? Attribute = null);

public sealed record FilterPreset(
    string Name,
    IReadOnlyList<AttributeThreshold> AttributeThresholds,
    int? MinAge = null,
    int? MaxAge = null,
    int? MinCa = null,
    int? MinPa = null,
    int? MaxCa = null,
    int? MaxPa = null,
    IReadOnlyList<PlayerPosition>? Positions = null,
    int PositionMinRating = 15)
{
    public override string ToString() => Name;
}

public static class PositionLabels
{
    public static string Short(PlayerPosition p) => p switch
    {
        PlayerPosition.Goalkeeper => "GK",
        PlayerPosition.DefenderLeft => "DL",
        PlayerPosition.DefenderCentral => "DC",
        PlayerPosition.DefenderRight => "DR",
        PlayerPosition.WingBackLeft => "WBL",
        PlayerPosition.WingBackRight => "WBR",
        PlayerPosition.DefensiveMidfielder => "DM",
        PlayerPosition.MidfielderLeft => "ML",
        PlayerPosition.MidfielderCentral => "MC",
        PlayerPosition.MidfielderRight => "MR",
        PlayerPosition.AttackingMidLeft => "AML",
        PlayerPosition.AttackingMidCentral => "AMC",
        PlayerPosition.AttackingMidRight => "AMR",
        PlayerPosition.Striker => "ST",
        _ => p.ToString()
    };
}

public static class FilterPresetStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<FilterPreset> Defaults { get; } =
    [
        new("Wonderkids (U21, PA >= 165)", [], MaxAge: 21, MinPa: 165),
        new("Fast Striker", [
            new(PlayerAttribute.Pace, 15),
            new(PlayerAttribute.Acceleration, 15),
            new(PlayerAttribute.Finishing, 15),
            new(PlayerAttribute.Composure, 14),
            new(PlayerAttribute.OffTheBall, 14)
        ], Positions: [PlayerPosition.Striker]),
        new("Playmaker", [
            new(PlayerAttribute.Passing, 16),
            new(PlayerAttribute.Vision, 16),
            new(PlayerAttribute.Technique, 15),
            new(PlayerAttribute.FirstTouch, 15),
            new(PlayerAttribute.Decisions, 14)
        ], Positions: [PlayerPosition.AttackingMidCentral, PlayerPosition.MidfielderCentral]),
        new("Ball-Playing Defender", [
            new(PlayerAttribute.Tackling, 15),
            new(PlayerAttribute.Marking, 15),
            new(PlayerAttribute.Positioning, 15),
            new(PlayerAttribute.Strength, 14),
            new(PlayerAttribute.Passing, 13)
        ], Positions: [PlayerPosition.DefenderCentral]),
        new("Sweeper Keeper", [
            new(PlayerAttribute.Reflexes, 15),
            new(PlayerAttribute.OneOnOnes, 14),
            new(PlayerAttribute.Handling, 14),
            new(PlayerAttribute.RushingOut, 13),
            new(PlayerAttribute.Kicking, 13)
        ], Positions: [PlayerPosition.Goalkeeper]),
    ];

    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fm27Editor", "filter-presets.json");

    public static IReadOnlyList<FilterPreset> LoadOrDefaults(string? path = null)
    {
        var file = path ?? DefaultFilePath;
        if (!File.Exists(file)) return Defaults.ToArray();
        try
        {
            var loaded = JsonSerializer.Deserialize<List<FilterPreset>>(File.ReadAllText(file), JsonOptions);
            return loaded is { Count: > 0 } ? loaded : Defaults.ToArray();
        }
        catch
        {
            return Defaults.ToArray();
        }
    }

    public static void Save(IEnumerable<FilterPreset> presets, string? path = null)
    {
        var file = path ?? DefaultFilePath;
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(file, JsonSerializer.Serialize(presets.ToArray(), JsonOptions));
    }

    public static IReadOnlyList<FilterPreset> Upsert(IReadOnlyList<FilterPreset> existing, FilterPreset preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Name))
            throw new ArgumentException("Preset name cannot be empty", nameof(preset));
        var normalized = preset with { Name = preset.Name.Trim() };
        var list = existing.ToList();
        int idx = list.FindIndex(p => string.Equals(p.Name, normalized.Name, StringComparison.InvariantCultureIgnoreCase));
        if (idx >= 0) list[idx] = normalized;
        else list.Add(normalized);
        return list;
    }

    public static IReadOnlyList<FilterPreset> Remove(IReadOnlyList<FilterPreset> existing, string name) =>
        existing.Where(p => !string.Equals(p.Name, name.Trim(), StringComparison.InvariantCultureIgnoreCase)).ToArray();
}

public sealed class PlayersModule : IEditorModule
{
    static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    const CompareOptions IgnoreCaseAndAccents = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public string Title => "Players";
    public IReadOnlyList<ColumnDescriptor> Columns { get; } =
    [
        new("Name", "DisplayName"),
        new("Age", "Age"),
        new("CA", "CurrentAbility"),
        new("PA", "PotentialAbility"),
        new("Club", "ClubName"),
        new("Nationality", "NationalityName")
    ];

    public async Task<IReadOnlyList<object>> LoadAsync(IEditorBackend backend, CancellationToken ct) =>
        await Task.Run<IReadOnlyList<object>>(() =>
        {
            var gameDate = backend.GameDate;
            var all = backend.Players.LoadAll();
            foreach (var p in all) p.GameDate = gameDate;
            return all.OrderByDescending(p => p.CurrentAbility).Cast<object>().ToArray();
        }, ct);

    public bool Matches(object row, string query)
    {
        var p = (PlayerRecord)row;
        return ContainsText(p.DisplayName, query) || ContainsText(p.FullName ?? "", query);
    }

    public bool MatchesFilter(PlayerRecord p, PlayerFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.NameQuery) && !Matches(p, filter.NameQuery.Trim()))
            return false;
        if (!string.IsNullOrWhiteSpace(filter.Club))
        {
            var c = filter.Club.Trim();
            if (!ContainsText(p.ClubName, c) && !ContainsText(p.Club?.Name ?? "", c))
                return false;
        }
        if (!string.IsNullOrWhiteSpace(filter.Nationality))
        {
            var n = filter.Nationality.Trim();
            if (!ContainsText(p.NationalityName, n) && !ContainsText(p.Nationality?.ShortName ?? "", n))
                return false;
        }
        if (!string.IsNullOrWhiteSpace(filter.Division))
        {
            var d = filter.Division.Trim();
            if (!ContainsText(p.DivisionName, d) && !ContainsText(p.Division?.Name ?? "", d))
                return false;
        }
        if (filter.MinAge is { } minAge && (p.Age is not { } age1 || age1 < minAge))
            return false;
        if (filter.MaxAge is { } maxAge && (p.Age is not { } age2 || age2 > maxAge))
            return false;
        if (filter.MinCa is { } minCa && p.CurrentAbility < minCa)
            return false;
        if (filter.MaxCa is { } maxCa && p.CurrentAbility > maxCa)
            return false;
        if (filter.MinPa is { } minPa && p.PotentialAbility < minPa)
            return false;
        if (filter.MaxPa is { } maxPa && p.PotentialAbility > maxPa)
            return false;
        if (filter.Positions.Count > 0)
        {
            int minPos = Math.Clamp(filter.PositionMinRating, 1, 20);
            bool anyPos = false;
            for (int i = 0; i < filter.Positions.Count; i++)
            {
                if (p.Position(filter.Positions[i]) >= minPos)
                {
                    anyPos = true;
                    break;
                }
            }
            if (!anyPos) return false;
        }
        for (int i = 0; i < filter.AttributeThresholds.Count; i++)
        {
            var t = filter.AttributeThresholds[i];
            int rating = p.Attribute(t.Attribute);
            if (rating < t.MinValue || (t.MaxValue is { } maximum && rating > maximum))
                return false;
        }
        return true;
    }

    public IReadOnlyList<PlayerRecord> FilterAndSort(IEnumerable<PlayerRecord> players, PlayerFilter filter, PlayerSortSpec sort)
    {
        var filtered = players.Where(p => MatchesFilter(p, filter));
        IOrderedEnumerable<PlayerRecord> ordered = sort.Key switch
        {
            PlayerSortKey.CurrentAbility => OrderNum(filtered, p => (int)p.CurrentAbility, sort.Descending),
            PlayerSortKey.PotentialAbility => OrderNum(filtered, p => (int)p.PotentialAbility, sort.Descending),
            PlayerSortKey.Age => OrderNullableNum(filtered, p => p.Age, sort.Descending),
            PlayerSortKey.Name => OrderText(filtered, p => p.DisplayName, sort.Descending),
            PlayerSortKey.Club => OrderNonEmptyText(filtered, p => p.ClubName, sort.Descending),
            PlayerSortKey.Nationality => OrderNonEmptyText(filtered, p => p.NationalityName, sort.Descending),
            PlayerSortKey.Division => OrderNonEmptyText(filtered, p => p.DivisionName, sort.Descending),
            PlayerSortKey.Attribute when sort.Attribute is { } attr => OrderNum(filtered, p => p.Attribute(attr), sort.Descending),
            _ => OrderNum(filtered, p => (int)p.CurrentAbility, true),
        };
        return ordered.ThenByDescending(p => p.CurrentAbility).ThenBy(p => p.DisplayName, StringComparer.InvariantCultureIgnoreCase).ToArray();
    }

    static IOrderedEnumerable<PlayerRecord> OrderNum(IEnumerable<PlayerRecord> src, Func<PlayerRecord, int> key, bool desc) =>
        desc ? src.OrderByDescending(key) : src.OrderBy(key);

    static IOrderedEnumerable<PlayerRecord> OrderNullableNum(IEnumerable<PlayerRecord> src, Func<PlayerRecord, int?> key, bool desc) =>
        desc
            ? src.OrderBy(p => key(p) is null ? 1 : 0).ThenByDescending(p => key(p) ?? 0)
            : src.OrderBy(p => key(p) is null ? 1 : 0).ThenBy(p => key(p) ?? 0);

    static IOrderedEnumerable<PlayerRecord> OrderText(IEnumerable<PlayerRecord> src, Func<PlayerRecord, string> key, bool desc) =>
        desc
            ? src.OrderByDescending(key, StringComparer.InvariantCultureIgnoreCase)
            : src.OrderBy(key, StringComparer.InvariantCultureIgnoreCase);

    static IOrderedEnumerable<PlayerRecord> OrderNonEmptyText(IEnumerable<PlayerRecord> src, Func<PlayerRecord, string> key, bool desc) =>
        desc
            ? src.OrderBy(p => string.IsNullOrEmpty(key(p)) ? 1 : 0).ThenByDescending(key, StringComparer.InvariantCultureIgnoreCase)
            : src.OrderBy(p => string.IsNullOrEmpty(key(p)) ? 1 : 0).ThenBy(key, StringComparer.InvariantCultureIgnoreCase);

    static bool ContainsText(string haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) && Compare.IndexOf(haystack, needle, IgnoreCaseAndAccents) >= 0;
}

/// <summary>Keeps the sort order while filters change; replacing the source or sort invalidates it.</summary>
public sealed class PlayerSearchIndex
{
    readonly object gate = new();
    IReadOnlyList<object>? source;
    PlayerSortSpec? sort;
    IReadOnlyList<PlayerRecord> ordered = [];

    public IReadOnlyList<PlayerRecord> Filter(IReadOnlyList<object> rows, PlayersModule module, PlayerFilter filter, PlayerSortSpec order, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(source, rows) || sort != order)
            {
                var sorted = module.FilterAndSort(rows.OfType<PlayerRecord>(), new PlayerFilter(), order);
                cancellationToken.ThrowIfCancellationRequested();
                ordered = sorted;
                source = rows;
                sort = order;
            }
            var result = new List<PlayerRecord>();
            foreach (var player in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (module.MatchesFilter(player, filter)) result.Add(player);
            }
            return result;
        }
    }
}

/// <summary>Runs only the newest pending search and never returns an obsolete result.</summary>
public sealed class LatestSearch
{
    readonly object gate = new();
    CancellationTokenSource? current;

    public async Task<T?> RunAsync<T>(Func<CancellationToken, T> search, TimeSpan delay) where T : class
    {
        CancellationTokenSource request;
        lock (gate)
        {
            current?.Cancel();
            current = request = new CancellationTokenSource();
        }
        try
        {
            await Task.Delay(delay, request.Token).ConfigureAwait(false);
            var result = await Task.Run(() => search(request.Token), request.Token).ConfigureAwait(false);
            lock (gate) return ReferenceEquals(current, request) && !request.IsCancellationRequested ? result : null;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { return null; }
        finally
        {
            lock (gate) { if (ReferenceEquals(current, request)) current = null; }
            request.Dispose();
        }
    }
}
public static class PlayerFields
{
    static string Label(string s) => s switch
    {
        "FreeKicks" => "Free Kick Taking", "TendencyToPunch" => "Punching (Tendency)", "OneOnOnes" => "One On Ones",
        _ => Regex.Replace(s, "(?<!^)([A-Z])", " $1")
    };
    public static IReadOnlyList<FieldDescriptor<PlayerRecord>> All { get; } = Create();
    static IReadOnlyList<FieldDescriptor<PlayerRecord>> Create()
    {
        var fields = new List<FieldDescriptor<PlayerRecord>> {
            new("CA", "CA", "Ability", FieldKind.Int, 1, 200, p => p.CurrentAbility, (w,p,v) => w.SetCurrentAbility(p,v)),
            new("PA", "PA", "Ability", FieldKind.Int, 1, 200, p => p.PotentialAbility, (w,p,v) => w.SetPotentialAbility(p,v)),
            new("HomeReputation", "Home", "Reputation", FieldKind.Int, 0, 10000, p => p.HomeReputation, (w,p,v) => w.SetReputation(p,Reputation.Home,v)),
            new("CurrentReputation", "Current", "Reputation", FieldKind.Int, 0, 10000, p => p.CurrentReputation, (w,p,v) => w.SetReputation(p,Reputation.Current,v)),
            new("WorldReputation", "World", "Reputation", FieldKind.Int, 0, 10000, p => p.WorldReputation, (w,p,v) => w.SetReputation(p,Reputation.World,v)),
            new("Height", "Height", "Body", FieldKind.Int, 100, 250, p => p.Height, (w,p,v) => w.SetHeight(p,v)),
            // Date fields carry yyyymmdd so the draft stays an int.
            new("Born", "Born", "Body", FieldKind.Date, 19000101, 21001231, p => DateField.ToInt(p.DateOfBirth), (w,p,v) => w.SetDateOfBirth(p, DateField.FromInt(v)))
        };
        foreach (var a in Enum.GetValues<PlayerAttribute>()) fields.Add(new(a.ToString(), Label(a.ToString()), PlayerAttributeInfo.GroupOf(a).ToString(), FieldKind.Rating20, 1, 20, p => p.Attribute(a), (w,p,v) => w.SetAttribute(p,a,v)));
        foreach (var a in Enum.GetValues<PersonalityAttribute>()) fields.Add(new(a.ToString(), Label(a.ToString()), "Personality", FieldKind.Rating20, 1, 20, p => p.PersonalityValue(a), (w,p,v) => w.SetPersonality(p,a,v)));
        foreach (var a in Enum.GetValues<PlayerPosition>()) fields.Add(new(a.ToString(), Label(a.ToString()), "Positions", FieldKind.Rating20, 1, 20, p => p.Position(a), (w,p,v) => w.SetPosition(p,a,v)));
        return fields;
    }
}

/// <summary>Dates in <see cref="FieldDescriptor{T}"/> drafts are ints written yyyymmdd; 0 means no date.</summary>
public static class DateField
{
    public static int ToInt(DateOnly? d) => d is { } x ? x.Year * 10000 + x.Month * 100 + x.Day : 0;
    public static DateOnly FromInt(int v) => new(v / 10000, v / 100 % 100, v % 100);
    public static bool TryParse(string? text, out int value)
    {
        value = 0;
        if (!DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)) return false;
        value = ToInt(d);
        return true;
    }
}

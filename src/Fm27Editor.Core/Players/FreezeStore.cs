using System.Text.Json;
using System.Text.Json.Serialization;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Players;

/// <summary>Portable freeze data: IDs and values only, never addresses from a previous session.</summary>
public sealed record SavedFrozenPlayer(int Id, FrozenAttribute[] Fields);
public sealed record SavedFrozenClub(int Id, long Balance, FreezeMode Mode);
public sealed record SavedFrozenStaff(int Id, FrozenStaffAttribute[] Fields);
public sealed record SavedFreezes(SavedFrozenPlayer[] Players, SavedFrozenClub[] Clubs, SavedFrozenStaff[]? Staff = null);

[JsonSerializable(typeof(Dictionary<string, SavedFreezes>))]
internal partial class FreezeJsonContext : JsonSerializerContext;

public static class FreezeStore
{
    /// <summary>Like FMLibreEditor, keys a career by storefront and its human-manager IDs.</summary>
    public static string SaveKey(GameSession session)
    {
        string store = session.Profile.Build.Contains("Game Pass", StringComparison.OrdinalIgnoreCase) ? "gamepass" : "steam";
        int[] ids = session.Staff.LoadAll().Where(s => s.Kind.Manager).Select(s => s.Id).Order().ToArray();
        if (ids.Length == 0 || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Length)
            throw new InvalidOperationException("Cannot identify the save's human managers");
        return store + ":" + string.Join(',', ids);
    }

    public static SavedFreezes Snapshot(IEnumerable<FrozenPlayer> players, IEnumerable<ClubBalanceFreezer> clubs, IEnumerable<FrozenStaff>? staff = null) =>
        new(players.Select(p => new SavedFrozenPlayer(p.Id, p.Fields.ToArray())).ToArray(),
            clubs.Select(c => new SavedFrozenClub(c.Id, c.Value, c.Mode)).ToArray(),
            (staff ?? []).Select(s => new SavedFrozenStaff(s.Id, s.Fields.ToArray())).ToArray());

    public static SavedFreezes Load(string path, string key)
    {
        var all = Read(path);
        return all.TryGetValue(key, out var entry) ? entry : new([], []);
    }

    public static void Save(string path, string key, SavedFreezes snapshot)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Save key is required", nameof(key));
        var all = Read(path);
        all[key] = snapshot;
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(all, FreezeJsonContext.Default.DictionaryStringSavedFreezes));
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    static Dictionary<string, SavedFreezes> Read(string path) => !File.Exists(path) ? [] :
        JsonSerializer.Deserialize(File.ReadAllText(path), FreezeJsonContext.Default.DictionaryStringSavedFreezes)
        ?? throw new InvalidDataException("Freeze data is empty");

    /// <summary>Resolve saved IDs against fresh records. A missing or duplicate ID is not rebound.</summary>
    public static (IReadOnlyList<FrozenPlayer> Players, IReadOnlyList<ClubBalanceFreezer> Clubs, IReadOnlyList<FrozenStaff> Staff) Resolve(
        SavedFreezes saved, IEnumerable<PlayerRecord> players, IEnumerable<ClubEntry> clubs, IEnumerable<StaffRecord>? staff = null)
    {
        var playerIds = players.Where(p => p.Id is > 0).GroupBy(p => p.Id!.Value).ToDictionary(g => g.Key, g => g.ToArray());
        var clubIds = clubs.Where(c => c.Id is > 0).GroupBy(c => c.Id!.Value).ToDictionary(g => g.Key, g => g.ToArray());
        var staffIds = (staff ?? []).Where(s => s.Id > 0).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.ToArray());
        var restoredPlayers = new List<FrozenPlayer>();
        var restoredClubs = new List<ClubBalanceFreezer>();
        var restoredStaff = new List<FrozenStaff>();
        foreach (var p in saved.Players)
            if (playerIds.TryGetValue(p.Id, out var found) && found.Length == 1)
                restoredPlayers.Add(new FrozenPlayer(p.Id, found[0].Address, p.Fields));
        foreach (var c in saved.Clubs)
            if (clubIds.TryGetValue(c.Id, out var found) && found.Length == 1)
                restoredClubs.Add(new ClubBalanceFreezer(c.Id, found[0].Address, c.Balance, c.Mode));
        if (saved.Staff is not null)
        {
            foreach (var s in saved.Staff)
                if (staffIds.TryGetValue(s.Id, out var found) && found.Length == 1)
                    restoredStaff.Add(new FrozenStaff(s.Id, found[0].Address, found[0].StaffAddress, s.Fields));
        }
        return (restoredPlayers, restoredClubs, restoredStaff);
    }
}

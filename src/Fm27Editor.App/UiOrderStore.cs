using System.Text.Json;

namespace Fm27Editor.App;

/// <summary>Remembers the section order the user dragged into place, per entity kind (player, staff, club...).</summary>
static class UiOrderStore
{
    static readonly object Gate = new();
    static Dictionary<string, List<string>>? cache;

    static string FilePath => Path.Combine(AppContext.BaseDirectory, "ui-order.json");

    static Dictionary<string, List<string>> Load()
    {
        if (cache is not null) return cache;
        lock (Gate)
        {
            if (cache is not null) return cache;
            try
            {
                cache = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(FilePath)) ?? []
                    : [];
            }
            catch (Exception)
            {
                cache = [];
            }
            return cache;
        }
    }

    public static IReadOnlyList<string> Order(string entity, IReadOnlyList<string> known)
    {
        var stored = Load();
        if (!stored.TryGetValue(entity, out var saved) || saved.Count == 0) return known;
        var kept = saved.Where(known.Contains).Distinct().ToList();
        kept.AddRange(known.Where(k => !kept.Contains(k)));
        return kept;
    }

    public static void Save(string entity, IReadOnlyList<string> order)
    {
        var stored = Load();
        stored[entity] = [.. order];
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception) { }
    }
}

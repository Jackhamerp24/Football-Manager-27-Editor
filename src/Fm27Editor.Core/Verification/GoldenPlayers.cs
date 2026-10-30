using System.Text.Json;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Verification;

/// <summary>Players whose values were copied from the game UI; the independent oracle for offsets.</summary>
public sealed record GoldenPlayer(
    string Name, string? FirstName, string? LastName, string? CommonName, bool NoCommonName,
    int? Height, DateOnly? DateOfBirth, IReadOnlyDictionary<PlayerAttribute, int> Attributes)
{
    /// <summary>Club name as the player profile header shows it.</summary>
    public string? Club { get; init; }
    /// <summary>Weekly wage; only when the game shows an exact figure, not a scouted range.</summary>
    public int? Wage { get; init; }
    public DateOnly? ContractExpires { get; init; }
}

public static class GoldenPlayers
{
    public static IReadOnlyList<GoldenPlayer> Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("players").EnumerateArray().Select(p => new GoldenPlayer(
            p.GetProperty("name").GetString()!,
            Opt(p, "firstName"), Opt(p, "lastName"), Opt(p, "commonName"),
            p.TryGetProperty("noCommonName", out var nc) && nc.GetBoolean(),
            p.TryGetProperty("height", out var h) ? h.GetInt32() : null,
            p.TryGetProperty("dateOfBirth", out var d) ? DateOnly.Parse(d.GetString()!) : null,
            p.GetProperty("attributes").EnumerateObject().ToDictionary(a => Enum.Parse<PlayerAttribute>(a.Name), a => a.Value.GetInt32())
        )
        {
            Club = Opt(p, "club"),
            Wage = p.TryGetProperty("wage", out var w) ? w.GetInt32() : null,
            ContractExpires = p.TryGetProperty("contractExpires", out var e) ? DateOnly.Parse(e.GetString()!) : null,
        }).ToList();
    }

    static string? Opt(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() : null;

    /// <summary>Field-by-field differences between a decoded player and its golden values.</summary>
    public static List<string> Compare(GoldenPlayer g, PlayerRecord p)
    {
        var diffs = new List<string>();
        void Check<T>(string field, T? expected, T actual) where T : struct
        {
            if (expected is { } e && !e.Equals(actual)) diffs.Add($"{field}: expected {e}, read {actual}");
        }
        void CheckText(string field, string? expected, string? actual)
        {
            if (expected is not null && expected != actual) diffs.Add($"{field}: expected '{expected}', read '{actual}'");
        }
        CheckText("FirstName", g.FirstName, p.FirstName);
        CheckText("LastName", g.LastName, p.LastName);
        CheckText("CommonName", g.CommonName, p.CommonName);
        if (g.NoCommonName && !string.IsNullOrEmpty(p.CommonName)) diffs.Add($"CommonName: expected none, read '{p.CommonName}'");
        if (g.Name != p.DisplayName) diffs.Add($"DisplayName: expected '{g.Name}', read '{p.DisplayName}'");
        Check("Height", g.Height, (int)p.Height);
        Check("DateOfBirth", g.DateOfBirth, p.DateOfBirth ?? default);
        foreach (var (a, v) in g.Attributes) Check(a.ToString(), (int?)v, p.Attribute(a));
        CheckText("Club", g.Club, p.Club?.DisplayName);
        Check("Wage", g.Wage, p.Contract?.Wage ?? -1);
        Check("ContractExpires", g.ContractExpires, p.Contract?.Expires ?? default);
        return diffs;
    }
}

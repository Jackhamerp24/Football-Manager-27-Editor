using System.Globalization;
using System.Text.Json;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

/// <summary>A portable file contains player attributes and personality ratings only.</summary>
public static class PlayerAttributeFile
{
    public static string Export(PlayerRecord player, bool csv)
    {
        var values = Enum.GetValues<PlayerAttribute>().ToDictionary(a => a.ToString(), player.Attribute);
        foreach (var attribute in Enum.GetValues<PersonalityAttribute>()) values[$"Personality.{attribute}"] = player.PersonalityValue(attribute);
        return csv
            ? "Attribute,Value\n" + string.Join("\n", values.Select(v => $"{v.Key},{v.Value.ToString(CultureInfo.InvariantCulture)}")) + "\n"
            : JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true });
    }

    public static IReadOnlyDictionary<string, int> Parse(string content, bool csv)
    {
        Dictionary<string, int> values;
        if (csv)
        {
            var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 || lines[0].Trim().TrimStart('\uFEFF') != "Attribute,Value") throw new FormatException("Expected Attribute,Value header");
            values = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || !values.TryAdd(parts[0].Trim(), value))
                    throw new FormatException("Invalid or duplicate attribute");
            }
        }
        else values = JsonSerializer.Deserialize<Dictionary<string, int>>(content) ?? throw new FormatException("Invalid JSON");
        var attributes = Enum.GetValues<PlayerAttribute>();
        var personalities = Enum.GetValues<PersonalityAttribute>();
        if (values.Count != attributes.Length + personalities.Length ||
            attributes.Any(a => !values.TryGetValue(a.ToString(), out int value) || value is < 1 or > 20) ||
            personalities.Any(a => !values.TryGetValue($"Personality.{a}", out int value) || value is < 0 or > 20))
            throw new FormatException("The file must contain every attribute; attribute values are 1–20 and personality values are 0–20");
        return values;
    }

    public static void Stage(PlayerDraft draft, IReadOnlyDictionary<string, int> values)
    {
        foreach (var attribute in Enum.GetValues<PlayerAttribute>())
        {
            var field = PlayerFields.All.First(f => f.Id == attribute.ToString() && f.Kind == FieldKind.Rating20);
            draft.Stage(field, values[attribute.ToString()]);
        }
        foreach (var personality in Enum.GetValues<PersonalityAttribute>())
        {
            if (values[$"Personality.{personality}"] == 0) continue;
            var field = PlayerFields.All.First(f => f.Id == personality.ToString() && f.Group == "Personality");
            draft.Stage(field, values[$"Personality.{personality}"]);
        }
    }
}

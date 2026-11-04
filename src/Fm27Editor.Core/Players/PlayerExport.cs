using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Fm27Editor.Core.Players;

/// <summary>Exports player snapshots in their supplied order without exposing process addresses.</summary>
public static class PlayerExport
{
    sealed record Column(string Name, Func<PlayerRecord, object?> Value);

    static readonly Column[] Columns = BuildColumns();

    static Column[] BuildColumns()
    {
        var columns = new List<Column>
        {
            new("Id", p => p.Id), new("Name", p => p.DisplayName), new("FullName", p => p.FullName),
            new("Club", p => p.Club?.DisplayName), new("Nationality", p => p.Nationality?.DisplayName),
            new("Division", p => p.Division?.DisplayName), new("Age", p => p.Age),
            new("DateOfBirth", p => p.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("GameDate", p => p.GameDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("CurrentAbility", p => p.CurrentAbility), new("PotentialAbility", p => p.PotentialAbility),
            new("HomeReputation", p => p.HomeReputation), new("CurrentReputation", p => p.CurrentReputation),
            new("WorldReputation", p => p.WorldReputation), new("HeightCm", p => p.Height),
            new("WeeklyWage", p => p.Contract?.Wage),
            new("ContractExpires", p => p.Contract?.Expires?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("JoinedClub", p => p.Contract?.Joined?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        };
        columns.AddRange(Enum.GetValues<PlayerAttribute>().Select(a => new Column($"Attribute.{a}", p => p.Attribute(a))));
        columns.AddRange(Enum.GetValues<PlayerPosition>().Select(a => new Column($"Position.{a}", p => p.Position(a))));
        columns.AddRange(Enum.GetValues<PersonalityAttribute>().Select(a => new Column($"Personality.{a}", p => p.PersonalityValue(a))));
        return columns.ToArray();
    }

    /// <summary>Writes UTF-8 CSV with a BOM for spreadsheet compatibility. The caller owns the stream.</summary>
    public static void WriteCsv(Stream output, IEnumerable<PlayerRecord> players)
    {
        using var writer = new StreamWriter(output, new UTF8Encoding(true), 16384, leaveOpen: true) { NewLine = "\r\n" };
        writer.WriteLine(string.Join(',', Columns.Select(c => Csv(c.Name))));
        foreach (var player in players)
            writer.WriteLine(string.Join(',', Columns.Select(c => Csv(Convert.ToString(c.Value(player), CultureInfo.InvariantCulture) ?? ""))));
    }

    static string Csv(string value) => value.IndexOfAny([',', '"', '\r', '\n']) < 0
        ? value : '"' + value.Replace("\"", "\"\"") + '"';

    /// <summary>Writes a JSON array with the same fields and order as CSV. The caller owns the stream.</summary>
    public static void WriteJson(Stream output, IEnumerable<PlayerRecord> players)
    {
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });
        writer.WriteStartArray();
        foreach (var player in players)
        {
            writer.WriteStartObject();
            foreach (var column in Columns)
            {
                writer.WritePropertyName(column.Name);
                switch (column.Value(player))
                {
                    case null: writer.WriteNullValue(); break;
                    case string s: writer.WriteStringValue(s); break;
                    case int n: writer.WriteNumberValue(n); break;
                    case short n: writer.WriteNumberValue(n); break;
                    case byte n: writer.WriteNumberValue(n); break;
                    default: throw new InvalidOperationException($"Unsupported export field {column.Name}");
                }
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}

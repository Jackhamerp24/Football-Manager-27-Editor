using System.Globalization;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.App;

public sealed class NationPreferenceDraft
{
    readonly Dictionary<ulong, (NationPreference Row, string[] Text)> changes = [];
    public int Count => changes.Count;
    static string[] Original(NationPreference row) => [row.Rating.ToString(CultureInfo.InvariantCulture), row.Knowledge.ToString(CultureInfo.InvariantCulture), row.MinAge.ToString(CultureInfo.InvariantCulture), row.MaxAge.ToString(CultureInfo.InvariantCulture)];
    public string[] TextFor(NationPreference row) => changes.TryGetValue(row.Address, out var d) ? (string[])d.Text.Clone() : Original(row);
    public void Set(NationPreference row, int column, string text)
    {
        var snapshot = changes.TryGetValue(row.Address, out var old) ? old.Row : row;
        var values = changes.TryGetValue(row.Address, out old) ? old.Text : Original(row);
        values[column] = text;
        if (values.SequenceEqual(Original(snapshot))) changes.Remove(row.Address); else changes[row.Address] = (snapshot, values);
    }
    public bool TryParse(out IReadOnlyList<(NationPreference Row, int Rating, int Knowledge, int MinAge, int MaxAge)> values)
    {
        var result = new List<(NationPreference, int, int, int, int)>();
        foreach (var (row, fields) in changes.Values)
        {
            if (!fields.All(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))) { values = []; return false; }
            var v = fields.Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            if (v[0] is < -128 or > 127 || v[1] is < 0 or > 255 || v[2] is < 0 or > 255 || v[3] is < 0 or > 255 || v[2] > v[3]) { values = []; return false; }
            result.Add((row, v[0], v[1], v[2], v[3]));
        }
        values = result;
        return true;
    }
}

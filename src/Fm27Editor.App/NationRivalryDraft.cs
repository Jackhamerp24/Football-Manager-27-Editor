using System.Globalization;
using Fm27Editor.Core.Nations;

namespace Fm27Editor.App;

/// <summary>Pending rival values retain the snapshot from the first edit until save or discard.</summary>
public sealed class NationRivalryDraft
{
    readonly Dictionary<ulong, (NationRivalry Snapshot, string Text)> changes = [];
    public int Count => changes.Count;
    public string TextFor(NationRivalry row) => changes.TryGetValue(row.Address, out var c)
        ? c.Text : row.Value.ToString(CultureInfo.InvariantCulture);
    public void Set(NationRivalry row, string text)
    {
        var snapshot = changes.TryGetValue(row.Address, out var old) ? old.Snapshot : row;
        if (text == snapshot.Value.ToString(CultureInfo.InvariantCulture)) changes.Remove(row.Address);
        else changes[row.Address] = (snapshot, text);
    }
    public bool TryParse(out IReadOnlyList<(NationRivalry Snapshot, int Value)> values)
    {
        var result = new List<(NationRivalry, int)>();
        foreach (var (snapshot, text) in changes.Values)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value is < 0 or > 100)
            { values = []; return false; }
            result.Add((snapshot, value));
        }
        values = result;
        return true;
    }
}

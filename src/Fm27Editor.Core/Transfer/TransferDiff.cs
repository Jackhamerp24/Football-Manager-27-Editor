namespace Fm27Editor.Core.Transfer;

/// <summary>One field that differs between two snapshots. Pointers are reported before narrower ints.</summary>
public sealed record SlotChange(
    string Section, string? Name, int Offset, int Width,
    string Before, string After,
    long? IntBefore, long? IntAfter,
    ulong? PointerBefore, ulong? PointerAfter);

/// <summary>
/// Compares two snapshots of the same player: object bytes (player, contract, pending contract, club),
/// the clause table, and the money fields. Offsets are relative to the object, so a capture survives a
/// save reload even though every address changed.
/// </summary>
public static class TransferDiff
{
    static readonly string[] Sections = ["player", "contract", "pending", "club"];

    public static IReadOnlyList<SlotChange> Compare(TransferSnapshot before, TransferSnapshot after)
    {
        var changes = new List<SlotChange>();
        foreach (var section in Sections)
            if (before.Object(section) is { } a && after.Object(section) is { } b)
                CompareBytes(section, a, b, changes);
        CompareClauses(before, after, changes);
        CompareFinance(before.Finance, after.Finance, changes);
        return changes;
    }

    static void CompareBytes(string section, ObjectDump a, ObjectDump b, List<SlotChange> changes)
    {
        var x = Convert.FromHexString(a.Hex);
        var y = Convert.FromHexString(b.Hex);
        int n = Math.Min(x.Length, y.Length), off;
        var covered = new bool[n];

        for (off = 0; off + 8 <= n; off += 8)
        {
            if (x.AsSpan(off, 8).SequenceEqual(y.AsSpan(off, 8))) continue;
            ulong pa = BitConverter.ToUInt64(x, off), pb = BitConverter.ToUInt64(y, off);
            if (!Pointerish(pa) && !Pointerish(pb)) continue;
            changes.Add(new(section, null, off, 8, Hex(x, off, 8), Hex(y, off, 8), null, null, pa, pb));
            for (int i = 0; i < 8; i++) covered[off + i] = true;
        }
        for (off = 0; off + 4 <= n; off += 4)
        {
            if (covered[off] || x.AsSpan(off, 4).SequenceEqual(y.AsSpan(off, 4))) continue;
            changes.Add(new(section, null, off, 4, Hex(x, off, 4), Hex(y, off, 4),
                BitConverter.ToInt32(x, off), BitConverter.ToInt32(y, off), null, null));
        }
    }

    static void CompareClauses(TransferSnapshot before, TransferSnapshot after, List<SlotChange> changes)
    {
        int n = Math.Min(before.Clauses.Count, after.Clauses.Count);
        for (int i = 0; i < n; i++)
        {
            var a = before.Clauses[i];
            var b = after.Clauses[i];
            if (a.Type == b.Type && a.Value == b.Value) continue;
            changes.Add(new("clause", $"type {a.Type}->{b.Type}", i * 8, 4,
                a.Value.ToString(), b.Value.ToString(), a.Value, b.Value, null, null));
        }
        for (int i = n; i < before.Clauses.Count; i++)
            changes.Add(new("clause", $"type {before.Clauses[i].Type} removed", i * 8, 4,
                before.Clauses[i].Value.ToString(), "-", before.Clauses[i].Value, null, null, null));
        for (int i = n; i < after.Clauses.Count; i++)
            changes.Add(new("clause", $"type {after.Clauses[i].Type} added", i * 8, 4,
                "-", after.Clauses[i].Value.ToString(), null, after.Clauses[i].Value, null, null));
    }

    static void CompareFinance(Players.ClubFinance? a, Players.ClubFinance? b, List<SlotChange> changes)
    {
        if (a is null || b is null) return;
        Field("balance", a.Balance, b.Balance);
        Field("transferBudget", a.TransferBudget, b.TransferBudget);
        Field("spentThisSeason", a.SpentThisSeason, b.SpentThisSeason);
        Field("wageSpend", a.WageSpend, b.WageSpend);

        void Field(string name, int? pa, int? pb)
        {
            if (pa == pb) return;
            changes.Add(new("finance", name, 0, 4, pa?.ToString() ?? "-", pb?.ToString() ?? "-", pa, pb, null, null));
        }
    }

    /// <summary>A qword only counts as a pointer when its high half is a small user-space page number,
    /// so two packed int32 fields are never reported as one pointer.</summary>
    static bool Pointerish(ulong v) => v >= 0x1_0000 && (v >> 32) is >= 0x1 and <= 0x7FFF;

    static string Hex(byte[] bytes, int offset, int length) => Convert.ToHexString(bytes, offset, length);
}

using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Players;

/// <summary>Applies disjoint writes after checking snapshots; restores completed writes if a later write fails.</summary>
public static class FutureTransferTransaction
{
    public static void Apply(IProcessMemory memory, IReadOnlyList<PlannedWrite> guards, IReadOnlyList<PlannedWrite> writes)
    {
        for (int i = 0; i < writes.Count; i++)
        {
            var w = writes[i];
            if (w.Address == 0 || w.Value.Length == 0 || w.Value.Length != w.Expected.Length)
                throw new ArgumentException("Invalid planned write", nameof(writes));
            ulong end = checked(w.Address + (ulong)w.Value.Length);
            for (int j = 0; j < i; j++)
                if (w.Address < checked(writes[j].Address + (ulong)writes[j].Value.Length) && writes[j].Address < end)
                    throw new ArgumentException("Overlapping planned writes", nameof(writes));
        }
        foreach (var g in guards) Check(memory, g);
        foreach (var w in writes) Check(memory, w);
        var completed = new List<PlannedWrite>();
        try
        {
            foreach (var w in writes)
            {
                Check(memory, w);
                if (!memory.TryWrite(w.Address, w.Value)) throw new MemoryAccessException($"Could not write {w.What}; the failed write may have been partial");
                completed.Add(w);
                if (!memory.Read(w.Address, w.Value.Length).AsSpan().SequenceEqual(w.Value))
                    throw new MemoryAccessException($"Could not verify {w.What}");
            }
        }
        catch (Exception error)
        {
            var rollbackErrors = new List<Exception>();
            for (int i = completed.Count - 1; i >= 0; i--)
            {
                var w = completed[i];
                try
                {
                    if (!memory.Read(w.Address, w.Value.Length).AsSpan().SequenceEqual(w.Value) ||
                        !memory.TryWrite(w.Address, w.Expected) ||
                        !memory.Read(w.Address, w.Expected.Length).AsSpan().SequenceEqual(w.Expected))
                        throw new MemoryAccessException($"Could not safely restore {w.What}");
                }
                catch (Exception rollback) { rollbackErrors.Add(rollback); }
            }
            if (rollbackErrors.Count != 0) throw new AggregateException("Future-transfer write failed; rollback incomplete", [error, .. rollbackErrors]);
            throw;
        }
    }

    static void Check(IProcessMemory memory, PlannedWrite w)
    {
        if (w.Address == 0 || w.Expected.Length == 0 || !memory.Read(w.Address, w.Expected.Length).AsSpan().SequenceEqual(w.Expected))
            throw new StaleObjectException($"{w.What} changed since it was read");
    }
}

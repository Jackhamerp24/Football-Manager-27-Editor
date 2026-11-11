namespace Fm27Editor.Core.MassEdit;

/// <summary>Struct-of-arrays plan storage: one slot per changed or refused (target, field) pair.</summary>
public sealed class MassPlanBuffer
{
    public const byte FlagChange = 0, FlagRefused = 1;

    int[] target = new int[64];
    ushort[] field = new ushort[64];
    long[] before = new long[64];
    long[] after = new long[64];
    byte[] flags = new byte[64];
    readonly Dictionary<int, string> problems = [];

    public int Count { get; private set; }
    public int Capacity => target.Length;
    public int RefusedCount { get; private set; }
    public int ChangeCount => Count - RefusedCount;

    public int TargetIndex(int row) => target[row];
    public ushort FieldId(int row) => field[row];
    public long Before(int row) => before[row];
    public long After(int row) => after[row];
    public byte Flag(int row) => flags[row];
    public bool CanApply(int row) => flags[row] == FlagChange;
    public string? Problem(int row) => problems.TryGetValue(row, out var text) ? text : null;

    public void Add(int targetIndex, ushort fieldId, long beforeValue, long afterValue)
    {
        Ensure(Count + 1);
        target[Count] = targetIndex;
        field[Count] = fieldId;
        before[Count] = beforeValue;
        after[Count] = afterValue;
        flags[Count] = FlagChange;
        Count++;
    }

    public void AddRefused(int targetIndex, ushort fieldId, long beforeValue, long afterValue, string problem)
    {
        Add(targetIndex, fieldId, beforeValue, afterValue);
        flags[Count - 1] = FlagRefused;
        problems[Count - 1] = problem;
        RefusedCount++;
    }

    void Ensure(int needed)
    {
        if (needed <= target.Length) return;
        int size = target.Length;
        while (size < needed) size *= 2;
        Array.Resize(ref target, size);
        Array.Resize(ref field, size);
        Array.Resize(ref before, size);
        Array.Resize(ref after, size);
        Array.Resize(ref flags, size);
    }
}

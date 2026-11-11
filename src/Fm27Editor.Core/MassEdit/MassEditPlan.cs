namespace Fm27Editor.Core.MassEdit;

/// <summary>Compact result of planning: target metadata stays in the caller's list; names resolve lazily.</summary>
public sealed class MassEditPlan(int targetCount, MassPlanBuffer changes, Func<int, string> nameOf)
{
    public int TargetCount { get; } = targetCount;
    public MassPlanBuffer Changes { get; } = changes;
    public Func<int, string> NameOf { get; } = nameOf;

    public int Rows => Changes.Count;
    public int ChangesCount => Changes.ChangeCount;
    public int RefusedCount => Changes.RefusedCount;
}

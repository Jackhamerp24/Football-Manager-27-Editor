using System.Collections;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.MassEdit;

public readonly record struct MassReviewRow(
    int TargetIndex,
    ushort FieldId,
    string Name,
    string Field,
    long Before,
    long After,
    string? Problem,
    bool CanApply,
    bool Selected);

/// <summary>
/// Read-only view over a plan with selection and optional ordering. Rows are created when a UI asks for them;
/// the plan itself stays struct-of-arrays.
/// </summary>
public sealed class MassReviewModel : IReadOnlyList<MassReviewRow>
{
    readonly MassEditPlan plan;
    readonly Dictionary<ushort, string> labels = [];
    readonly BitArray selected;
    int[]? order;

    public MassReviewModel(MassEditPlan plan, IEnumerable<(ushort FieldId, string Label)>? fields = null)
    {
        this.plan = plan;
        selected = new BitArray(plan.Changes.Count);
        if (fields is not null)
            foreach (var (id, label) in fields) labels[id] = label;
        for (int i = 0; i < plan.Changes.Count; i++)
            if (plan.Changes.CanApply(i)) selected[i] = true;
    }

    public MassEditPlan Plan => plan;
    public int Count => plan.Changes.Count;

    public MassReviewRow this[int index]
    {
        get
        {
            int row = order is null ? index : order[index];
            var changes = plan.Changes;
            int targetIndex = changes.TargetIndex(row);
            return new MassReviewRow(
                targetIndex,
                changes.FieldId(row),
                plan.NameOf(targetIndex),
                labels.TryGetValue(changes.FieldId(row), out var label) ? label : $"field {changes.FieldId(row)}",
                changes.Before(row),
                changes.After(row),
                changes.Problem(row),
                changes.CanApply(row),
                selected[row]);
        }
    }

    public int SelectedCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < selected.Length; i++)
                if (selected[i] && plan.Changes.CanApply(i)) count++;
            return count;
        }
    }

    public bool IsRowSelected(int row) => selected[row] && plan.Changes.CanApply(row);

    public void SetSelected(int displayIndex, bool value)
    {
        int row = order is null ? displayIndex : order[displayIndex];
        if (plan.Changes.CanApply(row)) selected[row] = value;
    }

    public void SelectAll(bool value)
    {
        for (int i = 0; i < selected.Length; i++)
            selected[i] = value && plan.Changes.CanApply(i);
    }

    public void InvertSelection()
    {
        for (int i = 0; i < selected.Length; i++)
            if (plan.Changes.CanApply(i)) selected[i] = !selected[i];
    }

    public void SortByField(bool descending)
    {
        order = Enumerable.Range(0, plan.Changes.Count).ToArray();
        Array.Sort(order, (a, b) =>
        {
            int c = plan.Changes.FieldId(a).CompareTo(plan.Changes.FieldId(b));
            if (c == 0) c = plan.Changes.TargetIndex(a).CompareTo(plan.Changes.TargetIndex(b));
            return descending ? -c : c;
        });
    }

    public void SortByName(bool descending)
    {
        order = Enumerable.Range(0, plan.Changes.Count).ToArray();
        Array.Sort(order, (a, b) =>
        {
            int c = string.Compare(plan.NameOf(plan.Changes.TargetIndex(a)), plan.NameOf(plan.Changes.TargetIndex(b)),
                StringComparison.OrdinalIgnoreCase);
            return descending ? -c : c;
        });
    }

    public void ClearSort() => order = null;

    public IEnumerator<MassReviewRow> GetEnumerator()
    {
        for (int i = 0; i < Count; i++) yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.MassEdit;

public interface IMassPlanner
{
    Task<MassEditPlan> PlanAsync<T>(IReadOnlyList<T> targets, IReadOnlyList<MassFieldSpec<T>> fields,
        Func<int, string> nameOf, IProgress<MassProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>Plans in batches; no game write and no per-target UI object.</summary>
public sealed class MassPlanner : IMassPlanner
{
    const int BatchSize = 2000;

    public Task<MassEditPlan> PlanAsync<T>(IReadOnlyList<T> targets, IReadOnlyList<MassFieldSpec<T>> fields,
        Func<int, string> nameOf, IProgress<MassProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Plan(targets, fields, nameOf, progress, cancellationToken), cancellationToken);

    static MassEditPlan Plan<T>(IReadOnlyList<T> targets, IReadOnlyList<MassFieldSpec<T>> fields,
        Func<int, string> nameOf, IProgress<MassProgress>? progress, CancellationToken cancellationToken)
    {
        var buffer = new MassPlanBuffer();
        int batch = 0;
        for (int start = 0; start < targets.Count; start += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int end = Math.Min(start + BatchSize, targets.Count);
            for (int i = start; i < end; i++)
            {
                foreach (var field in fields)
                {
                    long? before;
                    try { before = field.Read(targets[i]); }
                    catch (Exception ex) { buffer.AddRefused(i, field.FieldId, 0, 0, ex.Message); continue; }
                    if (before is not { } current) continue;

                    long next;
                    try
                    {
                        next = field.Mode == MassEditMode.Set ? field.Value : checked(current + field.Value);
                        if (next < field.Min || next > field.Max)
                            throw new ArgumentOutOfRangeException(nameof(field), next, $"{field.Label} must be between {field.Min} and {field.Max}");
                        field.Validate?.Invoke(next);
                    }
                    catch (Exception ex) { buffer.AddRefused(i, field.FieldId, current, current, ex.Message); continue; }
                    if (next != current) buffer.Add(i, field.FieldId, current, next);
                }
            }
            progress?.Report(new MassProgress(end, targets.Count, "Planning"));
            batch++;
        }
        return new MassEditPlan(targets.Count, buffer, nameOf);
    }
}

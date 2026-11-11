using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.MassEdit;

public interface IMassApplier
{
    Task<MassResult> ApplyAsync<T>(MassReviewModel model, IReadOnlyList<T> targets, IReadOnlyList<MassFieldSpec<T>> fields,
        IProgress<MassProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Writes selected rows in batches. Stale rows are skipped and reported; any other failure stops the batch immediately.
/// The caller supplies the guarded writers through MassFieldSpec.
/// </summary>
public sealed class MassApplier : IMassApplier
{
    const int BatchSize = 500;

    public Task<MassResult> ApplyAsync<T>(MassReviewModel model, IReadOnlyList<T> targets, IReadOnlyList<MassFieldSpec<T>> fields,
        IProgress<MassProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Apply(model, targets, fields, progress, cancellationToken), cancellationToken);

    static MassResult Apply<T>(MassReviewModel model, IReadOnlyList<T> targets, IReadOnlyList<MassFieldSpec<T>> fields,
        IProgress<MassProgress>? progress, CancellationToken cancellationToken)
    {
        var byId = fields.ToDictionary(field => field.FieldId);
        var skips = new List<MassSkip>();
        int written = 0;
        int done = 0;
        int total = model.Plan.Changes.ChangeCount;

        for (int start = 0; start < model.Plan.Changes.Count; start += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int end = Math.Min(start + BatchSize, model.Plan.Changes.Count);
            for (int row = start; row < end; row++)
            {
                if (!model.IsRowSelected(row)) continue;
                var changes = model.Plan.Changes;
                int targetIndex = changes.TargetIndex(row);
                if (targetIndex < 0 || targetIndex >= targets.Count)
                {
                    skips.Add(new MassSkip(targetIndex, changes.FieldId(row), "Target is outside the prepared list"));
                    continue;
                }
                if (!byId.TryGetValue(changes.FieldId(row), out var field))
                {
                    skips.Add(new MassSkip(targetIndex, changes.FieldId(row), "Field is no longer part of the plan"));
                    continue;
                }
                try
                {
                    field.Write(targets[targetIndex], changes.Before(row), changes.After(row));
                    written++;
                }
                catch (StaleObjectException ex)
                {
                    skips.Add(new MassSkip(targetIndex, changes.FieldId(row), ex.Message));
                }
            }
            done = Math.Min(total, done + (end - start));
            progress?.Report(new MassProgress(done, total, "Applying"));
        }
        return new MassResult(written, skips.Count, skips);
    }
}

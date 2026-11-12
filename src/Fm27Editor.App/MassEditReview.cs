using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

internal sealed partial class MassEditWindow
{
    /// <summary>Builds what each scope would change and opens the review tables; nothing is written until an Apply there.</summary>
    async Task ReviewAsync()
    {
        if (backend.State.Status != ConnectionStatus.Connected) { status.Text = "Not connected"; return; }
        apply.IsEnabled = false;
        try
        {
            // The form's values are copied here, on the window thread; building every plan (one read per
            // target per field) then runs in the background so the window stays responsive.
            SnapshotPlanInputs();
            List<MassPlan> plans;
            try
            {
                plans = await Task.Run(() => new[] { PlayerPlan(), CoachPlan(), ClubPlan(), NationPlan() }.Where(p => p.Rows.Count > 0).ToList());
            }
            finally { ClearPlanSnapshot(); }
            if (plans.Count == 0) { status.Text = "Nothing would change"; return; }
            await new MassEditReviewWindow(plans, WriteAsync).ShowDialog(this);
        }
        catch (Exception ex) { status.Text = UserMessage.For(ex); }
        finally { MarkDirty(); }
    }

    /// <summary>Writes the ticked lines of the given tables as one history group, then reloads their screens.</summary>
    async Task<int> WriteAsync(IReadOnlyList<MassPlan> plans, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        int written = 0;
        int done = 0;
        int total = plans.Sum(p => p.Rows.Count(r => r.Include && r.CanApply));
        progress?.Report((0, total));
        string scopes = string.Join(", ", plans.Select(p => p.Scope));
        try
        {
            await Task.Run(() =>
            {
                backend.Edits.Execute("Mass edit", scopes, $"Mass edit: {scopes}", () =>
                {
                    foreach (var plan in plans)
                        foreach (var row in plan.Rows.Where(r => r.Include && r.CanApply))
                        {
                            ct.ThrowIfCancellationRequested();
                            written += row.Apply!();
                            done++;
                            if (done % 200 == 0) progress?.Report((done, total));
                        }
                });
            }, ct);
        }
        catch (OperationCanceledException) { /* what was written stays in the history group; the caller reports it */ }
        progress?.Report((done, total));
        foreach (var plan in plans) await host.MassEditReloadAsync(plan.Scope);
        // The search catalogue now holds old values; read it again in the background for the next "Choose who to edit".
        if (written > 0 && backend.State.Status == ConnectionStatus.Connected) searchData = Task.Run(LoadSearchData);
        return written;
    }
}

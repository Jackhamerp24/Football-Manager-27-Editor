using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Fm27Editor.Core.Game;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
#if false // Coming soon: Force Accept Transfer is disabled until the mechanism is finished. Re-enable this block and the call in HookManagerView.cs.
    readonly List<(ulong Address, int Status)> forceAcceptSaved = [];

    /// <summary>
    /// Hook Manager section: a data-only on/off for transfer seller acceptance. On writes status 5 (seller accepted)
    /// on the human manager's current offers; Off writes the statuses saved at On back. No code patch and no hook.
    /// </summary>
    Control BuildForceAcceptTransferSection(GameSession session)
    {
        var panel = new StackPanel { Spacing = 6 };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        title.Children.Add(new TextBlock
        {
            Text = "Force Accept Transfer (Experimental)",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 8, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        const string explanation = "The selling clubs accept the transfer offers you have already made. " +
            "Offers you make later are not included: turn it off and on again after a new offer. Turning it off puts the offers back as they were.";
        var info = UiKit.InfoBadge(explanation);
        info.Margin = new Thickness(0, 8, 0, 0);
        title.Children.Add(info);
        panel.Children.Add(title);

        var on = new CheckBox { Content = "On", IsChecked = forceAcceptSaved.Count > 0 };
        var state = new TextBlock
        {
            FontSize = 12.5,
            Text = forceAcceptSaved.Count > 0 ? $"On: {forceAcceptSaved.Count} offer(s) saved" : "Off",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = UiKit.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(on);
        row.Children.Add(state);
        panel.Children.Add(row);

        var saved = forceAcceptSaved;
        bool busy = false;
        on.Click += async (_, _) =>
        {
            if (busy) return;
            bool accept = on.IsChecked == true;
            busy = true;
            on.IsEnabled = false;
            state.Text = accept ? "Reading offers…" : "Restoring…";
            try
            {
                if (accept)
                {
                    var result = await Task.Run(() =>
                    {
                        var clubs = session.Staff.LoadAll()
                            .Where(s => s.ManagerAttributes is not null && s.Club is not null)
                            .Select(s => s.Club!.Address).ToHashSet();
                        if (clubs.Count == 0) throw new InvalidOperationException("No human-manager club found");
                        // An offer made after Load Game may exist only in the heap; scan again rather than
                        // acting on the map cached at Load Game.
                        session.Proposals.Refresh();
                        var offers = session.Proposals.ReadAllForBuyer(clubs)
                            .Where(o => o.Status is 0 or 1 or 2).ToList();
                        saved.Clear();
                        int accepted = 0;
                        foreach (var offer in offers)
                        {
                            saved.Add((offer.Address, offer.Status));
                            session.ProposalWriter.SetStatus(offer, 5);
                            accepted++;
                        }
                        return accepted;
                    });
                    state.Text = result == 0 ? "On: no current offers to accept" : $"On: {result} offer(s) set to seller accepted";
                    status.Text = state.Text;
                }
                else
                {
                    var result = await Task.Run(() =>
                    {
                        int restored = 0, skipped = 0;
                        foreach (var (address, oldStatus) in saved)
                        {
                            var offer = session.Proposals.ReadOffer(address);
                            if (offer is null || offer.Status != 5) { skipped++; continue; }
                            session.ProposalWriter.SetStatus(offer, (byte)oldStatus);
                            restored++;
                        }
                        saved.Clear();
                        return (restored, skipped);
                    });
                    state.Text = result.restored == 0 && result.skipped == 0
                        ? "Off: nothing was saved to restore"
                        : $"Off: restored {result.restored} offer(s)" + (result.skipped > 0 ? $", {result.skipped} changed by the game" : "");
                    status.Text = state.Text;
                }
            }
            catch (Exception ex)
            {
                state.Text = $"Force Accept Transfer: {UserMessage.For(ex)}";
                status.Text = state.Text;
                on.IsChecked = !accept;
            }
            finally
            {
                busy = false;
                on.IsEnabled = true;
            }
        };
        return panel;
    }
#endif
}

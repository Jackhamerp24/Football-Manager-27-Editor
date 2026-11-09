using Avalonia.Threading;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    /// <summary>At start: if GitHub has a newer release, a small window sends the user to the download page.</summary>
    async Task NotifyIfUpdateAsync()
    {
        string? tag = await UpdateCheck.NewerReleaseAsync();
        if (tag is null) return;
        await Dispatcher.UIThread.InvokeAsync(() =>
            ShowNotice("Update available",
                $"A newer version of the editor ({tag}) is out. Download it from GitHub.",
                ("Later", false, null),
                ("Open GitHub", true, () =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateCheck.ReleasesPage) { UseShellExecute = true }); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { status.Text = "Could not open the browser: " + UpdateCheck.ReleasesPage; }
                    return Task.CompletedTask;
                })));
    }
}

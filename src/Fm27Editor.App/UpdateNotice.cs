using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Fm27Editor.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// At start: if GitHub has a newer release, offer to install it. Update now downloads the new exe, checks it against the
    /// release checksum, puts it in place of this one and restarts. Anything that goes wrong leaves this version as it was
    /// and offers the download page instead.
    /// </summary>
    async Task NotifyIfUpdateAsync()
    {
        string? exe = Environment.ProcessPath;
        if (exe is not null && Path.GetFileName(exe).Equals(UpdateCheck.ExeName, StringComparison.OrdinalIgnoreCase))
            await Task.Run(() => UpdateCheck.CleanUp(exe));
        var release = await UpdateCheck.NewerReleaseAsync();
        if (release is null) return;
        await Dispatcher.UIThread.InvokeAsync(() =>
            ShowNotice("Update available",
                $"Version {release.Tag} is out. Update now downloads it, closes the editor and opens the new version. "
                + "Changes you have not saved yet are lost; anything running in the game (Development Speed, Wonderkid Academy) keeps running.",
                ("Later", false, null),
                ("Update now", true, () => UpdateAsync(release))));
    }

    async Task UpdateAsync(ReleaseInfo release)
    {
        string? exe = Environment.ProcessPath;
        // Only the single-file release exe can replace itself; a development build or a renamed copy goes to the download page.
        if (exe is null || !Path.GetFileName(exe).Equals(UpdateCheck.ExeName, StringComparison.OrdinalIgnoreCase))
        {
            OpenReleasesPage();
            return;
        }
        status.Text = $"Downloading {release.Tag}…";
        try
        {
            string downloaded = await UpdateCheck.DownloadAsync(release, exe);
            UpdateCheck.Swap(exe, downloaded);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException
                                       or TaskCanceledException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            status.Text = "Update failed: " + ex.Message;
            ShowNotice("Update failed",
                $"The update could not be installed ({ex.Message}). This version is unchanged. You can download {release.Tag} from GitHub instead.",
                ("Close", false, null),
                ("Open GitHub", true, () => { OpenReleasesPage(); return Task.CompletedTask; }));
            return;
        }
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        else Close();
    }

    void OpenReleasesPage()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateCheck.ReleasesPage) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { status.Text = "Could not open the browser: " + UpdateCheck.ReleasesPage; }
    }
}

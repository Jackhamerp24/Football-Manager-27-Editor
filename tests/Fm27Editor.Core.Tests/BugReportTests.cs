using System.IO.Compression;
using Fm27Editor.Core.Diagnostics;

namespace Fm27Editor.Core.Tests;

public sealed class BugReportTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "fm27-bugreport-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static DateTimeOffset At(int second) => new(2026, 11, 10, 12, 0, second, TimeSpan.Zero);

    [Fact]
    public void A_run_that_closed_normally_and_one_that_did_not_are_told_apart()
    {
        using (var clean = new FlightRecorder(_dir, "1.0", At(1))) clean.Close();
        var crashed = new FlightRecorder(_dir, "1.0", At(2));
        crashed.Log("write", "x");
        // Not closed: this is what a killed process leaves behind. Read it back through a second recorder of the same folder.
        using var now = new FlightRecorder(_dir, "1.0", At(3));
        var ends = now.Sessions().Select(s => s.End).ToArray();
        Assert.Equal([SessionEnd.Clean, SessionEnd.Abrupt, SessionEnd.Running], ends);
        crashed.Dispose();
    }

    [Fact]
    public void Writes_are_logged_with_module_relative_addresses_and_both_values()
    {
        using var log = new FlightRecorder(_dir, "1.0", At(1));
        log.Write(0x7FF000001230, [1, 2], [3, 4], 0x7FF000000000, 0x7FF010000000, "Player: CA");
        log.Write(0x2000, [9], [8], 0x7FF000000000, 0x7FF010000000, null);
        var text = ReadShared(log.Path);
        Assert.Contains("module+0x1230 len 2 was 0102 now 0304 : Player: CA", text);
        Assert.Contains("heap 0x2000 len 1 was 09 now 08", text);
    }

    [Fact]
    public void Only_the_newest_ten_runs_are_kept()
    {
        for (int i = 0; i < 14; i++) new FlightRecorder(_dir, "1.0", At(i)).Close();
        Assert.Equal(FlightRecorder.KeptSessions, Directory.GetFiles(_dir, "session-*.log").Length);
        Assert.False(File.Exists(Path.Combine(_dir, "session-20261110-120000-000.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "session-20261110-120013-000.log")));
    }

    [Fact]
    public void A_log_stops_growing_at_its_size_limit_and_says_so()
    {
        using var log = new FlightRecorder(_dir, "1.0", At(1));
        string block = new('a', 100_000);
        for (int i = 0; i < 30; i++) log.Log("write", block);
        long size = new FileInfo(log.Path).Length;
        Assert.InRange(size, 1, FlightRecorder.MaxBytesPerSession + 200_000);
        Assert.Contains("size limit reached", ReadShared(log.Path));
    }

    [Fact]
    public void The_profile_folder_is_removed_from_text()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.DoesNotContain(profile, FlightRecorder.Redact($"opened {profile}\\Desktop\\x"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("opened …\\x", FlightRecorder.Redact($"opened {profile}\\Desktop\\x"));
    }

    [Theory]
    [InlineData(@"Graphics: D:\Pictures\Nguyen Van A\FM Graphics", "FM Graphics", "Nguyen")]
    [InlineData("Graphics: D:/Pictures/Nguyen Van A/FM Graphics", "FM Graphics", "Nguyen")]
    [InlineData(@"opened \\nas01\share\Nguyen\fm.log done", "fm.log", "Nguyen")]
    [InlineData(@"at C:\Users\someone\src\Thing.cs:line 12", "Thing.cs", "someone")]
    public void Absolute_paths_are_cut_to_their_last_name(string input, string kept, string gone)
    {
        var result = FlightRecorder.Redact(input);
        Assert.Contains(kept, result);
        Assert.DoesNotContain(gone, result);
    }

    [Fact]
    public void Text_without_a_path_is_left_alone()
    {
        Assert.Equal("Player Kobbie Mainoo: Contract (2 changes) 12:30", FlightRecorder.Redact("Player Kobbie Mainoo: Contract (2 changes) 12:30"));
    }

    [Fact]
    public void A_report_made_after_the_editor_was_closed_still_holds_the_earlier_runs()
    {
        var earlier = new FlightRecorder(_dir, "1.0", At(1));
        earlier.Write(0x10, [0], [1], 0, 0x100, "Language: add");
        earlier.Dispose();                                                             // closed normally
        File.WriteAllText(Path.Combine(_dir, "session-20261110-115900-000.log"), "start\nwrite module+0x20 ...\n");   // a run that never wrote an exit line
        using var now = new FlightRecorder(_dir, "1.0", At(5));
        string zipPath = BugReport.Create(now, Path.Combine(_dir, "out"), "Save would not load after adding a language", "1.0", [], [], At(6));

        using var zip = ZipFile.OpenRead(zipPath);
        string Read(string name) { using var r = new StreamReader(zip.GetEntry(name)!.Open()); return r.ReadToEnd(); }
        var report = Read("report.txt");
        Assert.Contains("Save would not load after adding a language", report);
        Assert.Contains("session-20261110-115900-000.log: ended without a normal exit", report);
        Assert.Contains("session-20261110-120001-000.log: ended normally", report);
        Assert.Contains("(not available: the editor was just started", report);
        Assert.Contains("module+0x20", Read("logs/session-20261110-115900-000.log"));
        Assert.Contains("Language: add", Read("logs/session-20261110-120001-000.log"));
    }

    [Fact]
    public void A_report_made_while_the_editor_is_open_carries_the_state_and_the_edits()
    {
        using var now = new FlightRecorder(_dir, "1.0", At(1));
        string zipPath = BugReport.Create(now, Path.Combine(_dir, "out"), "", "1.0", ["Game build: 27.0.0+2493426", "Unsaved changes: 2"],
            ["12:00:01 Player Kobbie Mainoo: Contract (2 changes)"], At(2));
        using var zip = ZipFile.OpenRead(zipPath);
        using var r = new StreamReader(zip.GetEntry("report.txt")!.Open());
        var report = r.ReadToEnd();
        Assert.Contains("Game build: 27.0.0+2493426", report);
        Assert.Contains("Unsaved changes: 2", report);
        Assert.Contains("Kobbie Mainoo: Contract", report);
        Assert.Contains("(nothing written)", report);
        Assert.Contains("this run", report);
    }
}

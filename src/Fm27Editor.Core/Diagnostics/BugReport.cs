using System.IO.Compression;
using System.Text;

namespace Fm27Editor.Core.Diagnostics;

/// <summary>
/// Packs what is needed to find out what damaged a save into one zip the player can send: their description, the state of the editor
/// and game right now (when the editor is still open), and the kept run logs (which also cover runs that already ended, so a report
/// can be made after the editor or the game was closed). Nothing is uploaded and no save file is included.
/// </summary>
public static class BugReport
{
    /// <param name="state">Lines describing the editor and game as they are now; empty when the editor was just started.</param>
    /// <param name="recentEdits">The edit history of the open session ("time kind entity title: changes"), newest last.</param>
    public static string Create(FlightRecorder recorder, string folder, string description, string appVersion,
        IReadOnlyList<string> state, IReadOnlyList<string> recentEdits, DateTimeOffset? now = null)
    {
        var time = now ?? DateTimeOffset.Now;
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"Fm27Editor-bug-report-{time:yyyyMMdd-HHmmss}.zip");
        recorder.Log("report", "bug report created");
        var sessions = recorder.Sessions();

        var text = new StringBuilder();
        text.AppendLine($"Football Manager 27 Editor {appVersion}");
        text.AppendLine($"Created {time:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine($"{Environment.OSVersion}; .NET {Environment.Version}");
        text.AppendLine();
        text.AppendLine("What happened (written by the player):");
        text.AppendLine(string.IsNullOrWhiteSpace(description) ? "(nothing written)" : FlightRecorder.Redact(description.Trim()));
        text.AppendLine();
        text.AppendLine("Editor and game now:");
        if (state.Count == 0) text.AppendLine("(not available: the editor was just started, so see the earlier runs below)");
        foreach (var line in state) text.AppendLine(FlightRecorder.Redact(line));
        text.AppendLine();
        text.AppendLine("Edits made in this run (newest last):");
        if (recentEdits.Count == 0) text.AppendLine("(none)");
        foreach (var line in recentEdits) text.AppendLine(FlightRecorder.Redact(line));
        text.AppendLine();
        text.AppendLine("Run logs in this report (oldest first):");
        foreach (var s in sessions)
            text.AppendLine($"{Path.GetFileName(s.Path)}: {s.End switch { SessionEnd.Running => "this run", SessionEnd.Clean => "ended normally", _ => "ended without a normal exit (crash, kill or power loss)" }}");

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Add(zip, "report.txt", text.ToString());
            foreach (var s in sessions)
            {
                try
                {
                    using var source = new FileStream(s.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(source);
                    Add(zip, "logs/" + Path.GetFileName(s.Path), reader.ReadToEnd());
                }
                catch (IOException) { }
            }
        }
        return path;
    }

    static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}

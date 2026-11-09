using System.Globalization;
using System.Text;

namespace Fm27Editor.Core.Diagnostics;

/// <summary>
/// A plain-text log of one run of the editor, written to disk line by line (flushed each time) so it survives the editor or the game
/// being closed or crashing. A run that ends normally writes <see cref="CleanExit"/> as its last line; a log without it ended abruptly.
/// Only the last <see cref="KeptSessions"/> runs are kept. The user's name in paths is replaced before anything is written.
/// </summary>
public sealed class FlightRecorder : IDisposable
{
    public const string CleanExit = "exit clean";
    public const int KeptSessions = 10;
    public const long MaxBytesPerSession = 2 * 1024 * 1024;
    const string FilePrefix = "session-", FileSuffix = ".log";

    readonly object _gate = new();
    readonly StreamWriter _writer;
    long _bytes;
    bool _truncated, _closed;

    public string Directory { get; }
    public string Path { get; }

    /// <summary>The recorder of this run; null until <see cref="Start"/> is called (tests and the CLI never start one).</summary>
    public static FlightRecorder? Current { get; private set; }

    public static string DefaultDirectory => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fm27Editor", "logs");

    public FlightRecorder(string directory, string appVersion, DateTimeOffset? now = null)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        var started = now ?? DateTimeOffset.Now;
        Path = System.IO.Path.Combine(directory, $"{FilePrefix}{started:yyyyMMdd-HHmmss-fff}{FileSuffix}");
        _writer = new StreamWriter(new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
        Prune(directory, Path);
        Log("start", $"editor {appVersion}; {Environment.OSVersion}; .NET {Environment.Version}; 64-bit {Environment.Is64BitProcess}");
    }

    /// <summary>Begins the log for this run and keeps it as <see cref="Current"/>. Never throws: a log that cannot be written must not stop the editor.</summary>
    public static FlightRecorder? Start(string appVersion)
    {
        try { return Current = new FlightRecorder(DefaultDirectory, appVersion); }
        catch (Exception) { return Current = null; }
    }

    public void Log(string category, string text)
    {
        lock (_gate)
        {
            if (_closed) return;
            try
            {
                string line = $"{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {category,-8} {Redact(text).ReplaceLineEndings(" | ")}";
                if (_bytes + line.Length > MaxBytesPerSession)
                {
                    if (_truncated) return;
                    _truncated = true;
                    line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} log      size limit reached; later lines are not recorded";
                }
                _writer.WriteLine(line);
                _bytes += line.Length + 2;
            }
            catch (IOException) { }
        }
    }

    /// <summary>One write to the game's memory: the address (module-relative when it lies inside the module), the old and new bytes, and what was being written.</summary>
    public void Write(ulong address, byte[] before, byte[] after, ulong moduleBase, ulong moduleEnd, string? what)
    {
        string where = address >= moduleBase && address < moduleEnd ? $"module+0x{address - moduleBase:X}" : $"heap 0x{address:X}";
        Log("write", $"{where} len {after.Length} was {Hex(before)} now {Hex(after)}{(what is null ? "" : " : " + what)}");
    }

    static string Hex(byte[] bytes) => Convert.ToHexString(bytes.Length > 32 ? bytes[..32] : bytes) + (bytes.Length > 32 ? "…" : "");

    /// <summary>Marks the run as ended normally.</summary>
    public void Close()
    {
        lock (_gate)
        {
            if (_closed) return;
            try { _writer.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {"end",-8} {CleanExit}"); } catch (IOException) { }
            _closed = true;
            _writer.Dispose();
        }
    }

    public void Dispose() => Close();

    /// <summary>Every kept log, oldest first, with whether it ended normally. The log being written now is included and marked as running.</summary>
    public IReadOnlyList<SessionLog> Sessions()
    {
        var found = new List<SessionLog>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, FilePrefix + "*" + FileSuffix).OrderBy(f => f, StringComparer.Ordinal))
        {
            bool running = string.Equals(file, Path, StringComparison.OrdinalIgnoreCase) && !_closed;
            found.Add(new SessionLog(file, running ? SessionEnd.Running : EndedCleanly(file) ? SessionEnd.Clean : SessionEnd.Abrupt));
        }
        return found;
    }

    static bool EndedCleanly(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long take = Math.Min(stream.Length, 512);
            stream.Seek(-take, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().TrimEnd().EndsWith(CleanExit, StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
    }

    static void Prune(string directory, string keep)
    {
        try
        {
            var old = System.IO.Directory.EnumerateFiles(directory, FilePrefix + "*" + FileSuffix).OrderByDescending(f => f, StringComparer.Ordinal).Skip(KeptSessions).ToList();
            foreach (var file in old.Where(f => !string.Equals(f, keep, StringComparison.OrdinalIgnoreCase))) File.Delete(file);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // A drive path (C:\a\b) or a network path (\server\share\a), up to the end of the line or the next character a path cannot hold.
    static readonly System.Text.RegularExpressions.Regex AbsolutePath = new(
        @"(?:[A-Za-z]:|\\\\[^\\/\s]+\\[^\\/\s]+)(?:[\\/][^\\/:*?""<>|\r\n]*)+",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Keeps only the last name of a path.</summary>
    static string TrimPaths(string text) => AbsolutePath.Replace(text, m =>
    {
        string path = m.Value.TrimEnd('\\', '/');
        return "…\\" + path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
    });

    /// <summary>Cuts every absolute path to its last name ("D:\Pictures\Me\FM Graphics" becomes "…\FM Graphics") and removes the user's profile folder, so a log can be sent to someone else.</summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = TrimPaths(text);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        string user = Environment.UserName;
        if (user.Length >= 3)
            foreach (char slash in "\\/") text = text.Replace($"{slash}{user}{slash}", $"{slash}%USER%{slash}", StringComparison.OrdinalIgnoreCase);
        return text;
    }
}

public enum SessionEnd { Running, Clean, Abrupt }

public sealed record SessionLog(string Path, SessionEnd End);

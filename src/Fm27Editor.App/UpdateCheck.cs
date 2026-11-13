using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Fm27Editor.App;

/// <summary>A newer release on GitHub: its tag and where its editor exe and checksum file are.</summary>
public sealed record ReleaseInfo(string Tag, string ExeUrl, string ChecksumsUrl);

/// <summary>
/// Asks GitHub's public API once at start for the latest release. Nothing is sent but the request itself; any failure
/// (offline, rate limit, no release yet) is silent. Builds without a real version (local builds, tests) never ask.
/// </summary>
public static class UpdateCheck
{
    const string Repository = "Jackhamerp24/Football-Manager-27-Editor";
    public const string ExeName = "Fm27Editor.exe", ChecksumsName = "SHA256SUMS.txt";
    public static string ReleasesPage => $"https://github.com/{Repository}/releases/latest";
    static string DownloadPrefix => $"https://github.com/{Repository}/releases/download/";

    /// <summary>Version of this build from the release tag; null for a local build (0.0.0).</summary>
    public static Version? CurrentVersion()
    {
        var text = typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return Parse(text) is { } v && v != new Version(0, 0, 0) ? v : null;
    }

    /// <summary>Reads "v1.2.3", "1.2.3-beta" or "1.2.3+sha" as 1.2.3; null when it is not a version.</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().TrimStart('v', 'V');
        int cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0) text = text[..cut];
        return Version.TryParse(text, out var v) ? v : null;
    }

    public static bool IsNewer(string? latestTag, Version current) => Parse(latestTag) is { } latest && latest > current;

    /// <summary>
    /// The release in a GitHub "latest release" answer when it is newer than <paramref name="current"/> and carries the editor
    /// exe and the checksum file, both from this repository's release downloads; otherwise null.
    /// </summary>
    public static ReleaseInfo? FromJson(JsonElement root, Version current)
    {
        string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (!IsNewer(tag, current) || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        string? Asset(string name) => assets.EnumerateArray()
            .Where(a => a.TryGetProperty("name", out var n) && n.GetString() == name)
            .Select(a => a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null)
            .FirstOrDefault(u => u is not null && u.StartsWith(DownloadPrefix, StringComparison.Ordinal));
        return Asset(ExeName) is { } exe && Asset(ChecksumsName) is { } sums ? new(tag!, exe, sums) : null;
    }

    /// <summary>The SHA-256 listed for <paramref name="file"/> in a "hash  name" checksum file, lower case; null when absent.</summary>
    public static string? ChecksumFor(string checksums, string file)
    {
        foreach (var line in checksums.Split('\n'))
        {
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == file && parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit))
                return parts[0].ToLowerInvariant();
        }
        return null;
    }

    static HttpClient Client(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Fm27Editor");
        return http;
    }

    /// <summary>The newest release when it is newer than this build, otherwise null.</summary>
    public static async Task<ReleaseInfo?> NewerReleaseAsync(CancellationToken ct = default)
    {
        if (CurrentVersion() is not { } current) return null;
        try
        {
            using var http = Client(TimeSpan.FromSeconds(6));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
            if (!response.IsSuccessStatusCode) return null;
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return FromJson(json.RootElement, current);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>
    /// Downloads the release exe next to <paramref name="exePath"/> as "<c>Fm27Editor.exe.new</c>" and checks it against the
    /// release's checksum file. Returns the downloaded file; throws when the download or the check fails (the partial file is removed).
    /// </summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, string exePath, CancellationToken ct = default)
    {
        string target = exePath + ".new";
        using var http = Client(TimeSpan.FromMinutes(5));
        string sums = await http.GetStringAsync(release.ChecksumsUrl, ct);
        string expected = ChecksumFor(sums, ExeName) ?? throw new InvalidDataException("The release has no checksum for the editor");
        try
        {
            await using (var source = await http.GetStreamAsync(release.ExeUrl, ct))
            await using (var file = File.Create(target))
                await source.CopyToAsync(file, ct);
            await using var check = File.OpenRead(target);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct)).ToLowerInvariant();
            if (actual != expected) throw new InvalidDataException("The downloaded file does not match the release checksum");
            return target;
        }
        catch
        {
            try { File.Delete(target); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>
    /// Puts <paramref name="downloaded"/> in place of the running <paramref name="exePath"/>: Windows lets a running exe be
    /// renamed, so it becomes "<c>.old</c>" (removed at the next start) and the new file takes its name. If the second step
    /// fails the old exe is put back.
    /// </summary>
    public static void Swap(string exePath, string downloaded)
    {
        string old = exePath + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(exePath, old);
        try { File.Move(downloaded, exePath); }
        catch { File.Move(old, exePath); throw; }
    }

    /// <summary>Removes what an earlier update left behind; best effort.</summary>
    public static void CleanUp(string exePath)
    {
        foreach (var leftover in new[] { exePath + ".old", exePath + ".new" })
            try { if (File.Exists(leftover)) File.Delete(leftover); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

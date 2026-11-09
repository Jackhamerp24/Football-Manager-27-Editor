using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Fm27Editor.App;

/// <summary>
/// Asks GitHub's public API once at start for the latest release. Nothing is sent but the request itself; any failure
/// (offline, rate limit, no release yet) is silent. Builds without a real version (local builds, tests) never ask.
/// </summary>
public static class UpdateCheck
{
    const string Repository = "Jackhamerp24/Football-Manager-27-Editor";
    public static string ReleasesPage => $"https://github.com/{Repository}/releases/latest";

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

    /// <summary>The newest release's tag when it is newer than this build, otherwise null.</summary>
    public static async Task<string?> NewerReleaseAsync(CancellationToken ct = default)
    {
        if (CurrentVersion() is not { } current) return null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Fm27Editor");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
            if (!response.IsSuccessStatusCode) return null;
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            string? tag = json.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            return IsNewer(tag, current) ? tag : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { return null; }
    }
}

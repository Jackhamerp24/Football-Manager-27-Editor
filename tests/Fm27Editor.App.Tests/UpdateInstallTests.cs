using System.Text.Json;
using Fm27Editor.App;

namespace Fm27Editor.App.Tests;

/// <summary>
/// The in-app update. The release answer and checksum file are the real ones GitHub served for v1.3.0 (14/11/2026):
/// three assets, and SHA256SUMS.txt written by the release workflow with CRLF line ends.
/// </summary>
public sealed class UpdateInstallTests
{
    const string Release130 = """
        {"tag_name":"v1.3.0","assets":[
          {"name":"Fm27Editor.exe","browser_download_url":"https://github.com/Jackhamerp24/Football-Manager-27-Editor/releases/download/v1.3.0/Fm27Editor.exe"},
          {"name":"Fm27Editor.Tools.exe","browser_download_url":"https://github.com/Jackhamerp24/Football-Manager-27-Editor/releases/download/v1.3.0/Fm27Editor.Tools.exe"},
          {"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/Jackhamerp24/Football-Manager-27-Editor/releases/download/v1.3.0/SHA256SUMS.txt"}]}
        """;

    const string Sums130 =
        "b6e51b4a381ea8b32ef311b7937c57d6f76138d5094b267c273a08c465ca457c  Fm27Editor.exe\r\n" +
        "ed431b4ed216c8176f3c9ede5968a629e17ecd45335beedcb75e4599e2b1ce0d  Fm27Editor.Tools.exe\r\n";

    static ReleaseInfo? Read(string json, string current) => UpdateCheck.FromJson(JsonDocument.Parse(json).RootElement, Version.Parse(current));

    [Fact]
    public void A_newer_release_gives_the_editor_exe_and_checksum_urls()
    {
        var r = Read(Release130, "1.2.0");
        Assert.NotNull(r);
        Assert.Equal("v1.3.0", r.Tag);
        Assert.Equal("https://github.com/Jackhamerp24/Football-Manager-27-Editor/releases/download/v1.3.0/Fm27Editor.exe", r.ExeUrl);
        Assert.EndsWith("/v1.3.0/SHA256SUMS.txt", r.ChecksumsUrl);
    }

    [Fact]
    public void The_same_or_an_older_release_offers_nothing()
    {
        Assert.Null(Read(Release130, "1.3.0"));
        Assert.Null(Read(Release130, "1.4.0"));
    }

    [Fact]
    public void A_release_without_the_checksum_file_or_with_a_foreign_download_is_not_offered()
    {
        Assert.Null(Read(Release130.Replace("\"SHA256SUMS.txt\"", "\"notes.txt\""), "1.2.0"));
        Assert.Null(Read(Release130.Replace("https://github.com/Jackhamerp24/Football-Manager-27-Editor/releases/download/v1.3.0/Fm27Editor.exe",
            "https://example.com/Fm27Editor.exe"), "1.2.0"));
    }

    [Fact]
    public void The_checksum_is_read_for_the_editor_and_not_for_the_tools()
    {
        Assert.Equal("b6e51b4a381ea8b32ef311b7937c57d6f76138d5094b267c273a08c465ca457c", UpdateCheck.ChecksumFor(Sums130, "Fm27Editor.exe"));
        Assert.Equal("ed431b4ed216c8176f3c9ede5968a629e17ecd45335beedcb75e4599e2b1ce0d", UpdateCheck.ChecksumFor(Sums130, "Fm27Editor.Tools.exe"));
        Assert.Null(UpdateCheck.ChecksumFor(Sums130, "Other.exe"));
        Assert.Null(UpdateCheck.ChecksumFor("nothex  Fm27Editor.exe", "Fm27Editor.exe"));
    }

    [Fact]
    public void Swap_puts_the_new_exe_in_place_and_cleanup_removes_the_old_one()
    {
        string dir = Directory.CreateTempSubdirectory("fm27update").FullName;
        try
        {
            string exe = Path.Combine(dir, "Fm27Editor.exe");
            File.WriteAllText(exe, "old");
            File.WriteAllText(exe + ".new", "new");
            UpdateCheck.Swap(exe, exe + ".new");
            Assert.Equal("new", File.ReadAllText(exe));
            Assert.Equal("old", File.ReadAllText(exe + ".old"));
            Assert.False(File.Exists(exe + ".new"));
            UpdateCheck.CleanUp(exe);
            Assert.False(File.Exists(exe + ".old"));
            Assert.Equal("new", File.ReadAllText(exe));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_failed_swap_puts_the_old_exe_back()
    {
        string dir = Directory.CreateTempSubdirectory("fm27update").FullName;
        try
        {
            string exe = Path.Combine(dir, "Fm27Editor.exe");
            File.WriteAllText(exe, "old");
            Assert.ThrowsAny<IOException>(() => UpdateCheck.Swap(exe, Path.Combine(dir, "missing.new")));
            Assert.Equal("old", File.ReadAllText(exe));
        }
        finally { Directory.Delete(dir, true); }
    }
}

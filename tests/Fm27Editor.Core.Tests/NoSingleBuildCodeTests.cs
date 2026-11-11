using System.Runtime.CompilerServices;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The editor must run on Game Pass and Steam. A check on one build's name, or an address hard-coded in Core, makes a feature
/// work on one store only and fail silently on the other (Nation Staff, Prevent retirement and Nation Agreements did exactly
/// that). Addresses and vtables belong in the profile JSON; code asks the profile.
/// </summary>
public class NoSingleBuildCodeTests
{
    // The path of this source file is known at compile time, so the repository is found wherever the test binaries are built.
    static string SourceRoot([CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Fm27Editor.slnx"))) dir = dir.Parent;
        Assert.True(dir is not null, "The repository root (Fm27Editor.slnx) was not found above this test file");
        return Path.Combine(dir!.FullName, "src", "Fm27Editor.Core");
    }

    static IEnumerable<(string File, int Line, string Text)> Code()
    {
        foreach (var file in Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            int n = 0;
            foreach (var line in File.ReadLines(file))
            {
                n++;
                string t = line.TrimStart();
                if (t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*")) continue;
                yield return (Path.GetFileName(file), n, line);
            }
        }
    }

    [Fact]
    public void No_code_is_gated_on_one_builds_name()
    {
        var hits = Code().Where(c => c.Text.Contains("\"27.0.0+2493426 Game Pass\"") || c.Text.Contains("GamePassManager") || c.Text.Contains("GamePassRegion"))
            .Select(c => $"{c.File}:{c.Line}").ToList();
        Assert.True(hits.Count == 0, "Code tied to one build: " + string.Join(", ", hits));
    }

    [Fact]
    public void No_module_address_is_hard_coded_outside_the_profile()
    {
        // Module RVAs are 0x4xxxxxx / 0x5xxxxxx. They may appear in profile JSON and in tests, not in Core code.
        var pattern = new System.Text.RegularExpressions.Regex(@"\b0x[45][0-9A-Fa-f]{6}\b");
        var hits = Code().Where(c => pattern.IsMatch(c.Text)).Select(c => $"{c.File}:{c.Line}").ToList();
        Assert.True(hits.Count == 0, "Hard-coded module address: " + string.Join(", ", hits));
    }
}

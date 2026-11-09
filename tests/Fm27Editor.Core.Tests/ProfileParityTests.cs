using System.Globalization;
using System.Text.Json;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The editor must work on both Game Pass and Steam, so the two profiles for build 27.0.0+2493426 describe the same set of
/// things. A feature measured on one build and missing from the other is a feature that silently does nothing there.
/// </summary>
public class ProfileParityTests
{
    static Dictionary<string, string> Flatten(string file)
    {
        var result = new Dictionary<string, string>();
        void Walk(JsonElement e, string path)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) Walk(p.Value, path + "/" + p.Name); break;
                case JsonValueKind.Array: int i = 0; foreach (var x in e.EnumerateArray()) Walk(x, path + "/" + i++); break;
                default: result[path] = e.ToString(); break;
            }
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "offsets", file)),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        Walk(doc.RootElement, "");
        return result;
    }

    static readonly Dictionary<string, string> GamePass = Flatten("27.0.0+2493426-gamepass.json");
    static readonly Dictionary<string, string> Steam = Flatten("27.0.0+2493426.json");

    // Entries that exist only on Game Pass on purpose.
    static readonly string[] GamePassOnly =
    [
        "/matchBuild", "/variant",                              // tell the two stores apart; Steam is the default
        "/directGameDateRva",                                   // a shortcut to the game date; Steam reads it through the date chain
        "/staff/directStaffVector",                             // Game Pass keeps staff objects in one vector; Steam keeps person parts
        "/club/lists/visionHolderVtables/2", "/club/lists/cultureVtables/2",   // a third class seen on Game Pass only; Steam has none
    ];

    [Fact]
    public void SteamHasEveryEntryGamePassHas()
    {
        var missing = GamePass.Keys.Where(k => !Steam.ContainsKey(k) && !GamePassOnly.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "Missing from the Steam profile: " + string.Join(", ", missing));
    }

    [Fact]
    public void NoAddressWasCopiedFromGamePassToSteam()
    {
        // The two binaries differ, so any address or vtable (a hex value of 0x100000 or more) must differ between the profiles.
        var same = GamePass.Where(kv => Steam.TryGetValue(kv.Key, out var v) && v == kv.Value && kv.Value.StartsWith("0x")
            && ulong.TryParse(kv.Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number) && number >= 0x100000).Select(kv => kv.Key).ToList();
        Assert.True(same.Count == 0, "Same address in both profiles: " + string.Join(", ", same));
    }
}

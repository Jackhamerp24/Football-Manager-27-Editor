using System.Collections;
using System.Globalization;
using System.Resources;
using Fm27Editor.App;

namespace Fm27Editor.App.Tests;

[CollectionDefinition("Language settings", DisableParallelization = true)]
public sealed class LanguageSettingsCollection { }

[Collection("Language settings")]
public sealed class AppLanguageTests
{
    [Fact]
    public void Resources_translate_by_key_and_fall_back_to_english()
    {
        Assert.Equal("Name", AppLanguage.Get("Label_Name", "en"));
        Assert.Equal("Tên", AppLanguage.Get("Label_Name", "vi"));
        Assert.Equal("Điều khoản giải phóng", AppLanguage.Get("Search_ReleaseClauses", "vi"));
        Assert.Equal("Phí giải phóng khi xuống hạng", AppLanguage.Get("Search_RelegationRelease", "vi"));
        Assert.Equal("Name", AppLanguage.Get("Label_Name", "fr"));
        Assert.Equal("missing_key", AppLanguage.Get("missing_key", "vi"));
    }

    [Fact]
    public void Vietnamese_resource_has_all_english_keys()
    {
        var resources = new ResourceManager("Fm27Editor.App.Resources.Strings", typeof(AppLanguage).Assembly);
        static HashSet<string> Keys(ResourceSet set) => set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
        using var en = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        using var vi = resources.GetResourceSet(CultureInfo.GetCultureInfo("vi"), true, false)!;
        Assert.Equal(Keys(en).OrderBy(x => x), Keys(vi).OrderBy(x => x));
    }

    [Fact]
    public void Translations_keep_format_placeholder_indexes()
    {
        var manager = new ResourceManager("Fm27Editor.App.Resources.Strings", typeof(AppLanguage).Assembly);
        var en = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var vi = manager.GetResourceSet(CultureInfo.GetCultureInfo("vi"), true, false)!;
        static string[] Placeholders(string value) => System.Text.RegularExpressions.Regex.Matches(value, @"\{(\d+)(?:[^}]*)\}").Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();
        foreach (DictionaryEntry entry in en)
            Assert.Equal(Placeholders((string)entry.Value!), Placeholders(vi.GetString((string)entry.Key)!));
    }
    [Fact]
    public void Saved_language_is_applied_on_next_load()
    {
        var file = Path.Combine(Path.GetTempPath(), "fm27-language-" + Guid.NewGuid().ToString("N"), "language.txt");
        var originalLanguage = AppLanguage.Current;
        var originalPath = AppLanguage.SettingPathOverride;
        AppLanguage.SettingPathOverride = file;
        try
        {
            AppLanguage.Save("vi");
            AppLanguage.Load();
            Assert.Equal("vi", AppLanguage.Current);
            Assert.Equal("Tên", AppLanguage.T("Name"));
            Assert.Equal("Số dư", AppLanguage.T("Balance"));
            Assert.Equal("Thăng bằng", AppLanguage.PlayerAttribute("Balance"));
            AppLanguage.Save("en");
            AppLanguage.Load();
            Assert.Equal("Name", AppLanguage.T("Name"));
            Assert.Throws<ArgumentOutOfRangeException>(() => AppLanguage.Save("fr"));
        }
        finally
        {
            AppLanguage.Save(originalLanguage);
            AppLanguage.Load();
            AppLanguage.SettingPathOverride = originalPath;
            Directory.Delete(Path.GetDirectoryName(file)!, true);
        }
    }
}

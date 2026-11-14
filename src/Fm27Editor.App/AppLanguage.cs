using System.Globalization;
using System.Resources;

namespace Fm27Editor.App;

/// <summary>Loads the user's language before constructing the UI; missing translations fall back to English.</summary>
internal static class AppLanguage
{
    static readonly ResourceManager Resources = new("Fm27Editor.App.Resources.Strings", typeof(AppLanguage).Assembly);
    internal static string? SettingPathOverride { get; set; }
    static string SettingPath => SettingPathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fm27Editor", "language.txt");
    public static string Current { get; private set; } = "en";

    public static void Load()
    {
        Current = "en";
        try { if (File.Exists(SettingPath) && File.ReadAllText(SettingPath).Trim() == "vi") Current = "vi"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static void Save(string language)
    {
        if (language is not ("en" or "vi")) throw new ArgumentOutOfRangeException(nameof(language));
        Directory.CreateDirectory(Path.GetDirectoryName(SettingPath)!);
        File.WriteAllText(SettingPath, language);
    }

    internal static string Get(string key, string? language = null) =>
        Resources.GetString(key, CultureInfo.GetCultureInfo(language ?? Current)) ?? key;

    internal static string PlayerAttribute(string english) => english == "Balance" ? Get("PlayerAttribute_Balance") : T(english);

    // Existing shared UI builders still receive English labels. Keep that adapter until each caller has a resource key.
    internal static string T(string english) => LegacyLabels.Keys.TryGetValue(english, out var key) ? Get(key) : english;
}

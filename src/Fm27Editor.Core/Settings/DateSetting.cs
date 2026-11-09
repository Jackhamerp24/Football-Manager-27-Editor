using System.Globalization;

namespace Fm27Editor.Core.Settings;

/// <summary>The three ways the editor can write a date.</summary>
public enum DateLayout { DayMonthYear, MonthDayYear, YearMonthDay }

/// <summary>
/// The one date format of the whole editor, chosen by the player in Settings (default DD-MM-YYYY) and remembered between runs.
/// Every date on screen is written with <see cref="Format(DateOnly)"/> and every typed date is read with <see cref="TryParse"/>.
/// Exports and the game's own files keep ISO dates; they are data, not screen text.
/// </summary>
public static class DateSetting
{
    static readonly string[] Patterns = ["dd-MM-yyyy", "MM-dd-yyyy", "yyyy-MM-dd"];
    static readonly string[] Hints = ["DD-MM-YYYY", "MM-DD-YYYY", "YYYY-MM-DD"];
    static string SettingPath => SettingPathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fm27Editor", "dateformat.txt");

    /// <summary>Tests point this at a temporary file so they never touch the player's saved choice.</summary>
    public static string? SettingPathOverride { get; set; }

    public static DateLayout Layout { get; private set; } = DateLayout.DayMonthYear;

    /// <summary>The .NET pattern, such as dd-MM-yyyy.</summary>
    public static string Pattern => Patterns[(int)Layout];

    /// <summary>The same for people, such as DD-MM-YYYY (for the text of error messages).</summary>
    public static string Hint => Hints[(int)Layout];

    public static string HintOf(DateLayout layout) => Hints[(int)layout];

    public static string PatternOf(DateLayout layout) => Patterns[(int)layout];

    /// <summary>Raised after the choice changes.</summary>
    public static event Action? Changed;

    /// <summary>Reads the saved choice; nothing saved (or something unknown) means the default, DD-MM-YYYY.</summary>
    public static void Load()
    {
        Layout = DateLayout.DayMonthYear;
        try
        {
            if (File.Exists(SettingPath) && Array.IndexOf(Patterns, File.ReadAllText(SettingPath).Trim()) is var at and >= 0) Layout = (DateLayout)at;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static void Set(DateLayout layout)
    {
        Layout = layout;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingPath)!);
            File.WriteAllText(SettingPath, Pattern);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    public static string Format(DateOnly date) => date.ToString(Pattern, CultureInfo.InvariantCulture);

    /// <summary>Empty text for no date.</summary>
    public static string Format(DateOnly? date) => date is { } d ? Format(d) : "";

    /// <summary>The chosen layout also with / or . between the parts and without leading zeros, plus ISO (year first) always.</summary>
    static string[] Accepted()
    {
        string main = Pattern;
        var list = new List<string> { main, main.Replace("dd", "d").Replace("MM", "M") };
        foreach (char separator in new[] { '/', '.' })
        {
            list.Add(main.Replace('-', separator));
            list.Add(list[1].Replace('-', separator));
        }
        list.Add("yyyy-MM-dd"); list.Add("yyyy-M-d");
        return [.. list.Distinct()];
    }

    /// <summary>Reads typed text in the chosen layout (or ISO); false for text that is not a date.</summary>
    public static bool TryParse(string? text, out DateOnly date) =>
        DateOnly.TryParseExact((text ?? "").Trim(), Accepted(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

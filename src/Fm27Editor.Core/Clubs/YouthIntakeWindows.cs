namespace Fm27Editor.Core.Clubs;

/// <summary>
/// A nation's usual youth intake window: first and last arrival day (month, day).
/// A single-day intake repeats the same day twice.
/// </summary>
public sealed record YouthIntakeWindow(int StartMonth, int StartDay, int EndMonth, int EndDay);

/// <summary>
/// The usual intake window per nation, as researched from several saves (Passion4FM's intake-dates guide:
/// windows start on the same day per nation every season and only shift by a day between years). This is a
/// fallback for display only: the game itself does not expose its dates where the editor could read them, so a
/// window from here is always labelled as usual and the game-read window, when ever found, takes precedence.
/// </summary>
public static class YouthIntakeWindows
{
    // Guide dates are day/month. Cypriot, Kosovar and other small-nation windows last a day or two; the big
    // European nations span most of March.
    static readonly Dictionary<string, YouthIntakeWindow> ByNation = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Austria"] = new(3, 3, 3, 7),
        ["Albania"] = new(4, 8, 4, 29),
        ["Andorra"] = new(4, 8, 4, 20),
        ["Azerbaijan"] = new(4, 8, 4, 27),
        ["Belarus"] = new(10, 5, 10, 31),
        ["Belgium"] = new(3, 19, 4, 16),
        ["Bosnia & Herzegovina"] = new(4, 8, 4, 30),
        ["Bulgaria"] = new(3, 7, 4, 16),
        ["Croatia"] = new(3, 9, 3, 20),
        ["Czech Rep."] = new(3, 19, 4, 10),
        ["Cyprus"] = new(4, 8, 4, 30),
        ["Denmark"] = new(3, 19, 3, 31),
        ["England"] = new(3, 14, 3, 31),
        ["Finland"] = new(9, 3, 9, 10),
        ["Faroe Islands"] = new(9, 21, 9, 30),
        ["France"] = new(3, 9, 3, 24),
        ["Germany"] = new(3, 9, 3, 31),
        ["Greece"] = new(3, 9, 3, 10),
        ["Gibraltar"] = new(3, 9, 3, 21),
        ["Holland"] = new(3, 26, 3, 31),
        ["Hungary"] = new(3, 9, 3, 14),
        ["Iceland"] = new(8, 29, 9, 26),
        ["Ireland"] = new(9, 22, 9, 30),
        ["Israel"] = new(3, 9, 3, 27),
        ["Italy"] = new(3, 19, 3, 31),
        ["Kosovo"] = new(4, 8, 4, 30),
        ["Latvia"] = new(9, 21, 9, 30),
        ["Lithuania"] = new(9, 21, 9, 30),
        ["Luxembourg"] = new(4, 8, 4, 30),
        ["Malta"] = new(4, 8, 4, 27),
        ["Montenegro"] = new(4, 8, 4, 30),
        ["N. Ireland"] = new(3, 14, 3, 30),
        ["North Macedonia"] = new(4, 8, 4, 30),
        ["Norway"] = new(2, 6, 2, 15),
        ["Poland"] = new(3, 17, 3, 31),
        ["Portugal"] = new(3, 19, 3, 31),
        ["Romania"] = new(3, 19, 3, 27),
        ["Russia"] = new(3, 1, 3, 28),
        ["San Marino"] = new(4, 6, 4, 23),
        ["Scotland"] = new(3, 14, 3, 23),
        ["Serbia"] = new(3, 5, 3, 16),
        ["Slovakia"] = new(3, 18, 3, 31),
        ["Slovenia"] = new(3, 20, 3, 24),
        ["Spain"] = new(3, 29, 4, 26),
        ["Sweden"] = new(9, 14, 10, 12),
        ["Switzerland"] = new(3, 19, 3, 22),
        ["Türkiye"] = new(3, 9, 3, 18),
        ["Ukraine"] = new(3, 16, 3, 31),
        ["Wales"] = new(3, 14, 3, 19),
        ["Argentina"] = new(4, 27, 4, 30),
        ["Bolivia"] = new(9, 21, 9, 30),
        ["Brazil"] = new(9, 22, 10, 20),
        ["Chile"] = new(9, 21, 10, 19),
        ["Colombia"] = new(1, 17, 1, 31),
        ["Ecuador"] = new(9, 22, 10, 19),
        ["Paraguay"] = new(9, 24, 9, 25),
        ["Peru"] = new(9, 28, 10, 3),
        ["Uruguay"] = new(1, 18, 2, 15),
        ["Venezuela"] = new(9, 21, 10, 19),
        ["Canada"] = new(9, 21, 9, 21),
        ["Costa Rica"] = new(4, 8, 4, 21),
        ["Mexico"] = new(2, 28, 3, 2),
        ["U.S.A"] = new(9, 21, 9, 23),
        ["South Africa"] = new(4, 3, 4, 5),
        ["Australia"] = new(10, 4, 10, 4),
        ["India"] = new(4, 18, 4, 18),
        ["Indonesia"] = new(7, 4, 7, 4),
        ["Japan"] = new(10, 5, 10, 31),
        ["South Korea"] = new(9, 15, 9, 15),
    };

    // Names the game uses that differ from the guide's labels.
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Northern Ireland"] = "N. Ireland",
        ["Czech Republic"] = "Czech Rep.",
        ["Czechia"] = "Czech Rep.",
        ["USA"] = "U.S.A",
        ["United States"] = "U.S.A",
        ["Turkey"] = "Türkiye",
    };

    /// <summary>The usual window for a nation name, or null when the guide knows no window for it.</summary>
    public static YouthIntakeWindow? UsualFor(string? nationName)
    {
        if (string.IsNullOrWhiteSpace(nationName)) return null;
        string name = nationName.Trim();
        if (ByNation.TryGetValue(name, out var window)) return window;
        return Aliases.TryGetValue(name, out string? key) && ByNation.TryGetValue(key, out window) ? window : null;
    }

    /// <summary>"14 Mar – 31 Mar" in the user's date format (no year: the window recurs every season).</summary>
    public static string FormatUsual(YouthIntakeWindow window)
    {
        // Day and month only: the window recurs every season, so a year (even a placeholder one) would be wrong.
        string Day(int month, int day) => new DateOnly(2024, month, day).ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture);
        if (window.StartMonth == window.EndMonth && window.StartDay == window.EndDay)
            return Day(window.StartMonth, window.StartDay);
        return $"{Day(window.StartMonth, window.StartDay)} – {Day(window.EndMonth, window.EndDay)}";
    }
}

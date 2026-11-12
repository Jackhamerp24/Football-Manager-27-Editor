using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fm27Editor.Core.Settings;

/// <summary>
/// Reads the currency the game itself is set to. FM keeps the choice as an integer in
/// %LOCALAPPDATA%\Sports Interactive\Football Manager 27\Preferences\version 6\selected_currency.xml; the integer is
/// a fixed table index, not alphabetical, so known indices are remembered in currency-map.json and every launch adds to it.
/// </summary>
public static partial class GameCurrency
{
    static string PreferencePath => PreferencePathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sports Interactive", "Football Manager 27", "Preferences", "version 6", "selected_currency.xml");

    static string MapPath => MapPathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fm27Editor", "currency-map.json");

    /// <summary>Tests point these at temporary files.</summary>
    public static string? PreferencePathOverride { get; set; }
    public static string? MapPathOverride { get; set; }

    /// <summary>Indices measured from FM27 on 2026-11-13 (GBP, EUR and USD from the in-game settings).</summary>
    static readonly Dictionary<int, string> Known = new() { [0] = "GBP", [7] = "EUR", [24] = "USD", [35] = "HKD", [38] = "INR", [64] = "VND" };

    /// <summary>The index the game is set to now, or null when the preference file is not readable.</summary>
    public static int? DetectedIndex { get; private set; }

    [GeneratedRegex(@"id=""value""\s+value=""(\d+)""")]
    private static partial Regex ValuePattern();

    public static bool TryRead(out int index)
    {
        index = 0;
        try
        {
            if (!File.Exists(PreferencePath)) return false;
            var match = ValuePattern().Match(File.ReadAllText(PreferencePath));
            if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) return false;
            DetectedIndex = index;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// While the player follows the game's currency: reads the game's index, maps it to a currency and shows money in it.
    /// False when the player picked a currency themselves, or the index is not known yet (the current currency stays).
    /// </summary>
    public static bool TryApply()
    {
        if (!TryRead(out int index) || !CurrencySetting.FollowGame) return false;
        var map = LoadMap();
        foreach (var pair in Known) map[pair.Key] = pair.Value;   // a measured index always wins over a learned one
        if (!map.TryGetValue(index, out string? code) || CurrencySetting.FindAvailable(code) is not { } currency) return false;
        CurrencySetting.ApplyDetected(currency);
        return true;
    }

    /// <summary>The currency the game is set to, when its index is known; null otherwise.</summary>
    public static CurrencyInfo? Detected()
    {
        if (!TryRead(out int index)) return null;
        var map = LoadMap();
        foreach (var pair in Known) map[pair.Key] = pair.Value;
        return map.TryGetValue(index, out string? code) ? CurrencySetting.FindAvailable(code) : null;
    }

    /// <summary>
    /// Remembers what an index means, so the next launch detects it. Only for indices not measured yet: a measured one
    /// (<see cref="Known"/>) is never overwritten. Call it only when the game's own currency is what is being recorded,
    /// never from the player's display choice (they may view in USD while the game uses VND).
    /// </summary>
    public static void Learn(int index, string code)
    {
        if (Known.ContainsKey(index)) return;
        var map = LoadMap();
        map[index] = code;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MapPath)!);
            File.WriteAllText(MapPath, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static Dictionary<int, string> LoadMap()
    {
        try
        {
            if (File.Exists(MapPath) && JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(MapPath)) is { } saved) return saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return [];
    }
}

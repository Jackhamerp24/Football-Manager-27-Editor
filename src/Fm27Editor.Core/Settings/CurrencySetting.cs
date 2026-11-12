using System.Globalization;

namespace Fm27Editor.Core.Settings;

/// <summary>One of the game's display currencies: how many whole units one pound buys, as measured from the game.</summary>
public sealed record CurrencyInfo(string Code, string Name, string Symbol, long UnitsForBase);

/// <summary>
/// The display currency chosen in Settings (default GBP). The game's money values are stored in pounds; this only converts
/// what the editor shows and what the player types, using the same fixed table the game uses (no live rates).
/// </summary>
public static class CurrencySetting
{
    /// <summary>The pound balance every currency row was read from in FM27 (PSG, 2026-11-13; see docs/CURRENCY-RATES.md).</summary>
    public const decimal BasePounds = 19_414_984m;

    public static IReadOnlyList<CurrencyInfo> All { get; } =
    [
        new("GBP", "Pound Sterling", "£", 19414984),
        new("CRC", "Costa Rican Colon", "₡", 11870885750),
        new("CZK", "Czech Koruna", "Kč", 546834818),
        new("DKK", "Danish Krone", "kr", 168949092),
        new("EGP", "Egyptian Pound", "E£", 1335710669),
        new("AED", "Emirati Dirham", "د.إ", 96103325),
        new("EUR", "Euro", "€", 22601005),
        new("HKD", "Hong Kong Dollar", "HK$", 205268885),
        new("HUF", "Hungarian Forint", "Ft", 8320834477),
        new("ISK", "Icelandic Krona", "kr", 3177698092),
        new("INR", "Indian Rupees", "₹", 2481491862),
        new("IDR", "Indonesian Rupiah", "Rp", 464015399316),
        new("JPY", "Japanese Yen", "¥", 4178789722),
        new("KZT", "Kazakhstani Tenge", "₸", 8534710363),
        new("KWD", "Kuwaiti Dinar", "KD", 8093949),
        new("MKD", "Macedonian Denar", "ден", 1390476026),
        new("MYR", "Malaysian Ringgit", "RM", 105839997),
        new("AUD", "Australian Dollar", "A$", 36625687),
        new("CAD", "Canadian Dollar", "C$", 36420400),
        new("CNY", "Chinese Yuan", "¥", 175876378),
        new("ALL", "Albanian Lek", "L", 2080654178),
        new("BAM", "Bosnia Mark", "KM", 44204501),
        new("BRL", "Brazilian Real", "R$", 134952697),
        new("CLP", "Chilean Peso", "CLP$", 24488926065),
        new("COP", "Colombian Peso", "COL$", 82905735749),
        new("ARS", "Argentine Peso", "AR$", 39586555138),
        new("MXN", "Mexican Peso", "MX$", 445077088),
        new("ILS", "Israeli New Shekel (NIS)", "₪", 79299619),
        new("TWD", "Taiwan Dollar", "NT$", 970749200),
        new("NZD", "New Zealand Dollar", "NZ$", 44935723),
        new("NOK", "Norwegian Krone", "kr", 244311912),
        new("PEN", "Peruvian Sol", "S/", 88043876),
        new("PHP", "Philippine Peso", "₱", 1636289199),
        new("PLN", "Polish Zloty", "zł", 97809127),
        new("QAR", "Qatari Riyal", "QR", 95245702),
        new("RON", "Romanian Leu", "lei", 118769004),
        new("RUB", "Russian Ruble", "₽", 2283916642),
        new("SAR", "Saudi Riyal", "SR", 98116875),
        new("RSD", "Serbian Dinar (Din)", "Din", 2651250622),
        new("SGD", "Singapore Dollar", "S$", 33313640),
        new("ZAR", "South African Rand", "R", 420992551),
        new("KRW", "South Korean Won", "₩", 35681342022),
        new("SEK", "Swedish Krona", "kr", 252452838),
        new("CHF", "Swiss Franc", "CHF", 21321375),
        new("THB", "Thai Baht", "฿", 869782085),
        new("TRY", "Turkish Lira", "₺", 1263514159),
        new("USD", "US Dollar", "$", 26164002),
        new("UAH", "Ukrainian Hryvnia", "₴", 1167684226),
        new("UYU", "Uruguayan Peso", "$U", 1053151566),
        new("VND", "Vietnamese Dong", "₫", 682962047353),
    ];

    static string SettingPath => SettingPathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fm27Editor", "currency.txt");

    /// <summary>Tests point this at a temporary file so they never touch the player's saved choice.</summary>
    public static string? SettingPathOverride { get; set; }

    public static CurrencyInfo Current { get; private set; } = All[0];

    /// <summary>
    /// True: the editor shows money in the currency FM itself is set to (re-read at every Load Game). False: the player
    /// picked a currency in Settings and it stays, whatever the game uses. Stored as "auto" or the currency code.
    /// </summary>
    public static bool FollowGame { get; private set; } = true;

    const string AutoValue = "auto";

    public static event Action? Changed;

    /// <summary>
    /// The currencies the player can use in this release: the four whose game index is measured (GBP, EUR, USD, VND).
    /// The rest of the table stays for later; until their indices are measured they are not offered.
    /// </summary>
    public static IReadOnlyList<CurrencyInfo> Available { get; } =
        All.Where(c => c.Code is "GBP" or "EUR" or "USD" or "VND").ToArray();

    /// <summary>An offered currency by code (<see cref="Available"/>); null for any other.</summary>
    public static CurrencyInfo? FindAvailable(string code) => Available.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    public static CurrencyInfo? Find(string code) => All.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>How many units one pound buys in the given currency.</summary>
    public static decimal RateOf(CurrencyInfo currency) => currency.UnitsForBase / BasePounds;

    public static decimal RatePerGbp => RateOf(Current);

    public static void Load()
    {
        Current = All[0];
        FollowGame = true;
        try
        {
            if (!File.Exists(SettingPath)) return;
            string text = File.ReadAllText(SettingPath).Trim();
            if (string.Equals(text, AutoValue, StringComparison.OrdinalIgnoreCase)) return;
            if (FindAvailable(text) is { } saved) { Current = saved; FollowGame = false; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The player's own choice: kept until they change it, never replaced by the game's currency.</summary>
    public static void Set(CurrencyInfo currency)
    {
        Current = currency;
        FollowGame = false;
        Save(currency.Code);
        Changed?.Invoke();
    }

    /// <summary>Back to following the game; the caller then applies what the game uses (<see cref="GameCurrency.TryApply"/>).</summary>
    public static void SetFollowGame()
    {
        FollowGame = true;
        Save(AutoValue);
        Changed?.Invoke();
    }

    /// <summary>The game's currency, shown while following the game; the stored choice stays "auto".</summary>
    internal static void ApplyDetected(CurrencyInfo currency)
    {
        if (!FollowGame || Current == currency) return;
        Current = currency;
        Changed?.Invoke();
    }

    static void Save(string value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingPath)!);
            File.WriteAllText(SettingPath, value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Pounds to the chosen currency, rounded the way the game rounds a whole unit.</summary>
    public static decimal ToDisplay(decimal pounds) => Math.Round(pounds * RatePerGbp, 0, MidpointRounding.AwayFromZero);

    /// <summary>The number the player typed back to pounds; the game stores whole pounds.</summary>
    public static long FromDisplay(decimal display) =>
        (long)Math.Round(display / RatePerGbp, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Like <see cref="FromDisplay(decimal)"/>, but a box set back to exactly what it showed for <paramref name="originalPounds"/>
    /// gives those pounds back. In a currency worth more than a pound (KWD) several pound values share one displayed number,
    /// so converting the shown number back could otherwise move an untouched value.
    /// </summary>
    public static long FromDisplay(decimal display, long originalPounds) =>
        display == ToDisplay(originalPounds) ? originalPounds : FromDisplay(display);

    /// <summary>The amount as the game shows it, such as ₫682,962,047,353 or £19,414,984.</summary>
    public static string Format(long pounds)
    {
        decimal value = ToDisplay(pounds);
        string digits = Math.Abs(value).ToString("N0", CultureInfo.InvariantCulture);
        return (value < 0 ? "-" : "") + Current.Symbol + digits;
    }
}

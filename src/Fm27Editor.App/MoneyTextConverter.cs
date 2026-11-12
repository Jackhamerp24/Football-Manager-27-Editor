using System.Globalization;
using Avalonia.Data.Converters;

namespace Fm27Editor.App;

/// <summary>
/// Text of a money box. Shows whole units with comma groups (1,000,000,000) and reads back any grouping a player types:
/// "1.000.000.000" (Vietnamese and most of Europe), "1,000,000,000", "1 000 000 000" or plain digits, with an optional
/// sign. Money is always whole units, so dots and commas are only ever separators here.
/// </summary>
internal sealed class MoneyTextConverter : IValueConverter
{
    public static readonly MoneyTextConverter Instance = new();

    // NumericUpDown calls Convert with the typed text (text → value) and ConvertBack with the value (value → text), the
    // reverse of a binding converter. Both directions are decided by what arrives, so either order works.
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Either(value);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Either(value);

    static object? Either(object? value) => value switch
    {
        string text => Parse(text),
        decimal d => d.ToString("N0", CultureInfo.InvariantCulture),
        null => null,
        IConvertible c => System.Convert.ToDecimal(c, CultureInfo.InvariantCulture).ToString("N0", CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>The number in <paramref name="text"/>, or null when it holds no digits or anything but digits and separators.</summary>
    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        bool negative = t.StartsWith('-');
        if (negative || t.StartsWith('+')) t = t[1..];
        var digits = new System.Text.StringBuilder(t.Length);
        foreach (char c in t)
        {
            if (char.IsAsciiDigit(c)) digits.Append(c);
            else if (c is '.' or ',' or ' ' or ' ' or '\'') continue;
            else return null;
        }
        if (digits.Length == 0 || !decimal.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        return negative ? -n : n;
    }
}

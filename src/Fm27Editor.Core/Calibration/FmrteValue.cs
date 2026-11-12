using System.Globalization;
using System.Text.RegularExpressions;

namespace Fm27Editor.Core.Calibration;

/// <summary>
/// Turns a value copied verbatim from FMRTE into the form the calibration compares: dates shown as M/D/YYYY become
/// yyyy-MM-dd, money like "£742,685" becomes 742685, and true/false become 1/0. Anything else is returned unchanged.
/// </summary>
public static partial class FmrteValue
{
    [GeneratedRegex(@"^\s*(\d{1,2})/(\d{1,2})/(\d{4})\s*$")]
    private static partial Regex UsDate();

    [GeneratedRegex(@"^\s*-?[£€$]\s*[\d,]+(\.\d+)?\s*$|^\s*-?[\d]{1,3}(,\d{3})+(\.\d+)?\s*$")]
    private static partial Regex Money();

    public static string Normalize(string value)
    {
        var date = UsDate().Match(value);
        if (date.Success)
        {
            int month = int.Parse(date.Groups[1].Value, CultureInfo.InvariantCulture);
            int day = int.Parse(date.Groups[2].Value, CultureInfo.InvariantCulture);
            int year = int.Parse(date.Groups[3].Value, CultureInfo.InvariantCulture);
            return month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
                ? new DateOnly(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : value;
        }
        if (Money().IsMatch(value))
            return value.Replace("£", "").Replace("€", "").Replace("$", "").Replace(",", "").Replace(" ", "").Trim();
        string trimmed = value.Trim();
        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)) return "1";
        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)) return "0";
        return value;
    }
}

using System.Globalization;
using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

/// <summary>Cities screen labels and input rules (FMRTE 27's City page).</summary>
public static class CityLabels
{
    /// <summary>FMRTE's Inhabitants Range dropdown; the index is the byte stored in the city (confirmed in RAM for 4 and 9).</summary>
    public static readonly string[] InhabitantRanges =
    [
        "Not Set", "0 - 1000", "1001 - 2500", "2501 - 5000", "5001 - 10000", "10001 - 25000", "25001 - 50000", "50001 - 100000",
        "100001 - 250000", "250001 - 500000", "500001 - 1000000", "1000001 - 2500000", "2500001 - 5000000", "5000001 - 10000000",
        "10000001 - 20000000", "20000001 +",
    ];

    public static string Inhabitants(int index) => index >= 0 && index < InhabitantRanges.Length ? InhabitantRanges[index] : index.ToString(CultureInfo.InvariantCulture);

    public static string Format(CityRecord city, CityField field) => field switch
    {
        CityField.Latitude => city.Latitude.ToString("0.000000", CultureInfo.InvariantCulture),
        CityField.Longitude => city.Longitude.ToString("0.000000", CultureInfo.InvariantCulture),
        CityField.Altitude => city.Altitude.ToString(CultureInfo.InvariantCulture),
        CityField.Inhabitants => city.Inhabitants.ToString(CultureInfo.InvariantCulture),
        _ => city.Attraction.ToString(CultureInfo.InvariantCulture),
    };

    public static string Label(CityField field) => field switch
    {
        CityField.Latitude => "Latitude", CityField.Longitude => "Longitude", CityField.Altitude => "Altitude",
        CityField.Inhabitants => "Inhabitants Range", _ => "Attraction",
    };

    /// <summary>Parses one pending text with the ranges the writer enforces.</summary>
    public static bool TryParse(CityField field, string text, out double value, out string? error)
    {
        error = null;
        text = text.Trim();
        bool ok;
        if (field is CityField.Latitude or CityField.Longitude)
            ok = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        else
        {
            ok = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole);
            value = whole;
        }
        (double min, double max) = field switch
        {
            CityField.Latitude => (-90.0, 90.0),
            CityField.Longitude => (-180.0, 180.0),
            CityField.Altitude => (short.MinValue, short.MaxValue),
            CityField.Inhabitants => (0, CityWriter.MaxInhabitants),
            _ => (0, CityWriter.MaxAttraction),
        };
        if (ok && value >= min && value <= max) return true;
        error = $"{Label(field)}: enter a number {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}";
        return false;
    }
}

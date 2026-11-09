using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Fm27Editor.App;

public enum ThemeMode { Dark, Light }

/// <summary>
/// The dark and the light (white and blue) look. Every UI colour in the app is written as the dark hex and asked for through
/// <see cref="Brush"/>; the brush returned is one shared object per colour, so switching the theme only changes the colour of
/// those objects and every control already on screen repaints at once, with no screen rebuilt and no state lost.
/// </summary>
public static class AppTheme
{
    static readonly Dictionary<string, SolidColorBrush> brushes = new(StringComparer.OrdinalIgnoreCase);
    static readonly object gate = new();
    /// <summary>Tests point this at a temporary file so they never touch the user's saved choice.</summary>
    internal static string? SettingPathOverride { get; set; }
    static string SettingPath => SettingPathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fm27Editor", "theme.txt");

    public static ThemeMode Mode { get; private set; } = ThemeMode.Dark;
    public static ThemeVariant Variant => Mode == ThemeMode.Light ? ThemeVariant.Light : ThemeVariant.Dark;

    /// <summary>Raised after every switch, on the UI thread.</summary>
    public static event Action? Changed;

    /// <summary>Reads the saved choice; call before any window is created so the first frame already has the right colours.</summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(SettingPath) && File.ReadAllText(SettingPath).Trim().Equals("light", StringComparison.OrdinalIgnoreCase)) Mode = ThemeMode.Light;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The shared brush for a colour given as its dark-theme hex ("#RRGGBB" or "#AARRGGBB").</summary>
    public static IBrush Brush(string darkHex)
    {
        lock (gate)
        {
            if (!brushes.TryGetValue(darkHex, out var brush))
                brushes[darkHex] = brush = new SolidColorBrush(Resolve(Color.Parse(darkHex), Mode));
            return brush;
        }
    }

    /// <summary>What a dark-theme colour looks like in <paramref name="mode"/>.</summary>
    public static Color Resolve(Color dark, ThemeMode mode) => mode == ThemeMode.Dark ? dark : LightPalette.Map(dark);

    public static void Toggle() => Set(Mode == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);

    public static void Set(ThemeMode mode)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Set(mode)); return; }
        Mode = mode;
        lock (gate)
            foreach (var (hex, brush) in brushes) brush.Color = Resolve(Color.Parse(hex), mode);
        if (Application.Current is { } app) app.RequestedThemeVariant = Variant;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingPath)!);
            File.WriteAllText(SettingPath, mode == ThemeMode.Light ? "light" : "dark");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Changed?.Invoke();
    }
}

/// <summary>
/// The light palette: white surfaces on a pale blue-grey page, dark blue-grey text, one blue accent. The colours that carry a
/// role (page, surface, border, text, accent, status) are listed; any other dark colour is turned into its light counterpart
/// by flipping lightness, which keeps tints (such as the faint green behind a good rating) readable on white.
/// </summary>
public static class LightPalette
{
    static readonly Dictionary<string, string> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // Pages and surfaces
        ["#0C0E14"] = "#EEF2F8", ["#131620"] = "#FFFFFF", ["#10131C"] = "#F7F9FC", ["#181C28"] = "#E6ECF5", ["#161A26"] = "#F3F6FB",
        ["#0E1119"] = "#FFFFFF", ["#191D2B"] = "#F1F5FA", ["#22283B"] = "#E2E8F3", ["#141722"] = "#EEF1F6", ["#171B28"] = "#E9EEF6",
        ["#181C29"] = "#FFFFFF", ["#11131C"] = "#F4F7FB", ["#0E1018"] = "#F4F6FA", ["#11141E"] = "#F4F7FB",
        // Borders and lines
        ["#222738"] = "#DDE4EF", ["#2D344B"] = "#C3CEDF", ["#1E2333"] = "#E3E8F0", ["#434C6B"] = "#9FB0CC", ["#282E42"] = "#D3DBE8",
        ["#252A3D"] = "#D9E0EC", ["#272D42"] = "#D6DEEA", ["#1D2130"] = "#E3E8F1", ["#334155"] = "#CBD5E1", ["#3A3F55"] = "#C9D2E0",
        ["#2B3147"] = "#D5DDEA", ["#2C334A"] = "#D3DBE8", ["#23293B"] = "#DEE5F0", ["#1D2233"] = "#E2E8F1", ["#2E354D"] = "#D0D8E6",
        ["#282F45"] = "#D6DEEA", ["#262C42"] = "#D9E0EC",
        // Text
        ["#F8FAFC"] = "#0B1220", ["#F1F5F9"] = "#111827", ["#E2E8F0"] = "#1E293B", ["#DCE2EE"] = "#243044", ["#CBD5E1"] = "#334155",
        ["#94A3B8"] = "#5B6B82", ["#7E8BA3"] = "#66758C", ["#64748B"] = "#8392A8", ["#475569"] = "#8491A5", ["#525B72"] = "#9AA6B8",
        // The one accent: purple becomes blue
        ["#7C3AED"] = "#2563EB", ["#8B5CF6"] = "#3B82F6", ["#6D28D9"] = "#1D4ED8", ["#A78BFA"] = "#1D4ED8", ["#C084FC"] = "#2563EB",
        ["#C4B5FD"] = "#1E40AF", ["#E9D5FF"] = "#1E3A8A", ["#241B42"] = "#DBEAFE", ["#231B42"] = "#DBEAFE", ["#2B2150"] = "#C7DBFE",
        ["#261B47"] = "#DBEAFE", ["#2E2452"] = "#C7DBFE", ["#221A3A"] = "#E0ECFF", ["#201B38"] = "#E0ECFF", ["#2563EB"] = "#2563EB",
        // Status and rating colours, darker so they read on white
        ["#F59E0B"] = "#B45309", ["#EAB308"] = "#A16207", ["#FACC15"] = "#A16207", ["#84CC16"] = "#4D7C0F", ["#4ADE80"] = "#15803D",
        ["#22C55E"] = "#16A34A", ["#16A34A"] = "#15803D", ["#38BDF8"] = "#0369A1", ["#F87171"] = "#DC2626", ["#EF4444"] = "#DC2626",
        ["#FFFFFF"] = "#FFFFFF",
    };

    public static Color Map(Color c)
    {
        if (c.A != 255)
        {
            if (c.R == 0 && c.G == 0 && c.B == 0) return c;            // shadows and scrims stay black
            var opaque = Map(Color.FromRgb(c.R, c.G, c.B));
            return Color.FromArgb(c.A, opaque.R, opaque.G, opaque.B);
        }
        return Table.TryGetValue($"#{c.R:X2}{c.G:X2}{c.B:X2}", out var hex) ? Color.Parse(hex) : Flip(c);
    }

    /// <summary>Lightness flipped, purple turned to blue, vivid colours darkened.</summary>
    static Color Flip(Color c)
    {
        var (h, s, l) = ToHsl(c);
        if (h is >= 250 and <= 310 && s > 0.25) h = 221;
        double flipped = s >= 0.45 && l >= 0.4 ? 0.62 - 0.45 * l : 0.96 - 0.88 * l;
        return FromHsl(h, s, Math.Clamp(flipped, 0.04, 0.97));
    }

    static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d == 0) return (0, 0, l);
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    static Color FromHsl(double h, double s, double l)
    {
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double Channel(double t)
        {
            t = (t % 1 + 1) % 1;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }
        double hue = h / 360;
        return s == 0
            ? Color.FromRgb((byte)Math.Round(l * 255), (byte)Math.Round(l * 255), (byte)Math.Round(l * 255))
            : Color.FromRgb((byte)Math.Round(Channel(hue + 1.0 / 3) * 255), (byte)Math.Round(Channel(hue) * 255), (byte)Math.Round(Channel(hue - 1.0 / 3) * 255));
    }
}

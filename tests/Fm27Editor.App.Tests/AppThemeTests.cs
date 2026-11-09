using Avalonia.Media;
using Fm27Editor.App;

namespace Fm27Editor.App.Tests;

[Collection("Theme")]
public sealed class AppThemeTests : IDisposable
{
    readonly string settings = Path.Combine(Path.GetTempPath(), "fm27-theme-test-" + Guid.NewGuid().ToString("N") + ".txt");

    public AppThemeTests() => AppTheme.SettingPathOverride = settings;

    public void Dispose()
    {
        AvaloniaTestThread.Run(() => AppTheme.Set(ThemeMode.Dark));
        AppTheme.SettingPathOverride = null;
        File.Delete(settings);
    }

    // WCAG 2.x contrast ratio, computed here so the palette is judged by the published formula and not by itself.
    static double Luminance(Color c)
    {
        static double Lin(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }
    static double Contrast(Color a, Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
    static Color Light(string darkHex) => LightPalette.Map(Color.Parse(darkHex));

    [Theory]
    [InlineData("#F8FAFC", "#0C0E14", 12)]   // primary text on the page
    [InlineData("#F1F5F9", "#131620", 12)]   // text on a surface
    [InlineData("#CBD5E1", "#131620", 7)]    // secondary text on a surface
    [InlineData("#CBD5E1", "#0C0E14", 7)]
    [InlineData("#94A3B8", "#131620", 4.5)]  // muted text on a surface
    [InlineData("#94A3B8", "#0C0E14", 4.5)]  // muted text on the page
    [InlineData("#94A3B8", "#161A26", 4.5)]  // muted text on a zebra row
    [InlineData("#F59E0B", "#131620", 4.5)]  // warning text
    [InlineData("#F87171", "#131620", 4.5)]  // error text
    [InlineData("#4ADE80", "#131620", 4.5)]  // good rating
    [InlineData("#38BDF8", "#131620", 4.5)]  // top rating
    [InlineData("#FACC15", "#131620", 4.5)]  // average rating
    public void TextIsReadableOnItsBackgroundInTheLightTheme(string text, string background, double minimum) =>
        Assert.True(Contrast(Light(text), Light(background)) >= minimum, $"{text} on {background}: {Contrast(Light(text), Light(background)):F2}");

    [Fact]
    public void WhiteTextOnTheAccentIsReadable() => Assert.True(Contrast(Colors.White, Light("#7C3AED")) >= 4.5);

    [Theory]
    [InlineData("#7C3AED")] [InlineData("#6D28D9")] [InlineData("#8B5CF6")] [InlineData("#A78BFA")] [InlineData("#C084FC")]
    [InlineData("#241B42")] [InlineData("#2B2150")] [InlineData("#261B47")] [InlineData("#2E2452")]
    public void EveryPurpleBecomesBlue(string purple)
    {
        var c = Light(purple);
        double r = c.R / 255.0, g = c.G / 255.0, bl = c.B / 255.0, max = Math.Max(r, Math.Max(g, bl)), d = max - Math.Min(r, Math.Min(g, bl));
        double hue = d == 0 ? 0 : 60 * (max == r ? ((g - bl) / d + 6) % 6 : max == g ? (bl - r) / d + 2 : (r - g) / d + 4);
        Assert.InRange(hue, 205, 235);     // blue; purple sits around 255 to 275
    }

    [Fact]
    public void ThePageIsPaleAndTheSurfaceIsWhite()
    {
        Assert.Equal(Colors.White, Light("#131620"));
        Assert.True(Luminance(Light("#0C0E14")) > 0.8 && Luminance(Light("#0C0E14")) < Luminance(Colors.White));
    }

    [Theory]
    [InlineData("#0E1B18")] [InlineData("#181611")] [InlineData("#0D1A26")] [InlineData("#1A4339")]
    public void ColoursWithoutAnEntryBecomeLightTints(string dark)
    {
        var c = Light(dark);
        Assert.True(Luminance(c) > 0.6, $"{dark} -> #{c.R:X2}{c.G:X2}{c.B:X2}");
    }

    [Fact]
    public void TransparencyIsKeptAndScrimsStayBlack()
    {
        Assert.Equal(0xE6, Light("#E6080A10").A);
        Assert.Equal(Color.Parse("#AA000000"), Light("#AA000000"));
    }

    [Fact]
    public void DarkColoursAreNeverChanged() =>
        Assert.Equal(Color.Parse("#7C3AED"), AppTheme.Resolve(Color.Parse("#7C3AED"), ThemeMode.Dark));

    [Fact]
    public void ABrushAlreadyOnScreenChangesColourWhenTheThemeChanges()
    {
        AvaloniaTestThread.Run(() =>
        {
            var brush = (SolidColorBrush)AppTheme.Brush("#131620");
            Assert.Same(brush, AppTheme.Brush("#131620"));
            Assert.Equal(Color.Parse("#131620"), brush.Color);

            AppTheme.Set(ThemeMode.Light);
            Assert.Equal(Colors.White, brush.Color);
            Assert.Equal(Color.Parse("#2563EB"), ((SolidColorBrush)AppTheme.Brush("#7C3AED")).Color);

            AppTheme.Set(ThemeMode.Dark);
            Assert.Equal(Color.Parse("#131620"), brush.Color);
        });
    }

    [Fact]
    public void TheChoiceIsSavedAndReadBack()
    {
        AvaloniaTestThread.Run(() => AppTheme.Set(ThemeMode.Light));
        Assert.Equal("light", File.ReadAllText(settings));
        AvaloniaTestThread.Run(() => AppTheme.Set(ThemeMode.Dark));
        Assert.Equal("dark", File.ReadAllText(settings));

        File.WriteAllText(settings, "light");
        AppTheme.Load();
        Assert.Equal(ThemeMode.Light, AppTheme.Mode);
    }
}

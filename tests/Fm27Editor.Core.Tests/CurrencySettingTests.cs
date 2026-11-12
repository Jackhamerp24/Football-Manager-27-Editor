using Fm27Editor.Core.Settings;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The display currency is one global choice, so these tests change it and restore it (and point the saved choice at a
/// temporary file) like the date-format tests do.
/// </summary>
[Collection("CurrencySetting")]
public sealed class CurrencySettingTests : IDisposable
{
    readonly string file = Path.Combine(Path.GetTempPath(), "fm27-currency-test-" + Guid.NewGuid().ToString("N") + ".txt");
    readonly CurrencyInfo before = CurrencySetting.Current;

    public CurrencySettingTests() => CurrencySetting.SettingPathOverride = file;

    public void Dispose()
    {
        CurrencySetting.Set(before);
        CurrencySetting.SettingPathOverride = null;
        File.Delete(file);
    }

    static CurrencyInfo Get(string code) => CurrencySetting.Find(code) ?? throw new InvalidOperationException(code);

    [Fact]
    public void Table_HoldsEveryCurrencyMeasuredFromTheGame()
    {
        Assert.Equal(50, CurrencySetting.All.Count);
        Assert.Equal("GBP", CurrencySetting.All[0].Code);
        Assert.Equal(1m, CurrencySetting.RateOf(CurrencySetting.All[0]));
        Assert.All(CurrencySetting.All, c => Assert.True(CurrencySetting.RateOf(c) > 0));
    }

    [Theory]
    // Values read in the game's own currency screen on 2026-11-13 for PSG's £19,414,984.
    [InlineData("GBP", "£19,414,984")]
    [InlineData("USD", "$26,164,002")]
    [InlineData("EUR", "€22,601,005")]
    [InlineData("CRC", "₡11,870,885,750")]
    [InlineData("VND", "₫682,962,047,353")]
    [InlineData("KWD", "KD8,093,949")]
    public void Format_ReproducesTheBalanceTheGameShows(string code, string expected)
    {
        CurrencySetting.Set(Get(code));
        Assert.Equal(expected, CurrencySetting.Format(19_414_984));
    }

    /// <summary>
    /// The Kuwaiti dinar is worth more than a pound (8,093,949 KD for £19,414,984), so £1 shows as KD 0 and £3 as KD 1;
    /// typing the shown number back must not move an untouched value. Worked by hand from the table's rate.
    /// </summary>
    [Fact]
    public void An_untouched_amount_keeps_its_pounds_in_a_currency_worth_more_than_a_pound()
    {
        CurrencySetting.Set(Get("KWD"));
        Assert.Equal(0m, CurrencySetting.ToDisplay(1));
        Assert.Equal(0, CurrencySetting.FromDisplay(0m));          // plain conversion loses the pound
        Assert.Equal(1, CurrencySetting.FromDisplay(0m, 1));       // the box shows what it showed: pounds kept
        Assert.Equal(1m, CurrencySetting.ToDisplay(3));
        Assert.Equal(2, CurrencySetting.FromDisplay(1m));          // 1 / 0.4169 = 2.4 → 2
        Assert.Equal(3, CurrencySetting.FromDisplay(1m, 3));
        Assert.Equal(5, CurrencySetting.FromDisplay(2m, 3));       // a real change converts: 2 / 0.4169 = 4.8 → 5
    }

    [Fact]
    public void ToDisplay_And_FromDisplay_RoundTripInTheChosenCurrency()
    {
        CurrencySetting.Set(Get("JPY"));
        foreach (long pounds in new long[] { 0, 1, 999, 19_414_984, 2_147_483_647 })
        {
            long back = CurrencySetting.FromDisplay(CurrencySetting.ToDisplay(pounds));
            Assert.Equal(pounds, back);
        }
    }

    [Fact]
    public void Format_WritesNegativeAmountsWithTheSignBeforeTheSymbol()
    {
        CurrencySetting.Set(Get("GBP"));
        Assert.Equal("-£1,500", CurrencySetting.Format(-1_500));
    }

    [Fact]
    public void Load_KeepsTheSavedChoice_AndFallsBackToPoundsWhenUnknown()
    {
        CurrencySetting.Set(Get("USD"));
        CurrencySetting.Load();
        Assert.Equal("USD", CurrencySetting.Current.Code);

        // Not offered in this release (only GBP, EUR, USD, VND): a saved BRL falls back to pounds.
        CurrencySetting.Set(Get("BRL"));
        CurrencySetting.Load();
        Assert.Equal("GBP", CurrencySetting.Current.Code);

        File.WriteAllText(file, "NOPE");
        CurrencySetting.Load();
        Assert.Equal("GBP", CurrencySetting.Current.Code);
    }
}

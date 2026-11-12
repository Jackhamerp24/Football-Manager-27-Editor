using Fm27Editor.Core.Settings;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The game's own currency setting is an integer index; these tests use temporary preference and map files and restore
/// the display currency afterwards, like the date-format tests.
/// </summary>
[Collection("CurrencySetting")]
public sealed class GameCurrencyTests : IDisposable
{
    readonly string preference = Path.Combine(Path.GetTempPath(), "fm27-selcur-" + Guid.NewGuid().ToString("N") + ".xml");
    readonly string map = Path.Combine(Path.GetTempPath(), "fm27-curmap-" + Guid.NewGuid().ToString("N") + ".json");
    readonly string setting = Path.Combine(Path.GetTempPath(), "fm27-curset-" + Guid.NewGuid().ToString("N") + ".txt");
    readonly string? settingBefore = CurrencySetting.SettingPathOverride;

    public GameCurrencyTests()
    {
        GameCurrency.PreferencePathOverride = preference;
        GameCurrency.MapPathOverride = map;
        CurrencySetting.SettingPathOverride = setting;   // never touch the player's own choice
        CurrencySetting.SetFollowGame();
    }

    public void Dispose()
    {
        GameCurrency.PreferencePathOverride = null;
        GameCurrency.MapPathOverride = null;
        CurrencySetting.SettingPathOverride = settingBefore;
        CurrencySetting.Load();
        File.Delete(preference);
        File.Delete(map);
        File.Delete(setting);
    }

    void WritePreference(int value) =>
        File.WriteAllText(preference, $"<record>\n\t<integer id=\"value\" value=\"{value}\"/>\n</record>");

    [Theory]
    // Indices measured in FM27's settings: GBP, Euro, US Dollar.
    [InlineData(0, "GBP")]
    [InlineData(7, "EUR")]
    [InlineData(24, "USD")]
    [InlineData(64, "VND")]
    public void TryApply_MapsTheMeasuredIndices(int index, string code)
    {
        WritePreference(index);
        Assert.True(GameCurrency.TryApply());
        Assert.Equal(code, CurrencySetting.Current.Code);
        Assert.Equal(index, GameCurrency.DetectedIndex);
    }

    [Fact]
    public void TryApply_LeavesTheCurrencyAlone_WhenTheIndexIsUnknown()
    {
        WritePreference(7);
        Assert.True(GameCurrency.TryApply());
        WritePreference(42);

        Assert.False(GameCurrency.TryApply());
        Assert.Equal("EUR", CurrencySetting.Current.Code);
    }

    /// <summary>The user's rule: a player may view in USD while the game uses VND; Load Game must not undo that.</summary>
    [Fact]
    public void A_currency_picked_in_Settings_stays_whatever_the_game_uses()
    {
        CurrencySetting.Set(CurrencySetting.Find("USD")!);
        WritePreference(64);   // the game is on VND

        Assert.False(GameCurrency.TryApply());
        Assert.Equal("USD", CurrencySetting.Current.Code);
        CurrencySetting.Load();   // next launch
        Assert.Equal(("USD", false), (CurrencySetting.Current.Code, CurrencySetting.FollowGame));

        CurrencySetting.SetFollowGame();
        Assert.True(GameCurrency.TryApply());
        Assert.Equal("VND", CurrencySetting.Current.Code);
        CurrencySetting.Load();
        Assert.True(CurrencySetting.FollowGame);
    }

    [Fact]
    public void Learn_never_overwrites_a_measured_index()
    {
        GameCurrency.Learn(64, "USD");
        WritePreference(64);
        Assert.True(GameCurrency.TryApply());
        Assert.Equal("VND", CurrencySetting.Current.Code);
    }

    [Fact]
    public void Learn_RemembersAnUnknownIndexForTheNextLaunch()
    {
        WritePreference(42);
        Assert.False(GameCurrency.TryApply());

        GameCurrency.Learn(42, "EUR");
        Assert.True(GameCurrency.TryApply());
        Assert.Equal("EUR", CurrencySetting.Current.Code);
    }

    /// <summary>This release offers only GBP, EUR, USD and VND (the user's decision, 13/11/2026); others are not applied yet.</summary>
    [Fact]
    public void Only_the_four_released_currencies_are_offered_or_applied()
    {
        // Index 35 is measured as HKD, but HKD is not offered in this release: detection leaves the currency alone.
        WritePreference(7);
        Assert.True(GameCurrency.TryApply());
        WritePreference(35);
        Assert.False(GameCurrency.TryApply());
        Assert.Equal("EUR", CurrencySetting.Current.Code);

        Assert.Equal(["GBP", "EUR", "USD", "VND"], CurrencySetting.Available.Select(c => c.Code).OrderBy(c => c == "GBP" ? 0 : c == "EUR" ? 1 : c == "USD" ? 2 : 3));
        GameCurrency.Learn(42, "BRL");
        WritePreference(42);
        Assert.False(GameCurrency.TryApply());

        File.WriteAllText(CurrencySetting.SettingPathOverride!, "JPY");
        CurrencySetting.Load();
        Assert.True(CurrencySetting.FollowGame);   // a saved choice outside the four falls back to the game's currency
    }

    [Fact]
    public void TryApply_ReturnsFalse_WhenThePreferenceIsMissingOrUnreadable()
    {
        Assert.False(GameCurrency.TryApply());
        File.WriteAllText(preference, "not xml");
        Assert.False(GameCurrency.TryApply());
    }
}

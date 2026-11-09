using System.Runtime.CompilerServices;
using Fm27Editor.Core.Settings;

namespace Fm27Editor.App.Tests;

/// <summary>
/// Most App tests check what a screen decodes (a date from RAM, a contract end) and spell the expected text as ISO. The date
/// layout is the player's choice, so the whole test run pins ISO, on a throw-away settings file that is never the player's own;
/// <see cref="DateLayoutScreenTests"/> checks the layouts themselves.
/// </summary>
static class AppTestDefaults
{
    [ModuleInitializer]
    internal static void Pin()
    {
        DateSetting.SettingPathOverride = Path.Combine(Path.GetTempPath(), "fm27-app-tests-date-" + Environment.ProcessId + ".txt");
        DateSetting.Set(DateLayout.YearMonthDay);
    }
}

public sealed class DateLayoutScreenTests : IDisposable
{
    public void Dispose() => DateSetting.Set(DateLayout.YearMonthDay);

    [Theory]
    [InlineData(DateLayout.DayMonthYear, "30-03-2027")]
    [InlineData(DateLayout.MonthDayYear, "03-30-2027")]
    [InlineData(DateLayout.YearMonthDay, "2027-03-30")]
    public void ADateBoxWritesAndReadsTheChosenLayout(DateLayout layout, string text)
    {
        DateSetting.Set(layout);
        AvaloniaTestThread.Run(() =>
        {
            var box = new DateBox { Text = text };
            Assert.Equal(DateSetting.PatternOf(layout), box.Format);
            Assert.True(DateBox.TryParse(box.Text, out var day));
            Assert.Equal(new DateOnly(2027, 3, 30), day);
            Assert.Equal(text, DateSetting.Format(day));
        });
    }

    [Fact]
    public void TheStadiumDateFieldFollowsTheLayout()
    {
        DateSetting.Set(DateLayout.DayMonthYear);
        Assert.Equal("30-03-2027", StadiumLabels.Format(Fm27Editor.Core.Stadiums.StadiumField.BuildDate, 20270330));
        Assert.True(StadiumLabels.TryParse(Fm27Editor.Core.Stadiums.StadiumField.BuildDate, "30-03-2027", out long value, out _));
        Assert.Equal(20270330, value);
        Assert.False(StadiumLabels.TryParse(Fm27Editor.Core.Stadiums.StadiumField.BuildDate, "03-30-2027", out _, out string? error));
        Assert.Contains("DD-MM-YYYY", error);
    }
}

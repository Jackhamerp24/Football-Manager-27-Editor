using Fm27Editor.Core.Settings;

namespace Fm27Editor.Core.Tests;

/// <summary>The date format is one global choice; these tests change it, so they share a collection with the others that read it.</summary>
[Collection("DateSetting")]
public sealed class DateSettingTests : IDisposable
{
    readonly string file = Path.Combine(Path.GetTempPath(), "fm27-date-test-" + Guid.NewGuid().ToString("N") + ".txt");
    readonly DateLayout before = DateSetting.Layout;

    public DateSettingTests() => DateSetting.SettingPathOverride = file;

    public void Dispose()
    {
        DateSetting.Set(before);
        DateSetting.SettingPathOverride = null;
        File.Delete(file);
    }

    static readonly DateOnly Day = new(2027, 3, 30);

    [Theory]
    [InlineData(DateLayout.DayMonthYear, "30-03-2027", "DD-MM-YYYY")]
    [InlineData(DateLayout.MonthDayYear, "03-30-2027", "MM-DD-YYYY")]
    [InlineData(DateLayout.YearMonthDay, "2027-03-30", "YYYY-MM-DD")]
    public void ADateIsWrittenInTheChosenLayout(DateLayout layout, string expected, string hint)
    {
        DateSetting.Set(layout);
        Assert.Equal(expected, DateSetting.Format(Day));
        Assert.Equal(expected, DateSetting.Format((DateOnly?)Day));
        Assert.Equal("", DateSetting.Format((DateOnly?)null));
        Assert.Equal(hint, DateSetting.Hint);
    }

    [Fact]
    public void WithNothingSavedTheLayoutIsDayMonthYear()
    {
        DateSetting.Set(DateLayout.YearMonthDay);
        File.Delete(file);                                  // a fresh install has no file
        DateSetting.Load();
        Assert.Equal(DateLayout.DayMonthYear, DateSetting.Layout);
        Assert.Equal("30-03-2027", DateSetting.Format(Day));
    }

    [Theory]
    [InlineData(DateLayout.DayMonthYear, "30-03-2027")]
    [InlineData(DateLayout.DayMonthYear, "30/03/2027")]
    [InlineData(DateLayout.DayMonthYear, "30.3.2027")]
    [InlineData(DateLayout.DayMonthYear, "2027-03-30")]       // ISO is accepted in every layout
    [InlineData(DateLayout.MonthDayYear, "03-30-2027")]
    [InlineData(DateLayout.MonthDayYear, "3/30/2027")]
    [InlineData(DateLayout.MonthDayYear, "2027-03-30")]
    [InlineData(DateLayout.YearMonthDay, "2027-03-30")]
    [InlineData(DateLayout.YearMonthDay, "2027/3/30")]
    public void TypedDatesAreReadInTheChosenLayout(DateLayout layout, string text)
    {
        DateSetting.Set(layout);
        Assert.True(DateSetting.TryParse(text, out var day), text);
        Assert.Equal(Day, day);
    }

    [Theory]
    [InlineData(DateLayout.DayMonthYear, "03-30-2027")]       // month 30 does not exist: the other order is not guessed
    [InlineData(DateLayout.MonthDayYear, "30-03-2027")]
    [InlineData(DateLayout.YearMonthDay, "30-03-2027")]
    [InlineData(DateLayout.DayMonthYear, "")]
    [InlineData(DateLayout.DayMonthYear, "not a date")]
    public void OtherOrdersAndGarbageAreNotDates(DateLayout layout, string text)
    {
        DateSetting.Set(layout);
        Assert.False(DateSetting.TryParse(text, out _), text);
    }

    [Fact]
    public void AnAmbiguousDateMeansWhatTheLayoutSays()
    {
        DateSetting.Set(DateLayout.DayMonthYear);
        Assert.True(DateSetting.TryParse("05-06-2027", out var dayFirst));
        Assert.Equal(new DateOnly(2027, 6, 5), dayFirst);
        DateSetting.Set(DateLayout.MonthDayYear);
        Assert.True(DateSetting.TryParse("05-06-2027", out var monthFirst));
        Assert.Equal(new DateOnly(2027, 5, 6), monthFirst);
    }

    [Fact]
    public void TheChoiceIsSavedAndReadBack()
    {
        DateSetting.Set(DateLayout.MonthDayYear);
        Assert.Equal("MM-dd-yyyy", File.ReadAllText(file));
        DateSetting.Set(DateLayout.DayMonthYear);
        File.WriteAllText(file, "yyyy-MM-dd");
        DateSetting.Load();
        Assert.Equal(DateLayout.YearMonthDay, DateSetting.Layout);
        File.WriteAllText(file, "something else");          // an unknown value means the default
        DateSetting.Load();
        Assert.Equal(DateLayout.DayMonthYear, DateSetting.Layout);
    }

    [Fact]
    public void TheChangedEventFires()
    {
        int fired = 0;
        void Handler() => fired++;
        DateSetting.Changed += Handler;
        try { DateSetting.Set(DateLayout.MonthDayYear); } finally { DateSetting.Changed -= Handler; }
        Assert.Equal(1, fired);
    }
}

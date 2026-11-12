namespace Fm27Editor.App.Tests;

/// <summary>
/// A player typed "1.000.000.000" into a VND money box and it was refused (13/11/2026). Money is whole units, so every
/// usual grouping must read as the same number, and anything else must be refused rather than guessed.
/// </summary>
public class MoneyTextConverterTests
{
    [Theory]
    [InlineData("1.000.000.000", 1_000_000_000)]
    [InlineData("1,000,000,000", 1_000_000_000)]
    [InlineData("1 000 000 000", 1_000_000_000)]
    [InlineData("1000000000", 1_000_000_000)]
    [InlineData("-2.500.000", -2_500_000)]
    [InlineData("+750", 750)]
    [InlineData("0", 0)]
    public void Reads_any_grouping_of_whole_units(string text, long expected) =>
        Assert.Equal(expected, MoneyTextConverter.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1e9")]
    [InlineData("12-3")]
    [InlineData("₫")]
    public void Refuses_text_that_is_not_a_whole_amount(string text) =>
        Assert.Null(MoneyTextConverter.Parse(text));

    /// <summary>
    /// A real NumericUpDown with the converter: typing "1.000.000.000" must give the value 1,000,000,000, and a value set in
    /// code must show as text. The first version had the directions swapped: boxes showed nothing and typing was ignored.
    /// </summary>
    [Fact]
    public void Works_inside_a_NumericUpDown_in_both_directions()
    {
        AvaloniaTestThread.Run(() =>
        {
            var box = new Avalonia.Controls.NumericUpDown { Minimum = -1_000_000_000_000m, Maximum = 1_000_000_000_000m, Value = 0, TextConverter = MoneyTextConverter.Instance };
            var window = new Avalonia.Controls.Window { Content = box };
            window.Show();
            try
            {
                Assert.Equal("0", box.Text);
                box.Value = 25_000_000m;
                Assert.Equal("25,000,000", box.Text);
                box.Text = "1.000.000.000";
                box.Focus();
                window.Focus();   // leaving the box commits the text
                Assert.Equal(1_000_000_000m, box.Value);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Shows_whole_units_with_comma_groups() =>
        Assert.Equal("1,000,000,000", MoneyTextConverter.Instance.Convert(1_000_000_000m, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
}

using Fm27Editor.App;

namespace Fm27Editor.App.Tests;

public class DebtSourceLabelsTests
{
    [Fact]
    public void FMRTE_sources_are_shown_for_observed_codes()
    {
        var table = ClubListBuilder.BuildDebts([new ClubDebtRow(5, 119983496), new ClubDebtRow(15, 36700000)]);
        Assert.Equal("State", table.Rows[0].Cells[0].Text);
        Assert.Equal("Miscellaneous", table.Rows[1].Cells[0].Text);
    }
}

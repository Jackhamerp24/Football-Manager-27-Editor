using Fm27Editor.App;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App.Tests;

public class StaffNavigationTests
{
    [Fact]
    public void Open_selected_matches_current_address_and_unique_id_even_for_duplicate_names()
    {
        var kind = new StaffKind(0, 0, 0, false);
        StaffRecord Person(ulong address, int id) => new()
        {
            Address = address, StaffAddress = address + 1, Id = id,
            Attributes = [], Personality = [], Kind = kind,
        };
        var jim = new MainWindow.StaffRow(Person(0x10000, 5209579), "Same Name", "", "", "", null, 96, 133);
        var david = new MainWindow.StaffRow(Person(0x20000, 2019010), "Same Name", "", "", "", null, 128, 140);
        MainWindow.StaffRow[] rows = [jim, david];

        Assert.Same(jim, StaffNavigation.Find(rows, 0x10000, 5209579));
        Assert.Same(david, StaffNavigation.Find(rows, 0x20000, 2019010));
        Assert.Null(StaffNavigation.Find(rows, 0x10000, 2019010));
        Assert.Null(StaffNavigation.Find(rows, 0x30000, 5209579));
    }
}

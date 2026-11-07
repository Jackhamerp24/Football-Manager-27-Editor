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

    [Fact]
    public void Filter_matches_folded_name_or_unique_id_while_respecting_club_and_job_filters()
    {
        var kind = new StaffKind(0, 0, 0, false);
        StaffRecord Person(ulong address, int id) => new()
        {
            Address = address, StaffAddress = address + 1, Id = id,
            Attributes = [], Personality = [], Kind = kind,
        };
        var ferguson = new MainWindow.StaffRow(Person(0x10000, 2020287), "Alex Ferguson", "Director", "Man Utd", "Scotland", 84, 150, 200);
        var duong = new MainWindow.StaffRow(Person(0x20000, 2002081897), "Duong Vu", "Manager", "Man Utd", "Vietnam", 30, 160, 180);
        var pep = new MainWindow.StaffRow(Person(0x30000, 6700222), "Pep Guardiola", "Manager", "Man City", "Spain", 55, 195, 200);
        MainWindow.StaffRow[] rows = [ferguson, duong, pep];

        Assert.Equal([ferguson], StaffFilter.Apply(rows, "2020287", null, null));
        Assert.Equal([duong], StaffFilter.Apply(rows, "2002081897", "Man Utd", "Manager"));
        Assert.Empty(StaffFilter.Apply(rows, "2020287", "Man City", null));
        Assert.Empty(StaffFilter.Apply(rows, "2020287", "Man Utd", "Manager"));
    }
}

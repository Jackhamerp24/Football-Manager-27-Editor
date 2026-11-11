using Fm27Editor.App;

namespace Fm27Editor.App.Tests;

public class MassPlanTests
{
    [Fact]
    public void The_summary_counts_targets_fields_and_only_the_changes_that_will_be_written()
    {
        var applied = 0;
        var rows = new List<MassRow>
        {
            MassRow.Change("A", "Condition", 6000, 7000, () => { applied++; return 1; }),
            MassRow.Change("B", "Condition", 6000, 7000, () => { applied++; return 1; }),
            MassRow.Change("C", "Sharpness", 5000, 9000, () => { applied++; return 1; }),
            MassRow.Refused("D", "Condition", "Condition must be between 5000 and 10000"),
        };
        rows.Add(new MassRow { Name = "E", Field = "Condition", Problem = "refused but ticked", Include = true });   // a ticked line that cannot be written
        rows[1].Include = false;                                    // the player left out in the review table
        var plan = new MassPlan("Player", 5, rows);
        Assert.Equal(2, plan.Selected);                             // A and C: B is unticked, D is refused
        Assert.Equal(2, plan.Fields);                               // Condition and Sharpness
        Assert.Equal("5 players, 2 field(s), 2 change(s) selected", plan.Summary("players"));
        Assert.False(rows[3].CanApply);
        Assert.False(rows[3].Include);
        Assert.Equal("Condition must be between 5000 and 10000", rows[3].Note);
        foreach (var row in rows.Where(r => r.Include && r.CanApply)) row.Apply!();
        Assert.Equal(2, applied);
    }

    [Fact]
    public void An_action_line_stands_for_all_targets_and_has_no_before_value()
    {
        var row = MassRow.Action("1,234 players", "Preset", "All-in-one", () => 1234);
        Assert.Equal("", row.Before);
        Assert.Equal("All-in-one", row.After);
        Assert.True(row.CanApply && row.Include);
        Assert.Equal(1234, row.Apply!());
    }
}

using Fm27Editor.App;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App.Tests;

public class StaffDraftTests
{
    [Fact]
    public void StaffSearchNameIgnoresAccentsAndTracksRenamedCopies()
    {
        var staff = new StaffRecord { Address = 1, StaffAddress = 2, Attributes = [], Personality = [], Kind = new StaffKind(0, 0, 0, false) };
        var row = new MainWindow.StaffRow(staff, "José Mourinho", "", "", "", null, 0, 0);
        Assert.Equal("jose mourinho", row.SearchName);
        Assert.Equal("joachim low", (row with { Name = "Joachim Löw" }).SearchName);
    }

    [Fact]
    public void Parses_every_change_within_its_range()
    {
        var d = new StaffDraft();
        d.Changes["attr:CoachingAttacking"] = " 19 ";
        d.Changes["CA"] = "200";
        d.Changes["rep:World"] = "10000";
        d.Changes["mgr:MediaHandling"] = "72"; // Duong Vu in FMRTE; manager attributes go to 100
        Assert.True(d.TryParse(out var v, out var error));
        Assert.Null(error);
        Assert.Equal(19, v["attr:CoachingAttacking"]);
        Assert.Equal(200, v["CA"]);
        Assert.Equal(10000, v["rep:World"]);
        Assert.Equal(72, v["mgr:MediaHandling"]);
    }

    [Theory]
    [InlineData("attr:Pressing", "21")]
    [InlineData("pers:Loyalty", "0")]
    [InlineData("PA", "201")]
    [InlineData("rep:Home", "-1")]
    [InlineData("attr:Tempo", "1x")]
    [InlineData("mgr:MediaHandling", "101")]
    public void Any_invalid_value_rejects_the_whole_draft(string key, string text)
    {
        var d = new StaffDraft();
        d.Changes["attr:Width"] = "12";
        d.Changes[key] = text;
        Assert.False(d.TryParse(out var v, out var error));
        Assert.Empty(v);
        Assert.NotNull(error);
    }

    /// <summary>Codes from the job record of Man Utd staff, named as FMRTE 27's Job column shows them.</summary>
    [Fact]
    public void Job_codes_measured_on_Man_Utd_have_names_and_others_stay_raw()
    {
        Assert.Equal("Director", StaffLabels.Job(6));           // Alex Ferguson
        Assert.Equal("Coach", StaffLabels.Job(2));              // Jonathan Woodgate
        Assert.Equal("GK Coach", StaffLabels.Job(34));          // Craig Mawson
        Assert.Equal("Head of Youth Development", StaffLabels.Job(64)); // Stephen Torpey
        Assert.Equal("President", StaffLabels.Job(70));          // Florentino Pérez, Giuseppe Marotta
        Assert.Equal("Owner", StaffLabels.Job(66));              // John Henry
        Assert.Equal("Chief Doctor", StaffLabels.Job(38));       // Ricard Pruna (Barcelona)
        Assert.Equal("Chief Scout", StaffLabels.Job(44));        // Juni Calafat (Real Madrid)
        Assert.Equal("Managing Director", StaffLabels.Job(8));   // David Hopkinson (Newcastle)
        Assert.Equal("Job 99", StaffLabels.Job(99));
        Assert.Equal("", StaffLabels.Job(null));
        Assert.Equal(Enum.GetValues<StaffAttribute>().Length, StaffLabels.Groups.Sum(g => g.Items.Length));
        Assert.Equal(Enum.GetValues<StaffAttribute>().Length, StaffLabels.Groups.SelectMany(g => g.Items).Select(i => i.Attribute).Distinct().Count());
    }
}

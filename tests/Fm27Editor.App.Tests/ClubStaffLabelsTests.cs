using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.App.Tests;

/// <summary>
/// Expected cells are FMRTE 27's Man Utd Staff tab (3/11/2026, game date 15/8/2026): Omar Berrada, Chairperson, 48, France,
/// 135 / 145; Alex Ferguson, Director, 84, Scotland, 188 / 195; Alan Wright, Ast. Manager, 54, England, 80 / 114.
/// Job codes 4, 6, 20 are the codes the same screen showed in RAM (docs on StaffRecord.JobCode).
/// </summary>
public class ClubStaffLabelsTests
{
    static readonly DateOnly Today = new(2026, 8, 15);

    static StaffRecord Staff(string common, short? job, DateOnly? born, string? nation, short ca, short pa) => new()
    {
        Address = 1, StaffAddress = 2, Attributes = [], Personality = [], Kind = new StaffKind(0, 0, 0, false),
        CommonName = common, JobCode = job, DateOfBirth = born, CurrentAbility = ca, PotentialAbility = pa,
        Nationality = nation is null ? null : new NationInfo(0x10, nation, nation),
    };

    [Fact]
    public void Rows_match_FMRTE_job_name_age_nation_and_abilities()
    {
        var table = ClubStaffLabels.BuildTable(
        [
            Staff("Omar Berrada", 4, new(1978, 3, 3), "France", 135, 145),
            Staff("Alex Ferguson", 6, new(1941, 12, 31), "Scotland", 188, 195),
            Staff("Alan Wright", 20, new(1971, 9, 28), "England", 80, 114),
        ], Today);
        Assert.Equal(["Job", "Name", "Age", "Team", "Nation", "Current Ability", "PA"], table.Columns.Select(c => c.Header));
        Assert.Equal(
            ["Chairperson|Omar Berrada|48||France|135|145", "Director|Alex Ferguson|84||Scotland|188|195",
             "Ast. Manager|Alan Wright|54||England|80|114"],
            table.Rows.Select(r => string.Join('|', r.Cells.Select(c => c.Text))));
    }

    [Fact]
    public void Age_counts_whole_years_and_is_blank_without_a_birth_date_or_game_date()
    {
        Assert.Equal(54, ClubStaffLabels.Age(new(1971, 8, 16), Today)); // birthday tomorrow
        Assert.Equal(55, ClubStaffLabels.Age(new(1971, 8, 15), Today));
        Assert.Null(ClubStaffLabels.Age(null, Today));
        Assert.Null(ClubStaffLabels.Age(new(1971, 8, 15), null));
        var blank = ClubStaffLabels.BuildTable([Staff("Nobody", null, null, null, 1, 2)], null);
        Assert.Equal("|Nobody||||1|2", string.Join('|', blank.Rows[0].Cells.Select(c => c.Text)));
    }

    [Fact]
    public void Rows_show_FMRTE_team_names_for_first_and_youth_teams()
    {
        var table = ClubStaffLabels.BuildTable(
        [
            Staff("George Willis", 16, new(1995, 1, 1), "England", 131, 180) with { TeamName = "Sheffield United U21" },
            Staff("Jim O'Brien", 16, new(1987, 1, 1), "Republic of Ireland", 96, 133) with { TeamName = "Sheffield United U18" },
            Staff("Chris Wilder", 16, new(1967, 1, 1), "England", 129, 140) with { TeamName = "Sheffield United" },
        ], Today);
        Assert.Equal(["Sheffield United U21", "Sheffield United U18", "Sheffield United"],
            table.Rows.Select(r => r.Cells[3].Text));
    }
}

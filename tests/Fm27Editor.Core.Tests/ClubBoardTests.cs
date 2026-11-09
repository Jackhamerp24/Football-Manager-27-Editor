using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Board filtering on the staff list. Job codes and labels follow FMRTE 27's Staff tab (Chairperson 4, Director 6,
/// Director of Football 10, Owner 66); a coach (2) must never be listed as board.
/// </summary>
public class ClubBoardTests
{
    const ulong ClubA = 0x5000_0000, ClubB = 0x6000_0000;
    static readonly StaffKind Kind = new(0x4D3B358, 0x4D3B358, 0x100, false);

    static StaffRecord Person(string name, ulong club, string clubName, short? job) => new()
    {
        Address = 0x1000 + (ulong)(job ?? 0), StaffAddress = 0x2000, Id = 1, CommonName = name,
        Club = club == 0 ? null : new ClubInfo(club, clubName, clubName), JobCode = job,
        Attributes = new byte[80], Personality = new byte[8], Kind = Kind,
    };

    static IReadOnlyList<StaffRecord> Staff() =>
    [
        Person("Erik Manager", ClubA, "Man Utd", 16),
        Person("Jim Coach", ClubA, "Man Utd", 2),
        Person("Sir Director", ClubA, "Man Utd", 6),
        Person("Lady Chair", ClubA, "Man Utd", 4),
        Person("Owner Two", ClubA, "Man Utd", 66),
        Person("Football Director", ClubA, "Man Utd", 10),
        Person("Other Owner", ClubB, "PSG", 66),
        Person("No Job", ClubA, "Man Utd", null),
    ];

    [Fact]
    public void Lists_only_board_roles_of_one_club_in_catalogue_order()
    {
        var board = ClubBoard.Members(Staff(), ClubA);
        Assert.Equal(["Owner", "Chairperson", "Director", "Director of Football"], board.Select(m => m.Role));
        Assert.Equal(["Owner Two", "Lady Chair", "Sir Director", "Football Director"], board.Select(m => m.Staff.DisplayName));
        Assert.DoesNotContain(board, m => m.Staff.DisplayName is "Erik Manager" or "Jim Coach" or "No Job");
        Assert.Single(ClubBoard.Members(Staff(), ClubB));
    }

    [Fact]
    public void Role_catalogue_covers_the_board_codes_and_leaves_other_jobs_empty()
    {
        Assert.Equal("Chairperson", ClubBoard.RoleName(4));
        Assert.Equal("Director", ClubBoard.RoleName(6));
        Assert.Equal("Technical Director", ClubBoard.RoleName(88));
        Assert.Equal("", ClubBoard.RoleName(2));
        Assert.Equal("", ClubBoard.RoleName(null));
        Assert.True(ClubBoard.IsBoard(66));
        Assert.False(ClubBoard.IsBoard(16));
        Assert.Equal(9, ClubBoard.Roles.Count);
    }
}

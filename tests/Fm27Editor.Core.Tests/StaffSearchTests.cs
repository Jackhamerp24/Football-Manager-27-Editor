using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Tests;

public class StaffSearchTests
{
    static readonly DateOnly Today = new(2026, 11, 10);

    static StaffRecord Staff(string first, string last, DateOnly? born = null, short ca = 100, short pa = 120,
        short? job = 2, string? nation = "England", (StaffAttribute Attribute, int Value)[]? attributes = null)
    {
        var bytes = new byte[Enum.GetValues<StaffAttribute>().Length];
        foreach (var (attribute, value) in attributes ?? []) bytes[(int)attribute] = (byte)value;
        return new StaffRecord
        {
            Address = 1,
            StaffAddress = 2,
            FirstName = first,
            LastName = last,
            DateOfBirth = born,
            Nationality = nation is null ? null : new NationInfo(1, nation, nation),
            JobCode = job,
            CurrentAbility = ca,
            PotentialAbility = pa,
            Attributes = bytes,
            Personality = new byte[8],
            Kind = new StaffKind(0, 0, 0, false),
        };
    }

    [Fact]
    public void NameAndNationalityMatchCaseInsensitively()
    {
        var staff = Staff("Rene", "Meulensteen", nation: "Netherlands");
        Assert.True(StaffSearch.Matches(staff, new StaffSearchCriteria(NameContains: "meulen", Nationality: "netherlands"), Today));
        Assert.False(StaffSearch.Matches(staff, new StaffSearchCriteria(NameContains: "meulen", Nationality: "England"), Today));
    }

    [Fact]
    public void AgeUsesTheSuppliedDateAndSkipsUnknownBirthdays()
    {
        var staff = Staff("Rene", "Meulensteen", born: new DateOnly(1964, 3, 25));
        Assert.True(StaffSearch.Matches(staff, new StaffSearchCriteria(MinAge: 62, MaxAge: 62), Today));
        Assert.False(StaffSearch.Matches(staff, new StaffSearchCriteria(MinAge: 63), Today));
        Assert.False(StaffSearch.Matches(staff, new StaffSearchCriteria(MaxAge: 61), Today));
        Assert.False(StaffSearch.Matches(Staff("No", "Birthday"), new StaffSearchCriteria(MinAge: 1), Today));
    }

    [Fact]
    public void AbilityAndAttributeRangesAreInclusive()
    {
        var staff = Staff("Rene", "Meulensteen", ca: 150, pa: 170,
            attributes: [(StaffAttribute.CoachingTechnical, 18), (StaffAttribute.Motivating, 15)]);
        var criteria = new StaffSearchCriteria(MinCa: 150, MaxCa: 150, MinPa: 170, MaxPa: 170,
            AttributeRanges: new Dictionary<StaffAttribute, (int, int)> { [StaffAttribute.CoachingTechnical] = (18, 20) });
        Assert.True(StaffSearch.Matches(staff, criteria, Today));
        var tooHigh = criteria with { AttributeRanges = new Dictionary<StaffAttribute, (int, int)> { [StaffAttribute.CoachingTechnical] = (19, 20) } };
        Assert.False(StaffSearch.Matches(staff, tooHigh, Today));
        Assert.False(StaffSearch.Matches(staff, criteria with { MaxCa = 149 }, Today));
    }

    [Fact]
    public void JobFilterMatchesTheRawCode()
    {
        var staff = Staff("Rene", "Meulensteen", job: 2);
        Assert.True(StaffSearch.Matches(staff, new StaffSearchCriteria(JobCode: 2), Today));
        Assert.False(StaffSearch.Matches(staff, new StaffSearchCriteria(JobCode: 16), Today));
        Assert.False(StaffSearch.Matches(Staff("No", "Job", job: null), new StaffSearchCriteria(JobCode: 2), Today));
    }

    [Fact]
    public void ClubAndReputationRangesAreInclusive()
    {
        var staff = Staff("Rene", "Meulensteen") with
        {
            Club = new ClubInfo(1, "Manchester United", "Man Utd"),
            HomeReputation = 7000,
            CurrentReputation = 6500,
            WorldReputation = 6000,
        };
        var criteria = new StaffSearchCriteria(
            ClubContains: "united",
            MinHomeReputation: 7000, MaxHomeReputation: 7000,
            MinCurrentReputation: 6500, MaxCurrentReputation: 6500,
            MinWorldReputation: 6000, MaxWorldReputation: 6000);
        Assert.True(StaffSearch.Matches(staff, criteria, Today));
        Assert.False(StaffSearch.Matches(staff, criteria with { ClubContains = "Liverpool" }, Today));
        Assert.False(StaffSearch.Matches(staff, criteria with { MaxHomeReputation = 6999 }, Today));
    }

    [Fact]
    public void EmptyCriteriaMatchesEverything()
    {
        Assert.True(StaffSearch.Matches(Staff("Anyone", "Here"), new StaffSearchCriteria(), Today));
    }
}

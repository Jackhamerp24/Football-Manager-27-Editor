using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class OffsetProfileTests
{
    static string Json => File.ReadAllText(TestWorld.ProfilePath);

    [Fact]
    public void Fm27_profile_has_the_measured_root_and_vtables()
    {
        var p = OffsetProfile.Load(Json);
        Assert.Equal("27.0.0+2486695", p.Build);
        Assert.Equal(0x4AE1492u, p.BuildStringRva);
        Assert.Equal(0x57037C0u, p.PlayerListRootRva);
        Assert.Equal(new[] { 0x4D27228u, 0x4F75A08u }, p.PlayerVtableRvas);
    }

    [Fact]
    public void Missing_attribute_offset_fails_at_load()
    {
        var broken = Json.Replace("\"Concentration\": \"0x194\"", "\"Unused\": \"0x194\"");
        Assert.Throws<FormatException>(() => OffsetProfile.Load(broken));
    }

    [Fact]
    public void Decimal_offsets_are_rejected()
    {
        var broken = Json.Replace("\"height\": \"0x22E\"", "\"height\": \"558\"");
        Assert.Throws<FormatException>(() => OffsetProfile.Load(broken));
    }

    [Fact]
    public void Written_json_loads_back_to_the_same_offsets()
    {
        var original = OffsetProfile.Load(Json);
        var copy = OffsetProfile.Load(original.ToJson());

        Assert.Equal(original.Build, copy.Build);
        Assert.Equal(original.PlayerListRootRva, copy.PlayerListRootRva);
        Assert.Equal(original.PlayerVtableRvas, copy.PlayerVtableRvas);
        Assert.Equal(original.Attributes.OrderBy(k => k.Key), copy.Attributes.OrderBy(k => k.Key));
        Assert.Equal(original.Positions.OrderBy(k => k.Key), copy.Positions.OrderBy(k => k.Key));
        Assert.Equal(original.Personality.OrderBy(k => k.Key), copy.Personality.OrderBy(k => k.Key));
        Assert.Equivalent(original.Fields, copy.Fields);
        Assert.Equivalent(original.Club, copy.Club);
        Assert.Equal(0x15F, copy.Attributes[PlayerAttribute.Crossing]);
        Assert.Equal(0x294, copy.Fields.Id);
        Assert.Equal(0xB0, copy.Club!.TransferClauses!.Pointer);
        Assert.Equal(0x4DC4088u, copy.Club.TransferClauses.CompetitionBonusVtableRva);
        // Year founded is int16 (Sheffield 1889, Everton 1878, Man Utd 1878, Man City 1894, PSG 1970); +0xDA is another field.
        Assert.Equal([0x100, 0xD8], copy.Club.Fields![Clubs.ClubField.YearFounded].Path);
        Assert.Equal(Clubs.ClubFieldType.Int16, copy.Club.Fields[Clubs.ClubField.YearFounded].Type);
        Assert.Equal([0x150, 0xC], copy.Club.Fields[Clubs.ClubField.Balance].Path);
        // FMRTE: Oxford embargo ends 31/12/2026, Sheffield none; first-team reputation Oxford 5400, Sheffield 6200.
        Assert.Equal([0x150, 0x28], copy.Club.Fields[Clubs.ClubField.EmbargoEnd].Path);
        Assert.Equal(Clubs.ClubFieldType.Date, copy.Club.Fields[Clubs.ClubField.EmbargoEnd].Type);
        Assert.Equal(Clubs.ClubFieldRoot.Team, copy.Club.Fields[Clubs.ClubField.TeamReputation].Root);
        Assert.Equal([0xA8], copy.Club.Fields[Clubs.ClubField.TeamReputation].Path);
        // Each checked on Sheffield 708 and Oxford 695 against FMRTE (docs/club-plan.md, re/club/STATUS.md 20:45).
        (Clubs.ClubField f, int[] path, Clubs.ClubFieldType type)[] expected =
        [
            (Clubs.ClubField.ChairmanStatus, [0x100, 0xDE], Clubs.ClubFieldType.Int16),        // 6916 / 0
            (Clubs.ClubField.SeasonTicketHolders, [0x100, 0xC0], Clubs.ClubFieldType.Int32),   // 19725 / 6500
            (Clubs.ClubField.SocialMediaFollowers, [0x100, 0xF8], Clubs.ClubFieldType.Int32),  // 574287 / -1
            (Clubs.ClubField.AverageAttendance, [0xB0, 0x68], Clubs.ClubFieldType.Int32),      // 27676 / 11123
            (Clubs.ClubField.MinimumAttendance, [0xB0, 0x6C], Clubs.ClubFieldType.Int32),      // 10000 / 4000
            (Clubs.ClubField.MaximumAttendance, [0xB0, 0x70], Clubs.ClubFieldType.Int32),      // 34595 / 20000
            (Clubs.ClubField.Training, [0x100, 0x120], Clubs.ClubFieldType.Byte),              // 13 / 10
            (Clubs.ClubField.YouthImportance, [0x100, 0x126], Clubs.ClubFieldType.Byte),       // 14 / 13
            (Clubs.ClubField.YouthFacilities, [0x100, 0x12B], Clubs.ClubFieldType.Byte),       // 12 / 8
            (Clubs.ClubField.YouthCoaching, [0x100, 0x12C], Clubs.ClubFieldType.Byte),         // 13 / 8
            (Clubs.ClubField.YouthRecruitment, [0x100, 0x12D], Clubs.ClubFieldType.Byte),      // 15 / 11
            (Clubs.ClubField.SupportersHardcore, [0x100, 0x106], Clubs.ClubFieldType.Byte),    // 12 / 0
            (Clubs.ClubField.SupportersCasual, [0x100, 0x10B], Clubs.ClubFieldType.Byte),      // 14 / 0
            (Clubs.ClubField.SupporterPatience, [0x100, 0x116], Clubs.ClubFieldType.Byte),     // 6 / 10
            (Clubs.ClubField.SupporterExpectations, [0x100, 0x119], Clubs.ClubFieldType.Byte), // 10 / 10
            // Tied on Sheffield (15/15, 7/7); separated by FMRTE Everton 17/14, 7/14 and Tottenham 18/14, 14/7 (lane C2).
            (Clubs.ClubField.SupporterLoyalty, [0x100, 0x114], Clubs.ClubFieldType.Byte),
            (Clubs.ClubField.SupporterPassion, [0x100, 0x115], Clubs.ClubFieldType.Byte),
            (Clubs.ClubField.SupporterAffluence, [0x100, 0x117], Clubs.ClubFieldType.Byte),
            (Clubs.ClubField.SupporterTemperament, [0x100, 0x118], Clubs.ClubFieldType.Byte),
            // Finance: Sheffield 708 / Everton 650 FMRTE (lane C1); embargo also Oxford 695 and Ismaily 106757.
            (Clubs.ClubField.TransferBudgetSeason, [0x150, 0x7C8], Clubs.ClubFieldType.Int32),    // 6,000,000 / 45,000,000
            (Clubs.ClubField.TransferBudgetRemaining, [0x150, 0x7C4], Clubs.ClubFieldType.Int32), // 6,000,000 / 42,158,348
            (Clubs.ClubField.WageBudget, [0x150, 0x808], Clubs.ClubFieldType.Int32),              // 496,589 / 2,146,625
            (Clubs.ClubField.WageUsed, [0x150, 0x814], Clubs.ClubFieldType.Int32),                // 490,710 / 2,133,119
            (Clubs.ClubField.MaxWage, [0x150, 0x80C], Clubs.ClubFieldType.Int32),                 // 25,000 / 294,973
            (Clubs.ClubField.AverageTicketPrice, [0x150, 0x10], Clubs.ClubFieldType.Float),       // 35.0f / 57.0f
            (Clubs.ClubField.EmbargoAppealDate, [0x150, 0x2C], Clubs.ClubFieldType.Date),         // none / Ismaily 31/12/2026
            (Clubs.ClubField.EmbargoReason, [0x150, 0x33], Clubs.ClubFieldType.Byte),             // 0 None / 0x04 Transfer Ban / 0x10 attached players
            (Clubs.ClubField.EmbargoAgeRestriction, [0x150, 0x30], Clubs.ClubFieldType.Byte),     // 0 / Ismaily 15
            // C1 22:20 FMRTE: Shenzhen / Ismaily / Tottenham / PSG, independently measured.
            (Clubs.ClubField.EmbargoStart, [0x150, 0x24], Clubs.ClubFieldType.Date),             // Shenzhen 2022-09-01 (day 244)
            (Clubs.ClubField.OwnershipType, [0x100, 0x129], Clubs.ClubFieldType.Byte),           // 1 LL / 3 Member-Owned / 5 Consortium
            (Clubs.ClubField.ChairpersonTitle, [0x100, 0x12A], Clubs.ClubFieldType.Byte),        // 1 Chairperson / 2 President
            (Clubs.ClubField.TransferRevenuePercent, [0x150, 0x7D0], Clubs.ClubFieldType.Int32), // 100 Sheffield / 90 Tottenham
            (Clubs.ClubField.RichBenefactor, [0x150, 0x34], Clubs.ClubFieldType.Byte),           // 3 Underwriter / 1 PSG / 0 Ismaily
            (Clubs.ClubField.StadiumRentalPerYear, [0x150, 0x7F8], Clubs.ClubFieldType.Int32),   // 0 Sheffield / 4295159 PSG
        ];
        foreach (var (f, path, type) in expected)
        {
            Assert.Equal(path, copy.Club.Fields[f].Path);
            Assert.Equal(type, copy.Club.Fields[f].Type);
        }
        Assert.Equal(new uint[] { 0x4CF0CC8, 0x4CF07F8 }, copy.Club.Fields[Clubs.ClubField.EmbargoStart].ObjectVtables);
        Assert.Equal(new uint[] { 0x4CF0CC8, 0x4CF07F8 }, copy.Club.Fields[Clubs.ClubField.RichBenefactor].ObjectVtables);
        Assert.Equal(new uint[] { 0x4CF0CC8 }, copy.Club.Fields[Clubs.ClubField.TransferRevenuePercent].ObjectVtables);
        Assert.Equal(new uint[] { 0x4CF0CC8 }, copy.Club.Fields[Clubs.ClubField.StadiumRentalPerYear].ObjectVtables);
        Assert.Equal(new ClubListOffsets(0x18, 0xA0, 0xC8, 0x78, 0x4DBAC98, 0x4B0E5C8,
            0x50, 0x2C, 0x2D, 0x20, 0x24, 0x8, 0xA, 0x118, 0x38, 0, 8, 0x20, 0x30, 0x138, 0xC, 0xA), copy.Club.Lists);
    }
}

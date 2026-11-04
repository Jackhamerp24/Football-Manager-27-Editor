using Fm27Editor.Core.Players;

namespace Fm27Editor.App;

/// <summary>
/// FMRTE 27's Career Plans dropdowns in its order, with the game's codes (FMRTE 26's CareerPlanCondition, JobType and
/// CareerPlanScope enums). Confirmed by FMRTE 27 writing "Has Knowledge Of" as 3 on Bruno Fernandes and by Jari Litmanen's
/// plans (Wants to be a Coach 2 / Ast. Manager 20 at AFC Ajax, Club).
/// </summary>
public static class CareerPlanLabels
{
    public static readonly IReadOnlyList<(int Code, string Name)> Conditions =
    [
        (1, "Wants to be a"), (2, "Doesn't want to be a"), (3, "Has Knowledge Of"), (4, "Doesn't Have Knowledge Of"),
        (5, "Prefers to scout in"), (6, "Doesn't want to scout in"),
    ];

    public static readonly IReadOnlyList<(int Code, string Name)> Jobs =
    [
        (1, "Player"), (2, "Coach"), (3, "Under 18s Coach"), (4, "Chairperson"), (6, "Director"), (8, "Managing Director"),
        (10, "Director of Football"), (12, "Physio"), (14, "Scout"), (16, "Manager"), (17, "Under 18s Manager"), (20, "Ast. Manager"),
        (21, "Under 18s Assistant Manager"), (22, "Set Piece Coach"), (24, "General Manager"), (26, "Fitness Coach"),
        (27, "Under 18s Fitness Coach"), (34, "GK Coach"), (35, "Under 18s Goalkeeping Coach"), (38, "Chief Doctor"),
        (40, "Head of Sports Science"), (44, "Chief Scout"), (45, "Under 18s Chief Scout"), (46, "Doctor"), (48, "Sports Scientist"),
        (50, "Head Physio"), (52, "U19 Manager"), (58, "Recruitment Analyst"), (60, "Performance Analyst"),
        (61, "Under 18s Performance Analyst"), (62, "Head Performance Analyst"), (64, "Head of Youth Development"), (66, "Owner"),
        (70, "President"), (86, "Loan Manager"), (88, "Technical Director"), (89, "Under 18s Technical Director"), (144, "Caretaker Manager"),
    ];

    public static readonly IReadOnlyList<(int Code, string Name)> Scopes = [(1, "Club"), (2, "International")];

    public static readonly IReadOnlyList<(CareerItemType Type, string Name)> ItemTypes =
        [(CareerItemType.Club, "Club"), (CareerItemType.Continent, "Continent"), (CareerItemType.Nation, "Nation"), (CareerItemType.Competition, "Competition")];

    public static string Name(IReadOnlyList<(int Code, string Name)> list, int code) =>
        list.FirstOrDefault(x => x.Code == code).Name ?? $"Code {code}";
}

/// <summary>Transfer Proposals labels: FMRTE 27's Type text, and FM26's TransferOfferStatus names as FMRTE 27 shows them.</summary>
public static class ProposalLabels
{
    public static string Type(ProposalType t) => t switch
    {
        ProposalType.Transfer => "Transfer", ProposalType.Loan => "Loan", ProposalType.Trial => "Trial", _ => "B Club Move",
    };

    /// <summary>8, 10, 12, 19 seen in FMRTE 27; the rest are FM26's enum names.</summary>
    public static string Status(int code) => code switch
    {
        0 => "Offer made", 1 => "Negotiating offer", 2 => "Offer rejected", 3 => "Cancelled", 5 => "Offer accepted by seller",
        8 => "Player accepted", 10 => "Unknown status 1", 11 => "Player ready to sign", 12 => "Ready to sign", 15 => "Unknown status 2",
        16 => "Unknown status 3", 18 => "Delayed", 19 => "Confirmed", 20 => "Completed", _ => $"Code {code}",
    };
}

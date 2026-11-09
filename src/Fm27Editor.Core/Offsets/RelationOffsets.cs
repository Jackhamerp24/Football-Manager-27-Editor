namespace Fm27Editor.Core.Offsets;

/// <summary>
/// Player → contract → team → club. Optional in a profile: builds without it still load players.
/// </summary>
public sealed class ContractOffsets
{
    /// <summary>Pointer inside the player object to its current contract.</summary>
    public required int PlayerContract { get; init; }
    public required uint ContractVtableRva { get; init; }
    /// <summary>Pointer inside the contract to the team the player is registered with.</summary>
    public required int Team { get; init; }
    /// <summary>Pointer to the employer's senior team, separate from the player's playing team.</summary>
    public int? SeniorTeam { get; init; }
    /// <summary>Person pointer and job byte in a player's contract.</summary>
    public int? Owner { get; init; }
    public int? Job { get; init; }
    /// <summary>int32, weekly, in the game's base currency.</summary>
    public required int Wage { get; init; }
    /// <summary>int16 day of year (1-based) + int16 year.</summary>
    public required int ExpiryDate { get; init; }
    public required int JoinedDate { get; init; }
    /// <summary>Pointer inside the team object to its club.</summary>
    public required int TeamClub { get; init; }
    /// <summary>Agreed playing time, one byte (index into <see cref="Players.PlayingTime"/>); optional.</summary>
    public int? PlayingTime { get; init; }

    // Measured on FM26 26.3.2 against FMRTE's contract screen and confirmed on FM27 27.0.0 (Acerbi at PSG).
    /// <summary>int16 contract type (0 part time, 1 full time, 2 amateur, 3 youth, 6 non contract).</summary>
    public int? Type { get; init; }
    /// <summary>Start date; the day-of-year int16 keeps other flags in its bits above 9.</summary>
    public int? Started { get; init; }
    public int? LoyaltyBonus { get; init; }
    /// <summary>sbyte, -1 when no number is assigned (FM27: +0x5D; FMRTE Man Utd Bruno 8, Heaton 22, Martínez 6).</summary>
    public int? SquadNumber { get; init; }
    public int? Happiness { get; init; }
    public int? PlayingTimeHappiness { get; init; }
    public int? FuturePlayingTime { get; init; }
    /// <summary>int16 year the future playing time applies from.</summary>
    public int? FuturePlayingTimeYear { get; init; }
    /// <summary>Date the player joined the club, inside the <b>player</b> object (FM27 +0x3A4: writing 20/7/2026 there on
    /// Tielemans changed FMRTE's Joined Club and left Signed, contract + joinedDate, at 13/7/2026).</summary>
    public int? PlayerJoinedClub { get; init; }
    /// <summary>Vector (begin/end/cap) of 8-byte entries: int32 value, int16 parameter, int16 type.</summary>
    public int? Clauses { get; init; }
}

/// <summary>Player → playing team, and the team's squad vector (begin, end, capacity pointers).</summary>
public sealed class TeamOffsets
{
    public required uint VtableRva { get; init; }
    /// <summary>Pointer inside the player object to the team he plays for (can differ from the contract team).</summary>
    public required int PlayerTeam { get; init; }
    /// <summary>Squad vector begin; end and capacity follow at +8 and +16.</summary>
    public required int Squad { get; init; }
    /// <summary>Optional team classification and age bytes, measured for first team, U21 and U18.</summary>
    public int? Classification { get; init; }
    public int? AgeLimit { get; init; }
    public int? Captain { get; init; }
    public int? ViceCaptain { get; init; }
}

/// <summary>
/// Current in-game date: [module + RootRva] → object, then [object + PointerOffset] → node when
/// PointerOffset is set (otherwise the node is the object itself); node + DateOffset holds int16 day of
/// year (1-based) + int16 year. When ObjectVtableRva is set, the object must carry that vtable.
/// </summary>
public sealed record DateChain(uint RootRva, int? PointerOffset, int DateOffset, uint? ObjectVtableRva = null);

public sealed class GameDateOffsets
{
    public required DateChain Primary { get; init; }
    /// <summary>An independent chain to the same date; both must agree before the date is trusted.</summary>
    public DateChain? CrossCheck { get; init; }
}

public sealed class ClubOffsets
{
    public ClubListOffsets? Lists { get; init; }
    public ClubFinanceListOffsets? FinanceLists { get; init; }
    public ClubRelationshipOffsets? Relationships { get; init; }
    public required uint ClubVtableRva { get; init; }
    /// <summary>Pointers to length-prefixed UTF-8 strings.</summary>
    public required int Name { get; init; }
    public required int ShortName { get; init; }
    public ClubFinanceOffsets? Finance { get; init; }
    /// <summary>int32 club ID as the game UI shows it (Man Utd 680, PSG 868).</summary>
    public int? Id { get; init; }
    /// <summary>Nation* FMRTE shows as the club's Nation (Man Utd and Swansea: England; Swansea's +0xE8 is Wales).</summary>
    public int? Nation { get; init; }
    /// <summary>Inline vectors of staff-part pointers, and the club's gender marker.</summary>
    public int? MedicalStaff { get; init; }
    public int? CoachingStaff { get; init; }
    public int? ScoutingStaff { get; init; }
    public int? Gender { get; init; }
    /// <summary>Measured allocator size classes for staff department vectors; absent means no growth beyond capacity.</summary>
    public IReadOnlyList<int>? StaffAllocationClasses { get; init; }
    public TransferClauseOffsets? TransferClauses { get; init; }
    /// <summary>Single-value club and team fields (profile <c>club.fields</c>); see docs/club-plan.md.</summary>
    public IReadOnlyDictionary<Clubs.ClubField, Clubs.ClubFieldOffset>? Fields { get; init; }
}

public sealed record NationRivalryOffsets(int Team, uint TeamVtable, int Vector, int EntrySize, int Type, int RivalType, int Value);
public sealed record NationPreferenceOffsets(int Team, uint TeamVtable, int Vector, int EntrySize);

/// <summary>
/// Club → holder object whose first field is a vector (begin/end/cap) of pointers to the clauses on that
/// club's players. The vector also holds objects of other kinds (links to other clubs); only the vtables
/// below are read as clauses. Record layouts are in <see cref="Clubs.TransferClauseReader"/>.
/// </summary>
public sealed class TransferClauseOffsets
{
    /// <summary>Pointer inside the club object to the holder.</summary>
    public required int Pointer { get; init; }
    public required uint SellOnVtableRva { get; init; }
    public uint? AppearanceFeeVtableRva { get; init; }
    public uint? CompetitionBonusVtableRva { get; init; }
}

/// <summary>
/// Club → finance object, and the totals the club screens show. Measured on Manchester United only
/// (re/FM27-findings.md section 6), so every field stays optional except the pointer.
/// </summary>
public sealed class ClubFinanceOffsets
{
    /// <summary>Pointer inside the club object to its finance object.</summary>
    public required int Pointer { get; init; }
    /// <summary>Full finance object (0x8B0 bytes, budgets and wages). Small clubs have a compact one instead.</summary>
    public uint? VtableRva { get; init; }
    /// <summary>Compact finance object of small clubs: only the first <see cref="CompactSize"/> bytes (balance,
    /// embargo) exist; the objects sit back to back, so a budget offset would hit the next club's object.</summary>
    public uint? CompactVtableRva { get; init; }
    public int? CompactSize { get; init; }

    /// <summary>
    /// Whether a field at <paramref name="offset"/> (<paramref name="size"/> bytes) exists in a finance object with
    /// vtable <paramref name="vtableRva"/>. Null when the vtable is neither known class (do not touch it).
    /// </summary>
    public bool? Holds(uint vtableRva, int offset, int size)
    {
        if (VtableRva is null && CompactVtableRva is null) return true;
        if (vtableRva == VtableRva) return true;
        if (vtableRva == CompactVtableRva) return CompactSize is { } s && offset + size <= s;
        return null;
    }
    /// <summary>Overall balance, int32.</summary>
    public int? Balance { get; init; }
    /// <summary>Transfer budget still available, int32.</summary>
    public int? TransferBudget { get; init; }
    /// <summary>Spent on transfers this season, int32.</summary>
    public int? SpentThisSeason { get; init; }
    /// <summary>Wage spend per week, int32.</summary>
    public int? WageSpend { get; init; }
    /// <summary>Player wage bill per week; moves by exactly the contract wage on a transfer.</summary>
    public int? WageBill { get; init; }
    /// <summary>Transfer expenditure this month.</summary>
    public int? TransferSpendMonth { get; init; }
    /// <summary>Income from players sold this month.</summary>
    public int? PlayersSold { get; init; }
}

/// <summary>
/// Player → contract that has been agreed but not yet completed. On 27.0.0+2486695 the pointer is at
/// player+0x328 and holds 0 until a deal is pending; the completed contract is a different pointer
/// (player+0x330). Clause table offset is where a run of (int32 value, int16 -1, int16 type) starts.
/// </summary>
public sealed class PendingContractOffsets
{
    /// <summary>Pointer inside the player object.</summary>
    public required int PlayerPointer { get; init; }
    public int? Clauses { get; init; }
}

/// <summary>Player → primary nation object (nationality).</summary>
public sealed class NationOffsets
{
    public required int PlayerNation { get; init; }
    public required uint VtableRva { get; init; }
    public required int Name { get; init; }
    public required int ShortName { get; init; }
    /// <summary>Every nation: [module + ListRootRva] + ListOffset is a begin/end vector of nation pointers (251 on FM27).</summary>
    public uint? ListRootRva { get; init; }
    public int? ListOffset { get; init; }
    /// <summary>int32 ID as FMRTE shows it (Vietnam 145, England 765).</summary>
    public int? Id { get; init; }
    /// <summary>City* of the Capital (Vietnam Ha Noi, Brazil Brasília (DF)); FMRTE shows it as plain text, so it is read only.</summary>
    public int? Capital { get; init; }
    /// <summary>Continent* (Asia, Brazil South America); the women's continent is a separate pointer at +0x2F0 that FMRTE did not change.</summary>
    public int? Continent { get; init; }
    /// <summary>Stadium* of the national team (Vietnam My Dinh, England Wembley, Brazil none); read only in the editor.</summary>
    public int? Stadium { get; init; }
    /// <summary>National teams of the men's side (profile nation.teams): nation + Pointer is the team holder, whose begin/end/cap vector of team pointers sits at +List.</summary>
    public NationTeamOffsets? Teams { get; init; }
    /// <summary>The women's national team (profile nation.womenTeam, 0x110 on 27.0.0+2493426): staff knowledge can point at it.</summary>
    public int? WomenTeam { get; init; }
    public NationRivalryOffsets? Rivalries { get; init; }
    public NationPreferenceOffsets? Preferences { get; init; }
    /// <summary>Single-value nation and national-team fields (profile <c>nation.fields</c>, paths from the nation object).</summary>
    public IReadOnlyDictionary<Nations.NationField, Clubs.ClubFieldOffset>? Fields { get; init; }
}

/// <summary>Team → competition (league / division) object.</summary>
public sealed class CompetitionOffsets
{
    public required int TeamCompetition { get; init; }
    public required uint VtableRva { get; init; }
    public required int Name { get; init; }
    public required int ShortName { get; init; }
    /// <summary>int32 internal index, the one clauses refer to (Championship 8, LaLiga 51, LaLiga Hypermotion 52).</summary>
    public int? Index { get; init; }
    /// <summary>int32 ID as FMRTE shows it (LaLiga 67, LaLiga Hypermotion 68).</summary>
    public int? Id { get; init; }
    /// <summary>Game-wide competition list: pointer vector at [module + ListRootRva] + ListOffset (8,213 on 3/11/2026).</summary>
    public int? ListRootRva { get; init; }
    public int? ListOffset { get; init; }
    /// <summary>int16 Reputation; FMRTE 27 writes the same value to <see cref="ReputationMirror"/> (Süper Lig 155 → 156).</summary>
    public int? Reputation { get; init; }
    public int? ReputationMirror { get; init; }
    /// <summary>Colours are 4 bytes B, G, R, A. Winter ball dates are int16 day-of-year + int16 year (1900 = none).</summary>
    public int? Foreground { get; init; }
    public int? Background { get; init; }
    public int? Trim { get; init; }
    public int? WinterBallColour { get; init; }
    public int? WinterBallStart { get; init; }
    public int? WinterBallEnd { get; init; }
    /// <summary>Byte FMRTE's Substitution Rules dropdown edits. Measured 5/11/2026 on build 2493426: 0xFF is
    /// "Do Not Override Sub Rules" (Premier League and Championship both held FF) and 0x11 is "5 from 9"
    /// (Premier League after FMRTE wrote it). Values for the other options are not mapped.</summary>
    public int? SubstitutionRules { get; init; }
    /// <summary>Prize Money path and vtables verified against FMRTE on this build.</summary>
    public CompetitionPrizeOffsets? PrizeMoney { get; init; }
    public uint? SquadRulesContainerVtableRva { get; init; }
}

public sealed record CompetitionPrizeOffsets(int Holder, int Stages, int StageOwner, int StagePrize,
    int PerWin, int PerDraw, int PerDefeat, int Classification,
    uint HolderVtableRva, uint StageVtableRva, uint PrizeVtableRva);

/// <summary>
/// Award object (FMRTE 27's Award page): game-wide pointer vector at [module + ListRootRva] + ListOffset (263 on 3/11/2026).
/// Reputation is an int16; the winner's home and world reputation are single bytes.
/// </summary>
public sealed class AwardOffsets
{
    public required uint VtableRva { get; init; }
    public required int ListRootRva { get; init; }
    public required int ListOffset { get; init; }
    public required int Index { get; init; }
    public required int Id { get; init; }
    public required int Name { get; init; }
    public required int ShortName { get; init; }
    public required int Reputation { get; init; }
    public required int WinnerHomeReputation { get; init; }
    public required int WinnerWorldReputation { get; init; }
}

/// <summary>Nation → national-team holder → vector of team objects; Reputation is an int32 inside each team (FMRTE's Teams panel).</summary>
public sealed record NationTeamOffsets(int Pointer, int List, uint TeamVtable, int Reputation);

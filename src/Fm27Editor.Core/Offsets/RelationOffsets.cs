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
    /// <summary>int32, weekly, in the game's base currency.</summary>
    public required int Wage { get; init; }
    /// <summary>int16 day of year (1-based) + int16 year.</summary>
    public required int ExpiryDate { get; init; }
    public required int JoinedDate { get; init; }
    /// <summary>Pointer inside the team object to its club.</summary>
    public required int TeamClub { get; init; }
}

/// <summary>
/// Current in-game date: [module + RootRva] → object, [object + PointerOffset] → node,
/// node + DateOffset holds int16 day of year (1-based) + int16 year.
/// </summary>
public sealed record DateChain(uint RootRva, int PointerOffset, int DateOffset);

public sealed class GameDateOffsets
{
    public required DateChain Primary { get; init; }
    /// <summary>An independent chain to the same date; both must agree before the date is trusted.</summary>
    public DateChain? CrossCheck { get; init; }
}

public sealed class ClubOffsets
{
    public required uint ClubVtableRva { get; init; }
    /// <summary>Pointers to length-prefixed UTF-8 strings.</summary>
    public required int Name { get; init; }
    public required int ShortName { get; init; }
}

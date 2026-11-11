using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

public sealed record ClubInfo(ulong Address, string? Name, string? ShortName, Gender Gender = Gender.Unknown)
{
    public string DisplayName => ShortName ?? Name ?? "";
}

public sealed record NationInfo(ulong Address, string? Name, string? ShortName, int? Id = null)
{
    public string DisplayName => Name ?? ShortName ?? "";
}

public sealed record CompetitionInfo(ulong Address, string? Name, string? ShortName)
{
    public string DisplayName => ShortName ?? Name ?? "";
}

/// <summary>The player's current contract. Wage is weekly in the game's base currency.</summary>
public sealed record ContractInfo(ulong Address, int Wage, DateOnly? Expires, DateOnly? Joined, ClubInfo? Club);

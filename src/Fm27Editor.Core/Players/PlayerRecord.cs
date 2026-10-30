namespace Fm27Editor.Core.Players;

/// <summary>A decoded snapshot of one player object, taken at a single point in time.</summary>
public sealed class PlayerRecord
{
    public required ulong Address { get; init; }
    public required uint VtableRva { get; init; }

    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? CommonName { get; init; }
    public string? FullName { get; init; }
    public string DisplayName => !string.IsNullOrEmpty(CommonName) ? CommonName : $"{FirstName} {LastName}".Trim();

    public required short CurrentAbility { get; init; }
    public required short PotentialAbility { get; init; }
    public required short HomeReputation { get; init; }
    public required short CurrentReputation { get; init; }
    public required short WorldReputation { get; init; }
    public required short Height { get; init; }
    public DateOnly? DateOfBirth { get; init; }

    /// <summary>Null when the profile has no contract offsets or the player has no contract.</summary>
    public ContractInfo? Contract { get; init; }
    public ClubInfo? Club => Contract?.Club;

    /// <summary>Raw bytes on the game's 1-100 scale, indexed by <see cref="PlayerAttribute"/>.</summary>
    public required byte[] RawAttributes { get; init; }
    public required int AttributeScale { get; init; }
    /// <summary>Indexed by <see cref="PlayerPosition"/>, 1-20.</summary>
    public required byte[] Positions { get; init; }
    /// <summary>Indexed by <see cref="PersonalityAttribute"/>, 1-20.</summary>
    public required byte[] Personality { get; init; }

    public byte Raw(PlayerAttribute a) => RawAttributes[(int)a];
    public int Attribute(PlayerAttribute a) => AttributeScaling.ToDisplay(RawAttributes[(int)a], AttributeScale);
    public int Position(PlayerPosition p) => Positions[(int)p];
    public int PersonalityValue(PersonalityAttribute p) => Personality[(int)p];

    public int? AgeOn(DateOnly date)
    {
        if (DateOfBirth is not { } dob) return null;
        int age = date.Year - dob.Year;
        return date < dob.AddYears(age) ? age - 1 : age;
    }
}

public static class AttributeScaling
{
    /// <summary>Game value 1-100 to the displayed 1-20. Never below 1, as in the game UI.</summary>
    public static int ToDisplay(byte raw, int scale) => Math.Max(1, (int)Math.Round(raw / (double)scale));

    public static byte ToRaw(int display, int scale) => (byte)(display * scale);
}

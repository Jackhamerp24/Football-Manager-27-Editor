using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.History;

/// <summary>Names the bytes of a player object for the edit history, from the profile's own offsets.</summary>
public static class PlayerChangeLabels
{
    /// <summary>
    /// Labeler for the player object at <paramref name="player"/>: attributes (shown on the 1–20 scale), personality,
    /// positions, ability, reputation, height and date of birth. Other bytes are left unnamed.
    /// </summary>
    public static Func<ulong, int, ChangeLabel?> For(OffsetProfile profile, ulong player)
    {
        var byOffset = new Dictionary<int, ChangeLabel>();
        foreach (var (attribute, offset) in profile.Attributes) byOffset[offset] = new(Words(attribute.ToString()), Math.Max(1, profile.AttributeScale));
        foreach (var (attribute, offset) in profile.Personality) byOffset[offset] = new(Words(attribute.ToString()));
        foreach (var (position, offset) in profile.Positions) byOffset[offset] = new(Words(position.ToString()));
        var f = profile.Fields;
        byOffset[f.CurrentAbility] = new("Current Ability");
        byOffset[f.PotentialAbility] = new("Potential Ability");
        byOffset[f.HomeReputation] = new("Home Reputation");
        byOffset[f.CurrentReputation] = new("Current Reputation");
        byOffset[f.WorldReputation] = new("World Reputation");
        byOffset[f.Height] = new("Height");
        byOffset[f.DateOfBirth] = new("Date of Birth");
        return (address, length) => address >= player && address - player < (ulong)profile.PlayerObjectSize
            && byOffset.TryGetValue((int)(address - player), out var label) ? label : null;
    }

    /// <summary>"FirstTouch" → "First Touch".</summary>
    public static string Words(string name)
    {
        var text = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])))) text.Append(' ');
            text.Append(name[i]);
        }
        return text.ToString();
    }
}

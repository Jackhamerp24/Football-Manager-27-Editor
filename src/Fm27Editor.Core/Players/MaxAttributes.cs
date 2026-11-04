namespace Fm27Editor.Core.Players;

public sealed record MaxAttributesPlan(IReadOnlyList<PlayerMassChange> Changes, int Unavailable);

/// <summary>
/// Sets every performance attribute to 20. Injury Proneness and Dirtiness are left untouched because high
/// values are bad for those two; the project owner asked for them to keep their current value.
/// </summary>
public static class MaxAttributes
{
    public static MaxAttributesPlan Plan(IEnumerable<PlayerRecord> players)
    {
        var selection = players.ToArray();
        if (selection.Any(p => p.Id is not > 0) || selection.Select(p => p.Id).Distinct().Count() != selection.Length)
            throw new ArgumentException("Players must have unique positive IDs");
        var changes = new List<PlayerMassChange>();
        int unavailable = 0;
        foreach (var attribute in Enum.GetValues<PlayerAttribute>())
        {
            if (attribute is PlayerAttribute.Dirtiness or PlayerAttribute.InjuryProneness) continue;
            var valid = selection.Where(p => p.Raw(attribute) is >= 1 and <= 100).ToArray();
            unavailable += selection.Length - valid.Length;
            changes.AddRange(PlayerMassEdit.Plan(valid, attribute, MassEditMode.Set, 20));
        }
        return new(changes, unavailable);
    }
}

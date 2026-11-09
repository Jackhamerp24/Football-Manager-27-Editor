namespace Fm27Editor.Core.Players;

public static class PlayerScalarMassEdit
{
    public static readonly IReadOnlyList<PlayerField> Fields =
        [PlayerField.Condition, PlayerField.MatchSharpness, PlayerField.Jadedness, PlayerField.Morale];

    public static IReadOnlyList<PlayerPresetChange> Plan(IEnumerable<PlayerRecord> players, PlayerFieldAccess access,
        PlayerField field, MassEditMode mode, long value)
    {
        if (!Fields.Contains(field) || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid mass edit field or mode");
        // FMRTE 27 rejects Condition/Sharpness below 5000 on save; the editor uses the same floor.
        var (minimum, maximum) = field switch
        {
            PlayerField.Condition or PlayerField.MatchSharpness => (5000L, 10000L),
            PlayerField.Jadedness => (-500L, 1000L),
            PlayerField.Morale => (1L, 20L),
            _ => throw new ArgumentException("Invalid mass edit field")
        };
        var ids = new HashSet<int>();
        var changes = new List<PlayerPresetChange>();
        foreach (var player in players)
        {
            if (player.Id is not > 0 || !ids.Add(player.Id.Value)) throw new ArgumentException("Players must have unique positive IDs");
            long current = access.Read(player, field) ?? throw new StaleObjectException($"{player.DisplayName}: {field} is not readable");
            long target = mode == MassEditMode.Set ? value : checked(current + value);
            if (target < minimum || target > maximum) throw new ArgumentOutOfRangeException(nameof(value), target, $"{field} must be between {minimum} and {maximum}");
            access.ValidateValue(field, target);
            if (target != current) changes.Add(new(player, field, current, target));
        }
        return changes;
    }
}

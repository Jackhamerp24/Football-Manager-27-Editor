namespace Fm27Editor.Core.Players;

public enum PlayerPreset { Fitness, Morale }
public sealed record PlayerPresetChange(PlayerRecord Player, PlayerField Field, long Before, long After);

/// <summary>Plans reversible single-value squad presets without modifying injuries or list records.</summary>
public static class PlayerPresetBatch
{
    public static IReadOnlyList<PlayerPresetChange> Plan(IEnumerable<PlayerRecord> players, PlayerFieldAccess fields, PlayerPreset preset)
    {
        (PlayerField Field, long Value)[] targets = preset switch
        {
            PlayerPreset.Fitness => [(PlayerField.Condition, 10000), (PlayerField.MatchSharpness, 10000), (PlayerField.Jadedness, -500)],
            PlayerPreset.Morale => [(PlayerField.Morale, 20)],
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };
        var ids = new HashSet<int>();
        var changes = new List<PlayerPresetChange>();
        foreach (var player in players)
        {
            if (player.Id is not > 0 || !ids.Add(player.Id.Value)) throw new ArgumentException("Players must have unique positive IDs");
            foreach (var (field, target) in targets)
            {
                long current = fields.Read(player, field) ?? throw new StaleObjectException($"{player.DisplayName}: {field} is not readable");
                if (current != target) changes.Add(new(player, field, current, target));
            }
        }
        return changes;
    }

    public static void Apply(PlayerPresetChange change, PlayerRepository repository, PlayerFieldAccess fields, Action? onWritten = null) =>
        Write(change, repository, fields, change.Before, change.After, onWritten);

    public static void Undo(PlayerPresetChange change, PlayerRepository repository, PlayerFieldAccess fields, Action? onWritten = null) =>
        Write(change, repository, fields, change.After, change.Before, onWritten);

    static void Write(PlayerPresetChange change, PlayerRepository repository, PlayerFieldAccess fields, long expected, long value, Action? onWritten)
    {
        var current = repository.Reload(change.Player.Address);
        if (current is null || current.Id != change.Player.Id) throw new StaleObjectException("Player changed since the preset was prepared");
        fields.Set(current, change.Field, expected, value);
        onWritten?.Invoke();
    }
}

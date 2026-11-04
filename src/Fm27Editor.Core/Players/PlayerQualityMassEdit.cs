namespace Fm27Editor.Core.Players;

public enum PlayerQuality { CurrentAbility, PotentialAbility, HomeReputation, CurrentReputation, WorldReputation, Height }
public sealed record PlayerQualityChange(PlayerRecord Player, PlayerQuality Field, int Before, int After);

/// <summary>Reversible bulk changes using the same guarded writes as the player editor.</summary>
public static class PlayerQualityMassEdit
{
    public static IReadOnlyList<PlayerQualityChange> Plan(IEnumerable<PlayerRecord> players, PlayerQuality field, MassEditMode mode, int value)
    {
        if (!Enum.IsDefined(field) || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid mass edit field or mode");
        var (min, max) = field switch
        {
            PlayerQuality.CurrentAbility or PlayerQuality.PotentialAbility => (1, 200),
            PlayerQuality.HomeReputation or PlayerQuality.CurrentReputation or PlayerQuality.WorldReputation => (0, 10000),
            PlayerQuality.Height => (100, 250),
            _ => throw new ArgumentException("Invalid mass edit field")
        };
        var ids = new HashSet<int>();
        var changes = new List<PlayerQualityChange>();
        foreach (var player in players)
        {
            if (player.Id is not > 0 || !ids.Add(player.Id.Value)) throw new ArgumentException("Players must have unique positive IDs");
            int current = Read(player, field);
            // Undo must be able to restore the original through the same writer.
            if (current < min || current > max) throw new ArgumentException($"{player.DisplayName}: original {field} cannot be restored by the writer");
            int target = mode == MassEditMode.Set ? value : checked(current + value);
            if (target < min || target > max) throw new ArgumentOutOfRangeException(nameof(value), target, $"{field} must be between {min} and {max}");
            if (current != target) changes.Add(new(player, field, current, target));
        }
        return changes;
    }

    public static void Apply(PlayerQualityChange change, PlayerRepository repository, PlayerWriter writer, Action? onWritten = null) =>
        Write(change, repository, writer, change.Before, change.After, onWritten);

    public static void Undo(PlayerQualityChange change, PlayerRepository repository, PlayerWriter writer, Action? onWritten = null) =>
        Write(change, repository, writer, change.After, change.Before, onWritten);

    static void Write(PlayerQualityChange change, PlayerRepository repository, PlayerWriter writer, int expected, int value, Action? onWritten)
    {
        var current = repository.Reload(change.Player.Address);
        if (current is null || current.Id != change.Player.Id || Read(current, change.Field) != expected)
            throw new StaleObjectException("Player or field changed since the batch was prepared");
        switch (change.Field)
        {
            case PlayerQuality.CurrentAbility: writer.SetCurrentAbility(current, value); break;
            case PlayerQuality.PotentialAbility: writer.SetPotentialAbility(current, value); break;
            case PlayerQuality.HomeReputation: writer.SetReputation(current, Reputation.Home, value); break;
            case PlayerQuality.CurrentReputation: writer.SetReputation(current, Reputation.Current, value); break;
            case PlayerQuality.WorldReputation: writer.SetReputation(current, Reputation.World, value); break;
            case PlayerQuality.Height: writer.SetHeight(current, value); break;
            default: throw new ArgumentException("Invalid mass edit field");
        }
        onWritten?.Invoke();
    }

    static int Read(PlayerRecord player, PlayerQuality field) => field switch
    {
        PlayerQuality.CurrentAbility => player.CurrentAbility,
        PlayerQuality.PotentialAbility => player.PotentialAbility,
        PlayerQuality.HomeReputation => player.HomeReputation,
        PlayerQuality.CurrentReputation => player.CurrentReputation,
        PlayerQuality.WorldReputation => player.WorldReputation,
        PlayerQuality.Height => player.Height,
        _ => throw new ArgumentException("Invalid mass edit field")
    };
}

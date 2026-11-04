namespace Fm27Editor.Core.Players;

public sealed record PlayerRatingField(PlayerPosition? Position = null, PersonalityAttribute? Personality = null)
{
    public override string ToString() => Position is { } p ? $"Position: {p}" : $"Personality: {Personality}";
}
public sealed record PlayerRatingChange(PlayerRecord Player, PlayerRatingField Field, int Before, int After);

public static class PlayerRatingMassEdit
{
    public static IReadOnlyList<PlayerRatingChange> Plan(IEnumerable<PlayerRecord> players, PlayerRatingField field, MassEditMode mode, int value)
    {
        if (!Enum.IsDefined(mode) ||
            !((field.Position is { } p && Enum.IsDefined(p) && field.Personality is null) ||
              (field.Personality is { } a && Enum.IsDefined(a) && field.Position is null)))
            throw new ArgumentException("Invalid mass edit field or mode");
        var ids = new HashSet<int>();
        var changes = new List<PlayerRatingChange>();
        foreach (var player in players)
        {
            if (player.Id is not > 0 || !ids.Add(player.Id.Value)) throw new ArgumentException("Players must have unique positive IDs");
            int current = Read(player, field);
            if (current is < 1 or > 20) throw new ArgumentException($"{player.DisplayName}: original {field} cannot be restored by the writer");
            int target = mode == MassEditMode.Set ? value : checked(current + value);
            if (target is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(value), target, "Value must be between 1 and 20");
            if (current != target) changes.Add(new(player, field, current, target));
        }
        return changes;
    }

    public static void Apply(PlayerRatingChange change, PlayerRepository repository, PlayerWriter writer, Action? onWritten = null) =>
        Write(change, repository, writer, change.Before, change.After, onWritten);
    public static void Undo(PlayerRatingChange change, PlayerRepository repository, PlayerWriter writer, Action? onWritten = null) =>
        Write(change, repository, writer, change.After, change.Before, onWritten);

    static void Write(PlayerRatingChange change, PlayerRepository repository, PlayerWriter writer, int expected, int value, Action? onWritten)
    {
        var current = repository.Reload(change.Player.Address);
        if (current is null || current.Id != change.Player.Id || Read(current, change.Field) != expected)
            throw new StaleObjectException("Player or rating changed since the batch was prepared");
        if (change.Field.Position is { } position) writer.SetPosition(current, position, value);
        else if (change.Field.Personality is { } personality) writer.SetPersonality(current, personality, value);
        else throw new ArgumentException("Invalid mass edit field");
        onWritten?.Invoke();
    }

    static int Read(PlayerRecord player, PlayerRatingField field) => field.Position is { } p
        ? player.Position(p) : player.PersonalityValue(field.Personality ?? throw new ArgumentException("Invalid mass edit field"));
}

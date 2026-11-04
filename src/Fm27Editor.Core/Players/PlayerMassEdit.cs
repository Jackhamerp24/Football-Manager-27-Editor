namespace Fm27Editor.Core.Players;

public enum MassEditMode { Set, Change }
public sealed record PlayerMassOperation(PlayerAttribute Attribute, MassEditMode Mode, int Value)
{
    public override string ToString() => $"{Attribute}: {Mode} {Value}";
}
public sealed record PlayerMassChange(PlayerRecord Player, PlayerAttribute Attribute, byte BeforeRaw, byte AfterRaw)
{
    public int Before => AttributeScaling.ToDisplay(BeforeRaw, Player.AttributeScale);
    public int After => AttributeScaling.ToDisplay(AfterRaw, Player.AttributeScale);
}

/// <summary>Plans a batch before any writes; each application and undo uses the guarded writer.</summary>
public static class PlayerMassEdit
{
    public static IReadOnlyList<PlayerMassChange> Plan(IEnumerable<PlayerRecord> players, IEnumerable<PlayerMassOperation> operations)
    {
        var selection = players.ToArray();
        var actions = operations.ToArray();
        if (actions.Length == 0 || actions.Select(a => a.Attribute).Distinct().Count() != actions.Length)
            throw new ArgumentException("Choose at least one operation; each attribute can only appear once", nameof(operations));
        return actions.SelectMany(a => Plan(selection, a.Attribute, a.Mode, a.Value)).ToArray();
    }

    public static IReadOnlyList<PlayerMassChange> Plan(IEnumerable<PlayerRecord> players, PlayerAttribute attribute, MassEditMode mode, int value)
    {
        if (!Enum.IsDefined(attribute) || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid mass edit");
        if (mode == MassEditMode.Set && value is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(value));
        if (mode == MassEditMode.Change && value is < -19 or > 19) throw new ArgumentOutOfRangeException(nameof(value));
        var changes = new List<PlayerMassChange>();
        var ids = new HashSet<int>();
        foreach (var player in players)
        {
            if (player.Id is not > 0 || !ids.Add(player.Id.Value)) throw new ArgumentException("Players must have unique positive IDs");
            byte original = player.Raw(attribute);
            if (original is < 1 or > 100) throw new ArgumentException($"{player.DisplayName}: invalid attribute value");
            int displayed = player.Attribute(attribute);
            int target = mode == MassEditMode.Set ? value : Math.Clamp(displayed + value, 1, 20);
            byte raw = target == displayed ? original : AttributeScaling.ToRaw(target, player.AttributeScale);
            if (raw != original) changes.Add(new(player, attribute, original, raw));
        }
        return changes;
    }

    public static PlayerRecord Apply(PlayerMassChange change, PlayerRepository repository, PlayerWriter writer, Action? onWritten = null)
    {
        var player = Current(change, repository, change.BeforeRaw);
        writer.SetAttributeRaw(player, change.Attribute, change.AfterRaw);
        onWritten?.Invoke();
        return repository.Reload(player.Address) ?? throw new StaleObjectException("Player disappeared after writing");
    }

    public static PlayerRecord Undo(PlayerMassChange change, PlayerRepository repository, PlayerWriter writer, Action? onWritten = null)
    {
        var player = Current(change, repository, change.AfterRaw);
        writer.SetAttributeRaw(player, change.Attribute, change.BeforeRaw);
        onWritten?.Invoke();
        return repository.Reload(player.Address) ?? throw new StaleObjectException("Player disappeared after undo");
    }

    static PlayerRecord Current(PlayerMassChange change, PlayerRepository repository, byte expected)
    {
        var player = repository.Reload(change.Player.Address);
        if (player is null || player.Id != change.Player.Id || player.Raw(change.Attribute) != expected)
            throw new StaleObjectException("Player or attribute changed since the batch was prepared");
        return player;
    }
}

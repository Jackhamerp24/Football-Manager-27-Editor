namespace Fm27Editor.Core.Players;

public sealed record PlayerWageChange(PlayerRecord Player, ulong Contract, int Before, int After);

public static class PlayerWageMassEdit
{
    public static IReadOnlyList<PlayerWageChange> Plan(IEnumerable<PlayerRecord> players, MassEditMode mode, int value)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Invalid mass edit mode");
        var ids = new HashSet<int>();
        var changes = new List<PlayerWageChange>();
        foreach (var player in players)
        {
            if (player.Id is not > 0 || !ids.Add(player.Id.Value)) throw new ArgumentException("Players must have unique positive IDs");
            var contract = player.Contract ?? throw new InvalidOperationException($"{player.DisplayName} has no contract");
            int before = contract.Wage;
            int after = mode == MassEditMode.Set ? value : checked(before + value);
            if (before is < 0 or > 10_000_000 || after is < 0 or > 10_000_000)
                throw new ArgumentOutOfRangeException(nameof(value), after, "Wage must be between 0 and 10,000,000");
            if (after != before) changes.Add(new(player, contract.Address, before, after));
        }
        return changes;
    }

    public static void Apply(PlayerWageChange change, PlayerRepository repository, ContractWriter writer, Action? onWritten = null) =>
        Write(change, repository, writer, change.Before, change.After, onWritten);
    public static void Undo(PlayerWageChange change, PlayerRepository repository, ContractWriter writer, Action? onWritten = null) =>
        Write(change, repository, writer, change.After, change.Before, onWritten);

    static void Write(PlayerWageChange change, PlayerRepository repository, ContractWriter writer, int expected, int value, Action? onWritten)
    {
        var current = repository.Reload(change.Player.Address);
        if (current is null || current.Id != change.Player.Id || current.Contract?.Address != change.Contract || current.Contract.Wage != expected)
            throw new StaleObjectException("Player or contract changed since the batch was prepared");
        writer.SetWage(current, value);
        onWritten?.Invoke();
    }
}

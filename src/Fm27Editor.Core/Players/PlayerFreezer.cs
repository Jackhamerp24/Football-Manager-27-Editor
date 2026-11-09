namespace Fm27Editor.Core.Players;

public enum FreezeMode { Fixed, AllowIncrease, AllowDecrease }

/// <summary>A captured raw attribute. Raw values preserve the game's sub-display precision.</summary>
public sealed record FrozenAttribute(PlayerAttribute Attribute, byte Value, FreezeMode Mode);

/// <summary>One player's freeze state. An address belongs only to the currently attached save.</summary>
public sealed class FrozenPlayer
{
    readonly Dictionary<PlayerAttribute, FrozenAttribute> fields;
    public int Id { get; }
    public ulong Address { get; }
    public IReadOnlyCollection<FrozenAttribute> Fields => fields.Values;

    public FrozenPlayer(int id, ulong address, IEnumerable<FrozenAttribute> attributes)
    {
        if (id <= 0 || address == 0) throw new ArgumentOutOfRangeException(nameof(id));
        Id = id;
        Address = address;
        fields = attributes.ToDictionary(a => a.Attribute);
        if (fields.Count == 0 || fields.Values.Any(a => a.Value is < 1 or > 100 || !Enum.IsDefined(a.Attribute) || !Enum.IsDefined(a.Mode)))
            throw new ArgumentException("Invalid frozen attributes", nameof(attributes));
    }

    public static FrozenPlayer Capture(PlayerRecord player, IEnumerable<(PlayerAttribute Attribute, FreezeMode Mode)> attributes) =>
        new(player.Id ?? 0, player.Address, attributes.Select(a => new FrozenAttribute(a.Attribute, player.Raw(a.Attribute), a.Mode)));

    public static IEnumerable<(PlayerAttribute Attribute, FreezeMode Mode)> AllAttributes(PlayerRecord player)
    {
        foreach (var attr in Enum.GetValues<PlayerAttribute>())
        {
            if (player.Raw(attr) is >= 1 and <= 100)
                yield return (attr, FreezeMode.Fixed);
        }
    }

    /// <summary>Adopts a deliberate editor change to an already frozen attribute.</summary>
    public bool AcceptEdit(PlayerRecord player, PlayerAttribute attribute)
    {
        if (player.Id != Id || player.Address != Address) throw new StaleObjectException("Frozen player changed");
        if (!fields.TryGetValue(attribute, out var field)) return false;
        byte value = player.Raw(attribute);
        if (value is < 1 or > 100) throw new StaleObjectException("Player attribute is invalid");
        fields[attribute] = field with { Value = value };
        return true;
    }

    /// <summary>Reloads the same player and applies corrections through the normal guarded writer.</summary>
    public int Reconcile(PlayerRepository players, PlayerWriter writer)
    {
        var current = players.Reload(Address);
        if (current is null || current.Id != Id) throw new StaleObjectException("Frozen player changed or is no longer available");
        int writes = 0;
        foreach (var field in fields.Values.ToArray())
        {
            byte value = current.Raw(field.Attribute);
            if (field.Mode == FreezeMode.AllowIncrease && value > field.Value ||
                field.Mode == FreezeMode.AllowDecrease && value < field.Value)
            {
                fields[field.Attribute] = field with { Value = value };
                continue;
            }
            if (value == field.Value) continue;
            writer.SetAttributeRaw(current, field.Attribute, field.Value);
            writes++;
        }
        return writes;
    }
}

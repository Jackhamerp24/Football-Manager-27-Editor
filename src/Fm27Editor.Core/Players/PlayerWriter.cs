using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Writes single player fields. Every write first re-checks that the object still is a player
/// and that the field still holds the value from the caller's snapshot, so a save reload or a
/// game-side change can never be overwritten blindly.
/// </summary>
public sealed class PlayerWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public void SetAttribute(PlayerRecord player, PlayerAttribute attribute, int display)
    {
        RequireRange(display, 1, 20, attribute.ToString());
        WriteGuarded(player, profile.Attributes[attribute], [player.Raw(attribute)],
            [AttributeScaling.ToRaw(display, profile.AttributeScale)]);
    }

    public void SetPosition(PlayerRecord player, PlayerPosition position, int value)
    {
        RequireRange(value, 1, 20, position.ToString());
        WriteGuarded(player, profile.Positions[position], [player.Positions[(int)position]], [(byte)value]);
    }

    public void SetPersonality(PlayerRecord player, PersonalityAttribute attribute, int value)
    {
        RequireRange(value, 1, 20, attribute.ToString());
        WriteGuarded(player, profile.Personality[attribute], [player.Personality[(int)attribute]], [(byte)value]);
    }

    public void SetCurrentAbility(PlayerRecord player, int value)
    {
        RequireRange(value, 1, 200, "Current ability");
        WriteGuarded(player, profile.Fields.CurrentAbility, Int16(player.CurrentAbility), Int16((short)value));
    }

    public void SetPotentialAbility(PlayerRecord player, int value)
    {
        RequireRange(value, 1, 200, "Potential ability");
        WriteGuarded(player, profile.Fields.PotentialAbility, Int16(player.PotentialAbility), Int16((short)value));
    }

    static byte[] Int16(short v) => BitConverter.GetBytes(v);

    static void RequireRange(int value, int min, int max, string field)
    {
        if (value < min || value > max) throw new ArgumentOutOfRangeException(field, value, $"{field} must be {min}-{max}");
    }

    void WriteGuarded(PlayerRecord player, int offset, byte[] expected, byte[] value)
    {
        ulong vt = memory.ReadUInt64(player.Address);
        if (!profile.PlayerVtableRvas.Any(v => moduleBase + v == vt))
            throw new StaleObjectException($"0x{player.Address:X} is no longer a player object");
        ulong address = player.Address + (ulong)offset;
        var current = memory.Read(address, expected.Length);
        if (!current.AsSpan().SequenceEqual(expected))
            throw new StaleObjectException($"Value at 0x{address:X} changed since it was read");
        if (!memory.TryWrite(address, value))
            throw new MemoryAccessException($"Write at 0x{address:X} failed");
        if (!memory.Read(address, value.Length).AsSpan().SequenceEqual(value))
            throw new MemoryAccessException($"Write at 0x{address:X} did not stick");
    }
}

public sealed class StaleObjectException(string message) : Exception(message);

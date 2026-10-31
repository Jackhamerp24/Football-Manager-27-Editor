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

    /// <summary>Home, current or world reputation, int16, 0-10000 as FMRTE edits it.</summary>
    public void SetReputation(PlayerRecord player, Reputation kind, int value)
    {
        RequireRange(value, 0, 10000, $"{kind} reputation");
        var (offset, current) = kind switch
        {
            Reputation.Home => (profile.Fields.HomeReputation, player.HomeReputation),
            Reputation.Current => (profile.Fields.CurrentReputation, player.CurrentReputation),
            _ => (profile.Fields.WorldReputation, player.WorldReputation),
        };
        WriteGuarded(player, offset, Int16(current), Int16((short)value));
    }

    /// <summary>Height in centimetres, int16.</summary>
    public void SetHeight(PlayerRecord player, int centimetres)
    {
        RequireRange(centimetres, 100, 250, "Height");
        WriteGuarded(player, profile.Fields.Height, Int16(player.Height), Int16((short)centimetres));
    }

    /// <summary>Stored as int16 day of year (1-based) + int16 year.</summary>
    public void SetDateOfBirth(PlayerRecord player, DateOnly date)
    {
        RequireRange(date.Year, 1900, 2100, "Birth year");
        if (player.DateOfBirth is not { } current)
            throw new StaleObjectException($"0x{player.Address:X} has no readable birth date to compare against");
        WriteGuarded(player, profile.Fields.DateOfBirth, Date(current), Date(date));
    }

    static byte[] Date(DateOnly d) => [.. Int16((short)d.DayOfYear), .. Int16((short)d.Year)];

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

public enum Reputation { Home, Current, World }

public sealed class StaleObjectException(string message) : Exception(message);

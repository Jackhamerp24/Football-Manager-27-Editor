using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Per-player tactical familiarity, measured against FM Studio 27 v1.1.1's "Tactical Effectiveness" action on Arsenal
/// (6/11): the button raised exactly these player fields — tactical familiarity at +0x190 (0..100), eight role/position
/// ratings at +0x155..+0x15C (0..20), condition at +0x25C (0..10000), morale at +0x26C (0..20) and one more 0..20 value
/// at +0x2F8. Saliba 35→100 / Havertz 81→100 with the same fields, so +0x190 is the value FMRTE-style editors show.
/// </summary>
public sealed record PlayerFamiliarity(ulong Address, string Snapshot, byte Tactical, byte[] Roles, ushort Condition, byte Morale, byte Extra)
{
    public const int TacticalOffset = 0x190, RolesOffset = 0x155, RoleCount = 8, ConditionOffset = 0x25C, MoraleOffset = 0x26C, ExtraOffset = 0x2F8;
    public const int MaxTactical = 100, MaxRole = 20, MaxCondition = 10000, MaxMorale = 20;
}

public sealed class PlayerFamiliarityAccess(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public PlayerFamiliarity? Read(ulong player)
    {
        if (!IsPlayer(player)) return null;
        try
        {
            byte[] b = memory.Read(player, PlayerFamiliarity.ExtraOffset + 1);
            byte[] roles = new byte[PlayerFamiliarity.RoleCount];
            Array.Copy(b, PlayerFamiliarity.RolesOffset, roles, 0, roles.Length);
            return new(player, Convert.ToHexString(b), b[PlayerFamiliarity.TacticalOffset], roles,
                BitConverter.ToUInt16(b, PlayerFamiliarity.ConditionOffset), b[PlayerFamiliarity.MoraleOffset], b[PlayerFamiliarity.ExtraOffset]);
        }
        catch (MemoryAccessException) { return null; }
    }

    /// <summary>Sets the 0..100 tactical familiarity and leaves every other player byte untouched.</summary>
    public void SetTactical(PlayerFamiliarity snapshot, int value)
    {
        if (value is < 0 or > PlayerFamiliarity.MaxTactical) throw new ArgumentOutOfRangeException(nameof(value));
        RequireCurrent(snapshot);
        Write(snapshot.Address + PlayerFamiliarity.TacticalOffset, [(byte)value]);
    }

    /// <summary>Sets one role/position rating (0..20) at +0x155 + index.</summary>
    public void SetRole(PlayerFamiliarity snapshot, int index, int value)
    {
        if (index is < 0 or >= PlayerFamiliarity.RoleCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (value is < 0 or > PlayerFamiliarity.MaxRole) throw new ArgumentOutOfRangeException(nameof(value));
        RequireCurrent(snapshot);
        Write(snapshot.Address + PlayerFamiliarity.RolesOffset + (ulong)index, [(byte)value]);
    }

    void RequireCurrent(PlayerFamiliarity snapshot)
    {
        if (!IsPlayer(snapshot.Address)) throw new StaleObjectException("The player changed since it was read");
        var current = memory.Read(snapshot.Address, PlayerFamiliarity.ExtraOffset + 1);
        if (Convert.ToHexString(current) != snapshot.Snapshot) throw new StaleObjectException("Tactical familiarity changed since it was read");
    }

    void Write(ulong address, byte[] bytes)
    {
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write tactical familiarity at 0x{address:X}");
    }

    bool IsPlayer(ulong address)
    {
        if (address == 0) return false;
        var head = new byte[8];
        if (!memory.TryRead(address, head)) return false;
        ulong vtable = BitConverter.ToUInt64(head);
        return profile.PlayerVtableRvas.Any(rva => vtable == moduleBase + rva);
    }
}

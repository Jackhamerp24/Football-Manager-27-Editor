using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>Maintains a club's balance at its captured amount or higher while connected.</summary>
public sealed class ClubBalanceFreezer
{
    public int Id { get; }
    public ulong Address { get; }
    public long Value { get; private set; }
    public FreezeMode Mode { get; }

    public ClubBalanceFreezer(int id, ulong address, long value, FreezeMode mode)
    {
        if (id <= 0 || address == 0 || mode is not (FreezeMode.Fixed or FreezeMode.AllowIncrease)) throw new ArgumentException("Invalid frozen club");
        Id = id;
        Address = address;
        Value = value;
        Mode = mode;
    }

    public static ClubBalanceFreezer Capture(ClubEntry club, ClubFieldAccess access, FreezeMode mode) =>
        new(club.Id ?? 0, club.Address, access.Read(club.Address, ClubField.Balance)
            ?? throw new StaleObjectException("Club balance is not readable"), mode);

    public bool Reconcile(IProcessMemory memory, OffsetProfile profile, ClubFieldAccess access)
    {
        int idOffset = profile.Club?.Id ?? throw new InvalidOperationException("Profile has no club ID offset");
        if (BitConverter.ToInt32(memory.Read(Address + (ulong)idOffset, 4)) != Id)
            throw new StaleObjectException("Frozen club changed or is no longer available");
        long current = access.Read(Address, ClubField.Balance) ?? throw new StaleObjectException("Club balance is no longer readable");
        if (Mode == FreezeMode.AllowIncrease && current > Value) { Value = current; return false; }
        if (current == Value) return false;
        access.Set(Address, ClubField.Balance, current, Value);
        return true;
    }
}

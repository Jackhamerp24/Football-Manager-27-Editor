using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>Edits observed affiliation fields only after validating both clubs, vector membership and record snapshot.</summary>
public sealed class ClubAffiliationWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public void SetAnnualFee(ulong club, ClubAffiliationEntry entry, int fee)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationAnnualFee is not { } offset || offset < 0 || offset > layout.AffiliationSize - 4 || entry.AnnualFee is null)
            throw new InvalidOperationException("Annual fee is unavailable for this build or record");
        if (fee < 0) throw new ArgumentOutOfRangeException(nameof(fee), "Negative affiliation fees are unverified");
        RequireCurrent(club, entry);
        Write(entry.Address + (ulong)offset, BitConverter.GetBytes(fee));
    }

    public void SetEndDate(ulong club, ClubAffiliationEntry entry, DateOnly date)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationEnd is not { } offset || offset < 0 || offset > layout.AffiliationSize - 4 || entry.EndDate is null)
            throw new InvalidOperationException("End date is unavailable for this build or record");
        SetDate(club, entry, date, offset);
    }

    public void SetStartDate(ulong club, ClubAffiliationEntry entry, DateOnly date)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        int offset = layout.AffiliationStart;
        if (!layout.AffiliationStartEditable || offset < 0 || offset > layout.AffiliationSize - 4 || entry.StartDate is null)
            throw new InvalidOperationException("Start date is unavailable for this build or record");
        SetDate(club, entry, date, offset);
    }

    public void SetFriendlyChance(ulong club, ClubAffiliationEntry entry, int value)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationFriendlyChance is not { } offset || offset < 0 || offset >= layout.AffiliationSize || entry.FriendlyChance is null)
            throw new InvalidOperationException("Friendly chance is unavailable for this build or record");
        if (value is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value));
        RequireCurrent(club, entry);
        Write(entry.Address + (ulong)offset, [(byte)value]);
    }

    public void SetParentWagePercent(ulong club, ClubAffiliationEntry entry, int value)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationParentWagePercent is not { } offset || offset < 0 || offset >= layout.AffiliationSize || entry.ParentWagePercent is null)
            throw new InvalidOperationException("Parent wage percentage is unavailable for this build or record");
        if (value is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(value));
        RequireCurrent(club, entry);
        Write(entry.Address + (ulong)offset, [(byte)value]);
    }

    public void SetPlayFriendlies(ulong club, ClubAffiliationEntry entry, bool value)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationPlayFriendlies is not { } offset || offset < 0 || offset >= layout.AffiliationSize || entry.PlayFriendlies is null)
            throw new InvalidOperationException("Play Friendlies is unavailable for this build or record");
        RequireCurrent(club, entry);
        byte original = Convert.FromHexString(entry.Snapshot!.Bytes)[offset];
        Write(entry.Address + (ulong)offset, [(byte)(value ? original | 0x10 : original & ~0x10)]);
    }

    public void SetFirstOption(ulong club, ClubAffiliationEntry entry, bool value)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationFirstOption is not { } offset || offset < 0 || offset >= layout.AffiliationSize || entry.FirstOption is null)
            throw new InvalidOperationException("First Option To Buy is unavailable for this build or record");
        RequireCurrent(club, entry);
        byte original = Convert.FromHexString(entry.Snapshot!.Bytes)[offset];
        Write(entry.Address + (ulong)offset, [(byte)(value ? original | 0x08 : original & ~0x08)]);
    }

    public void SetPlayersOnLoan(ulong club, ClubAffiliationEntry entry, bool value)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationPlayersOnLoan is not { } offset || offset < 0 || offset >= layout.AffiliationSize || entry.PlayersOnLoan is null)
            throw new InvalidOperationException("Players Go On Loan is unavailable for this build or record");
        RequireCurrent(club, entry);
        byte original = Convert.FromHexString(entry.Snapshot!.Bytes)[offset];
        Write(entry.Address + (ulong)offset, [(byte)(value ? original | 0x04 : original & ~0x04)]);
    }

    public void SetScoutingShared(ulong club, ClubAffiliationEntry entry, bool value)
    {
        var layout = profile.Club?.Lists ?? throw new InvalidOperationException("Profile has no affiliation offsets");
        if (layout.AffiliationScoutingShared is not { } offset || offset < 0 || offset >= layout.AffiliationSize || entry.ScoutingShared is null)
            throw new InvalidOperationException("Scouting Knowledge Shared is unavailable for this build or record");
        RequireCurrent(club, entry);
        byte original = Convert.FromHexString(entry.Snapshot!.Bytes)[offset];
        Write(entry.Address + (ulong)offset, [(byte)(value ? original | 0x40 : original & ~0x40)]);
    }

    void SetDate(ulong club, ClubAffiliationEntry entry, DateOnly date, int offset)
    {
        if (date.Year is <= 1900 or > 2300) throw new ArgumentOutOfRangeException(nameof(date));
        RequireCurrent(club, entry);
        byte[] original = Convert.FromHexString(entry.Snapshot!.Bytes);
        ushort day = (ushort)((BitConverter.ToUInt16(original, offset) & 0xFE00) | date.DayOfYear);
        byte[] bytes = new byte[4];
        BitConverter.GetBytes(day).CopyTo(bytes, 0);
        BitConverter.GetBytes((short)date.Year).CopyTo(bytes, 2);
        Write(entry.Address + (ulong)offset, bytes);
    }

    void RequireCurrent(ulong club, ClubAffiliationEntry entry)
    {
        if (entry.Snapshot is not { } snapshot || snapshot.Club != club
            || (entry.ParentClub?.Address != club && entry.FeederClub?.Address != club))
            throw new StaleObjectException("Affiliation snapshot is unavailable");
        var current = new ClubListReader(memory, profile, moduleBase).ReadAffiliations(club).FirstOrDefault(r => r.Address == entry.Address);
        if (current != entry) throw new StaleObjectException("The affiliation or its containing list changed since it was read");
    }

    void Write(ulong address, byte[] bytes)
    {
        if (!memory.TryWrite(address, bytes) || !memory.Read(address, bytes.Length).AsSpan().SequenceEqual(bytes))
            throw new MemoryAccessException($"Could not write affiliation field at 0x{address:X}");
    }
}

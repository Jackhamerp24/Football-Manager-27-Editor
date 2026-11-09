using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

/// <summary>How the game labels a deal status word. The low byte is the code, a 0xFF second byte means "still open".</summary>
public static class TransferDealStatus
{
    public const int OfferMade = 0x0000FF00;
    public const int OfferAccepted = 0xFF02;
    public const int PlayerAccepted = 0xFF03;
    public const int Rejected = 0xFF04;
    public const int Cancelled = 0xFF05;
    public const int Confirmed = 0x0701;
    public const int Delayed = 0x01000002;

    /// <summary>Game text for one status word. Never returns the raw number, so a UI cannot leak a code.</summary>
    public static string Label(int statusRaw)
    {
        if (statusRaw == Delayed) return "Delayed";
        if ((statusRaw & 0xFFFF) == Confirmed) return "Confirmed";
        return (statusRaw & 0xFF) switch
        {
            0 or 1 => "Offer made",
            2 => "Offer accepted",
            3 => "Player accepted",
            4 => "Rejected",
            5 => "Cancelled",
            _ => "In progress",
        };
    }
}

/// <summary>
/// One transfer deal / negotiation object. Layout confirmed on FM27 <c>27.0.0+2493426</c> against a live bid
/// (Bento, Olympique de Marseille to Manchester United, £1,000,000, FMRTE status "Offer Made"):
/// vtable +0x4EE4098, person +0x38, buying team +0x40, +0x48, selling team +0x50, fee +0x58, day counter +0x5C,
/// accept flag +0x84, status +0x88. Fee, date and status offsets match FM Studio 1.1.1's save methods too.
/// </summary>
public sealed record TransferDeal(
    ulong Address,
    ulong PersonAddress,
    ulong BuyingTeam,
    ulong SellingTeam,
    int Fee,
    int StatusRaw,
    short DayCounter,
    byte[] SnapshotBytes)
{
    public string Status => TransferDealStatus.Label(StatusRaw);

    public bool IsAccepted => StatusRaw is TransferDealStatus.OfferAccepted or TransferDealStatus.PlayerAccepted
        or TransferDealStatus.Confirmed;

    /// <summary>Still waiting for an answer: not accepted, rejected, cancelled, confirmed or delayed.</summary>
    public bool IsOpen => !IsAccepted && StatusRaw is not (TransferDealStatus.Rejected or TransferDealStatus.Cancelled
        or TransferDealStatus.Delayed);
}

public sealed class TransferDealReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    /// <summary>Deal class vtable RVAs: the profile's transfer vtable (Game Pass and Steam each name their own), plus two from FM Studio.</summary>
    IEnumerable<uint> VtableRvas => profile.Proposals is { } p ? [p.TransferVtable, 74_494_248, 83_071_720] : [74_494_248, 83_071_720];

    const int PersonOffset = 0x38, BuyingTeamOffset = 0x40, SellingTeamOffset = 0x50;
    const int FeeOffset = 0x58, DayCounterOffset = 0x5C, AcceptFlagOffset = 0x84, StatusOffset = 0x88;
    const int ObjectSize = 0x90, MaxName = 200;

    public bool IsDeal(ulong address)
    {
        if (address < 0x10000) return false;
        Span<byte> head = stackalloc byte[8];
        if (!memory.TryRead(address, head)) return false;
        ulong vtable = BitConverter.ToUInt64(head);
        return VtableRvas.Any(rva => vtable == moduleBase + rva);
    }

    public TransferDeal? Read(ulong address)
    {
        if (!IsDeal(address)) return null;
        var raw = memory.Read(address, ObjectSize);
        if (raw.Length < ObjectSize) return null;
        return new(address,
            BitConverter.ToUInt64(raw, PersonOffset),
            BitConverter.ToUInt64(raw, BuyingTeamOffset),
            BitConverter.ToUInt64(raw, SellingTeamOffset),
            BitConverter.ToInt32(raw, FeeOffset),
            BitConverter.ToInt32(raw, StatusOffset),
            BitConverter.ToInt16(raw, DayCounterOffset),
            raw);
    }

    /// <summary>The deal a player is currently negotiating, or null when the player has none.</summary>
    public TransferDeal? FindForPlayer(ulong personAddress, IReadOnlyList<ulong>? candidates = null)
    {
        foreach (var address in candidates ?? Scan())
        {
            var deal = Read(address);
            if (deal is not null && deal.PersonAddress == personAddress) return deal;
        }
        return null;
    }

    /// <summary>Every deal whose person pointer is one of <paramref name="personAddresses"/>.</summary>
    public IReadOnlyList<TransferDeal> FindAll(IReadOnlySet<ulong> personAddresses)
    {
        var list = new List<TransferDeal>();
        foreach (var address in Scan())
        {
            var deal = Read(address);
            if (deal is not null && personAddresses.Contains(deal.PersonAddress)) list.Add(deal);
        }
        return list;
    }

    /// <summary>Every writable-memory object carrying a deal vtable (FM Studio scans the heap the same way).</summary>
    public IReadOnlyList<ulong> Scan()
    {
        if (memory is not WindowsProcessMemory windows) return [];
        var found = new List<ulong>();
        var buffer = new byte[1 << 22];
        foreach (var region in windows.EnumerateWritableRegions())
        {
            if (region.IsModuleData) continue;
            for (ulong offset = 0; offset + 8 <= region.Size; offset += (ulong)buffer.Length)
            {
                int length = (int)Math.Min((ulong)buffer.Length, region.Size - offset);
                if (!memory.TryRead(region.Start + offset, buffer.AsSpan(0, length))) break;
                for (int i = 0; i + 8 <= length; i += 8)
                {
                    ulong vtable = BitConverter.ToUInt64(buffer, i);
                    if (VtableRvas.Any(rva => vtable == moduleBase + rva))
                        found.Add(region.Start + offset + (ulong)i);
                }
            }
        }
        return found;
    }

    /// <summary>Club a team pointer belongs to, or null when the pointer is not a team of this build.</summary>
    public ulong ClubOf(ulong team)
    {
        if (team == 0 || profile.Team is not { } t || profile.Contract is not { } c) return 0;
        if (!TryReadUInt64(team, out ulong vtable) || vtable != moduleBase + t.VtableRva) return 0;
        return TryReadUInt64(team + (ulong)c.TeamClub, out ulong club) ? club : 0;
    }

    public string? ClubName(ulong club)
    {
        if (club == 0 || profile.Club is not { } k || !TryReadUInt64(club, out ulong vtable)
            || vtable != moduleBase + k.ClubVtableRva) return null;
        return Text(club + (ulong)k.Name) ?? Text(club + (ulong)k.ShortName);
    }

    public string? TeamName(ulong team) => ClubName(ClubOf(team));

    /// <summary>Game text of a day counter, or null when the counter is empty.</summary>
    public static DateOnly? DayCounterToDate(short counter, int seasonYear)
    {
        if (counter <= 0) return null;
        int day = counter & 0x1FF;
        int year = seasonYear + ((counter >> 9) & 0x3F);
        if (day is < 1 or > 366 || year is < 1900 or > 2200) return null;
        try { return DateOnly.FromDayNumber(new DateOnly(year, 1, 1).DayNumber + day - 1); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    bool TryReadUInt64(ulong address, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        value = 0;
        if (!memory.TryRead(address, b)) return false;
        value = BitConverter.ToUInt64(b);
        return true;
    }

    string? Text(ulong field)
    {
        if (!TryReadUInt64(field, out ulong p) || p == 0) return null;
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(p, len)) return null;
        int count = BitConverter.ToInt32(len);
        if (count is < 0 or > MaxName) return null;
        var b = new byte[count];
        return memory.TryRead(p + 4, b) ? Encoding.UTF8.GetString(b) : null;
    }
}

/// <summary>Guarded writes to a deal object: vtable, expected value, write, read back.</summary>
public sealed class TransferDealWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    readonly TransferDealReader reader = new(memory, profile, moduleBase);

    const int FeeOffset = 0x58, StatusOffset = 0x88, AcceptFlagOffset = 0x84;

    public void SetFee(TransferDeal deal, int expected, int value)
    {
        Guard(deal, FeeOffset, expected);
        if (!memory.TryWrite(deal.Address + FeeOffset, BitConverter.GetBytes(value)))
            throw new MemoryAccessException($"deal 0x{deal.Address:X} fee write failed");
        Verify(deal, FeeOffset, value);
    }

    public void SetStatus(TransferDeal deal, int expected, int value)
    {
        Guard(deal, StatusOffset, expected);
        if (!memory.TryWrite(deal.Address + StatusOffset, BitConverter.GetBytes(value)))
            throw new MemoryAccessException($"deal 0x{deal.Address:X} status write failed");
        Verify(deal, StatusOffset, value);
        if (value is TransferDealStatus.OfferAccepted or TransferDealStatus.PlayerAccepted)
        {
            var flag = memory.Read(deal.Address + AcceptFlagOffset, 1);
            if (flag[0] != 2 && !memory.TryWrite(deal.Address + AcceptFlagOffset, [2]))
                throw new MemoryAccessException($"deal 0x{deal.Address:X} accept flag write failed");
        }
    }

    /// <summary>Accept the offer, doing exactly what the game's own accept path does (status word + accept flag).</summary>
    public void AcceptOffer(TransferDeal deal) => SetStatus(deal, deal.StatusRaw, TransferDealStatus.OfferAccepted);

    void Guard(TransferDeal deal, int offset, int expected)
    {
        if (!reader.IsDeal(deal.Address)) throw new StaleObjectException($"deal 0x{deal.Address:X} is gone");
        var current = memory.Read(deal.Address + (ulong)offset, 4);
        if (BitConverter.ToInt32(current, 0) != expected)
            throw new StaleObjectException($"deal 0x{deal.Address:X} field +0x{offset:X} changed ({expected} -> {BitConverter.ToInt32(current, 0)})");
    }

    void Verify(TransferDeal deal, int offset, int value)
    {
        var back = memory.Read(deal.Address + (ulong)offset, 4);
        if (BitConverter.ToInt32(back, 0) != value)
            throw new MemoryAccessException($"deal 0x{deal.Address:X} field +0x{offset:X} did not take {value}");
    }
}

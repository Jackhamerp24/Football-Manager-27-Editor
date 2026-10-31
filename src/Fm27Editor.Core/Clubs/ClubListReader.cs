using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Clubs;

public sealed record ClubListClub(ulong Address, int? Id, string? Name, string? ShortName)
{
    public string? DisplayName => Name ?? ShortName;
}

/// <summary>Confirmed culture fields only. Source, Value, ForCompetition and unverified objective records are omitted.</summary>
public sealed record ClubVisionEntry(ulong Address, byte TypeCode, byte Importance, DateOnly? StartDate,
    DateOnly? EndDate, short? ItemIndex, ClubListClub? ClubItem);

/// <summary>Fee, end date and benefit bits have no second independent oracle and are deliberately absent.</summary>
public sealed record ClubAffiliationEntry(ulong Address, ClubListClub? ParentClub, ClubListClub? FeederClub,
    DateOnly? StartDate, byte TypeCode);

/// <summary>The two competition IDs are indistinguishable and every observed year is zero; only Level is confirmed.</summary>
public sealed record ClubRegionalDivisionEntry(ulong Address, byte Level);

/// <summary>Finances → Sponsors and Other. Annual value and the Renew Income / Fixed Value flags are not confirmed and are absent.</summary>
public sealed record ClubSponsorEntry(ulong Address, byte IncomeTypeCode, int TotalValue, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>Finances → Debt and Loans. Monthly repayment/interest have one oracle only and are absent.</summary>
public sealed record ClubDebtEntry(ulong Address, byte SourceCode, int OriginalDebt, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>Read-only, bounded snapshots of the layouts confirmed in re/club/lists.md. No allocator or write access.</summary>
public sealed class ClubListReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxEntries = 512;
    ClubListOffsets? Layout => profile.Club?.Lists;

    public IReadOnlyList<ClubVisionEntry> ReadClubVision(ulong teamAddress, IReadOnlyList<ClubEntry>? clubs = null)
    {
        if (Layout is not { } l || profile.Team is not { } t || !Vtable(teamAddress, t.VtableRva)
            || !Fits(l.CultureSize, (0, 8), (l.VisionType, 1), (l.Importance, 1), (l.VisionStart, 4),
                (l.VisionEnd, 4), (l.ItemIndex, 2), (l.ItemMarker, 2))) return [];
        ulong detail = PointerAt(teamAddress, l.TeamDetails), holder = PointerAt(detail, l.Vision);
        if (!Vtable(holder, l.VisionHolderVtable) || Vector(PointerAt(holder, l.VisionVector)) is not { } pointers) return [];
        var result = new List<ClubVisionEntry>();
        foreach (ulong address in pointers)
        {
            if (!Vtable(address, l.CultureVtable) || Bytes(address, l.CultureSize) is not { } b) continue;
            // 00/03 was observed on all four independently named club-item references; other marker kinds stay unresolved.
            short? index = BitConverter.ToUInt16(b, l.ItemMarker) == 0x0300
                && BitConverter.ToInt16(b, l.ItemIndex) is >= 0 and var i ? i : null;
            result.Add(new(address, b[l.VisionType], b[l.Importance], Date(b, l.VisionStart), Date(b, l.VisionEnd),
                index, index is { } idx ? FindClub(idx, clubs) : null));
        }
        return result.AsReadOnly();
    }

    public IReadOnlyList<ClubAffiliationEntry> ReadAffiliations(ulong clubAddress)
    {
        if (Layout is not { } l || !ClubRoot(clubAddress)
            || !Fits(l.AffiliationSize, (l.Parent, 8), (l.Feeder, 8), (l.AffiliationStart, 4), (l.AffiliationType, 1))
            || Vector(Field(clubAddress, l.Affiliations)) is not { } pointers) return [];
        var result = new List<ClubAffiliationEntry>();
        foreach (ulong address in pointers)
        {
            if (Bytes(address, l.AffiliationSize) is not { } b) continue;
            var parent = Club(BitConverter.ToUInt64(b, l.Parent));
            var feeder = Club(BitConverter.ToUInt64(b, l.Feeder));
            if (parent is null || feeder is null) continue;
            result.Add(new(address, parent, feeder, Date(b, l.AffiliationStart), b[l.AffiliationType]));
        }
        return result.AsReadOnly();
    }

    public IReadOnlyList<ClubRegionalDivisionEntry> ReadRegionalDivisions(ulong clubAddress)
    {
        if (Layout is not { } l || !ClubRoot(clubAddress) || !Fits(l.RegionalSize, (l.Level, 1))
            || Vector(PointerAt(clubAddress, l.RegionalPointer)) is not { } pointers) return [];
        var result = new List<ClubRegionalDivisionEntry>();
        foreach (ulong address in pointers)
            if (Bytes(address, l.RegionalSize) is { } b) result.Add(new(address, b[l.Level]));
        return result.AsReadOnly();
    }

    public IReadOnlyList<ClubSponsorEntry> ReadSponsors(ulong clubAddress)
    {
        if (profile.Club?.FinanceLists is not { } f || !ClubRoot(clubAddress)
            || !Fits(f.SponsorSize, (f.SponsorStart, 4), (f.SponsorEnd, 4), (f.SponsorTotal, 4), (f.SponsorType, 1))
            || Vector(PointerAt(clubAddress, f.SponsorsPointer)) is not { } pointers) return [];
        var result = new List<ClubSponsorEntry>();
        foreach (ulong address in pointers)
            if (Bytes(address, f.SponsorSize) is { } b)
                result.Add(new(address, b[f.SponsorType], BitConverter.ToInt32(b, f.SponsorTotal), Date(b, f.SponsorStart), Date(b, f.SponsorEnd)));
        return result.AsReadOnly();
    }

    public IReadOnlyList<ClubDebtEntry> ReadDebts(ulong clubAddress)
    {
        if (profile.Club?.FinanceLists is not { } f || !ClubRoot(clubAddress)
            || !Fits(f.DebtSize, (f.DebtOriginal, 4), (f.DebtEnd, 4), (f.DebtStart, 4), (f.DebtSource, 1))
            || Vector(Field(PointerAt(clubAddress, f.DebtsPointer), f.DebtsVector)) is not { } pointers) return [];
        var result = new List<ClubDebtEntry>();
        foreach (ulong address in pointers)
            if (Bytes(address, f.DebtSize) is { } b)
                result.Add(new(address, b[f.DebtSource], BitConverter.ToInt32(b, f.DebtOriginal), Date(b, f.DebtStart), Date(b, f.DebtEnd)));
        return result.AsReadOnly();
    }

    bool ClubRoot(ulong address) => profile.Club is { } k && Vtable(address, k.ClubVtableRva);

    ClubListClub? FindClub(short index, IReadOnlyList<ClubEntry>? clubs)
    {
        foreach (var club in clubs ?? [])
            if (ClubRoot(club.Address) && Bytes(club.Address + 8, 4) is { } b && BitConverter.ToInt32(b) == index)
                return Club(club.Address);
        return null;
    }

    ClubListClub? Club(ulong address)
    {
        if (!ClubRoot(address) || profile.Club is not { } k) return null;
        int? id = k.Id is { } off && Bytes(Field(address, off), 4) is { } b ? BitConverter.ToInt32(b) : null;
        return new(address, id, Name(PointerAt(address, k.Name)), Name(PointerAt(address, k.ShortName)));
    }

    string? Name(ulong address)
    {
        if (Bytes(address, 4) is not { } b) return null;
        int size = BitConverter.ToInt32(b);
        return size is > 0 and <= 200 && Bytes(address + 4, size) is { } name ? Encoding.UTF8.GetString(name) : null;
    }

    // Read precisely [begin,end). Capacity is validated, never enumerated. No partially aligned or enormous vectors.
    ulong[]? Vector(ulong header)
    {
        if (Bytes(header, 24) is not { } h) return null;
        ulong begin = BitConverter.ToUInt64(h), end = BitConverter.ToUInt64(h, 8), cap = BitConverter.ToUInt64(h, 16);
        if (begin == 0) return end == 0 && cap == 0 ? [] : null;
        if (!Address(begin) || end < begin || cap < end || (end - begin) % 8 != 0 || (cap - begin) % 8 != 0
            || (cap - begin) / 8 > MaxEntries) return null;
        if (end == begin) return [];
        if (Bytes(begin, (int)(end - begin)) is not { } bytes) return null;
        var pointers = new ulong[bytes.Length / 8];
        for (int i = 0; i < pointers.Length; i++) pointers[i] = BitConverter.ToUInt64(bytes, i * 8);
        return pointers;
    }

    static bool Fits(int size, params (int Offset, int Width)[] fields) => size is > 0 and <= 4096
        && fields.All(f => f.Offset >= 0 && f.Offset <= size - f.Width);
    static bool Address(ulong address) => address is >= 0x10000 and <= 0x7FFF_FFFF_FFFF;
    static ulong Field(ulong address, int offset) => Address(address) && offset is >= 0 and <= 0x10000 ? address + (ulong)offset : 0;
    byte[]? Bytes(ulong address, int length)
    {
        if (!Address(address)) return null;
        var bytes = new byte[length];
        return memory.TryRead(address, bytes) ? bytes : null;
    }
    ulong PointerAt(ulong address, int offset) => Q(Field(address, offset));
    ulong Q(ulong address) => Bytes(address, 8) is { } b && BitConverter.ToUInt64(b) is var p && Address(p) ? p : 0;
    bool Vtable(ulong address, uint rva) => Q(address) == moduleBase + rva;
    static DateOnly? Date(byte[] b, int offset)
    {
        int day = BitConverter.ToUInt16(b, offset) & 0x1FF, year = BitConverter.ToInt16(b, offset + 2);
        if (year is <= 1900 or > 2300 || day < 1 || day > (DateTime.IsLeapYear(year) ? 366 : 365)) return null;
        return new DateOnly(year, 1, 1).AddDays(day - 1);
    }
}

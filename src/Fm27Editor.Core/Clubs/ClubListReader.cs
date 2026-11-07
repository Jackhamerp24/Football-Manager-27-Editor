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
    DateOnly? EndDate, short? ItemIndex, ClubListClub? ClubItem, ClubVisionSnapshot? Snapshot = null,
    int? Target = null, byte? Status = null);

public sealed record ClubVisionSnapshot(ulong Team, ulong Detail, ulong Holder, ulong Vector, ulong Begin, ulong End,
    ulong Capacity, ulong Slot, string Bytes);

public sealed record ClubAffiliationSnapshot(ulong Club, ulong Begin, ulong End, ulong Capacity, ulong Slot, string Bytes);

/// <summary>Benefit bits and negative fee encodings remain unresolved.</summary>
public sealed record ClubAffiliationEntry(ulong Address, ClubListClub? ParentClub, ClubListClub? FeederClub,
    DateOnly? StartDate, byte TypeCode, int? AnnualFee = null, ClubAffiliationSnapshot? Snapshot = null, DateOnly? EndDate = null,
    byte? FriendlyChance = null, byte? ParentWagePercent = null, bool? PlayFriendlies = null, bool? FirstOption = null,
    bool? PlayersOnLoan = null, bool? ScoutingShared = null);

/// <summary>The two competition IDs are indistinguishable and every observed year is zero; only Level is confirmed.</summary>
public sealed record ClubRegionalDivisionEntry(ulong Address, byte Level);

/// <summary>Finances → Sponsors and Other. Annual value and the Renew Income / Fixed Value flags are not confirmed and are absent.</summary>
public sealed record ClubSponsorEntry(ulong Address, byte IncomeTypeCode, int TotalValue, DateOnly? StartDate, DateOnly? EndDate,
    bool? RenewIncome = null, bool? FixedValue = null);

/// <summary>Immutable identity and bytes of a debt record, including its containing vector.</summary>
public sealed record ClubDebtSnapshot(ulong Club, ulong Holder, ulong Begin, ulong End, ulong Capacity, ulong Slot, string Bytes);

public sealed record ClubDebtEntry(ulong Address, byte SourceCode, int OriginalDebt, DateOnly? StartDate, DateOnly? EndDate,
    int? MonthlyRepayment = null, int? MonthlyInterestRepayment = null, ClubDebtSnapshot? Snapshot = null, bool? InterestOnly = null);

/// <summary>Read-only, bounded snapshots of the layouts confirmed in re/club/lists.md. No allocator or write access.</summary>
public sealed class ClubListReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxEntries = 512;
    ClubListOffsets? Layout => profile.Club?.Lists;

    /// <summary>
    /// Club vision rows for one team. Club teams use different class vtables from national teams (measured 6/11/2026:
    /// club team 0x4C90A78, holder 0x4D6AF68, culture record 0x4D79198), so this walk validates the pointer chain and
    /// the record layout instead of pinning one vtable per build.
    /// </summary>
    public IReadOnlyList<ClubVisionEntry> ReadClubVision(ulong teamAddress, IReadOnlyList<ClubEntry>? clubs = null)
    {
        if (Layout is not { } l || profile.Team is not { } t || !Vtable(teamAddress, t.VtableRva)
            || !Fits(l.CultureSize, (0, 8), (l.VisionType, 1), (l.Importance, 1), (l.VisionStart, 4),
                (l.VisionEnd, 4), (l.ItemIndex, 2), (l.ItemMarker, 2))) return [];
        ulong detail = PointerAt(teamAddress, l.TeamDetails);
        if (detail == 0) return [];
        ulong holder = PointerAt(detail, l.Vision);
        if (holder == 0 || !l.AcceptsHolderVtable(Rva(Q(holder)))) return [];
        ulong header = PointerAt(holder, l.VisionVector);
        if (header == 0 || VectorSnapshot(header) is not { } vector) return [];
        var result = new List<ClubVisionEntry>();
        for (int row = 0; row < vector.Pointers.Length; row++)
        {
            ulong address = vector.Pointers[row];
            if (address == 0 || !l.AcceptsCultureVtable(Rva(Q(address)))
                || Bytes(address, l.CultureSize) is not { } b) continue;
            // 00/03 was observed on all four independently named club-item references; other marker kinds stay unresolved.
            short? index = BitConverter.ToUInt16(b, l.ItemMarker) == 0x0300
                && BitConverter.ToInt16(b, l.ItemIndex) is >= 0 and var i ? i : null;
            result.Add(new(address, b[l.VisionType], b[l.Importance], Date(b, l.VisionStart), Date(b, l.VisionEnd),
                index, index is { } idx ? FindClub(idx, clubs) : null,
                new(teamAddress, detail, holder, header, vector.Begin, vector.End, vector.Capacity,
                    vector.Begin + (ulong)row * 8, Convert.ToHexString(b)),
                l.VisionTarget >= 0 && l.VisionTarget + 4 <= b.Length ? BitConverter.ToInt32(b, l.VisionTarget) : null,
                l.VisionStatus >= 0 && l.VisionStatus < b.Length ? b[l.VisionStatus] : null));
        }
        return result.AsReadOnly();
    }

    public IReadOnlyList<ClubAffiliationEntry> ReadAffiliations(ulong clubAddress)
    {
        if (Layout is not { } l || !ClubRoot(clubAddress)
            || !Fits(l.AffiliationSize, (l.Parent, 8), (l.Feeder, 8), (l.AffiliationStart, 4), (l.AffiliationType, 1))
            || VectorSnapshot(Field(clubAddress, l.Affiliations)) is not { } vector) return [];
        var result = new List<ClubAffiliationEntry>();
        for (int i = 0; i < vector.Pointers.Length; i++)
        {
            ulong address = vector.Pointers[i];
            if (Bytes(address, l.AffiliationSize) is not { } b) continue;
            var parent = Club(BitConverter.ToUInt64(b, l.Parent));
            var feeder = Club(BitConverter.ToUInt64(b, l.Feeder));
            if (parent is null || feeder is null) continue;
            int? fee = l.AffiliationAnnualFee is { } offset && Fits(l.AffiliationSize, (offset, 4))
                && BitConverter.ToInt32(b, offset) is >= 0 and var value ? value : null;
            result.Add(new(address, parent, feeder, Date(b, l.AffiliationStart), b[l.AffiliationType], fee,
                new(clubAddress, vector.Begin, vector.End, vector.Capacity, vector.Begin + (ulong)i * 8, Convert.ToHexString(b)),
                l.AffiliationEnd is { } end && Fits(l.AffiliationSize, (end, 4)) ? Date(b, end) : null,
                l.AffiliationFriendlyChance is { } chance && Fits(l.AffiliationSize, (chance, 1)) && b[chance] <= 100 ? b[chance] : null,
                l.AffiliationParentWagePercent is { } wage && Fits(l.AffiliationSize, (wage, 1)) && b[wage] <= 100 ? b[wage] : null,
                l.AffiliationPlayFriendlies is { } friendly && Fits(l.AffiliationSize, (friendly, 1)) ? (b[friendly] & 0x10) != 0 : null,
                l.AffiliationFirstOption is { } option && Fits(l.AffiliationSize, (option, 1)) ? (b[option] & 0x08) != 0 : null,
                l.AffiliationPlayersOnLoan is { } loan && Fits(l.AffiliationSize, (loan, 1)) ? (b[loan] & 0x04) != 0 : null,
                l.AffiliationScoutingShared is { } scouting && Fits(l.AffiliationSize, (scouting, 1)) ? (b[scouting] & 0x40) != 0 : null));
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
                result.Add(new(address, b[f.SponsorType], BitConverter.ToInt32(b, f.SponsorTotal), Date(b, f.SponsorStart), Date(b, f.SponsorEnd),
                    f.SponsorRenew is { } renew && Fits(f.SponsorSize, (renew, 1)) ? b[renew] switch { 0 => false, 1 => true, _ => (bool?)null } : null,
                    f.SponsorFixed is { } fixedValue && Fits(f.SponsorSize, (fixedValue, 1)) ? b[fixedValue] switch { 0 => false, 1 => true, _ => (bool?)null } : null));
        return result.AsReadOnly();
    }

    public IReadOnlyList<ClubDebtEntry> ReadDebts(ulong clubAddress)
    {
        if (profile.Club?.FinanceLists is not { } f || !ClubRoot(clubAddress)
            || !Fits(f.DebtSize, (f.DebtOriginal, 4), (f.DebtEnd, 4), (f.DebtStart, 4), (f.DebtSource, 1))) return [];
        ulong holder = PointerAt(clubAddress, f.DebtsPointer);
        if (holder == 0 || VectorSnapshot(Field(holder, f.DebtsVector)) is not { } vector) return [];
        var result = new List<ClubDebtEntry>();
        for (int i = 0; i < vector.Pointers.Length; i++)
        {
            ulong address = vector.Pointers[i];
            if (Bytes(address, f.DebtSize) is { } b)
                result.Add(new(address, b[f.DebtSource], BitConverter.ToInt32(b, f.DebtOriginal), Date(b, f.DebtStart), Date(b, f.DebtEnd),
                    f.MonthlyRepayment is { } repayment && Fits(f.DebtSize, (repayment, 4)) ? BitConverter.ToInt32(b, repayment) : null,
                    f.MonthlyInterestRepayment is { } interest && Fits(f.DebtSize, (interest, 4)) ? BitConverter.ToInt32(b, interest) : null,
                    new(clubAddress, holder, vector.Begin, vector.End, vector.Capacity, vector.Begin + (ulong)i * 8, Convert.ToHexString(b)),
                    f.DebtInterestOnly is { } flag && Fits(f.DebtSize, (flag, 1)) ? b[flag] switch { 0 => false, 1 => true, _ => (bool?)null } : null));
        }
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
    sealed record VectorView(ulong Begin, ulong End, ulong Capacity, ulong[] Pointers);
    ulong[]? Vector(ulong header) => VectorSnapshot(header)?.Pointers;
    VectorView? VectorSnapshot(ulong header)
    {
        if (Bytes(header, 24) is not { } h) return null;
        ulong begin = BitConverter.ToUInt64(h), end = BitConverter.ToUInt64(h, 8), cap = BitConverter.ToUInt64(h, 16);
        if (begin == 0) return end == 0 && cap == 0 ? new(begin, end, cap, []) : null;
        if (!Address(begin) || end < begin || cap < end || (end - begin) % 8 != 0 || (cap - begin) % 8 != 0
            || (cap - begin) / 8 > MaxEntries) return null;
        if (end == begin) return new(begin, end, cap, []);
        if (Bytes(begin, (int)(end - begin)) is not { } bytes) return null;
        var pointers = new ulong[bytes.Length / 8];
        for (int i = 0; i < pointers.Length; i++) pointers[i] = BitConverter.ToUInt64(bytes, i * 8);
        return new(begin, end, cap, pointers);
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
    uint Rva(ulong vtable) => (uint)(vtable - moduleBase);
    bool Vtables(ulong address, IReadOnlyList<uint> rvas) => rvas.Count > 0 && rvas.Any(rva => Vtable(address, rva));
    static DateOnly? Date(byte[] b, int offset)
    {
        int day = BitConverter.ToUInt16(b, offset) & 0x1FF, year = BitConverter.ToInt16(b, offset + 2);
        if (year is <= 1900 or > 2300 || day < 1 || day > (DateTime.IsLeapYear(year) ? 366 : 365)) return null;
        return new DateOnly(year, 1, 1).AddDays(day - 1);
    }
}

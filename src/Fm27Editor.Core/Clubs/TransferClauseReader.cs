using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Clubs;

public enum TransferClauseKind { SellOn, AppearanceFee, CompetitionBonus }

/// <summary>Owner and vector position captured alongside a clause.</summary>
public sealed record TransferClauseOrigin(ulong Club, ulong Holder, ulong Begin, ulong End, ulong Capacity, ulong Slot);

/// <summary>
/// A clause agreed in an earlier transfer of one of the club's players, owed to <see cref="Beneficiary"/>.
/// Fields that a kind does not have stay null. <see cref="SignedFor"/> is the fee the player was signed for, as
/// FMRTE shows it on a sell-on (null when the game stores -1).
/// </summary>
public sealed record TransferClause(
    ulong Address,
    TransferClauseKind Kind,
    int PlayerId,
    ClubInfo? Beneficiary,
    int? Amount,
    short? Percentage,
    int? SignedFor,
    int? Appearances,
    int? AppearancesSoFar,
    int? CompetitionIndex,
    CompetitionInfo? Competition,
    DateOnly? Ends,
    TransferClauseOrigin? Origin = null,
    string? Snapshot = null);

/// <summary>
/// Record layouts on 27.0.0+2486695, checked against FMRTE 27's clause screens (Almería, Getafe, Sheffield United):
/// <list type="bullet">
/// <item>sell-on, 0x28: +0x08 player ID, +0x10 club, +0x18 int32 signed for (-1 none), +0x22 int16 percent;</item>
/// <item>appearance fee, 0x28: +0x08 player ID, +0x10 club, +0x18 int32 fee, +0x1C int16 appearances, +0x1E int16 so far;</item>
/// <item>competition bonus, 0x38: +0x08 player ID, +0x10 end date (1/1/1900 none), +0x24 int32 competition index
/// (competition +0x8), +0x28 int32 fee, +0x30 club.</item>
/// </list>
/// </summary>
/// <param name="competitions">Competitions by internal index, to name competition bonuses; see <see cref="CompetitionDirectory"/>.</param>
public sealed class TransferClauseReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase, IReadOnlyDictionary<int, CompetitionInfo>? competitions = null)
{
    const int MaxClauses = 512, RecordSize = 0x38, MaxName = 200;

    IReadOnlyDictionary<int, CompetitionInfo>? _competitions = competitions;
    List<ulong>? _scanned;
    readonly object _scanLock = new();

    /// <summary>Clauses of one club; <paramref name="competitions"/> names competition bonuses (defaults to the constructor's map).</summary>
    public IReadOnlyList<TransferClause> Read(ulong clubAddress, IReadOnlyDictionary<int, CompetitionInfo>? competitions = null)
    {
        if (clubAddress == 0 || profile.Club is not { TransferClauses: { } t } k) return [];
        if (!Q(clubAddress, out ulong vt) || vt != moduleBase + k.ClubVtableRva) return [];
        _competitions = competitions ?? _competitions;
        return t.Scan ? Scanned(clubAddress, t) : Vectored(clubAddress, t);
    }

    /// <summary>Every clause found by the vtable scan, for screens that filter by player themselves.</summary>
    public IReadOnlyList<TransferClause> ReadAll(IReadOnlyDictionary<int, CompetitionInfo>? competitions = null)
    {
        if (profile.Club?.TransferClauses is not { Scan: true } t) return [];
        _competitions = competitions ?? _competitions;
        return Scanned(0, t);
    }

    /// <summary>
    /// 27.0.1+ keeps clause records in heap blocks with no reachable club vector: every record is found by its
    /// vtable once per reader and filtered by the club pointer inside it. Checked against FMRTE 27 on 27.0.1+2494687
    /// (Arda Güler, Fenerbahçe: FMRTE wrote the sell-on percentage at record+0x22).
    /// </summary>
    IReadOnlyList<TransferClause> Scanned(ulong clubAddress, TransferClauseOffsets t)
    {
        var clauses = new List<TransferClause>();
        var b = new byte[RecordSize];
        foreach (var address in ScannedObjects(t))
        {
            if (!memory.TryRead(address, b)) continue;
            ulong v = BitConverter.ToUInt64(b) - moduleBase;
            int player = BitConverter.ToInt32(b, 8);
            if (v == t.SellOnVtableRva)
            {
                if (clubAddress != 0 && BitConverter.ToUInt64(b, 0x10) != clubAddress) continue;
                int signedFor = BitConverter.ToInt32(b, 0x18);
                clauses.Add(new(address, TransferClauseKind.SellOn, player, Club(BitConverter.ToUInt64(b, 0x10)),
                    null, BitConverter.ToInt16(b, 0x22), signedFor < 0 ? null : signedFor, null, null, null, null, null, null,
                    Convert.ToHexString(b.AsSpan(0, 0x28))));
            }
            else if (t.AppearanceFeeVtableRva is { } app && v == app)
            {
                if (clubAddress != 0 && BitConverter.ToUInt64(b, 0x10) != clubAddress) continue;
                // +0x1E on this build is the clause type (FMRTE: "First team Appearances" for 4), not the count so
                // far, so the progress is reported as unknown instead of showing a wrong number.
                clauses.Add(new(address, TransferClauseKind.AppearanceFee, player, Club(BitConverter.ToUInt64(b, 0x10)),
                    BitConverter.ToInt32(b, 0x18), null, null, BitConverter.ToInt16(b, 0x1C), null, null, null, null, null,
                    Convert.ToHexString(b.AsSpan(0, 0x28))));
            }
            else if (t.CompetitionBonusVtableRva is { } comp && v == comp)
            {
                if (clubAddress != 0 && BitConverter.ToUInt64(b, 0x30) != clubAddress) continue;
                int index = BitConverter.ToInt32(b, 0x24);
                clauses.Add(new(address, TransferClauseKind.CompetitionBonus, player, Club(BitConverter.ToUInt64(b, 0x30)),
                    BitConverter.ToInt32(b, 0x28), null, null, null, null, index, _competitions?.GetValueOrDefault(index),
                    Date(BitConverter.ToInt16(b, 0x10), BitConverter.ToInt16(b, 0x12)), null, Convert.ToHexString(b)));
            }
        }
        return clauses;
    }

    /// <summary>Every heap object carrying one of the clause vtables; scanned once per reader/session.</summary>
    IReadOnlyList<ulong> ScannedObjects(TransferClauseOffsets t)
    {
        if (_scanned is not null) return _scanned;
        lock (_scanLock)
        {
            if (_scanned is not null) return _scanned;
            var found = new List<ulong>();
            if (memory is WindowsProcessMemory windows)
            {
                var sell = moduleBase + t.SellOnVtableRva;
                ulong? appearance = t.AppearanceFeeVtableRva is { } a ? moduleBase + a : null;
                ulong? competition = t.CompetitionBonusVtableRva is { } c ? moduleBase + c : null;
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
                            ulong v = BitConverter.ToUInt64(buffer, i);
                            if (v == sell || v == appearance || v == competition) found.Add(region.Start + offset + (ulong)i);
                        }
                    }
                }
            }
            return _scanned = found;
        }
    }

    IReadOnlyList<TransferClause> Vectored(ulong clubAddress, TransferClauseOffsets t)
    {
        if (!Q(clubAddress + (ulong)t.Pointer, out ulong holder) || holder == 0) return [];
        if (!Q(holder, out ulong begin) || !Q(holder + 8, out ulong end) || !Q(holder + 16, out ulong capacity)
            || end < begin || capacity < end || (end - begin) % 8 != 0 || (capacity - begin) % 8 != 0
            || (end - begin) / 8 > MaxClauses) return [];

        var clauses = new List<TransferClause>();
        var b = new byte[RecordSize];
        for (ulong p = begin; p < end; p += 8)
        {
            // The sell-on record is 0x28 long; a 0x38 read may cross a segment end, so fall back to 0x28.
            if (!Q(p, out ulong c) || c == 0) continue;
            bool full = memory.TryRead(c, b);
            if (!full && !memory.TryRead(c, b.AsSpan(0, 0x28))) continue;
            ulong v = BitConverter.ToUInt64(b) - moduleBase;
            int player = BitConverter.ToInt32(b, 8);
            var origin = new TransferClauseOrigin(clubAddress, holder, begin, end, capacity, p);
            if (v == t.SellOnVtableRva)
            {
                int signedFor = BitConverter.ToInt32(b, 0x18);
                clauses.Add(new(c, TransferClauseKind.SellOn, player, Club(BitConverter.ToUInt64(b, 0x10)),
                    null, BitConverter.ToInt16(b, 0x22), signedFor < 0 ? null : signedFor, null, null, null, null, null, origin,
                    Convert.ToHexString(b.AsSpan(0, 0x28))));
            }
            else if (v == t.AppearanceFeeVtableRva)
                clauses.Add(new(c, TransferClauseKind.AppearanceFee, player, Club(BitConverter.ToUInt64(b, 0x10)),
                    BitConverter.ToInt32(b, 0x18), null, null, BitConverter.ToInt16(b, 0x1C), BitConverter.ToInt16(b, 0x1E), null, null, null, origin,
                    Convert.ToHexString(b.AsSpan(0, 0x28))));
            else if (v == t.CompetitionBonusVtableRva && full)
            {
                int index = BitConverter.ToInt32(b, 0x24);
                clauses.Add(new(c, TransferClauseKind.CompetitionBonus, player, Club(BitConverter.ToUInt64(b, 0x30)),
                    BitConverter.ToInt32(b, 0x28), null, null, null, null, index, competitions?.GetValueOrDefault(index),
                    Date(BitConverter.ToInt16(b, 0x10), BitConverter.ToInt16(b, 0x12)), origin, Convert.ToHexString(b)));
            }
        }
        return clauses;
    }

    static DateOnly? Date(short dayOfYear, short year)
    {
        if (year <= 1900 || year > 2300 || dayOfYear < 1 || dayOfYear > 366) return null;
        var d = new DateOnly(year, 1, 1).AddDays(dayOfYear - 1);
        return d.Year == year ? d : null;
    }

    ClubInfo? Club(ulong address)
    {
        var k = profile.Club!;
        if (address == 0 || !Q(address, out ulong vt) || vt != moduleBase + k.ClubVtableRva) return null;
        return new ClubInfo(address, StringAt(address + (ulong)k.Name), StringAt(address + (ulong)k.ShortName));
    }

    string? StringAt(ulong field)
    {
        if (!Q(field, out ulong p) || p == 0) return null;
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(p, len)) return null;
        int n = BitConverter.ToInt32(len);
        if (n < 0 || n > MaxName) return null;
        var bytes = new byte[n];
        return memory.TryRead(p + 4, bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }

    bool Q(ulong address, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        bool ok = memory.TryRead(address, b);
        value = ok ? BitConverter.ToUInt64(b) : 0;
        return ok;
    }
}

using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Offered contracts and future transfers (FM27, save 15/8/2026; diffed against FMRTE 27 on Omar Marmoush and Alejandro Balde,
/// 2/11/2026).
/// <list type="bullet">
/// <item>[player + <see cref="PlayerOffers"/>] + 0 is a vector of pointers to records whose +<see cref="RecordContract"/> is the
/// offered contract and +<see cref="RecordOfferId"/> the offer id (Balde 5624, Marmoush 21).</item>
/// <item>The contract has the ordinary contract layout: wage +0x20, type byte +0xC3 (0 Part Time, 1 Full Time, 2 Amateur, 3 Youth; 4 shows blank in FMRTE), rolling bit 0x08 at +0x2B, started +0x44,
/// expires +0x48 (low 9 bits day of year, upper bits flags). Full contracts use vtable +0x4B00AE8, loan contracts +0x4DC91F8.</item>
/// <item>[offer list root] + <see cref="DateVector"/> is a vector of pointers to 16-byte transfer dates (person*, date, offer id):
/// 81 entries, one per Confirmed offer; FMRTE's Transfer Date moved Marmoush's from 1/7/2027 to 3/7/2027.</item>
/// </list>
/// </summary>
public sealed record FutureTransferOffsets(int PlayerOffers, int RecordContract, int RecordOfferId, int DateVector, int DatePerson, int Date, int DateOfferId,
    int Wage, int Type, int Rolling, int RollingMask, int Started, int Expires, uint FullContractVtable, uint LoanContractVtable)
{
    static readonly string[] Keys = ["playerOffers", "recordContract", "recordOfferId", "dateVector", "datePerson", "date", "dateOfferId",
        "wage", "type", "rolling", "rollingMask", "started", "expires", "fullContractVtable", "loanContractVtable"];

    internal static FutureTransferOffsets Load(JsonElement e)
    {
        long[] v = Keys.Select(key => Convert.ToInt64(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"player.futureTransfers.{key}: expected hex"), 16)).ToArray();
        return new((int)v[0], (int)v[1], (int)v[2], (int)v[3], (int)v[4], (int)v[5], (int)v[6], (int)v[7], (int)v[8], (int)v[9], (int)v[10],
            (int)v[11], (int)v[12], (uint)v[13], (uint)v[14]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        long[] v = [PlayerOffers, RecordContract, RecordOfferId, DateVector, DatePerson, Date, DateOfferId, Wage, Type, Rolling, RollingMask, Started, Expires,
            FullContractVtable, LoanContractVtable];
        w.WriteStartObject("futureTransfers");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        w.WriteEndObject();
    }
}

/// <summary>The contract an offer proposes. <see cref="Type"/> uses <see cref="ContractType"/> codes.</summary>
public sealed record OfferContract(ulong Address, int OfferId, bool Loan, int Wage, int Type, bool Rolling, DateOnly? Started, DateOnly? Expires, string Snapshot);

public sealed record OfferContractFields(int Wage, ContractType Type, bool Rolling, DateOnly Started, DateOnly Expires);

/// <summary>A Confirmed offer with its agreed transfer date (FMRTE's Future Transfer tab).</summary>
public sealed record FutureTransfer(TransferProposal Offer, ulong DateAddress, DateOnly Date, int RawDate);

public sealed class FutureTransferReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxRecords = 100, MaxDates = 100_000;

    /// <summary>Contracts offered to the player, by offer id.</summary>
    public IReadOnlyDictionary<int, OfferContract> OfferContracts(PlayerRecord player)
    {
        var map = new Dictionary<int, OfferContract>();
        if (profile.FutureTransfers is not { } o || !IsPlayer(player.Address)) return map;
        if (!Q(player.Address + (ulong)o.PlayerOffers, out ulong holder) || holder == 0) return map;
        if (!Q(holder, out ulong begin) || !Q(holder + 8, out ulong end) || begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxRecords) return map;
        for (ulong p = begin; p < end; p += 8)
        {
            if (!Q(p, out ulong record) || record == 0 || !Q(record + (ulong)o.RecordContract, out ulong contract)) continue;
            var id = new byte[4];
            if (!memory.TryRead(record + (ulong)o.RecordOfferId, id) || ReadContract(contract, BitConverter.ToInt32(id)) is not { } c) continue;
            map.TryAdd(c.OfferId, c);
        }
        return map;
    }

    public OfferContract? ReadContract(ulong contract, int offerId)
    {
        if (profile.FutureTransfers is not { } o) return null;
        var b = new byte[Math.Max(o.Expires + 4, o.Type + 1)];
        if (contract == 0 || !memory.TryRead(contract, b)) return null;
        ulong vt = BitConverter.ToUInt64(b, 0) - moduleBase;
        if (vt != o.FullContractVtable && vt != o.LoanContractVtable) return null;
        return new(contract, offerId, vt == o.LoanContractVtable, BitConverter.ToInt32(b, o.Wage), b[o.Type],
            (b[o.Rolling] & o.RollingMask) != 0, DateAt(b, o.Started), DateAt(b, o.Expires), Convert.ToHexString(b));
    }

    /// <summary>The player's Confirmed offers that carry a transfer date.</summary>
    public IReadOnlyList<FutureTransfer> Read(PlayerRecord player, IReadOnlyList<TransferProposal> proposals)
    {
        var result = new List<FutureTransfer>();
        if (profile.FutureTransfers is not { } o || profile.Proposals is not { } p || profile.Relations is not { } r) return result;
        ulong person = player.Address + (ulong)r.PlayerPerson;
        if (!Q(moduleBase + p.ListRootRva, out ulong root) || root == 0) return result;
        if (!Q(root + (ulong)o.DateVector, out ulong begin) || !Q(root + (ulong)o.DateVector + 8, out ulong end) || begin == 0 || end < begin || (end - begin) / 8 > MaxDates) return result;
        var raw = memory.Read(begin, (int)(end - begin));
        var rec = new byte[o.DateOfferId + 4];
        for (int i = 0; i < raw.Length; i += 8)
        {
            ulong at = BitConverter.ToUInt64(raw, i);
            if (at == 0 || !memory.TryRead(at, rec) || BitConverter.ToUInt64(rec, o.DatePerson) != person) continue;
            int id = BitConverter.ToInt32(rec, o.DateOfferId);
            if (proposals.FirstOrDefault(x => x.Id == id) is not { } offer || DateAt(rec, o.Date) is not { } date) continue;
            result.Add(new(offer, at, date, BitConverter.ToInt32(rec, o.Date)));
        }
        return result;
    }

    internal static DateOnly? DateAt(byte[] b, int at)
    {
        int day = BitConverter.ToInt16(b, at) & 0x1FF, year = BitConverter.ToInt16(b, at + 2);
        if (year <= 1900 || year > 2300 || day < 1 || day > 366) return null;
        var d = new DateOnly(year, 1, 1).AddDays(day - 1);
        return d.Year == year ? d : null;
    }

    bool IsPlayer(ulong address) => Q(address, out ulong vt) && profile.PlayerVtableRvas.Any(r => vt == moduleBase + r);

    bool Q(ulong address, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        bool ok = address != 0 && memory.TryRead(address, b);
        value = ok ? BitConverter.ToUInt64(b) : 0;
        return ok;
    }
}

/// <summary>
/// Guarded writes for offered contracts and transfer dates: the contract or date record must be byte-for-byte as read; dates
/// keep their flag bits (as FMRTE did); read-back.
/// </summary>
public sealed class FutureTransferWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public void SetContract(OfferContract contract, OfferContractFields f)
    {
        var o = profile.FutureTransfers ?? throw new InvalidOperationException("Profile has no future transfer offsets");
        if (f.Wage < 0) throw new ArgumentOutOfRangeException(nameof(f), "Wages cannot be negative");
        if (f.Expires < f.Started) throw new ArgumentOutOfRangeException(nameof(f), "The contract cannot expire before it starts");
        var before = Convert.FromHexString(contract.Snapshot);
        if (!memory.Read(contract.Address, before.Length).AsSpan().SequenceEqual(before)) throw new StaleObjectException("The offered contract changed since it was read");
        var after = (byte[])before.Clone();
        BitConverter.GetBytes(f.Wage).CopyTo(after, o.Wage);
        after[o.Type] = (byte)f.Type;
        after[o.Rolling] = (byte)(f.Rolling ? after[o.Rolling] | o.RollingMask : after[o.Rolling] & ~o.RollingMask);
        PutDate(after, o.Started, f.Started);
        PutDate(after, o.Expires, f.Expires);
        Write(contract.Address, after);
    }

    public void SetDate(FutureTransfer transfer, DateOnly date)
    {
        var o = profile.FutureTransfers ?? throw new InvalidOperationException("Profile has no future transfer offsets");
        ulong at = transfer.DateAddress + (ulong)o.Date;
        var now = memory.Read(at, 4);
        if (BitConverter.ToInt32(now) != transfer.RawDate || BitConverter.ToInt32(memory.Read(transfer.DateAddress + (ulong)o.DateOfferId, 4)) != transfer.Offer.Id)
            throw new StaleObjectException("The transfer date changed since it was read");
        PutDate(now, 0, date);
        Write(at, now);
    }

    static void PutDate(byte[] b, int at, DateOnly d)
    {
        short raw = BitConverter.ToInt16(b, at);
        BitConverter.GetBytes((short)((raw & ~0x1FF) | d.DayOfYear)).CopyTo(b, at);
        BitConverter.GetBytes((short)d.Year).CopyTo(b, at + 2);
    }

    void Write(ulong at, byte[] data)
    {
        if (!memory.TryWrite(at, data) || !memory.Read(at, data.Length).AsSpan().SequenceEqual(data))
            throw new MemoryAccessException($"Could not write {data.Length} bytes at 0x{at:X}");
    }
}

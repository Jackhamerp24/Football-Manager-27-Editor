using System.Text;
using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Transfer offers: [module + <see cref="ListRootRva"/>] + <see cref="ListOffset"/> is a vector of offers,
/// unioned at read time with a heap scan for transfer-vtable objects: new builds keep live offers outside any
/// static vector (Perisic's Man Utd bid, id 109, lives only in the heap on 27.0.1+2494687). Offer layout as in
/// FM26's TransferOffer: person (the player's database object on new builds) +0x38, buying team +0x40, selling team +0x50,
/// int32 fee +0x58, int32 id +0x5C, status byte +0x8D. A player's own list is the int32 id vector whose header is at
/// [player + <see cref="PlayerIds"/>]. Checked against FMRTE 27's Transfer Proposals tab on Omar Marmoush (Transfer, Man City
/// → Tottenham, £20,000,000, Confirmed), Alejandro Balde (Loan, Barcelona → Man Utd, £150,000, Ready to sign) and others;
/// FMRTE 27 writing Balde's Value 150,000 → 150,001 changed +0x58 only (2/11/2026).
/// </summary>
public sealed record ProposalOffsets(uint ListRootRva, int ListOffset, int Person, int Buyer, int Seller, int Value, int Id, int Status, int PlayerIds,
    uint TransferVtable, uint LoanVtable, uint TrialVtable, uint BClubMoveVtable,
    uint? ManagerRva = null, uint? ManagerVtable = null, int OfferVector = 0x20)
{
    static readonly string[] Keys = ["listRootRva", "listOffset", "person", "buyer", "seller", "value", "id", "status", "playerIds",
        "transferVtable", "loanVtable", "trialVtable", "bClubMoveVtable"];

    internal static ProposalOffsets Load(JsonElement e)
    {
        long Hex(string key) => Convert.ToInt64(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"player.proposals.{key}: expected hex"), 16);
        long[] v = Keys.Select(Hex).ToArray();
        return new((uint)v[0], (int)v[1], (int)v[2], (int)v[3], (int)v[4], (int)v[5], (int)v[6], (int)v[7], (int)v[8], (uint)v[9], (uint)v[10], (uint)v[11], (uint)v[12],
            e.TryGetProperty("managerRva", out _) ? (uint)Hex("managerRva") : null,
            e.TryGetProperty("managerVtable", out _) ? (uint)Hex("managerVtable") : null,
            e.TryGetProperty("offerVector", out _) ? (int)Hex("offerVector") : 0x20);
    }

    internal void Write(Utf8JsonWriter w)
    {
        long[] v = [ListRootRva, ListOffset, Person, Buyer, Seller, Value, Id, Status, PlayerIds, TransferVtable, LoanVtable, TrialVtable, BClubMoveVtable];
        w.WriteStartObject("proposals");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        if (ManagerRva is { } manager) w.WriteString("managerRva", $"0x{manager:X}");
        if (ManagerVtable is { } managerVtable) w.WriteString("managerVtable", $"0x{managerVtable:X}");
        if (ManagerRva is not null) w.WriteString("offerVector", $"0x{OfferVector:X}");
        w.WriteEndObject();
    }
}

/// <summary>Offer kinds by class. Transfer and Loan are as FMRTE 27 labels them; Trial and B Club Move are inferred from the
/// deals (free agents joining clubs; Benfica B → Benfica) and FM26's offer classes, since FMRTE showed none of them.</summary>
public enum ProposalType { Transfer, Loan, Trial, BClubMove }

public sealed record TransferProposal(ulong Address, ProposalType Type, int Id, string? From, string? To, int Value, int Status);

/// <summary>An offer involving a club, including offers for staff as well as players.</summary>
public sealed record ClubTransferOffer(TransferProposal Proposal, string? PersonName, int? PersonId, ulong PersonAddress,
    ulong SellerClubAddress, ulong BuyerClubAddress);

public sealed class TransferProposalReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxIds = 100, MaxOffers = 100_000, MaxName = 200;

    /// <summary>FMRTE 27 lists status 8, 10, 12 and 19 and hides 2 (rejected), 3 (cancelled), 15 and 20 (completed).</summary>
    public static bool ShownByFmrte(int status) => status is not (2 or 3 or 15 or 20);

    /// <summary>Reads the global offer vector once and selects offers involving the club by object address.</summary>
    public IReadOnlyList<ClubTransferOffer> ReadForClub(ulong clubAddress, IReadOnlyList<PlayerRecord> players, IReadOnlyList<Staff.StaffRecord> staff)
    {
        if (profile.Proposals is not { } o || profile.Relations is not { } rel || profile.Club is not { } club ||
            !Q(clubAddress, out var clubVtable) || clubVtable != moduleBase + club.ClubVtableRva) return [];
        var persons = new Dictionary<ulong, (string Name, int? Id)>();
        foreach (var player in players) persons.TryAdd(player.Address + (ulong)rel.PlayerPerson, (player.DisplayName, player.Id));
        foreach (var person in staff) persons.TryAdd(person.Address, (person.DisplayName, person.Id));
        // New builds point offer.person at the player's database object instead of the person part,
        // so match those offers back through the players' own id lists.
        var byOfferId = new Dictionary<int, (string Name, int? Id)>();
        foreach (var player in players)
        {
            if (!Q(player.Address + (ulong)o.PlayerIds, out ulong header) || header == 0) continue;
            if (!Q(header, out ulong begin) || !Q(header + 8, out ulong end) || begin == 0 || end < begin || (end - begin) % 4 != 0 || (end - begin) / 4 > MaxIds) continue;
            var ids = memory.Read(begin, (int)(end - begin));
            for (int i = 0; i + 4 <= ids.Length; i += 4)
                byOfferId.TryAdd(BitConverter.ToInt32(ids, i), (player.DisplayName, player.Id));
        }
        var result = new List<ClubTransferOffer>();
        foreach (var address in OffersCached(o).Values)
        {
            var bytes = new byte[o.Status + 1];
            if (!memory.TryRead(address, bytes)) continue;
            ulong seller = TeamClub(BitConverter.ToUInt64(bytes, o.Seller));
            ulong buyer = TeamClub(BitConverter.ToUInt64(bytes, o.Buyer));
            if (seller != clubAddress && buyer != clubAddress) continue;
            if (ReadOffer(address) is not { } offer || !ShownByFmrte(offer.Status)) continue;
            ulong person = BitConverter.ToUInt64(bytes, o.Person);
            if (!persons.TryGetValue(person, out var match) || match.Name is null) byOfferId.TryGetValue(offer.Id, out match);
            result.Add(new(offer, match.Name, match.Id, person, seller, buyer));
        }
        return result;
    }

    ulong TeamClub(ulong team)
    {
        if (team == 0 || profile.Team is not { } t || profile.Contract is not { } c || profile.Club is not { } k ||
            !Q(team, out var vt) || vt != moduleBase + t.VtableRva || !Q(team + (ulong)c.TeamClub, out var club) ||
            !Q(club, out var clubVt) || clubVt != moduleBase + k.ClubVtableRva) return 0;
        return club;
    }

    /// <summary>The offers the player's id list names, in that order; all statuses.</summary>
    public IReadOnlyList<TransferProposal> Read(PlayerRecord player)
    {
        if (profile.Proposals is not { } o || !Q(player.Address, out ulong vt) || !profile.PlayerVtableRvas.Any(r => vt == moduleBase + r)) return [];
        if (!Q(player.Address + (ulong)o.PlayerIds, out ulong header) || header == 0) return [];
        if (!Q(header, out ulong begin) || !Q(header + 8, out ulong end) || begin == 0 || end < begin || (end - begin) % 4 != 0 || (end - begin) / 4 > MaxIds) return [];
        var ids = memory.Read(begin, (int)(end - begin));
        if (ids.Length == 0) return [];
        if (OfferVector(o) is (true, var offersBegin, var offersCount))
        {
            var direct = new List<TransferProposal>();
            for (int i = 0; i < ids.Length; i += 4)
            {
                int id = BitConverter.ToInt32(ids, i);
                if (id < 0 || id >= offersCount) continue;
                if (Q(offersBegin + (ulong)id * 8, out ulong offer) && offer != 0 && ReadOffer(offer) is { } p && p.Id == id)
                    direct.Add(p);
            }
            return direct;
        }
        // Older builds (or a profile without the manager keys): the static vector plus the one-off heap scan.
        var offers = new Dictionary<int, ulong>(OffersCached(o));
        foreach (var (id, address) in Offers(o)) offers.TryAdd(id, address);
        var result = new List<TransferProposal>();
        for (int i = 0; i < ids.Length; i += 4)
            if (offers.TryGetValue(BitConverter.ToInt32(ids, i), out ulong offer) && ReadOffer(offer) is { } p) result.Add(p);
        return result;
    }

    public TransferProposal? ReadOffer(ulong offer)
    {
        if (profile.Proposals is not { } o) return null;
        var b = new byte[o.Status + 1];
        if (offer == 0 || !memory.TryRead(offer, b)) return null;
        ulong vt = BitConverter.ToUInt64(b, 0) - moduleBase;
        ProposalType? type = vt == o.TransferVtable ? ProposalType.Transfer : vt == o.LoanVtable ? ProposalType.Loan
            : vt == o.TrialVtable ? ProposalType.Trial : vt == o.BClubMoveVtable ? ProposalType.BClubMove : null;
        if (type is not { } t) return null;
        return new(offer, t, BitConverter.ToInt32(b, o.Id), ClubOf(BitConverter.ToUInt64(b, o.Seller)), ClubOf(BitConverter.ToUInt64(b, o.Buyer)),
            BitConverter.ToInt32(b, o.Value), b[o.Status]);
    }

    Dictionary<int, ulong>? _offerCache;

    /// <summary>True once the one-off offer scan has run. The player tab rail checks this so it never starts the scan
    /// on the UI thread just to decide whether to show a tab.</summary>
    public bool HasCachedOffers => _offerCache is not null || (profile.Proposals is { } o && OfferVector(o).Valid);

    /// <summary>Reads the offer map now (one heap scan) when the build has no manager vector; a no-op on 27.0.1+.</summary>
    public void EnsureLoaded()
    {
        if (profile.Proposals is { } o && !OfferVector(o).Valid) _ = OffersCached(o);
    }

    readonly object _offerLock = new();

    /// <summary>Drops the cached offer map; the manager vector needs no refresh (it is read live every time).</summary>
    public void Refresh()
    {
        if (profile.Proposals is { } o && OfferVector(o).Valid) return;
        lock (_offerLock) _offerCache = null;
    }

    /// <summary>
    /// The live offers vector inside TRANSFER_MANAGER (the singleton at <c>managerRva</c>): begin/end/capacity, one
    /// <c>offer*</c> per id at <c>begin + id*8</c>. It is read every time, so an offer made after Load Game is present
    /// without a scan or a cache.
    /// </summary>
    (bool Valid, ulong Begin, long Count) OfferVector(ProposalOffsets o)
    {
        if (o.ManagerRva is not { } slot || o.ManagerVtable is not { } vtable) return default;
        if (!Q(moduleBase + slot, out ulong manager) || manager == 0) return default;
        if (!Q(manager, out ulong managerVtable) || managerVtable != moduleBase + vtable) return default;
        if (!Q(manager + (ulong)o.OfferVector, out ulong begin) || !Q(manager + (ulong)o.OfferVector + 8, out ulong end)) return default;
        if (begin == 0 || end < begin || (end - begin) % 8 != 0) return default;
        long count = (long)((end - begin) / 8);
        return count is > 0 and <= MaxOffers ? (true, begin, count) : default;
    }

    /// <summary>
    /// Offers by id, cached for the reader's lifetime: the static root first (fast path, and the only
    /// source on older builds), unioned with one heap scan for transfer-vtable objects. New builds keep
    /// live offers outside any static vector (Perisic's Man Utd bid lives only in the heap), so the scan
    /// is what finds them; the cache keeps one session to a single scan. Entries are revalidated on use.
    /// </summary>
    Dictionary<int, ulong> OffersCached(ProposalOffsets o)
    {
        lock (_offerLock)
        {
            if (_offerCache is not null) return _offerCache;
            var map = Offers(o);
            foreach (var (id, address) in ScanOffers(o)) map.TryAdd(id, address);
            return _offerCache = map;
        }
    }

    /// <summary>
    /// Every offer whose buyer is one of these clubs, including rejected offers (the force-accept toggle needs
    /// status 2 offers too). Uses the same one-off offer scan as <see cref="Read"/>.
    /// </summary>
    public IReadOnlyList<TransferProposal> ReadAllForBuyer(IReadOnlySet<ulong> buyerClubs)
    {
        if (profile.Proposals is not { } o || buyerClubs.Count == 0) return [];
        var result = new List<TransferProposal>();
        if (OfferVector(o) is (true, var begin, var count))
        {
            var bytes0 = new byte[o.Status + 1];
            for (long id = 0; id < count; id++)
            {
                if (!Q(begin + (ulong)id * 8, out ulong address) || address == 0) continue;
                if (!memory.TryRead(address, bytes0)) continue;
                if (!buyerClubs.Contains(TeamClub(BitConverter.ToUInt64(bytes0, o.Buyer)))) continue;
                if (ReadOffer(address) is { } found) result.Add(found);
            }
            return result;
        }
        var bytes = new byte[o.Status + 1];
        foreach (var address in OffersCached(o).Values)
        {
            if (!memory.TryRead(address, bytes)) continue;
            if (!buyerClubs.Contains(TeamClub(BitConverter.ToUInt64(bytes, o.Buyer)))) continue;
            if (ReadOffer(address) is { } offer) result.Add(offer);
        }
        return result;
    }

    /// <summary>Every offer-vtable object in writable memory, by its id field: transfer, loan, trial and B club move.
    /// The class names and vtables were read from the live objects on Game Pass 27.0.1 (RTTI: FULL_TRANSFER_OFFER
    /// 0x4C26C48, LOAN_OFFER 0x4B29D68, TRIAL_OFFER 0x49DE488, B_CLUB_MOVE_OFFER 0x49D6928).</summary>
    Dictionary<int, ulong> ScanOffers(ProposalOffsets o)
    {
        var map = new Dictionary<int, ulong>();
        if (memory is not WindowsProcessMemory windows) return map;
        ulong transfer = moduleBase + o.TransferVtable,
            loan = o.LoanVtable == 0 ? 0 : moduleBase + o.LoanVtable,
            trial = o.TrialVtable == 0 ? 0 : moduleBase + o.TrialVtable,
            bMove = o.BClubMoveVtable == 0 ? 0 : moduleBase + o.BClubMoveVtable;
        // Reading the game's writable memory is I/O bound (about 4 GB, ~200 MB/s on one thread), so the one-off
        // scan reads regions in parallel. Matches are rare, so each worker collects locally and merges once.
        var hits = new List<(int Id, ulong Address)>();
        Parallel.ForEach(windows.EnumerateWritableRegions().Where(r => !r.IsModuleData),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8) }, region =>
        {
            var buffer = new byte[1 << 22];
            var id = new byte[4];
            var local = new List<(int, ulong)>();
            for (ulong offset = 0; offset + 8 <= region.Size; offset += (ulong)buffer.Length)
            {
                int length = (int)Math.Min((ulong)buffer.Length, region.Size - offset);
                if (!memory.TryRead(region.Start + offset, buffer.AsSpan(0, length))) break;
                var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(buffer.AsSpan(0, length));
                int w = 0;
                if (System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated)
                {
                    var v0 = System.Runtime.Intrinsics.Vector256.Create(transfer);
                    var v1 = System.Runtime.Intrinsics.Vector256.Create(loan == 0 ? ulong.MaxValue : loan);
                    var v2 = System.Runtime.Intrinsics.Vector256.Create(trial == 0 ? ulong.MaxValue : trial);
                    var v3 = System.Runtime.Intrinsics.Vector256.Create(bMove == 0 ? ulong.MaxValue : bMove);
                    int lanes = System.Runtime.Intrinsics.Vector256<ulong>.Count;
                    for (; w + lanes <= words.Length; w += lanes)
                    {
                        var value = System.Runtime.Intrinsics.Vector256.LoadUnsafe(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(words.Slice(w)));
                        var match = System.Runtime.Intrinsics.Vector256.Equals(value, v0)
                            | System.Runtime.Intrinsics.Vector256.Equals(value, v1)
                            | System.Runtime.Intrinsics.Vector256.Equals(value, v2)
                            | System.Runtime.Intrinsics.Vector256.Equals(value, v3);
                        if (match == System.Runtime.Intrinsics.Vector256<ulong>.Zero) continue;
                        uint bits = System.Runtime.Intrinsics.Vector256.ExtractMostSignificantBits(match);
                        while (bits != 0)
                        {
                            int lane = System.Numerics.BitOperations.TrailingZeroCount(bits);
                            bits &= bits - 1;
                            ulong address = region.Start + offset + (ulong)(w + lane) * 8;
                            if (memory.TryRead(address + (ulong)o.Id, id)) local.Add((BitConverter.ToInt32(id), address));
                        }
                    }
                }
                for (; w < words.Length; w++)
                {
                    ulong v = words[w];
                    if (v != transfer && (loan == 0 || v != loan) && (trial == 0 || v != trial) && (bMove == 0 || v != bMove)) continue;
                    ulong address = region.Start + offset + (ulong)w * 8;
                    if (memory.TryRead(address + (ulong)o.Id, id)) local.Add((BitConverter.ToInt32(id), address));
                }
            }
            lock (hits) hits.AddRange(local);
        });
        foreach (var (id, address) in hits) map.TryAdd(id, address);
        return map;
    }

    Dictionary<int, ulong> Offers(ProposalOffsets o)
    {
        var map = new Dictionary<int, ulong>();
        if (!Q(moduleBase + o.ListRootRva, out ulong root) || root == 0) return map;
        if (!Q(root + (ulong)o.ListOffset, out ulong begin) || !Q(root + (ulong)o.ListOffset + 8, out ulong end) || begin == 0 || end < begin || (end - begin) / 8 > MaxOffers) return map;
        var raw = memory.Read(begin, (int)(end - begin));
        var id = new byte[4];
        for (int i = 0; i < raw.Length; i += 8)
        {
            ulong offer = BitConverter.ToUInt64(raw, i);
            if (offer != 0 && memory.TryRead(offer + (ulong)o.Id, id)) map.TryAdd(BitConverter.ToInt32(id), offer);
        }
        return map;
    }

    string? ClubOf(ulong team)
    {
        if (team == 0 || profile.Team is not { } t || profile.Contract is not { } c || profile.Club is not { } k) return null;
        if (!Q(team, out ulong vt) || vt != moduleBase + t.VtableRva || !Q(team + (ulong)c.TeamClub, out ulong club)) return null;
        return StringAt(club + (ulong)k.Name) ?? StringAt(club + (ulong)k.ShortName);
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
        bool ok = address != 0 && memory.TryRead(address, b);
        value = ok ? BitConverter.ToUInt64(b) : 0;
        return ok;
    }
}

/// <summary>Sets an offer's fee (FMRTE's Value box). Guarded: same offer kind and id, fee still as read; read-back.</summary>
public sealed class TransferProposalWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public void SetValue(TransferProposal proposal, int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "The fee cannot be negative");
        var o = profile.Proposals ?? throw new InvalidOperationException("Profile has no proposal offsets");
        if (new TransferProposalReader(memory, profile, moduleBase).ReadOffer(proposal.Address) is not { } now
            || now.Type != proposal.Type || now.Id != proposal.Id || now.Value != proposal.Value)
            throw new StaleObjectException("The offer changed since it was read");
        ulong at = proposal.Address + (ulong)o.Value;
        var data = BitConverter.GetBytes(value);
        if (!memory.TryWrite(at, data) || !memory.Read(at, 4).AsSpan().SequenceEqual(data)) throw new MemoryAccessException($"Could not write 0x{at:X}");
    }

    /// <summary>
    /// Writes the offer status byte (FMRTE: 0 Offer made, 1 Negotiating, 2 Rejected, 5 Seller accepted,
    /// 8 Player accepted, 12 Ready to sign, 19 Confirmed). Guarded like the fee: same kind/id/status, read-back.
    /// </summary>
    public void SetStatus(TransferProposal proposal, byte status)
    {
        var o = profile.Proposals ?? throw new InvalidOperationException("Profile has no proposal offsets");
        if (new TransferProposalReader(memory, profile, moduleBase).ReadOffer(proposal.Address) is not { } now
            || now.Type != proposal.Type || now.Id != proposal.Id || now.Status != proposal.Status)
            throw new StaleObjectException("The offer changed since it was read");
        ulong at = proposal.Address + (ulong)o.Status;
        byte[] data = [status];
        if (!memory.TryWrite(at, data) || !memory.Read(at, 1).AsSpan().SequenceEqual(data))
            throw new MemoryAccessException($"Could not write 0x{at:X}");
    }
}

using System.Text;
using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Transfer offers: [module + <see cref="ListRootRva"/>] + <see cref="ListOffset"/> is a vector of every offer (6,205 on
/// 15/8/2026). Offer layout as in FM26's TransferOffer: person (player + 0x288) +0x38, buying team +0x40, selling team +0x50,
/// int32 fee +0x58, int32 id +0x5C, status byte +0x8D. A player's own list is the int32 id vector whose header is at
/// [player + <see cref="PlayerIds"/>]. Checked against FMRTE 27's Transfer Proposals tab on Omar Marmoush (Transfer, Man City
/// → Tottenham, £20,000,000, Confirmed), Alejandro Balde (Loan, Barcelona → Man Utd, £150,000, Ready to sign) and others;
/// FMRTE 27 writing Balde's Value 150,000 → 150,001 changed +0x58 only (2/11/2026).
/// </summary>
public sealed record ProposalOffsets(uint ListRootRva, int ListOffset, int Person, int Buyer, int Seller, int Value, int Id, int Status, int PlayerIds,
    uint TransferVtable, uint LoanVtable, uint TrialVtable, uint BClubMoveVtable)
{
    static readonly string[] Keys = ["listRootRva", "listOffset", "person", "buyer", "seller", "value", "id", "status", "playerIds",
        "transferVtable", "loanVtable", "trialVtable", "bClubMoveVtable"];

    internal static ProposalOffsets Load(JsonElement e)
    {
        long[] v = Keys.Select(key => Convert.ToInt64(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"player.proposals.{key}: expected hex"), 16)).ToArray();
        return new((uint)v[0], (int)v[1], (int)v[2], (int)v[3], (int)v[4], (int)v[5], (int)v[6], (int)v[7], (int)v[8], (uint)v[9], (uint)v[10], (uint)v[11], (uint)v[12]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        long[] v = [ListRootRva, ListOffset, Person, Buyer, Seller, Value, Id, Status, PlayerIds, TransferVtable, LoanVtable, TrialVtable, BClubMoveVtable];
        w.WriteStartObject("proposals");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
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
        var result = new List<ClubTransferOffer>();
        foreach (var address in Offers(o).Values)
        {
            var bytes = new byte[o.Status + 1];
            if (!memory.TryRead(address, bytes)) continue;
            ulong seller = TeamClub(BitConverter.ToUInt64(bytes, o.Seller));
            ulong buyer = TeamClub(BitConverter.ToUInt64(bytes, o.Buyer));
            if (seller != clubAddress && buyer != clubAddress) continue;
            if (ReadOffer(address) is not { } offer || !ShownByFmrte(offer.Status)) continue;
            ulong person = BitConverter.ToUInt64(bytes, o.Person);
            persons.TryGetValue(person, out var match);
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
        var offers = Offers(o);
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
}

using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Transfer;

public sealed record CompletedTransferPlan(ulong Player, ulong Person, ulong Buyer, ulong Seller, int Fee, DateOnly Today,
    ulong Root, int OfferId, int Serial, FutureTransferVectorSnapshot? Ids, FutureTransferVectorSnapshot? Links, IReadOnlyList<PlannedWrite> Guards);

/// <summary>
/// The record FM keeps of a finished transfer. FM's own move of Cole Palmer Chelsea → Man Utd for £50M (In-Game Editor, Steam
/// 27.0.1+2503222, 14/11/2026) left a transfer offer with status 0x14, fee, buyer and seller teams in the first empty slot of
/// the offer list, its ID on <c>player+0x320</c> and a 72-byte link on <c>player+0x328</c>; the buyer's FFP fell by the fee.
/// A move that changed only the club finances (Saka Arsenal → Liverpool, same fee) left the buyer's FFP untouched, so the
/// buyer's transfer expenditure is counted from these records.
/// </summary>
public sealed class CompletedTransferRecord(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int OfferSerial = 0x7D4, MaxOffers = 100_000, MaxPersonOffers = 100, AuxSize = 0x60;

    public CompletedTransferPlan Plan(ulong player, ulong buyerTeam, ulong sellerTeam, int fee, DateOnly today)
    {
        var po = profile.Proposals ?? throw new InvalidOperationException("No offer offsets");
        var fo = profile.FutureTransfers ?? throw new InvalidOperationException("No future-transfer offsets");
        var rel = profile.Relations ?? throw new InvalidOperationException("No person offsets");
        if (fee < 0) throw new ArgumentOutOfRangeException(nameof(fee));
        var guards = new List<PlannedWrite>();
        byte[] Guard(ulong at, int length, string what) { var b = memory.Read(at, length); guards.Add(new(at, b, [], what)); return b; }
        ulong Pointer(ulong at, string what) => BitConverter.ToUInt64(Guard(at, 8, what));
        ulong root = Pointer(moduleBase + po.ListRootRva, "offer root");
        if (root == 0) throw new StaleObjectException("Offer root missing");
        int serial = BitConverter.ToInt32(Guard(root + OfferSerial, 4, "offer serial"));
        var offers = FutureTransferVectorSnapshot.Capture(memory, root + (ulong)po.ListOffset, 8, MaxOffers);
        guards.AddRange(offers.Guards);
        int slot = offers.IndexOf(x => BitConverter.ToUInt64(x) == 0);
        if (slot < 0) throw new InvalidOperationException("The game's offer list has no free slot");
        ulong ids = Pointer(player + (ulong)po.PlayerIds, "player offer IDs");
        ulong links = Pointer(player + (ulong)fo.PlayerOffers, "player offers");
        return new(player, player + (ulong)rel.PlayerPerson, buyerTeam, sellerTeam, fee, today, root, slot, serial,
            ids == 0 ? null : FutureTransferVectorSnapshot.Capture(memory, ids, 4, MaxPersonOffers),
            links == 0 ? null : FutureTransferVectorSnapshot.Capture(memory, links, 8, MaxPersonOffers), guards);
    }

    /// <summary>
    /// The finished-transfer offer. Same layout as the game's Palmer record; its dates (+0x6C..+0x7F) carried other flag bits
    /// and transfer-window days the editor does not know, so the dates of the game's staff records are used instead.
    /// </summary>
    public static byte[] Offer(ulong vtable, ulong person, ulong buyer, ulong seller, int fee, int id, int serial, DateOnly today, ulong aux)
    {
        var offer = Staff.FreeStaffSigning.Offer(vtable, person, buyer, id, serial, today);
        BitConverter.GetBytes(seller).CopyTo(offer, 0x50);
        BitConverter.GetBytes(fee).CopyTo(offer, 0x58);
        offer[0x89] = 7;                                       // the game's player record had 7 here (staff: 0xFF)
        BitConverter.GetBytes(aux).CopyTo(offer, 0xA8);
        return offer;
    }

    public void Apply(CompletedTransferPlan plan, Func<int, ulong> allocate)
    {
        var po = profile.Proposals!; var fo = profile.FutureTransfers!;
        var guards = new List<PlannedWrite>(plan.Guards);
        foreach (var v in new[] { plan.Ids, plan.Links }) if (v is not null) { v.Validate(memory); guards.AddRange(v.Guards); }
        var scratch = new List<PlannedWrite>(); var publish = new List<PlannedWrite>();
        ulong Fresh(byte[] data, string name)
        {
            ulong at = allocate(data.Length);
            if (at == 0) throw new MemoryAccessException($"Allocation failed: {name}");
            var before = memory.Read(at, data.Length);
            if (before.Any(x => x != 0)) throw new MemoryAccessException($"Allocation was not zeroed: {name}");
            scratch.Add(new(at, before, data, name)); return at;
        }
        void Vector(ulong slot, FutureTransferVectorSnapshot? snapshot, byte[] item)
        {
            if (snapshot is not null) { var w = snapshot.Append(memory, item, allocate); scratch.Add(w[0]); publish.Add(w[1]); return; }
            ulong data = Fresh(item, "new vector data");
            var h = new byte[24]; BitConverter.GetBytes(data).CopyTo(h, 0);
            BitConverter.GetBytes(data + (ulong)item.Length).CopyTo(h, 8); BitConverter.GetBytes(data + (ulong)item.Length).CopyTo(h, 16);
            publish.Add(new(slot, memory.Read(slot, 8), BitConverter.GetBytes(Fresh(h, "new vector holder")), "vector holder"));
        }
        ulong aux = Fresh(new byte[AuxSize], "offer terms");   // all zero in the game's record
        ulong address = Fresh(Offer(moduleBase + po.TransferVtable, plan.Person, plan.Buyer, plan.Seller, plan.Fee, plan.OfferId, plan.Serial, plan.Today, aux), "offer");
        var link = Staff.FreeStaffSigning.Link(plan.OfferId); link[0x41] = 1;   // the game's player link had 01 01 at +0x40
        ulong linkAddress = Fresh(link, "offer link");
        Vector(plan.Player + (ulong)po.PlayerIds, plan.Ids, BitConverter.GetBytes(plan.OfferId));
        Vector(plan.Player + (ulong)fo.PlayerOffers, plan.Links, BitConverter.GetBytes(linkAddress));
        ulong slotAddress = memory.ReadUInt64(plan.Root + (ulong)po.ListOffset) + (ulong)plan.OfferId * 8;
        publish.Add(new(slotAddress, new byte[8], BitConverter.GetBytes(address), "offer slot"));
        publish.Add(new(plan.Root + OfferSerial, BitConverter.GetBytes(plan.Serial), BitConverter.GetBytes(plan.Serial + 1), "offer serial"));
        FutureTransferTransaction.Apply(memory, guards, [.. scratch, .. publish]);
    }
}

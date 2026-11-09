using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Takes a future transfer back out: the reverse of <see cref="FutureTransferCreator"/>. The offer leaves the game's offer list and
/// date list and the player's two lists (offer ids, offer contracts); the offer list is flagged changed. The objects themselves are
/// left in memory, nothing points at them any more. Every list must hold the transfer exactly once and every vector is checked
/// again right before the writes, or nothing is written.
/// </summary>
public sealed class FutureTransferRemover(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int ChangedFlag = 0x50, MaxOffers = 100_000, MaxPlayerOffers = 100;

    public sealed record Plan(PlayerRecord Player, FutureTransfer Transfer, IReadOnlyList<PlannedWrite> Guards, IReadOnlyList<PlannedWrite> Writes);

    public Plan Prepare(PlayerRecord player, FutureTransfer transfer)
    {
        var po = profile.Proposals ?? throw new InvalidOperationException("Missing proposal offsets");
        var fo = profile.FutureTransfers ?? throw new InvalidOperationException("Missing future-transfer offsets");
        var guards = new List<PlannedWrite>();
        byte[] Guard(ulong at, int len, string name) { var b = memory.Read(at, len); guards.Add(new(at, b, [], name)); return b; }
        ulong Pointer(ulong at, string name) => BitConverter.ToUInt64(Guard(at, 8, name));

        ulong playerVtable = Pointer(player.Address, "player vtable");
        if (!profile.PlayerVtableRvas.Any(x => playerVtable == moduleBase + x)) throw new StaleObjectException("Player vtable changed");
        ulong root = Pointer(moduleBase + po.ListRootRva, "offer root");
        if (root == 0) throw new StaleObjectException("Offer root missing");
        Guard(root, 8, "offer root vtable");

        ulong offer = transfer.Offer.Address, date = transfer.DateAddress;
        int id = transfer.Offer.Id;
        if (BitConverter.ToInt32(Guard(offer + (ulong)po.Id, 4, "offer id")) != id) throw new StaleObjectException("The offer is no longer the one that was read");
        if (BitConverter.ToInt32(Guard(date + (ulong)fo.DateOfferId, 4, "date offer id")) != id) throw new StaleObjectException("The transfer date is no longer the one that was read");

        var offers = FutureTransferVectorSnapshot.Capture(memory, root + (ulong)po.ListOffset, 8, MaxOffers);
        var dates = FutureTransferVectorSnapshot.Capture(memory, root + (ulong)fo.DateVector, 8, MaxOffers);
        ulong idsHolder = Pointer(player.Address + (ulong)po.PlayerIds, "player offers holder");
        ulong linksHolder = Pointer(player.Address + (ulong)fo.PlayerOffers, "player contracts holder");
        if (idsHolder == 0 || linksHolder == 0) throw new InvalidOperationException("The player has no offers to remove");
        var ids = FutureTransferVectorSnapshot.Capture(memory, idsHolder, 4, MaxPlayerOffers);
        var links = FutureTransferVectorSnapshot.Capture(memory, linksHolder, 8, MaxPlayerOffers);

        bool IsOffer(ReadOnlySpan<byte> item) => BitConverter.ToUInt64(item) == offer;
        bool IsDate(ReadOnlySpan<byte> item) => BitConverter.ToUInt64(item) == date;
        bool IsId(ReadOnlySpan<byte> item) => BitConverter.ToInt32(item) == id;
        // The player's list holds a record per offer: its offer id sits at RecordOfferId. Read it from the game for each pointer.
        bool IsLink(ReadOnlySpan<byte> item) => BitConverter.ToUInt64(item) is var record && record != 0
            && memory.TryRead(record + (ulong)fo.RecordOfferId, new byte[4]) && BitConverter.ToInt32(memory.Read(record + (ulong)fo.RecordOfferId, 4)) == id;

        foreach (var (name, v, test) in new (string, FutureTransferVectorSnapshot, Func<ReadOnlySpan<byte>, bool>)[]
            { ("offer list", offers, IsOffer), ("transfer dates", dates, IsDate), ("player offer ids", ids, IsId), ("player offer contracts", links, IsLink) })
            if (v.CountOf(test) != 1) throw new StaleObjectException($"The transfer is not in the {name} exactly once");

        var writes = new List<PlannedWrite>();
        writes.AddRange(offers.RemoveAt(memory, offers.IndexOf(IsOffer)));
        writes.AddRange(dates.RemoveAt(memory, dates.IndexOf(IsDate)));
        writes.AddRange(ids.RemoveAt(memory, ids.IndexOf(IsId)));
        writes.AddRange(links.RemoveAt(memory, links.IndexOf(IsLink)));
        foreach (var v in new[] { offers, dates, ids, links }) guards.AddRange(v.Guards);
        writes.Add(new(root + ChangedFlag, memory.Read(root + ChangedFlag, 1), [1], "offer changed flag"));
        return new(player, transfer, guards, writes);
    }

    public void Apply(Plan plan) => FutureTransferTransaction.Apply(memory, plan.Guards, plan.Writes);
}

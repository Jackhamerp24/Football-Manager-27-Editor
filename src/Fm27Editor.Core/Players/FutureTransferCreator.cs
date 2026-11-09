using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Players;

/// <summary>Creation of a future transfer, traced on FMRTE for Game Pass 27.0.0+2493426. Call only with an attached, build-checked session.</summary>
public sealed class FutureTransferCreator(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int OfferSerial = 0x7D4, ChangedFlag = 0x50, ContractBytes = 200;
    const int MaxOffers = 100_000, MaxPlayerOffers = 100;

    public sealed record Plan(PlayerRecord Player, ulong Buyer, ulong Seller, int Fee, DateOnly Today, DateOnly Date,
        ulong Root, int Id, int Serial, byte[] Contract, FutureTransferVectorSnapshot Terms, FutureTransferVectorSnapshot ExtraTerms,
        FutureTransferVectorSnapshot Offers, FutureTransferVectorSnapshot Dates, ulong IdHolder, ulong ContractHolder,
        FutureTransferVectorSnapshot? PlayerIds, FutureTransferVectorSnapshot? PlayerContracts, IReadOnlyList<PlannedWrite> Guards,
        TransferTerms? OfferTerms = null);

    /// <param name="offerTerms">Transfer terms for the offer; null or all zero writes the empty terms object FMRTE writes without "Set transfer terms".</param>
    public Plan Prepare(PlayerRecord player, ulong buyer, int fee, DateOnly today, DateOnly date, TransferTerms? offerTerms = null)
    {
        // Every address comes from the profile, so the same code runs on Game Pass and Steam; the guards below refuse any object that is not the expected kind.
        if (fee < 0 || date <= today) throw new ArgumentException("A future date and non-negative fee are required");
        var po = profile.Proposals ?? throw new InvalidOperationException("Missing proposal offsets");
        var fo = profile.FutureTransfers ?? throw new InvalidOperationException("Missing future-transfer offsets");
        var co = profile.Contract ?? throw new InvalidOperationException("Missing contract offsets");
        var to = profile.Team ?? throw new InvalidOperationException("Missing team offsets");
        var rel = profile.Relations ?? throw new InvalidOperationException("Missing person offsets");
        var guards = new List<PlannedWrite>();
        byte[] Guard(ulong at, int len, string name) { var b = memory.Read(at, len); guards.Add(new(at, b, [], name)); return b; }
        ulong Pointer(ulong at, string name) => BitConverter.ToUInt64(Guard(at, 8, name));
        ulong playerVtable = Pointer(player.Address, "player vtable");
        if (!profile.PlayerVtableRvas.Any(x => playerVtable == moduleBase + x)) throw new StaleObjectException("Player vtable changed");
        if (Pointer(buyer, "buying team vtable") != moduleBase + to.VtableRva) throw new StaleObjectException("Invalid buying team");
        ulong contract = Pointer(player.Address + (ulong)co.PlayerContract, "current contract pointer");
        if (contract == 0) throw new InvalidOperationException("Player has no current contract");
        var source = Guard(contract, ContractBytes, "current contract");
        if (BitConverter.ToUInt64(source) != moduleBase + co.ContractVtableRva) throw new StaleObjectException("Invalid contract vtable");
        ulong seller = BitConverter.ToUInt64(source, co.Team);
        if (seller == 0 || Pointer(seller, "selling team vtable") != moduleBase + to.VtableRva || seller == buyer)
            throw new InvalidOperationException("Invalid selling team or destination");
        if (BitConverter.ToUInt64(source, 8) != player.Address + (ulong)rel.PlayerPerson)
            throw new StaleObjectException("Contract belongs to a different person");
        if (fo.CurrentLoanHolder is { } loan && Pointer(player.Address + (ulong)loan, "loan holder") != 0)
            throw new InvalidOperationException("Creation for an existing loan has not been verified");
        ulong root = Pointer(moduleBase + po.ListRootRva, "offer root");
        if (root == 0) throw new StaleObjectException("Offer root missing");
        Guard(root, 8, "offer root vtable");
        int serial = BitConverter.ToInt32(Guard(root + OfferSerial, 4, "offer serial"));
        if (serial < 0 || serial == int.MaxValue) throw new StaleObjectException("Invalid offer serial");
        var offers = FutureTransferVectorSnapshot.Capture(memory, root + (ulong)po.ListOffset, 8, MaxOffers);
        var dates = FutureTransferVectorSnapshot.Capture(memory, root + (ulong)fo.DateVector, 8, MaxOffers);
        int id = offers.Count;
        var rawOffers = offers.Items;
        for (int i = 0; i < rawOffers.Length; i += 8)
        {
            ulong address = BitConverter.ToUInt64(rawOffers, i);
            if (address != 0 && BitConverter.ToInt32(Guard(address + (ulong)po.Id, 4, "existing offer ID")) == id)
                throw new StaleObjectException("Next offer ID is already in use");
        }
        ulong ids = Pointer(player.Address + (ulong)po.PlayerIds, "player offers holder");
        ulong links = Pointer(player.Address + (ulong)fo.PlayerOffers, "player contracts holder");
        var playerIds = ids == 0 ? null : FutureTransferVectorSnapshot.Capture(memory, ids, 4, MaxPlayerOffers);
        var playerContracts = links == 0 ? null : FutureTransferVectorSnapshot.Capture(memory, links, 8, MaxPlayerOffers);
        var proposals = new TransferProposalReader(memory, profile, moduleBase).Read(player);
        if (new FutureTransferReader(memory, profile, moduleBase).Read(player, proposals).Count != 0)
            throw new InvalidOperationException("Player already has a future transfer");
        var terms = FutureTransferVectorSnapshot.Capture(memory, contract + 0x68, 8, 10_000);
        var extra = FutureTransferVectorSnapshot.Capture(memory, contract + 0x80, 32, 10_000);
        Guard(root + ChangedFlag, 1, "offer changed flag");
        if (offerTerms is not null) TransferTermsLayout.Records(offerTerms);   // rejects negative amounts before anything is allocated
        return new(player, buyer, seller, fee, today, date, root, id, serial, source, terms, extra, offers, dates,
            ids, links, playerIds, playerContracts, guards, offerTerms);
    }

    /// <summary>Allocates with the supplied target CRT allocator, then checks snapshots and writes through one transaction.</summary>
    public int Apply(Plan plan, Func<int, ulong> allocate)
    {
        var po = profile.Proposals!; var fo = profile.FutureTransfers!; var rel = profile.Relations!;
        var guards = new List<PlannedWrite>(plan.Guards);
        foreach (var v in new[] { plan.Terms, plan.ExtraTerms, plan.Offers, plan.Dates, plan.PlayerIds, plan.PlayerContracts })
            if (v is not null) { v.Validate(memory); guards.AddRange(v.Guards); }
        var scratch = new List<PlannedWrite>(); var publish = new List<PlannedWrite>();
        ulong Fresh(byte[] data, string name)
        {
            if (data.Length == 0) return 0;
            ulong at = allocate(data.Length);
            if (at == 0) throw new MemoryAccessException($"Allocation failed: {name}");
            var before = memory.Read(at, data.Length);
            if (before.Any(x => x != 0)) throw new MemoryAccessException($"Allocation was not zeroed: {name}");
            scratch.Add(new(at, before, data, name)); return at;
        }
        ulong termPtr = Fresh(plan.Terms.Items, "contract terms");
        ulong extraPtr = Fresh(plan.ExtraTerms.Items, "additional contract terms");
        var contract = FutureTransferContractTemplate.Build(plan.Contract, plan.Buyer, plan.Today, plan.Date,
            termPtr, plan.Terms.Items.Length, extraPtr, plan.ExtraTerms.Items.Length);
        ulong contractPtr = Fresh(contract, "future contract");
        ulong link = Fresh(FutureTransferRecordTemplate.ContractLink(contractPtr, plan.Id), "contract link");
        ulong person = plan.Player.Address + (ulong)rel.PlayerPerson;
        ulong date = Fresh(FutureTransferRecordTemplate.TransferDate(person, plan.Date, plan.Id), "future transfer date");
        var offerTerms = plan.OfferTerms ?? new TransferTerms(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, 0);
        var termRecords = TransferTermsLayout.Records(offerTerms);
        var termAddresses = new ulong[termRecords.Length];
        for (int i = 0; i < termRecords.Length; i++) termAddresses[i] = Fresh(termRecords[i], $"transfer term {i}");
        ulong auxiliary = Fresh(TransferTermsLayout.Object(offerTerms, termAddresses, termRecords), "offer auxiliary");
        ulong offer = Fresh(FutureTransferRecordTemplate.Offer(moduleBase + po.TransferVtable, person, plan.Buyer, plan.Seller,
            plan.Fee, plan.Id, plan.Serial, auxiliary, plan.Today, plan.Date), "offer");
        void Append(FutureTransferVectorSnapshot v, byte[] item)
        {
            var w = v.Append(memory, item, allocate);
            scratch.Add(w[0]); publish.Add(w[1]);
        }
        void PlayerVector(ulong holder, FutureTransferVectorSnapshot? snapshot, ulong slot, byte[] item)
        {
            if (holder != 0) { Append(snapshot!, item); return; }
            ulong data = Fresh(item, "new player vector data");
            var h = new byte[24]; BitConverter.GetBytes(data).CopyTo(h, 0);
            BitConverter.GetBytes(data + (ulong)item.Length).CopyTo(h, 8); BitConverter.GetBytes(data + (ulong)item.Length).CopyTo(h, 16);
            ulong newHolder = Fresh(h, "new player vector holder");
            publish.Add(new(slot, memory.Read(slot, 8), BitConverter.GetBytes(newHolder), "player vector holder"));
        }
        Append(plan.Offers, BitConverter.GetBytes(offer));
        Append(plan.Dates, BitConverter.GetBytes(date));
        PlayerVector(plan.IdHolder, plan.PlayerIds, plan.Player.Address + (ulong)po.PlayerIds, BitConverter.GetBytes(plan.Id));
        PlayerVector(plan.ContractHolder, plan.PlayerContracts, plan.Player.Address + (ulong)fo.PlayerOffers, BitConverter.GetBytes(link));
        publish.Add(new(plan.Root + OfferSerial, BitConverter.GetBytes(plan.Serial), BitConverter.GetBytes(plan.Serial + 1), "offer serial"));
        publish.Add(new(plan.Root + ChangedFlag, memory.Read(plan.Root + ChangedFlag, 1), [1], "offer changed flag"));
        FutureTransferTransaction.Apply(memory, guards, [.. scratch, .. publish]);
        return plan.Id;
    }
}

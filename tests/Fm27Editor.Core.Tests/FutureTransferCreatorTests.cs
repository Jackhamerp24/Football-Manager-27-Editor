using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class FutureTransferCreatorTests
{
    const ulong Module = 0x7FF000000000, Player = 0x10000000, Contract = 0x20000000,
        Seller = 0x30000000, Buyer = 0x30001000, Root = 0x40000000;
    readonly FakeMemory memory = new();
    readonly OffsetProfile profile = OffsetProfile.LoadFile(Path.Combine(AppContext.BaseDirectory, "offsets", "27.0.0+2493426-gamepass.json"));
    readonly PlayerRecord player;
    ulong next = 0x50000000;

    public FutureTransferCreatorTests()
    {
        var p = memory.Map(Player, 0x400);
        BitConverter.GetBytes(Module + profile.PlayerVtableRvas[0]).CopyTo(p, 0);
        BitConverter.GetBytes(Contract).CopyTo(p, profile.Contract!.PlayerContract);
        var c = memory.Map(Contract, 200);
        BitConverter.GetBytes(Module + profile.Contract.ContractVtableRva).CopyTo(c, 0);
        BitConverter.GetBytes(Player + (ulong)profile.Relations!.PlayerPerson).CopyTo(c, 8);
        BitConverter.GetBytes(Seller).CopyTo(c, profile.Contract.Team);
        var seller = memory.Map(Seller, 0x100); var buyer = memory.Map(Buyer, 0x100);
        BitConverter.GetBytes(Module + profile.Team!.VtableRva).CopyTo(seller, 0);
        BitConverter.GetBytes(Module + profile.Team.VtableRva).CopyTo(buyer, 0);
        var rootPointer = memory.Map(Module + profile.Proposals!.ListRootRva, 8);
        BitConverter.GetBytes(Root).CopyTo(rootPointer, 0);
        var root = memory.Map(Root, 0x800); root[0] = 1;
        BitConverter.GetBytes(100).CopyTo(root, 0x7D4);
        player = new PlayerRecord { Address = Player, VtableRva = profile.PlayerVtableRvas[0], Id = 123,
            CurrentAbility = 100, PotentialAbility = 120, HomeReputation = 1, CurrentReputation = 1,
            WorldReputation = 1, Height = 180, RawAttributes = [], AttributeScale = 5, Positions = [], Personality = [] };
    }

    ulong Allocate(int size) { ulong address = next; next += 0x1000; memory.Map(address, size); return address; }

    [Fact]
    public void CreatesFutureOfferAndReadsItBack()
    {
        var creator = new FutureTransferCreator(memory, profile, Module);
        var plan = creator.Prepare(player, Buyer, 12345678, new(2026, 9, 30), new(2027, 1, 1));
        Assert.Equal(0, memory.Writes);
        int id = creator.Apply(plan, Allocate);
        var offers = new TransferProposalReader(memory, profile, Module).Read(player);
        var future = new FutureTransferReader(memory, profile, Module).Read(player, offers);
        var transfer = Assert.Single(future);
        Assert.Equal(0, id); Assert.Equal(id, transfer.Offer.Id);
        Assert.Equal(12345678, transfer.Offer.Value); Assert.Equal(new DateOnly(2027, 1, 1), transfer.Date);
        var offered = new FutureTransferReader(memory, profile, Module).OfferContracts(player);
        Assert.True(offered.TryGetValue(id, out var contract));
        Assert.Equal(new DateOnly(2027, 1, 1), contract!.Started);
        Assert.Equal(new DateOnly(2030, 6, 30), contract.Expires);
        Assert.Equal(Seller, BitConverter.ToUInt64(memory.Read(Contract + (ulong)profile.Contract!.Team, 8)));
    }

    [Fact]
    public void TermsAreWrittenAsTheBrunoCaptureHas()
    {
        // Bruno capture (FMRTE 27 build8): records and the +0xC0/+0xC4/+0xC6 values of the 208-byte terms object.
        var terms = new TransferTerms(14000000, 36, 7654321, 31, 6543210, 11, 543210, 13, 4321098, 14, 3210987, 19, false, 87000, 12, false);
        var creator = new FutureTransferCreator(memory, profile, Module);
        creator.Apply(creator.Prepare(player, Buyer, 100, new(2026, 9, 30), new(2027, 1, 1), terms), Allocate);
        ulong begin = memory.ReadUInt64(Root + (ulong)profile.Proposals!.ListOffset);
        ulong offer = memory.ReadUInt64(begin);
        ulong obj = memory.ReadUInt64(offer + 0xA8);
        string[] expected = ["809FD500247F00000C000000", "B1CB74001F7F0000", "6AD763000B7F0000", "EA4908000D7F0000", "4AEF41000E7F0000",
            "EBFE3000FF7F000001006C0701006C07"];
        for (int i = 0; i < 6; i++)
        {
            ulong start = memory.ReadUInt64(obj + (ulong)(i * 24)), end = memory.ReadUInt64(obj + (ulong)(i * 24) + 8);
            Assert.Equal(expected[i], Convert.ToHexString(memory.Read(start, (int)(end - start))));
        }
        Assert.Equal(87000, BitConverter.ToInt32(memory.Read(obj + 0xC0, 4)));
        Assert.Equal(19, BitConverter.ToUInt16(memory.Read(obj + 0xC4, 2)));
        Assert.Equal(0, BitConverter.ToUInt16(memory.Read(obj + 0xC6, 2)));
    }

    [Fact]
    public void NoTermsLeaveTheEmptyTermsObject()
    {
        var creator = new FutureTransferCreator(memory, profile, Module);
        creator.Apply(creator.Prepare(player, Buyer, 100, new(2026, 9, 30), new(2027, 1, 1)), Allocate);
        ulong offer = memory.ReadUInt64(memory.ReadUInt64(Root + (ulong)profile.Proposals!.ListOffset));
        ulong obj = memory.ReadUInt64(offer + 0xA8);
        Assert.All(memory.Read(obj, 208), x => Assert.Equal(0, x));
    }

    FutureTransfer Create()
    {
        var creator = new FutureTransferCreator(memory, profile, Module);
        creator.Apply(creator.Prepare(player, Buyer, 12345678, new(2026, 9, 30), new(2027, 1, 1)), Allocate);
        var offers = new TransferProposalReader(memory, profile, Module).Read(player);
        return Assert.Single(new FutureTransferReader(memory, profile, Module).Read(player, offers));
    }

    IReadOnlyList<FutureTransfer> Transfers() =>
        new FutureTransferReader(memory, profile, Module).Read(player, new TransferProposalReader(memory, profile, Module).Read(player));

    [Fact]
    public void RemovingACreatedFutureTransferLeavesNoneAndAllowsANewOne()
    {
        var transfer = Create();
        Assert.True(memory.TryWrite(Root + 0x50, new byte[] { 0 }));                // creation flagged the list; clear it so the removal has to
        var remover = new FutureTransferRemover(memory, profile, Module);
        var plan = remover.Prepare(player, transfer);
        int before = memory.Writes;
        remover.Apply(plan);
        Assert.True(memory.Writes > before);
        Assert.Empty(Transfers());
        Assert.Empty(new TransferProposalReader(memory, profile, Module).Read(player));
        Assert.Empty(new FutureTransferReader(memory, profile, Module).OfferContracts(player));
        Assert.Equal(1, memory.Read(Root + 0x50, 1)[0]);                          // the offer list is flagged changed
        var again = Create();                                                      // the player can be given a transfer again
        Assert.Equal(new DateOnly(2027, 1, 1), again.Date);
    }

    [Fact]
    public void RemovalIsRefusedWhenAListChangedAndWritesNothing()
    {
        var transfer = Create();
        var remover = new FutureTransferRemover(memory, profile, Module);
        var plan = remover.Prepare(player, transfer);
        ulong idsHolder = memory.ReadUInt64(Player + (ulong)profile.Proposals!.PlayerIds);
        ulong idsBegin = memory.ReadUInt64(idsHolder);
        Assert.True(memory.TryWrite(idsBegin, BitConverter.GetBytes(999)));           // the player's id list changed after the plan
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => remover.Apply(plan));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void RemovalIsRefusedForATransferThatIsNotInTheLists()
    {
        var transfer = Create();
        var remover = new FutureTransferRemover(memory, profile, Module);
        remover.Apply(remover.Prepare(player, transfer));
        Assert.ThrowsAny<Exception>(() => remover.Prepare(player, transfer));          // already gone: not removed twice
    }

    [Fact]
    public void ChangedContractPreventsAnyPublishedWrites()
    {
        var creator = new FutureTransferCreator(memory, profile, Module);
        var plan = creator.Prepare(player, Buyer, 100, new(2026, 9, 30), new(2027, 1, 1));
        Assert.True(memory.TryWrite(Contract + 0x20, new byte[] { 3 }));
        int previous = memory.Writes;
        Assert.Throws<StaleObjectException>(() => creator.Apply(plan, Allocate));
        Assert.Equal(previous, memory.Writes);
        Assert.Equal(0UL, memory.ReadUInt64(Player + (ulong)profile.Proposals!.PlayerIds));
    }
}

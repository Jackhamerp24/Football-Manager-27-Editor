using Fm27Editor.Core.ForceAccept;
using Fm27Editor.Core.History;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// <see cref="ForceAcceptPlanner.Apply"/> is the only write path of this feature. The byte sequences are
/// FM27Studio's patch constants (the same bytes the Cheat Engine force-accept presets write), the RVAs are
/// synthetic, and the shipped profile carries no force-accept section — so every case the design says must be
/// refused is refused for the reason it names, with the fake memory seeing no write. Apply also demands a paused
/// game, because a multi-byte code patch can be torn if a thread executes it mid-write.
/// </summary>
public class ForceAcceptApplyTests
{
    const ulong Base = TestWorld.ModuleBase;
    const uint GeneralRequestsRva = 0x1000, ClubVisionRva = 0x1400, ContractRva = 0x1800, TransferRva = 0x1C00;

    static readonly byte[] Gate1Original = [0x0F, 0x85, 0xFD, 0x00, 0x00, 0x00];
    static readonly byte[] Gate1Patched = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90];
    static readonly byte[] Gate2Original = [0x78, 0x7C];
    static readonly byte[] Gate2Patched = [0x90, 0x90];
    static readonly byte[] ContractOriginal =
        [0x41, 0x89, 0xC7, 0x48, 0x8B, 0xB5, 0x78, 0x10, 0x00, 0x00, 0x89, 0x86, 0xD0, 0x1E, 0x00, 0x00];
    static readonly byte[] TransferOriginal = [0x44, 0x0F, 0xB6, 0xAF, 0x00, 0x08, 0x00, 0x00];
    static readonly byte[] ForeignSix = [0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC];
    static readonly byte[] ForeignTwo = [0xCC, 0xCC];
    static readonly byte[] ForeignEight = Enumerable.Repeat((byte)0xAA, 8).ToArray();

    const string AllRvas =
        """{"boardRequestsRva": "0x1000", "clubVisionRva": "0x1400", "contractOffersRva": "0x1800", "transferOffersRva": "0x1C00"}""";

    static string ShippedJson => File.ReadAllText(TestWorld.ProfilePath);

    /// <summary>The shipped profile plus one synthetic <c>forceAccept</c> section.</summary>
    static OffsetProfile ProfileWith(string forceAcceptJson)
    {
        var json = ShippedJson.TrimEnd();
        int close = json.LastIndexOf('}');
        return OffsetProfile.Load(json[..close] + ",\"forceAccept\":" + forceAcceptJson + json[close..]);
    }

    static void Put(FakeMemory memory, ulong address, byte[] bytes) =>
        bytes.CopyTo(memory.Map(address, bytes.Length), 0);

    /// <summary>Replaces bytes at an address already mapped: a second Map would hide behind the first segment.</summary>
    static void Overwrite(FakeMemory memory, ulong address, byte[] bytes) =>
        Assert.True(memory.TryWrite(address, bytes));

    static FakeMemory BoardMemory()
    {
        var memory = new FakeMemory();
        Put(memory, Base + GeneralRequestsRva, Gate1Original);
        Put(memory, Base + ClubVisionRva, Gate2Original);
        return memory;
    }

    [Fact]
    public void Apply_writes_every_pending_site_and_reads_each_write_back()
    {
        var memory = BoardMemory();
        var planner = new ForceAcceptPlanner(ProfileWith(AllRvas), memory, Base);
        var plan = planner.Plan(ForceAcceptPreset.BoardRequests);

        int patched = planner.Apply(plan, gamePaused: true);

        Assert.Equal(2, patched);
        Assert.Equal(2, memory.Writes);
        Assert.Equal(Gate1Patched, memory.Read(Base + GeneralRequestsRva, Gate1Patched.Length));
        Assert.Equal(Gate2Patched, memory.Read(Base + ClubVisionRva, Gate2Patched.Length));
        Assert.Equal(ForceAcceptPlanState.Active, planner.Plan(ForceAcceptPreset.BoardRequests).State);
    }

    [Fact]
    public void Apply_refuses_a_blocked_plan_and_writes_nothing()
    {
        var memory = new FakeMemory();
        Put(memory, Base + TransferRva, ForeignEight);
        var planner = new ForceAcceptPlanner(ProfileWith(AllRvas), memory, Base);
        var plan = planner.Plan(ForceAcceptPreset.TransferOffers);
        Assert.Equal(ForceAcceptPlanState.Blocked, plan.State);

        int writes = memory.Writes;
        Assert.Throws<InvalidOperationException>(() => planner.Apply(plan, gamePaused: true));

        Assert.Equal(writes, memory.Writes);
        Assert.Equal(ForeignEight, memory.Read(Base + TransferRva, ForeignEight.Length));
    }

    [Fact]
    public void Apply_refuses_the_contract_preset_even_when_its_site_looks_ready()
    {
        var memory = new FakeMemory();
        Put(memory, Base + ContractRva, ContractOriginal);
        var planner = new ForceAcceptPlanner(ProfileWith(AllRvas), memory, Base);
        var plan = planner.Plan(ForceAcceptPreset.ContractOffers);
        Assert.Equal(ForceAcceptPlanState.Unavailable, plan.State);   // guarded hook, not implemented

        Assert.Throws<InvalidOperationException>(() => planner.Apply(plan, gamePaused: true));
        Assert.Equal(0, memory.Writes);
        Assert.Equal(ContractOriginal, memory.Read(Base + ContractRva, ContractOriginal.Length));
    }

    [Fact]
    public void Apply_refuses_while_the_game_is_running()
    {
        var memory = BoardMemory();
        var planner = new ForceAcceptPlanner(ProfileWith(AllRvas), memory, Base);
        var plan = planner.Plan(ForceAcceptPreset.BoardRequests);
        Assert.Equal(ForceAcceptPlanState.Appliable, plan.State);

        Assert.Throws<InvalidOperationException>(() => planner.Apply(plan, gamePaused: false));

        Assert.Equal(0, memory.Writes);
        Assert.Equal(Gate1Original, memory.Read(Base + GeneralRequestsRva, Gate1Original.Length));
    }

    [Fact]
    public void One_faulty_gate_blocks_the_whole_preset_before_any_write()
    {
        var memory = BoardMemory();
        Overwrite(memory, Base + ClubVisionRva, ForeignTwo);  // one gate the game or another tool changed
        var planner = new ForceAcceptPlanner(ProfileWith(AllRvas), memory, Base);
        var plan = planner.Plan(ForceAcceptPreset.BoardRequests);
        Assert.Equal(ForceAcceptPlanState.Blocked, plan.State);
        Assert.Equal(ForceAcceptSiteState.Ready, plan.Sites[0].State);      // the other gate is perfectly writable
        Assert.Equal(ForceAcceptSiteState.Mismatch, plan.Sites[1].State);

        int writes = memory.Writes;
        Assert.Throws<InvalidOperationException>(() => planner.Apply(plan, gamePaused: true));

        Assert.Equal(writes, memory.Writes);
        Assert.Equal(Gate1Original, memory.Read(Base + GeneralRequestsRva, Gate1Original.Length));
        Assert.Equal(ForeignTwo, memory.Read(Base + ClubVisionRva, ForeignTwo.Length));
    }

    [Fact]
    public void A_site_that_changed_after_the_plan_is_refused_and_nothing_is_written()
    {
        var memory = BoardMemory();
        var planner = new ForceAcceptPlanner(ProfileWith(AllRvas), memory, Base);
        var plan = planner.Plan(ForceAcceptPreset.BoardRequests);
        Assert.Equal(ForceAcceptPlanState.Appliable, plan.State);

        Overwrite(memory, Base + GeneralRequestsRva, ForeignSix);  // the bytes moved between plan and apply
        int writes = memory.Writes;
        Assert.Throws<StaleObjectException>(() => planner.Apply(plan, gamePaused: true));

        Assert.Equal(writes, memory.Writes);
        Assert.Equal(ForeignSix, memory.Read(Base + GeneralRequestsRva, ForeignSix.Length));
        Assert.Equal(Gate2Original, memory.Read(Base + ClubVisionRva, Gate2Original.Length));
    }

    [Fact]
    public void A_profile_without_offsets_never_applies()
    {
        var memory = new FakeMemory();
        var planner = new ForceAcceptPlanner(OffsetProfile.Load(ShippedJson), memory, Base);
        var plan = planner.Plan(ForceAcceptPreset.TransferOffers);
        Assert.Equal(ForceAcceptPlanState.Unavailable, plan.State);

        Assert.Throws<InvalidOperationException>(() => planner.Apply(plan, gamePaused: true));
        Assert.Equal(0, memory.Writes);
    }

    [Fact]
    public void A_preset_that_is_already_active_is_not_written_again()
    {
        var memory = new FakeMemory();
        Put(memory, Base + GeneralRequestsRva, Gate1Patched);
        Put(memory, Base + ClubVisionRva, Gate2Patched);
        var planner = new ForceAcceptPlanner(ProfileWith(AllRvas), memory, Base);
        var plan = planner.Plan(ForceAcceptPreset.BoardRequests);
        Assert.Equal(ForceAcceptPlanState.Active, plan.State);

        int writes = memory.Writes;
        Assert.Throws<InvalidOperationException>(() => planner.Apply(plan, gamePaused: true));
        Assert.Equal(writes, memory.Writes);
    }

    [Fact]
    public void One_apply_is_one_history_group_and_undo_puts_the_original_bytes_back()
    {
        var fake = new FakeMemory();
        Put(fake, Base + GeneralRequestsRva, Gate1Original);
        Put(fake, Base + ClubVisionRva, Gate2Original);
        var journal = new ChangeJournal();
        var memory = new JournalMemory(fake, journal);
        var planner = new ForceAcceptPlanner(ProfileWith(AllRvas), memory, Base, journal);

        int patched = planner.Apply(planner.Plan(ForceAcceptPreset.BoardRequests), gamePaused: true);

        Assert.Equal(2, patched);
        var group = Assert.Single(journal.Groups);
        Assert.Equal(("Force Accept", "Board requests", "Force accept"), (group.Kind, group.Entity, group.Title));
        Assert.Equal(new[] { "General requests", "Club vision" }, group.Entries.Select(e => e.Name).ToArray());
        Assert.Equal(Gate1Patched, fake.Read(Base + GeneralRequestsRva, Gate1Patched.Length));

        var (restored, errors) = journal.Undo(group, memory);

        Assert.Equal((2, 0), (restored, errors.Count));
        Assert.True(group.FullyUndone);
        Assert.Equal(Gate1Original, fake.Read(Base + GeneralRequestsRva, Gate1Original.Length));
        Assert.Equal(Gate2Original, fake.Read(Base + ClubVisionRva, Gate2Original.Length));
    }

    [Fact]
    public void A_write_that_fails_part_way_puts_the_sites_already_written_back()
    {
        var fake = BoardMemory();
        var memory = new FailingWriteMemory(fake, Base + ClubVisionRva);   // the second site refuses to be written
        var planner = new ForceAcceptPlanner(ProfileWith(AllRvas), memory, Base);
        var plan = planner.Plan(ForceAcceptPreset.BoardRequests);
        Assert.Equal(ForceAcceptPlanState.Appliable, plan.State);

        Assert.Throws<MemoryAccessException>(() => planner.Apply(plan, gamePaused: true));

        // The first site was patched, then rolled back: the preset may not stand half applied.
        Assert.Equal(Gate1Original, fake.Read(Base + GeneralRequestsRva, Gate1Original.Length));
        Assert.Equal(Gate2Original, fake.Read(Base + ClubVisionRva, Gate2Original.Length));
    }

    /// <summary>Stands in for WindowsProcessMemory.OnWrite, recording each write the way that hook does.</summary>
    sealed class JournalMemory(IProcessMemory inner, ChangeJournal journal) : IProcessMemory
    {
        public bool TryRead(ulong address, Span<byte> buffer) => inner.TryRead(address, buffer);

        public bool TryWrite(ulong address, ReadOnlySpan<byte> data)
        {
            var before = new byte[data.Length];
            if (!inner.TryRead(address, before) || !inner.TryWrite(address, data)) return false;
            journal.Record(address, before, data.ToArray());
            return true;
        }
    }

    /// <summary>Every write succeeds except at one address, standing in for memory that refuses a write.</summary>
    sealed class FailingWriteMemory(IProcessMemory inner, ulong failAt) : IProcessMemory
    {
        public bool TryRead(ulong address, Span<byte> buffer) => inner.TryRead(address, buffer);

        public bool TryWrite(ulong address, ReadOnlySpan<byte> data) =>
            address == failAt ? false : inner.TryWrite(address, data);
    }
}

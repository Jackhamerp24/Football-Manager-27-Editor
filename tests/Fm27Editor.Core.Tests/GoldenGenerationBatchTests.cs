using System.Text.Json.Nodes;
using Fm27Editor.Core.History;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The regen-focused Golden Generation end to end against fake memory: what the preview says, what the run writes,
/// and what it refuses. Ability values are worked by hand from the specification; the byte offsets come from the
/// measured layout the fake game is built from.
/// </summary>
public class GoldenGenerationBatchTests
{
    readonly TestWorld _w = new();
    readonly ChangeJournal _journal = new();
    readonly JournalMemory _memory;
    readonly PlayerWriter _writer;
    readonly GoldenGenerationBatch _batch;

    public GoldenGenerationBatchTests()
    {
        _memory = new JournalMemory(_w.Memory, _journal);
        _writer = new PlayerWriter(_memory, _w.Profile, TestWorld.ModuleBase);
        _batch = new GoldenGenerationBatch(_writer, _w.Profile, _journal);
    }

    PlayerRecord Spawn(int id, int ca, int pa, ulong address, DateOnly? born = null, DateOnly? gameDate = null, ClubInfo? club = null) =>
        GoldenGenerationFixtures.Spawn(_w, id, ca, pa, address, born, gameDate, club);

    static GoldenGenerationRequest Request(WonderkidChance wonderkid = WonderkidChance.Off) =>
        new() { Potential = GoldenGenerationBands.Band(16), Wonderkid = wonderkid };   // 170–200

    [Fact]
    public void The_preview_shows_both_abilities_and_the_reason_each_player_was_held_back()
    {
        var young = Spawn(9001, 60, 90, 0x1000_3000, born: new(2010, 1, 1), gameDate: new(2026, 6, 1));   // age 16
        var tooOld = Spawn(9002, 60, 90, 0x1000_4000, born: new(2001, 1, 1), gameDate: new(2026, 6, 1));  // age 25
        var noId = GoldenGenerationFixtures.Player(null, born: new(2015, 1, 1), gameDate: new(2026, 6, 1));
        var seen = Spawn(9003, 60, 90, 0x1000_5000, born: new(2015, 1, 1), gameDate: new(2026, 6, 1));
        var rolled = Spawn(9004, 60, 90, 0x1000_6000, born: new(2015, 1, 1), gameDate: new(2026, 6, 1));
        var ledger = new GoldenGenerationLedger([9004]);

        var plan = _batch.Plan([young, tooOld, noId, seen, rolled],
            new() { Potential = GoldenGenerationBands.Band(16), Regens = new() { MaxAge = 21 } },
            baselineIds: [9003], ledger: ledger, random: new StubRandom(180));

        var written = Assert.Single(plan, entry => entry.WillWrite);
        Assert.Same(young, written.Player);
        Assert.Equal((90, 60, 180, 60), (written.PaBefore, written.CaBefore, written.PaAfter, written.CaAfter));

        Assert.Equal(GoldenGenerationSkipReason.TooOld, plan[1].Reason);
        Assert.Equal((90, 60, 90, 60), (plan[1].PaBefore, plan[1].CaBefore, plan[1].PaAfter, plan[1].CaAfter));
        Assert.Equal(GoldenGenerationSkipReason.PendingReview, plan[2].Reason);
        Assert.Equal(GoldenGenerationSkipReason.NotANewGeneration, plan[3].Reason);
        Assert.Equal(GoldenGenerationSkipReason.AlreadyRolled, plan[4].Reason);
        Assert.All(plan.Where(entry => !entry.WillWrite), entry => Assert.False(string.IsNullOrEmpty(entry.Detail)));
    }

    [Fact]
    public void A_run_writes_only_the_two_ability_bytes()
    {
        var player = Spawn(9101, 60, 90, 0x1000_5000);
        byte[] before = _memory.Read(player.Address, 0x3C8);

        // PA 180 in a 170–200 window; a hit on the five-in-a-hundred chance lifts 60 by 70% of the 120 gap → 144.
        var plan = _batch.Plan([player], Request(WonderkidChance.Five), random: new StubRandom(180, 0));
        var entry = Assert.Single(plan);
        Assert.Equal((90, 60, 180, 144), (entry.PaBefore, entry.CaBefore, entry.PaAfter, entry.CaAfter));

        var result = _batch.Apply(plan);
        Assert.Equal(1, result.Written);

        byte[] after = _memory.Read(player.Address, 0x3C8);
        var changed = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToList();
        // Worked by hand: 60 → 144 is 3C 00 → 90 00 and 90 → 180 is 5A 00 → B4 00, so only the low byte of each
        // ability differs. Nothing outside the two ability fields may move.
        Assert.Equal([0x264, 0x266], changed);
        Assert.Equal(144, BitConverter.ToInt16(after, 0x264));
        Assert.Equal(180, BitConverter.ToInt16(after, 0x266));
    }

    [Fact]
    public void The_whole_run_is_one_history_group_and_undo_puts_it_back()
    {
        var a = Spawn(9201, 60, 90, 0x1000_5000);
        var b = Spawn(9202, 70, 110, 0x1000_6000);
        byte[] beforeA = _memory.Read(a.Address, 0x3C8);
        byte[] beforeB = _memory.Read(b.Address, 0x3C8);

        var plan = _batch.Plan([a, b], Request(WonderkidChance.Five), random: new StubRandom(190, 0, 190, 0));
        Assert.Equal([151, 154], plan.Select(entry => entry.CaAfter));   // 60 + 91, 70 + 84
        Assert.Equal([190, 190], plan.Select(entry => entry.PaAfter));

        var result = _batch.Apply(plan);
        Assert.Equal(2, result.Written);

        var group = Assert.IsType<ChangeGroup>(result.Group);
        Assert.Same(group, Assert.Single(_journal.Groups));
        Assert.Equal(["Potential Ability", "Current Ability", "Potential Ability", "Current Ability"], group.Entries.Select(entry => entry.Name));

        var (restored, errors) = _journal.Undo(group, _memory);
        Assert.Equal(4, restored);
        Assert.Empty(errors);
        Assert.Equal(beforeA, _memory.Read(a.Address, 0x3C8));
        Assert.Equal(beforeB, _memory.Read(b.Address, 0x3C8));
    }

    [Fact]
    public void A_player_the_game_changed_since_the_preview_is_reported_and_the_rest_still_applies()
    {
        var moved = Spawn(9301, 60, 90, 0x1000_5000);
        var steady = Spawn(9302, 60, 90, 0x1000_6000);
        var plan = _batch.Plan([moved, steady], Request(), random: new StubRandom(180));
        Assert.True(_w.Memory.TryWrite(moved.Address + 0x266, BitConverter.GetBytes((short)99)));

        var result = _batch.Apply(plan);

        Assert.Equal(1, result.Written);
        var failure = Assert.Single(result.Failed);
        Assert.Equal(GoldenGenerationSkipReason.WriteFailed, failure.Reason);
        Assert.Same(moved, failure.Player);
        Assert.False(string.IsNullOrEmpty(failure.Detail));
        Assert.Equal(99, BitConverter.ToInt16(_memory.Read(moved.Address + 0x266, 2), 0));    // left as the game has it
        Assert.Equal(180, BitConverter.ToInt16(_memory.Read(steady.Address + 0x266, 2), 0));
    }

    [Fact]
    public void Progress_is_reported_for_every_player_and_cancellation_stops_the_run()
    {
        var players = Enumerable.Range(0, 4).Select(i => Spawn(9400 + i, 60, 90, (ulong)(0x1000_5000 + i * 0x400))).ToList();
        var selecting = new List<GoldenGenerationProgress>();

        var plan = _batch.Plan(players, Request(), progress: new InlineProgress<GoldenGenerationProgress>(selecting.Add), random: new StubRandom(180));
        Assert.Equal(new[] { 1, 2, 3, 4 }, selecting.Select(p => p.Done));
        Assert.All(selecting, p => Assert.Equal("Selecting", p.Phase));
        Assert.Equal(1.0, selecting[^1].Fraction);

        var writing = new List<GoldenGenerationProgress>();
        using var cts = new CancellationTokenSource();
        var cancel = new InlineProgress<GoldenGenerationProgress>(p => { writing.Add(p); if (p.Done == 2) cts.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => _batch.Apply(plan, progress: cancel, cancellationToken: cts.Token));

        Assert.Equal(new[] { 1, 2 }, writing.Select(p => p.Done));
        Assert.Equal(180, BitConverter.ToInt16(_memory.Read(players[0].Address + 0x266, 2), 0));
        Assert.Equal(180, BitConverter.ToInt16(_memory.Read(players[1].Address + 0x266, 2), 0));
        Assert.Equal(90, BitConverter.ToInt16(_memory.Read(players[2].Address + 0x266, 2), 0));
        Assert.Equal(90, BitConverter.ToInt16(_memory.Read(players[3].Address + 0x266, 2), 0));
    }

    [Fact]
    public void A_profile_without_a_player_id_offset_reports_pending_review_and_writes_nothing()
    {
        var node = JsonNode.Parse(File.ReadAllText(TestWorld.ProfilePath))!.AsObject();
        node["player"]!.AsObject().Remove("id");
        var profile = OffsetProfile.Load(node.ToJsonString());
        var player = Spawn(9501, 60, 90, 0x1000_5000);
        var batch = new GoldenGenerationBatch(new PlayerWriter(_memory, profile, TestWorld.ModuleBase), profile, _journal);

        var entry = Assert.Single(batch.Plan([player], Request()));
        Assert.Equal(GoldenGenerationSkipReason.PendingReview, entry.Reason);
        Assert.Equal("This build's profile has no player id offset", entry.Detail);

        int writes = _w.Memory.Writes;
        var result = batch.Apply([entry]);
        Assert.Equal(0, result.Written);
        Assert.Equal(1, result.PendingReview);
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Fact]
    public void A_core_filter_whose_data_is_missing_is_pending_review_instead_of_a_guess()
    {
        var window = GoldenGenerationBands.Band(16);
        var noGameDate = Spawn(9601, 60, 90, 0x1000_5000, born: new(2010, 1, 1), gameDate: null);
        var noBirthDate = Spawn(9602, 60, 90, 0x1000_6000, born: null, gameDate: new(2026, 6, 1));
        var fits = Spawn(9603, 60, 90, 0x1000_7000, born: new(2015, 1, 1), gameDate: new(2026, 6, 1));

        var byAge = _batch.Plan([noGameDate, fits], new() { Potential = window, Regens = new() { MaxAge = 21 } }, random: new StubRandom(180));
        Assert.Equal(GoldenGenerationSkipReason.PendingReview, byAge[0].Reason);
        Assert.Contains("age is unknown", byAge[0].Detail);
        Assert.Equal(GoldenGenerationSkipReason.None, byAge[1].Reason);

        var byBirth = _batch.Plan([noBirthDate, fits], new() { Potential = window, Regens = new() { BornAfter = new DateOnly(2010, 1, 1) } }, random: new StubRandom(180));
        Assert.Equal(GoldenGenerationSkipReason.PendingReview, byBirth[0].Reason);
        Assert.Contains("birth date", byBirth[0].Detail);
        Assert.Equal(GoldenGenerationSkipReason.None, byBirth[1].Reason);

        var byContract = _batch.Plan([fits], new() { Potential = window, Regens = new() { RequireYouthContract = true } }, random: new StubRandom(180));
        Assert.Equal(GoldenGenerationSkipReason.PendingReview, byContract[0].Reason);
        Assert.Contains("contract facts", byContract[0].Detail);

        var onYouth = _batch.Plan([fits], new() { Potential = window, Regens = new() { RequireYouthContract = true } },
            facts: new StubFacts(null, true), random: new StubRandom(180));
        Assert.Equal(GoldenGenerationSkipReason.None, onYouth[0].Reason);

        var notYouth = _batch.Plan([fits], new() { Potential = window, Regens = new() { RequireYouthContract = true } },
            facts: new StubFacts(null, false), random: new StubRandom(180));
        Assert.Equal(GoldenGenerationSkipReason.NotOnYouthContract, notYouth[0].Reason);
    }

    [Fact]
    public void A_player_the_filter_rules_out_is_neither_planned_nor_written()
    {
        var inReds = Spawn(9701, 60, 90, 0x1000_5000, club: GoldenGenerationFixtures.Club("Reds", 0x3000_0000));
        var inBlues = Spawn(9702, 60, 90, 0x1000_6000, club: GoldenGenerationFixtures.Club("Blues", 0x3000_1000));
        var request = new GoldenGenerationRequest
        {
            Potential = GoldenGenerationBands.Band(16),
            Filter = new([new(GoldenGenerationFilterKind.Club, Name: "Reds")]),
        };

        var plan = _batch.Plan([inReds, inBlues], request, random: new StubRandom(180));
        Assert.Equal(GoldenGenerationSkipReason.None, plan[0].Reason);
        Assert.Equal(GoldenGenerationSkipReason.FilterMismatch, plan[1].Reason);

        int writes = _w.Memory.Writes;
        var result = _batch.Apply(plan);
        Assert.Equal(1, result.Written);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(180, BitConverter.ToInt16(_memory.Read(inReds.Address + 0x266, 2), 0));
        Assert.Equal(90, BitConverter.ToInt16(_memory.Read(inBlues.Address + 0x266, 2), 0));
        Assert.Equal(writes + 1, _w.Memory.Writes);
    }

    [Fact]
    public void A_player_the_filter_needs_data_for_is_reported_for_review_and_never_written()
    {
        // The club row cannot be decided: this player exposes no club at all, which is an unknown, not a miss.
        var noClubData = Spawn(9703, 60, 90, 0x1000_7000);
        Assert.Null(noClubData.Club);
        var request = new GoldenGenerationRequest
        {
            Potential = GoldenGenerationBands.Band(16),
            Filter = new([new(GoldenGenerationFilterKind.Club, Name: "Reds")]),
        };

        var plan = _batch.Plan([noClubData], request, random: new StubRandom(180));
        var entry = Assert.Single(plan);
        Assert.Equal(GoldenGenerationSkipReason.PendingReview, entry.Reason);
        Assert.True(entry.IsPendingReview);
        Assert.False(entry.WillWrite);
        Assert.False(string.IsNullOrEmpty(entry.Detail));

        int writes = _w.Memory.Writes;
        int groups = _journal.Groups.Count;
        var result = _batch.Apply(plan);
        Assert.Equal(0, result.Written);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.PendingReview);
        Assert.Null(result.Group);
        Assert.Equal(writes, _w.Memory.Writes);
        Assert.Equal(groups, _journal.Groups.Count);
    }

    [Fact]
    public void The_ledger_only_records_what_was_written_and_a_second_run_holds_the_player_back()
    {
        var ledger = new GoldenGenerationLedger();
        var written = Spawn(9801, 60, 90, 0x1000_5000);
        var refused = Spawn(9802, 60, 90, 0x1000_6000);
        var request = Request();

        var first = _batch.Plan([written, refused], request, ledger: ledger, random: new StubRandom(180));
        Assert.True(_w.Memory.TryWrite(refused.Address + 0x266, BitConverter.GetBytes((short)99)));
        var result = _batch.Apply(first, ledger);

        Assert.Equal(1, result.Written);
        Assert.True(ledger.Contains(written.Id!.Value));
        Assert.False(ledger.Contains(refused.Id!.Value));

        var second = _batch.Plan([written, refused], request, ledger: ledger, random: new StubRandom(180));
        Assert.Equal(GoldenGenerationSkipReason.AlreadyRolled, second[0].Reason);
        Assert.Equal(GoldenGenerationSkipReason.None, second[1].Reason);   // the refused player was never rolled
    }

    [Fact]
    public void A_rolled_potential_below_current_ability_is_pending_review_and_writes_nothing()
    {
        var player = Spawn(9851, 60, 90, 0x1000_5000);
        int writes = _w.Memory.Writes;

        // Band 10–40 can roll below the player's current ability 60. That state is invalid, so the row is held.
        var plan = _batch.Plan([player], new() { Potential = GoldenGenerationBands.Band(0) }, random: new StubRandom(25));
        var entry = Assert.Single(plan);
        Assert.Equal(GoldenGenerationSkipReason.PendingReview, entry.Reason);
        Assert.Contains("below current ability", entry.Detail);

        var result = _batch.Apply(plan);
        Assert.Equal(0, result.Written);
        Assert.Equal(1, result.PendingReview);
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Fact]
    public void A_plan_that_changes_nothing_is_not_written_and_not_put_in_the_ledger()
    {
        // The rolled PA equals the current PA and the wonderkid chance is off, so there is nothing to write.
        var player = Spawn(9852, 60, 180, 0x1000_5000);
        var ledger = new GoldenGenerationLedger();
        int writes = _w.Memory.Writes;

        var plan = _batch.Plan([player], new() { Potential = GoldenGenerationBands.Band(16) }, random: new StubRandom(180));
        var entry = Assert.Single(plan);
        Assert.Equal(GoldenGenerationSkipReason.None, entry.Reason);
        Assert.False(entry.ChangesAbility);

        var result = _batch.Apply(plan, ledger);
        Assert.Equal(0, result.Written);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(ledger.Ids);
        Assert.Equal(writes, _w.Memory.Writes);
    }

    [Theory]
    [InlineData(0, 150)]      // below one
    [InlineData(150, 201)]    // above two hundred
    [InlineData(160, 150)]    // backwards
    public void A_potential_window_outside_one_to_two_hundred_or_backwards_is_refused(int min, int max) =>
        Assert.Throws<ArgumentException>(() => _batch.Plan([GoldenGenerationFixtures.Player(1)], new() { Potential = new(min, max) }));

    [Fact]
    public void A_wonderkid_chance_the_feature_does_not_offer_is_refused() =>
        Assert.Throws<ArgumentException>(() => _batch.Plan([GoldenGenerationFixtures.Player(1)],
            new() { Potential = GoldenGenerationBands.Band(0), Wonderkid = (WonderkidChance)7 }));

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
}

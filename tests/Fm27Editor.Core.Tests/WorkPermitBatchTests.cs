using Fm27Editor.Core.Competitions;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Batch work-permit grants on fake memory (the Steam 27.0.1+2494687 profile shape:
/// person index u32 at player+0x290, flag bit at +0x2A0, 0x20-byte permit records
/// sorted by person index). Expected values are hand-built from that layout, not from
/// the code under test.
/// </summary>
public class WorkPermitBatchTests
{
    readonly FakeMemory _memory = new();
    const ulong Module = 0x700000000000;
    ulong _next = 0x100000;
    ulong _arena = 0;
    ulong _arenaEnd = 0;

    ulong Allocate(int size = 0x1000) { ulong p = _next; _next += 0x2000; _memory.Map(p, size); return p; }
    void Put(ulong p, byte[] b) => Assert.True(_memory.TryWrite(p, b));
    void Q(ulong p, ulong value) => Put(p, BitConverter.GetBytes(value));
    void I(ulong p, int value) => Put(p, BitConverter.GetBytes(value));

    static OffsetProfile Profile2494687() =>
        GameSession.BuiltInProfiles().Single(p => p.Build == "27.0.1+2494687");

    ulong _header;
    OffsetProfile _profile = Profile2494687();
    ulong _nation;
    readonly List<(ulong Player, int Person)> _players = new();

    void Fixture(int people, int alreadyHave = 0)
    {
        uint playerVtable = _profile.PlayerVtableRvas[0];
        uint nationVtable = _profile.Nation!.VtableRva;
        _nation = Allocate();
        Q(_nation, Module + nationVtable);
        _header = Allocate(0x100);
        ulong records = Allocate(0x1000);
        ulong b = records;
        for (int i = 0; i < people; i++)
        {
            ulong p = Allocate(0x400);
            Q(p, Module + playerVtable);
            I(p + (ulong)WorkPermitTable.PersonIndexOffset, 100 + i);
            _players.Add((p, 100 + i));
        }
        for (int i = 0; i < alreadyHave; i++)
        {
            var record = new byte[WorkPermitTable.RecordSize];
            BitConverter.GetBytes(_nation).CopyTo(record, 0);
            BitConverter.GetBytes(_players[i].Person).CopyTo(record, 8);
            BitConverter.GetBytes((ushort)2026).CopyTo(record, 0x10);
            new byte[] { 0x01, 0x00, 0x6C, 0x07 }.CopyTo(record, 0x12);
            ulong at = b + (ulong)(i * WorkPermitTable.RecordSize);
            Put(at, record);
        }
        // Pointer vector: header points at a dedicated pointer array.
        ulong pointers = Allocate(0x100);
        for (int i = 0; i < alreadyHave; i++)
            Q(pointers + (ulong)(i * 8), b + (ulong)(i * WorkPermitTable.RecordSize));
        Q(_header, pointers);
        Q(_header + 8, pointers + (ulong)(alreadyHave * 8));
        Q(_header + 16, pointers + 0x80);
        _arena = Allocate(0x2000);
        _arenaEnd = _arena + 0x2000;
    }

    ulong AllocateFromArena(int size)
    {
        ulong at = _arena;
        _arena += (ulong)size;
        Assert.True(_arena <= _arenaEnd);
        return at;
    }

    PlayerRecord Record((ulong Player, int Person) p) => new()
    {
        Address = p.Player,
        VtableRva = _profile.PlayerVtableRvas[0],
        CurrentAbility = 100,
        PotentialAbility = 100,
        HomeReputation = 100,
        CurrentReputation = 100,
        WorldReputation = 100,
        Height = 180,
        RawAttributes = new byte[10],
        AttributeScale = 5,
        Positions = new byte[5],
        Personality = new byte[5],
    };

    IReadOnlyList<PlayerRecord> Records() => _players.Select(Record).ToArray();

    [Fact]
    public void Plan_skips_players_already_holding_the_permit_and_applies_the_rest()
    {
        Fixture(people: 3, alreadyHave: 1);
        var plan = WorkPermitBatch.Plan(_memory, _profile, Module, Records(), _nation, 2027, _ => "Testland", _header);
        Assert.Equal(2, plan.Targets.Count);
        var skip = Assert.Single(plan.Skipped);
        Assert.Contains("already holds", skip.Reason);

        var skips = new List<WorkPermitBatchSkip>();
        int granted = WorkPermitBatch.Apply(plan, _memory, _profile, Module, AllocateFromArena, skips.Add, _header);
        Assert.Equal(2, granted);
        Assert.Empty(skips);

        var table = new WorkPermitTable(_memory, _profile, Module, _header);
        var all = Records().SelectMany(p => table.Read(p, _ => "Testland")).ToArray();
        Assert.Equal(3, all.Length); // 2 new + 1 pre-existing, no duplicates
        Assert.All(all.Where(g => g.Year == 2027), g => Assert.Equal(_nation, g.Nation));
        Assert.Single(all.Where(g => g.Year == 2026));
        // Records stay sorted by person index.
        var persons = all.Select(g => BitConverter.ToInt32(_memory.Read(g.Record + 8, 4))).ToArray();
        Assert.Equal(persons.OrderBy(x => x).ToArray(), persons);
    }

    [Fact]
    public void Apply_skips_players_staled_after_planning()
    {
        Fixture(people: 2);
        var plan = WorkPermitBatch.Plan(_memory, _profile, Module, Records(), _nation, 2027, _ => "Testland", _header);
        Assert.Equal(2, plan.Targets.Count);

        Q(_players[1].Player, 0xDEADBEEF); // the game reused the object after planning

        var skips = new List<WorkPermitBatchSkip>();
        int granted = WorkPermitBatch.Apply(plan, _memory, _profile, Module, AllocateFromArena, skips.Add, _header);
        Assert.Equal(1, granted);
        var skip = Assert.Single(skips);
        Assert.Equal(_players[1].Player, skip.PlayerAddress);
        Assert.Contains("changed", skip.Reason);

        var table = new WorkPermitTable(_memory, _profile, Module, _header);
        Assert.Single(table.Read(Record(_players[0]), _ => "Testland"));
        Assert.Empty(table.Read(Record(_players[1]), _ => "Testland"));

        // Person index swapped under the same vtable: must not grant to the new person.
        Fixture(people: 1);
        var plan2 = WorkPermitBatch.Plan(_memory, _profile, Module, Records().TakeLast(1), _nation, 2027, _ => "Testland", _header);
        var swapped = _players[^1];
        I(swapped.Player + (ulong)WorkPermitTable.PersonIndexOffset, swapped.Person + 50);
        var skips2 = new List<WorkPermitBatchSkip>();
        Assert.Equal(0, WorkPermitBatch.Apply(plan2, _memory, _profile, Module, AllocateFromArena, skips2.Add, _header));
        var skip2 = Assert.Single(skips2);
        Assert.Contains("changed", skip2.Reason);
        var fresh = new WorkPermitTable(_memory, _profile, Module, _header);
        Assert.Empty(fresh.Read(Record(swapped), _ => "Testland"));
    }

    [Fact]
    public void Apply_skips_targets_granted_between_plan_and_apply()
    {
        Fixture(people: 2);
        var plan = WorkPermitBatch.Plan(_memory, _profile, Module, Records(), _nation, 2027, _ => "Testland", _header);
        Assert.Equal(2, plan.Targets.Count);

        // Someone else (another window, a second run) granted p0 after planning.
        new WorkPermitTable(_memory, _profile, Module, _header)
            .Add(Record(_players[0]), _nation, 2027, AllocateFromArena);

        var skips = new List<WorkPermitBatchSkip>();
        int granted = WorkPermitBatch.Apply(plan, _memory, _profile, Module, AllocateFromArena, skips.Add, _header);
        Assert.Equal(1, granted);
        var skip = Assert.Single(skips);
        Assert.Equal(_players[0].Player, skip.PlayerAddress);

        var table = new WorkPermitTable(_memory, _profile, Module, _header);
        Assert.Single(table.Read(Record(_players[0]), _ => "Testland"));
        Assert.Single(table.Read(Record(_players[1]), _ => "Testland"));
    }

    [Fact]
    public void Batch_is_undoable_as_a_whole()
    {
        Fixture(people: 2);
        var plan = WorkPermitBatch.Plan(_memory, _profile, Module, Records(), _nation, 2027, _ => "Testland", _header);
        Assert.Equal(2, WorkPermitBatch.Apply(plan, _memory, _profile, Module, AllocateFromArena, null, _header));

        // Undo = remove every added grant (what the history group reverts to).
        var table = new WorkPermitTable(_memory, _profile, Module, _header);
        foreach (var p in Records())
            foreach (var grant in table.Read(p, _ => "Testland").ToArray())
                table.Remove(p, grant, _ => "Testland");

        foreach (var p in Records()) Assert.Empty(table.Read(p, _ => "Testland"));
        Assert.All(_players, p => Assert.Equal(0, _memory.Read(p.Player + 0x2A0, 1)[0]));
    }

    [Fact]
    public void Copied_plan_without_baseline_is_refused()
    {
        Fixture(people: 2);
        var plan = WorkPermitBatch.Plan(_memory, _profile, Module, Records(), _nation, 2027, _ => "Testland", _header);
        Assert.Equal(2, plan.Targets.Count);

        var copied = new WorkPermitBatchPlan(plan.Targets, plan.Skipped, plan.Nation, plan.Year);
        var skips = new List<WorkPermitBatchSkip>();
        int before = _memory.Writes;
        Assert.Equal(0, WorkPermitBatch.Apply(copied, _memory, _profile, Module, AllocateFromArena, skips.Add, _header));
        Assert.Equal(before, _memory.Writes);
        Assert.Equal(2, skips.Count);
        Assert.All(skips, s => Assert.Contains("re-plan", s.Reason));
    }

    [Fact]
    public void Invalid_year_in_plan_skips_everyone()
    {
        Fixture(people: 2);
        var plan = WorkPermitBatch.Plan(_memory, _profile, Module, Records(), _nation, 1800, _ => "Testland", _header);
        Assert.Empty(plan.Targets);
        Assert.Equal(2, plan.Skipped.Count);
        Assert.All(plan.Skipped, s => Assert.Contains("year", s.Reason));

        int before = _memory.Writes;
        Assert.Equal(0, WorkPermitBatch.Apply(plan, _memory, _profile, Module, AllocateFromArena, null, _header));
        Assert.Equal(before, _memory.Writes);
    }

    [Fact]
    public void Missing_nation_offsets_give_empty_plan_and_no_writes()
    {
        Fixture(people: 2);
        var noNation = new OffsetProfile
        {
            Build = "test", Module = "test", BuildStringRva = 1,
            PlayerListRootRva = 1, PlayerListBeginOffset = 0, PlayerListEndOffset = 8,
            PlayerVtableRvas = _profile.PlayerVtableRvas, PlayerObjectSize = 0x400,
            AttributeScale = 5,
            Attributes = new Dictionary<PlayerAttribute, int>(),
            Positions = new Dictionary<PlayerPosition, int>(),
            Personality = new Dictionary<PersonalityAttribute, int>(),
            Fields = new PlayerFieldOffsets
            {
                CurrentAbility = 0, PotentialAbility = 0, HomeReputation = 0,
                CurrentReputation = 0, WorldReputation = 0, Height = 0, DateOfBirth = 0,
                FullName = 0, FirstName = 0, LastName = 0, CommonName = 0,
            },
        };
        var plan = WorkPermitBatch.Plan(_memory, noNation, Module, Records(), _nation, 2027, _ => "Testland", _header);
        Assert.Empty(plan.Targets);
        Assert.Equal(2, plan.Skipped.Count);

        int before = _memory.Writes;
        Assert.Equal(0, WorkPermitBatch.Apply(plan, _memory, noNation, Module, AllocateFromArena, null, _header));
        Assert.Equal(before, _memory.Writes);
    }

    [Fact]
    public void Invalid_nation_skips_everyone_without_writing()
    {
        Fixture(people: 2);
        int before = _memory.Writes;
        var plan = WorkPermitBatch.Plan(_memory, _profile, Module, Records(), 0x1234, 2027, _ => "Testland", _header);
        Assert.Empty(plan.Targets);
        Assert.Equal(0, WorkPermitBatch.Apply(plan, _memory, _profile, Module, AllocateFromArena, null, _header));
        Assert.Equal(before, _memory.Writes);
    }
}

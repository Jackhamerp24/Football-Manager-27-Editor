using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// Transfer data as measured on 27.0.0+2486695: pending contract at player+0x328 with a clause table at
/// +0x60 (int32 value, int16 -1, int16 type), club finance at club+0x150. Values mirror one real deal:
/// clauses 10032/7524/5016 types 32/34/38 and PSG's money after the signing.
/// </summary>
public class TransferDataTests
{
    const ulong Contract = 0x4000_0000, Team = 0x4000_1000, Club = 0x4000_2000, Finance = 0x4000_3000, Pending = 0x4000_4000;
    const uint ContractVtable = 0x4B00AE8, ClubVtable = 0x4CDA1F8;
    readonly TestWorld _w = new();

    public TransferDataTests()
    {
        var c = _w.Memory.Map(Contract, 0x60);
        TestWorld.Set64(c, 0x00, TestWorld.ModuleBase + ContractVtable);
        TestWorld.Set64(c, 0x10, Team);
        BitConverter.GetBytes(19233).CopyTo(c, 0x20);
        TestWorld.Set16(c, 0x48, 182); TestWorld.Set16(c, 0x4A, 2028); // 30 June 2028
        var team = _w.Memory.Map(Team, 0x40);
        TestWorld.Set64(team, 0x30, Club);
        var club = _w.Memory.Map(Club, 0x170);
        TestWorld.Set64(club, 0x00, TestWorld.ModuleBase + ClubVtable);
        TestWorld.Set64(club, 0xC0, 0x4000_5000); _w.Memory.MapString(0x4000_5000, "Paris Saint-Germain");
        TestWorld.Set64(club, 0xC8, 0x4000_5100); _w.Memory.MapString(0x4000_5100, "Paris SG");
        TestWorld.Set64(club, 0x150, Finance);
        var finance = _w.Memory.Map(Finance, 0x820);
        TestWorld.Set64(finance, 0, TestWorld.ModuleBase + 0x4CF0CC8); // full finance object (0x8B0)
        BitConverter.GetBytes(19_868_079).CopyTo(finance, 0x0C);
        BitConverter.GetBytes(2_490_716).CopyTo(finance, 0x4CC);
        BitConverter.GetBytes(23_826_184).CopyTo(finance, 0x7C4);
        BitConverter.GetBytes(3_790_266).CopyTo(finance, 0x814);

        var pending = _w.Memory.Map(Pending, 0x80);
        Clause(pending, 0x60, 10032, 32); Clause(pending, 0x68, 7524, 34); Clause(pending, 0x70, 5016, 38);
        BitConverter.GetBytes(-1).CopyTo(pending, 0x78); TestWorld.Set16(pending, 0x7C, 10);

        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x330, BitConverter.GetBytes(Contract)));
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x328, BitConverter.GetBytes(Pending)));
    }

    static void Clause(byte[] obj, int offset, int value, short type)
    {
        BitConverter.GetBytes(value).CopyTo(obj, offset);
        TestWorld.Set16(obj, offset + 4, -1);
        TestWorld.Set16(obj, offset + 6, type);
    }

    PlayerRecord Mainoo(OffsetProfile? profile = null) =>
        new PlayerRepository(_w.Memory, profile ?? _w.Profile, TestWorld.ModuleBase).Reload(TestWorld.Mainoo)!;

    IReadOnlyList<ContractClause> Clauses(PlayerRecord player) =>
        new PendingContractReader(_w.Memory, _w.Profile).ReadClauses(player.PendingContractAddress);

    [Fact]
    public void Pending_contract_pointer_and_clauses_are_read()
    {
        var player = Mainoo();
        Assert.Equal(Pending, player.PendingContractAddress);
        Assert.Equal(
            [new ContractClause(32, 10032), new ContractClause(34, 7524), new ContractClause(38, 5016)],
            Clauses(player));
    }

    [Fact]
    public void Clause_table_stops_at_the_first_entry_without_the_marker()
    {
        Assert.True(_w.Memory.TryWrite(Pending + 0x6C, BitConverter.GetBytes((short)7)));
        Assert.Equal([new ContractClause(32, 10032)], Clauses(Mainoo()));
    }

    [Fact]
    public void Player_without_pending_pointer_has_no_clauses()
    {
        Assert.True(_w.Memory.TryWrite(TestWorld.Mainoo + 0x328, BitConverter.GetBytes(0UL)));
        var player = Mainoo();
        Assert.Equal(0UL, player.PendingContractAddress);
        Assert.Empty(Clauses(player));
    }

    [Fact]
    public void Finance_reads_the_four_measured_fields()
    {
        var finance = new ClubFinanceReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Club);
        Assert.Equal(19_868_079, finance!.Balance);
        Assert.Equal(23_826_184, finance.TransferBudget);
        Assert.Equal(2_490_716, finance.SpentThisSeason);
        Assert.Equal(3_790_266, finance.WageSpend);
    }

    [Fact]
    public void Finance_is_null_when_the_club_object_is_not_a_club()
    {
        Assert.True(_w.Memory.TryWrite(Club, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1111110)));
        Assert.Null(new ClubFinanceReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Club));
        Assert.Equal(19233, Mainoo().Contract!.Wage);
    }

    [Fact]
    public void Snapshot_captures_player_contract_pending_club_and_finance()
    {
        var player = Mainoo();
        var snapshot = TransferSnapshot.Capture(
            _w.Memory, _w.Profile, TestWorld.ModuleBase, player, new DateOnly(2026, 7, 18),
            new ClubFinanceReader(_w.Memory, _w.Profile, TestWorld.ModuleBase).Read(Club), Clauses(player));

        Assert.Equal(["player", "contract", "pending", "club"], snapshot.Objects.Select(o => o.Kind));
        Assert.Equal(0x3C8, snapshot.Object("player")!.Size);
        Assert.Equal(3, snapshot.Clauses.Count);
        Assert.Equal(19_868_079, snapshot.Finance!.Balance);
        Assert.Equal("2026-07-18", snapshot.GameDate);
        Assert.Equal(19233, snapshot.Wage);
    }

    [Fact]
    public void Snapshot_survives_a_json_round_trip()
    {
        var player = Mainoo();
        var snapshot = TransferSnapshot.Capture(
            _w.Memory, _w.Profile, TestWorld.ModuleBase, player, null, null, Clauses(player));
        var path = Path.Combine(Path.GetTempPath(), $"fm27-snapshot-{Guid.NewGuid():N}.json");
        try
        {
            snapshot.Save(path);
            var loaded = TransferSnapshot.Load(path);
            Assert.Equal(snapshot.Objects.Count, loaded.Objects.Count);
            Assert.Equal(snapshot.Objects[0].Hex, loaded.Objects[0].Hex);
            Assert.Equal(snapshot.Clauses, loaded.Clauses);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Diff_reports_a_moved_pointer_and_a_changed_int()
    {
        var before = Snap(0x1F4_0000_0000, 19233, 19_868_079);
        var after = Snap(0x1F4_0000_1000, 40000, 19_000_000);

        var changes = TransferDiff.Compare(before, after);
        var pointer = Assert.Single(changes, c => c.Offset == 0x28);
        Assert.Equal(0x1F4_0000_0000UL, pointer.PointerBefore);
        Assert.Equal(0x1F4_0000_1000UL, pointer.PointerAfter);
        var wage = Assert.Single(changes, c => c.Offset == 0x20 && c.Width == 4);
        Assert.Equal(19233, wage.IntBefore);
        Assert.Equal(40000, wage.IntAfter);
        Assert.Contains(changes, c => c.Section == "finance" && c.Name == "balance");
    }

    [Fact]
    public void Diff_of_identical_snapshots_is_empty()
    {
        Assert.Empty(TransferDiff.Compare(Snap(0x1F4_0000_0000, 19233, null), Snap(0x1F4_0000_0000, 19233, null)));
    }

    static TransferSnapshot Snap(ulong pending, int wage, int? balance)
    {
        var bytes = new byte[0x30];
        BitConverter.GetBytes(wage).CopyTo(bytes, 0x20);
        BitConverter.GetBytes(pending).CopyTo(bytes, 0x28);
        return new TransferSnapshot
        {
            PlayerName = "Test",
            Objects = [new ObjectDump { Kind = "player", Size = bytes.Length, Hex = Convert.ToHexString(bytes) }],
            Finance = balance is { } value ? new ClubFinance(1, value, null, null, null) : null,
        };
    }
}

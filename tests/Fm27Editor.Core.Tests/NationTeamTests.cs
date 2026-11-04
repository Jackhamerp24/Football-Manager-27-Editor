using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Nations;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// National Teams → Teams panel (FMRTE 27, 3/11/2026). Vietnam's dropdown lists 8 teams (Vietnam, B, U23, Unknown, U21, U20, U19, U18);
/// FMRTE showed Reputation 3,000 for Vietnam, 2,400 for U23 and 1,500 for U19; Brazil 9,200 and England 9,600. RAM (PID 21248) holds
/// the same order at nation+0x108 → vector at +0x18 → team objects, Reputation int32 at +0xA8; the other Vietnam values are the RAM ones.
/// FMRTE saved 3001, 10001 and 30000 and changed only those four bytes.
/// </summary>
public class NationTeamTests
{
    const ulong Vietnam = 0xA000_0000, Brazil = 0xA000_1000, England = 0xA000_2000, NotANation = 0xA000_3000;
    static readonly int[] VietnamReputations = [3000, 2100, 2400, 2250, 2100, 1800, 1500, 1200];

    readonly FakeMemory _m = new();
    readonly TestWorld _w = new();
    ulong _next = 0xA100_0000;

    void Nation(ulong at, params int[] reputations)
    {
        var o = _m.Map(at, 0x300);
        TestWorld.Set64(o, 0, TestWorld.ModuleBase + _w.Profile.Nation!.VtableRva);
        ulong holder = _next; _next += 0x1000;
        TestWorld.Set64(o, 0x108, holder);
        var h = _m.Map(holder, 0x40);
        ulong list = _next; _next += 0x1000;
        TestWorld.Set64(h, 0x18, list); TestWorld.Set64(h, 0x20, list + (ulong)reputations.Length * 8); TestWorld.Set64(h, 0x28, list + (ulong)reputations.Length * 8);
        var l = _m.Map(list, reputations.Length * 8);
        for (int i = 0; i < reputations.Length; i++)
        {
            ulong team = _next; _next += 0x1000;
            TestWorld.Set64(l, i * 8, team);
            var t = _m.Map(team, 0x200);
            TestWorld.Set64(t, 0, TestWorld.ModuleBase + _w.Profile.Nation.Teams!.TeamVtable);
            BitConverter.GetBytes(reputations[i]).CopyTo(t, 0xA8);
            t[0xAC] = 0xEE;    // a neighbouring byte that no write may touch
        }
    }

    NationTeamReader Reader() => new(_m, _w.Profile, TestWorld.ModuleBase);

    [Fact]
    public void Teams_and_reputations_match_FMRTE()
    {
        Nation(Vietnam, VietnamReputations); Nation(Brazil, 9200, 7360); Nation(England, 9600, 7200, 6720);
        var v = Reader().Read(Vietnam);
        Assert.Equal(8, v.Count);                         // FMRTE's Team dropdown lists 8 Vietnam teams
        Assert.Equal(3000, v[0].Reputation);
        Assert.Equal(2400, v[2].Reputation);              // Vietnam U23
        Assert.Equal(1500, v[6].Reputation);              // Vietnam U19
        Assert.Equal(9200, Reader().Read(Brazil)[0].Reputation);
        Assert.Equal(9600, Reader().Read(England)[0].Reputation);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, v.Select(t => t.Index));
    }

    [Fact]
    public void Skipping_invalid_teams_preserves_the_original_positions()
    {
        // FMRTE dropdown: Vietnam is at position 0, U23 at 2, U19 at 6.
        Nation(Vietnam, VietnamReputations);
        ulong holder = _m.ReadUInt64(Vietnam + 0x108);
        ulong list = _m.ReadUInt64(holder + 0x18);
        Assert.True(_m.TryWrite(list, BitConverter.GetBytes(0UL)));
        Assert.True(_m.TryWrite(list + 3 * 8, BitConverter.GetBytes(0UL)));

        var teams = Reader().Read(Vietnam);
        Assert.Equal(new[] { 1, 2, 4, 5, 6, 7 }, teams.Select(t => t.Index));
        Assert.Equal(2400, Assert.Single(teams, t => t.Index == 2).Reputation);
        Assert.Equal(1500, Assert.Single(teams, t => t.Index == 6).Reputation);
    }

    [Fact]
    public void SetReputation_changes_only_the_four_bytes()
    {
        Nation(Vietnam, VietnamReputations);
        var team = Reader().Read(Vietnam)[0];
        var before = _m.Read(team.Address, 0x200);
        Reader().SetReputation(team, 30000);
        var after = _m.Read(team.Address, 0x200);
        Assert.Equal("30750000", Convert.ToHexString(after, 0xA8, 4));
        Array.Copy(before, 0xA8, after, 0xA8, 4);
        Assert.Equal(before, after);
        Assert.Equal(30000, Reader().Read(Vietnam)[0].Reputation);
        Assert.Equal(2100, Reader().Read(Vietnam)[1].Reputation);
    }

    [Fact]
    public void SetReputation_rejects_a_team_removed_from_the_original_slot()
    {
        Nation(Vietnam, VietnamReputations);
        var team = Reader().Read(Vietnam)[0];
        ulong holder = _m.ReadUInt64(Vietnam + 0x108);
        ulong list = _m.ReadUInt64(holder + 0x18);
        ulong replacement = _m.ReadUInt64(list + 8);
        Assert.True(_m.TryWrite(list, BitConverter.GetBytes(replacement)));
        var before = _m.Read(team.Address + 0xA8, 4);
        int writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => Reader().SetReputation(team, 3001));
        Assert.Equal(writes, _m.Writes);
        Assert.Equal(before, _m.Read(team.Address + 0xA8, 4));
    }

    [Fact]
    public void SetReputation_rejects_changed_capacity_and_team_vtable()
    {
        Nation(Vietnam, VietnamReputations);
        var team = Reader().Read(Vietnam)[0];
        ulong holder = _m.ReadUInt64(Vietnam + 0x108);
        ulong oldCapacity = _m.ReadUInt64(holder + 0x28);
        Assert.True(_m.TryWrite(holder + 0x28, BitConverter.GetBytes(oldCapacity + 8)));
        int writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => Reader().SetReputation(team, 3001));
        Assert.Equal(writes, _m.Writes);
        Assert.True(_m.TryWrite(holder + 0x28, BitConverter.GetBytes(oldCapacity)));
        Assert.True(_m.TryWrite(team.Address, BitConverter.GetBytes(TestWorld.ModuleBase + 0x1234560UL)));
        writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => Reader().SetReputation(team, 3001));
        Assert.Equal(writes, _m.Writes);
    }

    [Fact]
    public void Guards_refuse_out_of_range_stale_and_non_team_without_writing()
    {
        Nation(Vietnam, VietnamReputations);
        var team = Reader().Read(Vietnam)[0];
        int writes = _m.Writes;
        Assert.Throws<ArgumentOutOfRangeException>(() => Reader().SetReputation(team, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Reader().SetReputation(team, 30001));
        Assert.Equal(writes, _m.Writes);

        Assert.True(_m.TryWrite(team.Address + 0xA8, BitConverter.GetBytes(3001)));
        writes = _m.Writes;
        Assert.Throws<StaleObjectException>(() => Reader().SetReputation(team, 2000));
        Assert.Equal(writes, _m.Writes);

        var notTeam = _m.Map(0xA200_0000, 0x200);
        BitConverter.GetBytes(team.Reputation).CopyTo(notTeam, 0xA8);   // same value as the snapshot, but not the original slot object
        Assert.Throws<StaleObjectException>(() => Reader().SetReputation(team with { Address = 0xA200_0000 }, 2000));
        Assert.Equal(writes, _m.Writes);
    }

    [Fact]
    public void A_non_nation_or_missing_holder_reads_as_no_teams()
    {
        _m.Map(NotANation, 0x300);
        Assert.Empty(Reader().Read(NotANation));
        Assert.Empty(Reader().Read(0));
    }
}

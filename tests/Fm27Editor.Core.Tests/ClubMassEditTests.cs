using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Tests;

public class ClubMassEditTests
{
    [Fact]
    public void Balance_batch_restores_each_original_and_rejects_replaced_finance_object()
    {
        var w = new TestWorld();
        const ulong sheffield = 0x53000000, oxford = 0x53001000, fin1 = 0x53020000, fin2 = 0x53021000, replacement = 0x53022000;
        void MapClub(ulong address, int id, ulong finance)
        {
            var club = w.Memory.Map(address, 0x170);
            TestWorld.Set64(club, 0, TestWorld.ModuleBase + 0x4CDA1F8);
            BitConverter.GetBytes(id).CopyTo(club, 0xC);
            TestWorld.Set64(club, 0x150, finance);
        }
        void MapFinance(ulong address, int balance)
        {
            var bytes = w.Memory.Map(address, 0x8B0);
            TestWorld.Set64(bytes, 0, TestWorld.ModuleBase + 0x4CF0CC8);
            BitConverter.GetBytes(balance).CopyTo(bytes, 0xC);
        }
        // Balance values recorded from FMRTE: Sheffield 5,790,291; Oxford -31,697.
        MapClub(sheffield, 708, fin1); MapClub(oxford, 695, fin2);
        MapFinance(fin1, 5790291); MapFinance(fin2, -31697); MapFinance(replacement, 5790291);
        var clubs = new[] { new ClubEntry(sheffield, 708, "Sheffield", null, fin1, []), new ClubEntry(oxford, 695, "Oxford", null, fin2, []) };
        var plan = ClubMassEdit.Plan(clubs, ClubField.Balance, MassEditMode.Change, 100, int.MinValue, int.MaxValue, w.Memory, w.Profile, TestWorld.ModuleBase);
        Assert.Equal(new[] { (5790291L, 5790391L), (-31697L, -31597L) }, plan.Select(c => (c.Before, c.After)));
        foreach (var change in plan) ClubMassEdit.Apply(change, w.Memory, w.Profile, TestWorld.ModuleBase);
        Assert.Equal(5790391, BitConverter.ToInt32(w.Bytes(fin1 + 0xC, 4)));
        Assert.Equal(-31597, BitConverter.ToInt32(w.Bytes(fin2 + 0xC, 4)));
        foreach (var change in plan.Reverse()) ClubMassEdit.Undo(change, w.Memory, w.Profile, TestWorld.ModuleBase);
        Assert.Equal(5790291, BitConverter.ToInt32(w.Bytes(fin1 + 0xC, 4)));
        Assert.Equal(-31697, BitConverter.ToInt32(w.Bytes(fin2 + 0xC, 4)));
        Assert.True(w.Memory.TryWrite(sheffield + 0x150, BitConverter.GetBytes(replacement)));
        int writes = w.Memory.Writes;
        Assert.Throws<StaleObjectException>(() => ClubMassEdit.Apply(plan[0], w.Memory, w.Profile, TestWorld.ModuleBase));
        Assert.Equal(writes, w.Memory.Writes);
    }
}

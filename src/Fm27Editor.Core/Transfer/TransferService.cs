using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Transfer;

public sealed record PlannedWrite(ulong Address, byte[] Expected, byte[] Value, string What);

/// <summary>
/// Everything a move changes, computed up front. Nothing is written until <see cref="TransferService.Apply"/>,
/// which re-checks every expected value first.
/// </summary>
public sealed record TransferPlan(
    PlayerRecord Player, ClubEntry Seller, ClubEntry Buyer, ulong FromTeam, TeamEntry ToTeam, int Fee, int Wage,
    IReadOnlyList<PlannedWrite> Writes, IReadOnlyList<ulong>? NewSquad)
{
    /// <summary>The destination squad vector is full and needs a new buffer outside the game's allocator.</summary>
    public bool NeedsAllocation => NewSquad is not null;
}

/// <summary>
/// A permanent transfer, replicating what FM26's In-Game Editor "Move to other club" changes (measured with
/// before/after snapshots, then replayed on FM26 by hand and checked in game):
/// <list type="bullet">
/// <item>buyer: balance −fee, transfer spend this month +fee, spent this season +fee, transfer budget −fee (not below 0), wage bill +wage;</item>
/// <item>seller: balance +fee, players sold +fee, transfer budget +fee, wage bill −wage;</item>
/// <item>contract team and playing team → the buyer's first team, or a buyer team with room in its squad;</item>
/// <item>removed from the old team's squad vector, appended to the new one.</item>
/// </list>
/// Not done (IGE does): a new contract object, transfer history, news, solidarity/sell-on payments to third clubs.
/// The existing contract keeps its terms; change them afterwards with <see cref="ContractWriter"/>.
/// </summary>
public sealed class TransferService(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    ContractOffsets C => profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
    TeamOffsets T => profile.Team ?? throw new InvalidOperationException("Profile has no team offsets");
    ClubFinanceOffsets F => profile.Club?.Finance ?? throw new InvalidOperationException("Profile has no club finance offsets");

    /// <param name="intoTeam">Destination team; default is the buyer's first team when it has room, else the first buyer team with room.</param>
    public TransferPlan Plan(PlayerRecord player, IReadOnlyList<ClubEntry> clubs, ClubEntry buyer, int fee, TeamEntry? intoTeam = null)
    {
        if (fee < 0) throw new ArgumentOutOfRangeException(nameof(fee));
        if (!profile.PlayerVtableRvas.Any(v => moduleBase + v == memory.ReadUInt64(player.Address)))
            throw new StaleObjectException($"0x{player.Address:X} is no longer a player object");
        var contract = player.Contract ?? throw new InvalidOperationException("The player has no contract (free agent)");
        if (memory.ReadUInt64(player.Address + (ulong)C.PlayerContract) != contract.Address)
            throw new StaleObjectException("The player's contract changed since it was read");
        ulong contractTeam = memory.ReadUInt64(contract.Address + (ulong)C.Team);
        ulong fromTeam = memory.ReadUInt64(player.Address + (ulong)T.PlayerTeam);
        var seller = clubs.FirstOrDefault(c => c.Teams.Any(t => t.Address == contractTeam))
                     ?? throw new InvalidOperationException("Seller club not found for the contract team");
        if (seller.Address == buyer.Address) throw new InvalidOperationException("Player already belongs to that club");
        var firstTeam = buyer.FirstTeam ?? throw new InvalidOperationException($"{buyer.DisplayName} has no first team");
        var to = intoTeam ?? (firstTeam.HasRoom ? firstTeam : buyer.Teams.FirstOrDefault(t => t.HasRoom) ?? firstTeam);
        if (to.Club != buyer.Address) throw new ArgumentException("Team does not belong to the buyer", nameof(intoTeam));

        var writes = new List<PlannedWrite>();
        int wage = contract.Wage;
        AddMoney(writes, buyer, F.Balance, -fee, "buyer balance");
        AddMoney(writes, buyer, F.TransferSpendMonth, fee, "buyer transfer spend this month");
        AddMoney(writes, buyer, F.SpentThisSeason, fee, "buyer spent this season");
        AddMoney(writes, buyer, F.TransferBudget, -fee, "buyer transfer budget", floorAtZero: true);
        AddMoney(writes, buyer, F.WageBill, wage, "buyer wage bill");
        AddMoney(writes, seller, F.Balance, fee, "seller balance");
        AddMoney(writes, seller, F.PlayersSold, fee, "seller players sold");
        AddMoney(writes, seller, F.TransferBudget, fee, "seller transfer budget");
        AddMoney(writes, seller, F.WageBill, -wage, "seller wage bill");
        writes.Add(Pointer(contract.Address + (ulong)C.Team, firstTeam.Address, "contract team"));
        writes.Add(Pointer(player.Address + (ulong)T.PlayerTeam, to.Address, "playing team"));

        // Old squad: shift the tail left over the player and move end back by one.
        ulong squad = fromTeam + (ulong)T.Squad;
        ulong begin = memory.ReadUInt64(squad), end = memory.ReadUInt64(squad + 8);
        var old = memory.Read(begin, (int)(end - begin));
        int index = Enumerable.Range(0, old.Length / 8).FirstOrDefault(i => BitConverter.ToUInt64(old, i * 8) == player.Address, -1);
        if (index < 0) throw new StaleObjectException("Player is not in his team's squad");
        for (int i = index; i + 1 < old.Length / 8; i++)
            writes.Add(new PlannedWrite(begin + (ulong)i * 8, old[(i * 8)..(i * 8 + 8)], old[((i + 1) * 8)..((i + 2) * 8)], "old squad shift"));
        writes.Add(Pointer(squad + 8, end - 8, "old squad end"));

        // New squad: append in place when there is room, otherwise Apply allocates a bigger buffer.
        ulong toSquad = to.Address + (ulong)T.Squad;
        ulong tBegin = memory.ReadUInt64(toSquad), tEnd = memory.ReadUInt64(toSquad + 8), tCap = memory.ReadUInt64(toSquad + 16);
        IReadOnlyList<ulong>? newSquad = null;
        if (tEnd < tCap)
        {
            writes.Add(Pointer(tEnd, player.Address, "new squad slot"));
            writes.Add(Pointer(toSquad + 8, tEnd + 8, "new squad end"));
        }
        else
        {
            var items = memory.Read(tBegin, (int)(tEnd - tBegin));
            newSquad = [.. Enumerable.Range(0, items.Length / 8).Select(i => BitConverter.ToUInt64(items, i * 8)), player.Address];
        }
        return new TransferPlan(player, seller, buyer, fromTeam, to, fee, wage, writes, newSquad);
    }

    /// <summary>
    /// Checks every expected value, then writes. <paramref name="allocate"/> is required when the plan
    /// <see cref="TransferPlan.NeedsAllocation"/> (for the live game: <see cref="WindowsProcessMemory.Allocate"/>).
    /// </summary>
    public void Apply(TransferPlan plan, Func<int, ulong>? allocate = null)
    {
        foreach (var w in plan.Writes)
            if (!memory.Read(w.Address, w.Expected.Length).AsSpan().SequenceEqual(w.Expected))
                throw new StaleObjectException($"{w.What} changed since the plan was made; nothing written");
        var writes = plan.Writes.ToList();
        if (plan.NewSquad is { } items)
        {
            if (allocate is null) throw new InvalidOperationException("The destination squad is full; an allocator is needed");
            ulong toSquad = plan.ToTeam.Address + (ulong)T.Squad;
            var oldPointers = memory.Read(toSquad, 24);
            int capacity = items.Count * 2;
            ulong buffer = allocate(capacity * 8);
            var bytes = items.SelectMany(BitConverter.GetBytes).ToArray();
            writes.Add(new PlannedWrite(buffer, memory.Read(buffer, bytes.Length), bytes, "new squad buffer"));
            writes.Add(new PlannedWrite(toSquad, oldPointers,
                [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)bytes.Length), .. BitConverter.GetBytes(buffer + (ulong)capacity * 8)],
                "new squad begin/end/capacity"));
        }
        foreach (var w in writes)
        {
            if (!memory.Read(w.Address, w.Expected.Length).AsSpan().SequenceEqual(w.Expected))
                throw new StaleObjectException($"{w.What} changed while writing");
            if (!memory.TryWrite(w.Address, w.Value) || !memory.Read(w.Address, w.Value.Length).AsSpan().SequenceEqual(w.Value))
                throw new MemoryAccessException($"Write failed: {w.What}");
        }
    }

    void AddMoney(List<PlannedWrite> writes, ClubEntry club, int? offset, long delta, string what, bool floorAtZero = false)
    {
        if (offset is not { } o) throw new InvalidOperationException($"Profile has no offset for {what}");
        if (club.Finance == 0) throw new InvalidOperationException($"{club.DisplayName} has no finance object");
        // Small clubs keep only balance and embargo; their budget/wage totals do not exist and must not be written.
        uint vt = (uint)(memory.ReadUInt64(club.Finance) - moduleBase);
        switch (F.Holds(vt, o, 4))
        {
            case null: throw new StaleObjectException($"{club.DisplayName}'s finance object has an unknown layout");
            case false: return;
        }
        ulong a = club.Finance + (ulong)o;
        var current = memory.Read(a, 4);
        long v = BitConverter.ToInt32(current) + delta;
        if (floorAtZero && v < 0) v = 0;
        if (v < int.MinValue || v > int.MaxValue) throw new OverflowException($"{what} would overflow int32");
        writes.Add(new PlannedWrite(a, current, BitConverter.GetBytes((int)v), what));
    }

    PlannedWrite Pointer(ulong address, ulong value, string what) =>
        new(address, memory.Read(address, 8), BitConverter.GetBytes(value), what);
}

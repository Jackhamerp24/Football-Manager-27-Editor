using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Transfer;

public sealed record FreeAgentSigningPlan(PlayerRecord Player, ClubEntry Buyer, TeamEntry ToTeam, ulong ContractTeam,
    byte[] Contract, DateOnly Today, IReadOnlyList<PlannedWrite> Guards, IReadOnlyList<PlannedWrite> Writes, IReadOnlyList<ulong>? NewSquad)
{
    public int Wage => BitConverter.ToInt32(Contract, 0x20);
    public bool NeedsAllocation => NewSquad is not null;
}

/// <summary>
/// Signs a free agent at once, as FMRTE 27 build12's Move to club with "Move now" does (traced on Steam 27.0.1+2503222,
/// Lamine Guèye to Man City, 14/11/2026). There is no contract to move, so FMRTE allocates a 200-byte contract that is a
/// <b>copy of the contract of the first player in the destination squad</b> (same wage, expiry and every other byte), then
/// overwrites person +0x08 (player + 0x288), teams +0x10/+0x18, and the start and signed dates +0x44/+0x4C (today, no flag
/// bits). On the player it writes the contract pointer, playing team +0x130, parent team +0x138 and the two joined dates
/// +0x3A4/+0x3A8 (today), and appends him to the squad. No fee and no club finances are touched.
/// </summary>
public sealed class FreeAgentSigning(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int ContractSize = 200, StartDate = 0x44, SignedDate = 0x4C;
    const int MaxSquad = 200;

    ContractOffsets C => profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
    TeamOffsets T => profile.Team ?? throw new InvalidOperationException("Profile has no team offsets");

    public FreeAgentSigningPlan Plan(PlayerRecord player, ClubEntry buyer, DateOnly today, TeamEntry? intoTeam = null)
    {
        var guards = new List<PlannedWrite>();
        byte[] Guard(ulong at, int length, string what) { var b = memory.Read(at, length); guards.Add(new(at, b, [], what)); return b; }
        ulong Pointer(ulong at, string what) => BitConverter.ToUInt64(Guard(at, 8, what));

        ulong vtable = Pointer(player.Address, "player vtable");
        if (!profile.PlayerVtableRvas.Any(v => moduleBase + v == vtable)) throw new StaleObjectException($"0x{player.Address:X} is no longer a player object");
        GenderGuard.EnsureMalePlayer(memory, profile, player.Address);
        GenderGuard.EnsureMenClub(memory, profile, buyer.Address);
        if (Pointer(player.Address + (ulong)C.PlayerContract, "player contract") != 0)
            throw new InvalidOperationException("The player has a contract; use the normal transfer");
        if (profile.FutureTransfers?.CurrentLoanHolder is { } loan && Pointer(player.Address + (ulong)loan, "loan holder") != 0)
            throw new InvalidOperationException("The player is on loan");

        var firstTeam = buyer.FirstTeam ?? throw new InvalidOperationException($"{buyer.DisplayName} has no first team");
        var to = intoTeam ?? firstTeam;
        if (to.Club != buyer.Address) throw new ArgumentException("Team does not belong to the buyer", nameof(intoTeam));
        foreach (var team in new[] { firstTeam.Address, to.Address }.Distinct())
            if (Pointer(team, "team vtable") != moduleBase + T.VtableRva) throw new StaleObjectException("Team changed");

        // The contract FMRTE copies: the first player in the destination squad.
        var squadHeader = Guard(to.Address + (ulong)T.Squad, 24, "squad header");
        ulong begin = BitConverter.ToUInt64(squadHeader, 0), end = BitConverter.ToUInt64(squadHeader, 8), cap = BitConverter.ToUInt64(squadHeader, 16);
        if (begin == 0 || end < begin || cap < end || (end - begin) % 8 != 0 || (cap - begin) / 8 > MaxSquad)
            throw new StaleObjectException("Invalid squad vector");
        if (end == begin) throw new InvalidOperationException("The destination squad is empty, so there is no contract to copy");
        var squad = Guard(begin, checked((int)(end - begin)), "squad");
        var members = Enumerable.Range(0, squad.Length / 8).Select(i => BitConverter.ToUInt64(squad, i * 8)).ToList();
        if (members.Contains(player.Address)) throw new InvalidOperationException("The player is already in that squad");
        ulong first = members[0];
        if (!profile.PlayerVtableRvas.Any(v => moduleBase + v == Pointer(first, "template player vtable")))
            throw new StaleObjectException("The first squad member is not a player");
        ulong source = Pointer(first + (ulong)C.PlayerContract, "template contract pointer");
        if (source == 0) throw new InvalidOperationException("The first player in that squad has no contract to copy");
        var contract = (byte[])Guard(source, ContractSize, "template contract").Clone();   // the guard keeps the original bytes
        if (BitConverter.ToUInt64(contract) != moduleBase + C.ContractVtableRva) throw new StaleObjectException("Template contract changed");
        if (BitConverter.ToUInt64(contract, C.Team) != firstTeam.Address)
            throw new InvalidOperationException("The first player in that squad is under contract elsewhere; his contract can't be copied");

        int seniorAt = C.SeniorTeam ?? throw new InvalidOperationException("Profile has no contract senior team offset");
        int joinedAt = C.PlayerJoinedClub ?? throw new InvalidOperationException("Profile has no joined club offset");
        ulong person = player.Address + (ulong)LoanCreator.Person;
        BitConverter.GetBytes(person).CopyTo(contract, 0x08);
        BitConverter.GetBytes(firstTeam.Address).CopyTo(contract, C.Team);
        BitConverter.GetBytes(firstTeam.Address).CopyTo(contract, seniorAt);
        Date(today).CopyTo(contract, StartDate);
        Date(today).CopyTo(contract, SignedDate);

        var writes = new List<PlannedWrite>
        {
            Write(player.Address + (ulong)T.PlayerTeam, BitConverter.GetBytes(to.Address), "playing team"),
            Write(player.Address + LoanCreator.PlayerParentTeam, BitConverter.GetBytes(firstTeam.Address), "parent team"),
            Write(player.Address + (ulong)joinedAt, Date(today), "joined club"),
            Write(player.Address + LoanCreator.PlayerJoinedClubSecond, Date(today), "joined date"),
        };
        IReadOnlyList<ulong>? newSquad = null;
        if (end < cap)
        {
            writes.Add(Write(end, BitConverter.GetBytes(player.Address), "new squad slot"));
            writes.Add(Write(to.Address + (ulong)T.Squad + 8, BitConverter.GetBytes(end + 8), "new squad end"));
        }
        else newSquad = [.. members, player.Address];
        return new(player, buyer, to, firstTeam.Address, contract, today, guards, writes, newSquad);
    }

    /// <summary>Checks every snapshot, allocates the contract (and a bigger squad buffer when full), then writes.</summary>
    public void Apply(FreeAgentSigningPlan plan, Func<int, ulong> allocate)
    {
        foreach (var g in plan.Guards)
            if (!memory.Read(g.Address, g.Expected.Length).AsSpan().SequenceEqual(g.Expected))
                throw new StaleObjectException($"{g.What} changed since the plan was made; nothing written");
        foreach (var w in plan.Writes)
            if (!memory.Read(w.Address, w.Expected.Length).AsSpan().SequenceEqual(w.Expected))
                throw new StaleObjectException($"{w.What} changed since the plan was made; nothing written");
        var writes = new List<PlannedWrite>();
        ulong contract = allocate(ContractSize);
        if (contract == 0) throw new MemoryAccessException("Allocation failed: contract");
        var blank = memory.Read(contract, ContractSize);
        if (blank.Any(b => b != 0)) throw new MemoryAccessException("Allocation was not zeroed: contract");
        writes.Add(new(contract, blank, plan.Contract, "new contract"));
        if (plan.NewSquad is { } items)
        {
            ulong header = plan.ToTeam.Address + (ulong)T.Squad;
            int capacity = items.Count * 2;
            ulong buffer = allocate(capacity * 8);
            if (buffer == 0) throw new MemoryAccessException("Allocation failed: squad");
            var bytes = items.SelectMany(BitConverter.GetBytes).ToArray();
            writes.Add(new(buffer, memory.Read(buffer, bytes.Length), bytes, "new squad buffer"));
            writes.Add(new(header, memory.Read(header, 24),
                [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)bytes.Length), .. BitConverter.GetBytes(buffer + (ulong)capacity * 8)],
                "new squad begin/end/capacity"));
        }
        writes.AddRange(plan.Writes);
        // The contract pointer goes last: until then the player is still a free agent to the game.
        writes.Add(new(plan.Player.Address + (ulong)C.PlayerContract, new byte[8], BitConverter.GetBytes(contract), "player contract"));
        foreach (var w in writes)
        {
            if (!memory.Read(w.Address, w.Expected.Length).AsSpan().SequenceEqual(w.Expected))
                throw new StaleObjectException($"{w.What} changed while writing");
            if (!memory.TryWrite(w.Address, w.Value) || !memory.Read(w.Address, w.Value.Length).AsSpan().SequenceEqual(w.Value))
                throw new MemoryAccessException($"Write failed: {w.What}");
        }
    }

    PlannedWrite Write(ulong address, byte[] value, string what) => new(address, memory.Read(address, value.Length), value, what);

    /// <summary>Day of year in the low 9 bits with no flag bits, then the year, as FMRTE wrote it.</summary>
    static byte[] Date(DateOnly d) => [.. BitConverter.GetBytes((ushort)d.DayOfYear), .. BitConverter.GetBytes((short)d.Year)];
}

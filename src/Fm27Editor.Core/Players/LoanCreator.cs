using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Players;

/// <summary>
/// What a new loan agrees. <see cref="Wage"/> is the weekly wage the player earns while on loan (the borrowing club's
/// share). <see cref="AgreedPrice"/> and <see cref="BuyBackFee"/> use null or 0 for "none", which the game stores as -1
/// (FMRTE wrote -1 for both when its fields were £0 and the typed amount otherwise).
/// <see cref="PurchaseAfterMatches"/> 0 means none and is stored as 0xFE, the same way.
/// </summary>
public sealed record LoanOffer(int Wage, DateOnly Start, DateOnly Expires, int? AgreedPrice = null, int? BuyBackFee = null,
    int MonthlyFee = 0, int PurchaseAfterMatches = 0, bool CanBeRecalled = false);

/// <summary>Everything a new loan changes; nothing is written until <see cref="LoanCreator.Apply"/>.</summary>
public sealed record LoanCreatePlan(PlayerRecord Player, ulong ParentTeam, ulong BorrowingTeam, ulong TemplateContract,
    bool TemplateFromBorrowingTeam, byte[] Contract, IReadOnlyList<PlannedWrite> Writes, IReadOnlyList<ulong>? NewSquad)
{
    /// <summary>The borrowing squad vector is full and needs a new buffer outside the game's allocator.</summary>
    public bool NeedsAllocation => NewSquad is not null;
}

/// <summary>
/// Loans a player out to another club, the way FMRTE 27's Move → Loan does. Every step below was captured from FMRTE's
/// <c>WriteProcessMemory</c> calls on three players (Diego León → Sheffield United, Matheus Cunha → Tottenham Hotspur,
/// Phil Foden → Chelsea) and is replayed here:
/// <list type="bullet">
/// <item>A new 0xD4+ byte loan contract (vtable <see cref="FutureTransferOffsets.LoanContractVtable"/>, 0x100 bytes
/// allocated) is a <b>copy of an existing loan contract</b>: the first loan in the game's player order whose borrowing
/// team (+0x10) is the destination, else the first loan in the game. Sheffield took Kalvin Phillips' contract,
/// Tottenham Mudryk's, and Chelsea (no loan at all) Thelo Aasgaard's, the first loan of the save. FMRTE then overwrites
/// person +0x08 (player + 0x288), teams +0x10/+0x18, the three dates (+0x44 start, +0x48 expiry, +0x4C signed), wage
/// +0x20 and +0xD4, agreed price +0xC0 (-1 for none), buy-back +0xC8 (-1 for none), monthly fee +0xCC and +0xD0,
/// purchase-after-matches +0xF7 (0xFE for none), flags +0xB0 (0x02 = plays in cup matches, 0x04 = can be recalled,
/// observed 0x02 and 0x06) and +0xB4 = 0. Bytes it does not touch (+0x28, +0x54..+0x60, +0xE0..+0xFB) stay as the
/// template had them; their meaning is not known.</item>
/// <item>A 0x20 byte holder whose first qword is the contract; <c>player + 0x338</c> points at the holder.</item>
/// <item>Player playing team +0x130 = borrowing team, +0x138 = the contract (parent) team, and the two dates
/// +0x3A4/+0x3A8 = the loan start.</item>
/// <item>The borrowing team's squad vector gets the player. The parent squad vector, the player's own contract and club
/// finances are <b>not</b> touched (the game recomputes wages on Continue).</item>
/// </list>
/// Dates are written as FMRTE did: day of year in the low 9 bits, no flag bits, then the year.
/// </summary>
public sealed class LoanCreator(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int ContractSize = 0x100, HolderSize = 0x20;
    public const int PlayerParentTeam = 0x138, PlayerJoinedClubSecond = 0x3A8, Person = 0x288;
    public const int ContractPerson = 0x08, ContractTeam = 0x10, ContractSeniorTeam = 0x18, Flags = 0xB0, FlagsExtra = 0xB4,
        PurchaseAfterMatchesByte = 0xF7, WageCopy = 0xD4;
    public const int PlaysInCupMatches = 0x02, CanBeRecalledFlag = 0x04;
    const int MaxSquad = 200;

    ContractOffsets C => profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
    TeamOffsets T => profile.Team ?? throw new InvalidOperationException("Profile has no team offsets");
    FutureTransferOffsets F => profile.FutureTransfers is { CurrentLoanHolder: not null } f ? f : throw new InvalidOperationException("Profile has no loan offsets");

    /// <summary>The weekly wage for a share of the player's contract wage (truncated; FMRTE's rounding is unmeasured).</summary>
    public static int WageFromPercent(int contractWage, int percent) => (int)((long)contractWage * percent / 100);

    /// <summary>
    /// Finds the loan contract FMRTE would copy: the first in <paramref name="players"/> whose borrowing team is
    /// <paramref name="borrowingTeam"/>, otherwise the first loan contract of any team. Null when the game has no loan at all.
    /// </summary>
    public (ulong Contract, bool SameTeam)? FindTemplate(IEnumerable<PlayerRecord> players, ulong borrowingTeam)
    {
        ulong first = 0;
        int slot = F.CurrentLoanHolder!.Value;
        foreach (var p in players)
        {
            if (!Q(p.Address + (ulong)slot, out var holder) || !Q(holder, out var contract)) continue;
            if (!Q(contract, out var vtable) || vtable != moduleBase + F.LoanContractVtable) continue;
            if (first == 0) first = contract;
            if (Q(contract + ContractTeam, out var team) && team == borrowingTeam) return (contract, true);
        }
        return first == 0 ? null : (first, false);
    }

    /// <summary>
    /// Plans a loan of <paramref name="player"/> to <paramref name="borrowingTeam"/> (a team object address).
    /// <paramref name="players"/> is only searched for a template loan contract.
    /// </summary>
    public LoanCreatePlan Plan(PlayerRecord player, ulong borrowingTeam, LoanOffer offer, IEnumerable<PlayerRecord> players)
    {
        if (offer.Wage < 0) throw new ArgumentOutOfRangeException(nameof(offer), "Wages cannot be negative");
        if (offer.MonthlyFee < 0) throw new ArgumentOutOfRangeException(nameof(offer), "The monthly fee cannot be negative");
        if (offer.AgreedPrice < 0 || offer.BuyBackFee < 0) throw new ArgumentOutOfRangeException(nameof(offer), "Prices cannot be negative");
        if (offer.PurchaseAfterMatches is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(offer), "Purchase after matches must be 0-100");
        if (offer.Expires < offer.Start) throw new ArgumentOutOfRangeException(nameof(offer), "The loan cannot end before it starts");
        if (!IsPlayer(player.Address)) throw new StaleObjectException($"0x{player.Address:X} is no longer a player object");
        if (!IsTeam(borrowingTeam)) throw new StaleObjectException("The destination is not a team object");
        if (!Q(player.Address + (ulong)C.PlayerContract, out var contract) ||
            !Q(contract, out var contractVtable) || contractVtable != moduleBase + C.ContractVtableRva)
            throw new StaleObjectException("The player's contract is missing or is no longer a contract object");
        if (!Q(contract + (ulong)C.Team, out var parent) || !IsTeam(parent))
            throw new StaleObjectException("The contract team is missing or is no longer a team object");
        if (parent == borrowingTeam) throw new InvalidOperationException("The player is contracted to that team; a loan needs another team");
        if (!Q(player.Address + (ulong)T.PlayerTeam, out var playing) || !IsTeam(playing))
            throw new StaleObjectException("The playing team is missing or is no longer a team object");
        ulong holderSlot = player.Address + (ulong)F.CurrentLoanHolder!.Value;
        if (memory.ReadUInt64(holderSlot) != 0)
            throw new InvalidOperationException("The player already has a loan (or a loan record); recall it first");

        var template = FindTemplate(players, borrowingTeam) ?? throw new InvalidOperationException("The game has no loan contract to copy as a template");
        var bytes = memory.Read(template.Contract, ContractSize);
        Put64(bytes, 0, moduleBase + F.LoanContractVtable);
        Put64(bytes, ContractPerson, player.Address + Person);
        Put64(bytes, ContractTeam, borrowingTeam);
        Put64(bytes, ContractSeniorTeam, borrowingTeam);
        Put32(bytes, F.Wage, offer.Wage);
        Put32(bytes, WageCopy, offer.Wage);
        Date(bytes, F.Started, offer.Start);
        Date(bytes, F.Expires, offer.Expires);
        Date(bytes, LoanEditor.SignedDate, offer.Start);
        Put32(bytes, LoanEditor.AgreedPrice, offer.AgreedPrice is > 0 ? offer.AgreedPrice.Value : -1);
        Put32(bytes, LoanEditor.BuyBackFee, offer.BuyBackFee is > 0 ? offer.BuyBackFee.Value : -1);
        Put32(bytes, LoanEditor.PlayingMonthlyFee, offer.MonthlyFee);
        Put32(bytes, LoanEditor.UnusedMonthlyFee, offer.MonthlyFee);
        bytes[PurchaseAfterMatchesByte] = offer.PurchaseAfterMatches == 0 ? (byte)0xFE : (byte)offer.PurchaseAfterMatches;
        Put32(bytes, Flags, PlaysInCupMatches | (offer.CanBeRecalled ? CanBeRecalledFlag : 0));
        Put32(bytes, FlagsExtra, 0);

        var writes = new List<PlannedWrite>();
        IReadOnlyList<ulong>? newSquad = null;
        ulong squad = borrowingTeam + (ulong)T.Squad;
        if (!Q(squad, out var begin) || !Q(squad + 8, out var end) || !Q(squad + 16, out var capacity) ||
            end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxSquad)
            throw new StaleObjectException("The borrowing team's squad list is unreadable");
        var items = memory.Read(begin, (int)(end - begin));
        var current = Enumerable.Range(0, items.Length / 8).Select(i => BitConverter.ToUInt64(items, i * 8)).ToList();
        if (!current.Contains(player.Address))
        {
            if (end < capacity)
            {
                writes.Add(new PlannedWrite(end, memory.Read(end, 8), BitConverter.GetBytes(player.Address), "borrowing squad slot"));
                writes.Add(new PlannedWrite(squad + 8, memory.Read(squad + 8, 8), BitConverter.GetBytes(end + 8), "borrowing squad end"));
            }
            else newSquad = [.. current, player.Address];
        }
        writes.Add(new PlannedWrite(player.Address + (ulong)T.PlayerTeam, memory.Read(player.Address + (ulong)T.PlayerTeam, 8), BitConverter.GetBytes(borrowingTeam), "playing team"));
        writes.Add(new PlannedWrite(player.Address + PlayerParentTeam, memory.Read(player.Address + PlayerParentTeam, 8), BitConverter.GetBytes(parent), "parent team"));
        foreach (var at in new[] { C.PlayerJoinedClub, PlayerJoinedClubSecond })
        {
            var now = memory.Read(player.Address + (ulong)at, 4);
            var next = (byte[])now.Clone();
            Date(next, 0, offer.Start);
            writes.Add(new PlannedWrite(player.Address + (ulong)at, now, next, $"joined club date 0x{at:X}"));
        }
        // The pointer that makes the loan visible goes last, so a failure before it leaves the player as he was.
        writes.Add(new PlannedWrite(holderSlot, memory.Read(holderSlot, 8), [], "loan holder"));
        return new LoanCreatePlan(player, parent, borrowingTeam, template.Contract, template.SameTeam, bytes, writes, newSquad);
    }

    /// <summary>
    /// Re-checks every expected value, allocates the holder, the contract and (when the squad is full) a squad buffer
    /// through <paramref name="allocate"/>, writes them, then publishes the pointers. A failed write puts back the
    /// bytes already replaced.
    /// </summary>
    public void Apply(LoanCreatePlan plan, Func<int, ulong> allocate)
    {
        if (!IsPlayer(plan.Player.Address)) throw new StaleObjectException("The player object changed since the plan was made");
        foreach (var w in plan.Writes)
            if (w.Value.Length > 0 && !memory.Read(w.Address, w.Expected.Length).AsSpan().SequenceEqual(w.Expected))
                throw new StaleObjectException($"{w.What} changed since the plan was made; nothing written");
        var slot = plan.Writes[^1];
        if (!memory.Read(slot.Address, 8).AsSpan().SequenceEqual(slot.Expected))
            throw new StaleObjectException("The loan holder changed since the plan was made; nothing written");

        ulong holder = allocate(HolderSize), contract = allocate(ContractSize);
        var writes = new List<PlannedWrite>
        {
            new(holder, memory.Read(holder, 8), BitConverter.GetBytes(contract), "holder"),
            new(contract, memory.Read(contract, ContractSize), plan.Contract, "loan contract"),
        };
        if (plan.NewSquad is { } items)
        {
            ulong squad = plan.BorrowingTeam + (ulong)T.Squad;
            int capacity = items.Count + 8;
            ulong buffer = allocate(capacity * 8);
            var data = items.SelectMany(BitConverter.GetBytes).ToArray();
            writes.Add(new PlannedWrite(buffer, memory.Read(buffer, data.Length), data, "new borrowing squad buffer"));
            writes.Add(new PlannedWrite(squad, memory.Read(squad, 24),
                [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)data.Length), .. BitConverter.GetBytes(buffer + (ulong)capacity * 8)],
                "new borrowing squad begin/end/capacity"));
        }
        writes.AddRange(plan.Writes.Take(plan.Writes.Count - 1));
        writes.Add(slot with { Value = BitConverter.GetBytes(holder) });

        var done = new List<PlannedWrite>();
        try
        {
            foreach (var w in writes)
            {
                if (!memory.Read(w.Address, w.Expected.Length).AsSpan().SequenceEqual(w.Expected))
                    throw new StaleObjectException($"{w.What} changed while writing");
                if (!memory.TryWrite(w.Address, w.Value) || !memory.Read(w.Address, w.Value.Length).AsSpan().SequenceEqual(w.Value))
                    throw new MemoryAccessException($"Write failed: {w.What}");
                done.Add(w);
            }
        }
        catch
        {
            foreach (var w in Enumerable.Reverse(done)) memory.TryWrite(w.Address, w.Expected);
            throw;
        }
    }

    static void Put32(byte[] b, int at, int value) => BitConverter.GetBytes(value).CopyTo(b, at);
    static void Put64(byte[] b, int at, ulong value) => BitConverter.GetBytes(value).CopyTo(b, at);

    /// <summary>Day of year in the low 9 bits of a 16-bit value, then the year, with no flag bits (as FMRTE wrote it).</summary>
    static void Date(byte[] b, int at, DateOnly d)
    {
        BitConverter.GetBytes((short)d.DayOfYear).CopyTo(b, at);
        BitConverter.GetBytes((short)d.Year).CopyTo(b, at + 2);
    }

    bool IsPlayer(ulong address) => Q(address, out var vt) && profile.PlayerVtableRvas.Any(r => vt == moduleBase + r);
    bool IsTeam(ulong team) => Q(team, out var vt) && vt == moduleBase + T.VtableRva;

    bool Q(ulong address, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        value = 0;
        return address != 0 && memory.TryRead(address, b) && (value = BitConverter.ToUInt64(b)) != 0;
    }
}

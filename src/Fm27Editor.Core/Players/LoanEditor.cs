using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Players;

/// <summary>
/// The terms of one active loan, read from the loan contract behind <c>[player + currentLoanHolder]</c>.
/// <see cref="Wage"/> is what the player earns while on loan (the borrowing club's share); the four money fields use
/// -1 for "not agreed" exactly as FMRTE shows £0 in that case.
/// </summary>
public sealed record LoanTerms(int Wage, DateOnly? Started, DateOnly? Expires, DateOnly? Signed,
    int AgreedPrice, int BuyBackFee, int MonthlyFee, int UnusedMonthlyFee);

/// <summary>
/// One player's active loan with the object identities a guarded write needs. <see cref="Snapshot"/> is the whole
/// 0xD4-byte loan contract as read, so a write can refuse when any byte changed.
/// </summary>
public sealed record ActiveLoan(PlayerRecord Player, ulong ContractAddress, ulong HolderSlot, ulong Holder,
    ulong LoanContractAddress, ulong BorrowingTeam, ulong ParentTeam, LoanTerms Terms, string Snapshot);

/// <summary>Fields to write; null means "leave alone".</summary>
public sealed record LoanTermsEdit(int? Wage = null, DateOnly? Started = null, DateOnly? Expires = null,
    DateOnly? Signed = null, int? AgreedPrice = null, int? BuyBackFee = null, int? MonthlyFee = null,
    int? UnusedMonthlyFee = null);

/// <summary>What a recall will change. Nothing is written until <see cref="LoanEditor.ApplyRecall"/>.</summary>
public sealed record LoanRecallPlan(PlayerRecord Player, ulong ParentTeam, ulong BorrowingTeam, int SquadIndex,
    ulong SquadEnd, IReadOnlyList<PlannedWrite> Writes, IReadOnlyList<ulong>? NewParentSquad)
{
    /// <summary>The parent team's squad vector is full and needs a new buffer outside the game's allocator.</summary>
    public bool NeedsAllocation => NewParentSquad is not null;
}

/// <summary>
/// Edits one active loan: its terms, and recalling the player to the club he is contracted to.
/// <list type="bullet">
/// <item>Loan contract: <c>[player + currentLoanHolder]</c> → holder → contract (vtable 0x4D78AF8 on this build).
/// Wage +0x20, started +0x44, expires +0x48, sign date +0x4C, agreed price +0xC0, buy-back fee +0xC8, playing
/// monthly fee +0xCC, unused monthly fee +0xD0. The date and money offsets were checked against FMRTE 27 on this
/// build; FM Studio 1.1.1's Loan tab reads different money offsets (+0x80/+0xBC/0xC4), so its numbers are not used.</item>
/// <item>Recall is the same structure as a permanent move, which is verified in game: playing team +0x130 becomes the
/// contract team (<see cref="ContractOffsets.Team"/>), the player leaves the borrowing squad vector and joins the
/// parent one, and the holder pointer is cleared so the player reads as "not on loan" again. No money moves and the
/// contract is not touched; the game recalculates wages on the next Continue.</item>
/// <item>+0x138 is left alone: it holds a team for some players and a non-team object for others (16,047 of 50,039
/// players had it differ from +0x130), so it has no measured meaning here.</item>
/// </list>
/// </summary>
public sealed class LoanEditor(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    /// <summary>Signed date of the loan contract; the other dates come from the profile.</summary>
    public const int SignedDate = 0x4C;
    public const int AgreedPrice = 0xC0, BuyBackFee = 0xC8, PlayingMonthlyFee = 0xCC, UnusedMonthlyFee = 0xD0;
    const int SnapshotSize = 0xD4, MaxSquad = 200;

    ContractOffsets C => profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
    TeamOffsets T => profile.Team ?? throw new InvalidOperationException("Profile has no team offsets");

    /// <summary>The player's active loan, or null when he is not on loan or the structure is not readable.</summary>
    public ActiveLoan? Read(PlayerRecord player)
    {
        if (profile.FutureTransfers is not { } f || f.CurrentLoanHolder is not { } holderOffset) return null;
        if (!IsPlayer(player.Address) || !Q(player.Address + (ulong)C.PlayerContract, out var contract) || contract == 0)
            return null;
        if (!Q(contract, out var contractVtable) || contractVtable != moduleBase + C.ContractVtableRva) return null;
        if (!Q(contract + (ulong)C.Team, out var parent)) return null;
        if (!Q(player.Address + (ulong)T.PlayerTeam, out var borrowing) || borrowing == 0 || borrowing == parent) return null;
        ulong slot = player.Address + (ulong)holderOffset;
        if (!Q(slot, out var holder) || holder == 0) return null;
        if (!Q(holder, out var loan) || loan == 0) return null;
        if (!Q(loan, out var loanVtable) || loanVtable != moduleBase + f.LoanContractVtable) return null;
        var bytes = new byte[SnapshotSize];
        if (!memory.TryRead(loan, bytes)) return null;
        int wage = BitConverter.ToInt32(bytes, f.Wage);
        return new ActiveLoan(player, contract, slot, holder, loan, borrowing, parent,
            new LoanTerms(wage, FutureTransferReader.DateAt(bytes, f.Started), FutureTransferReader.DateAt(bytes, f.Expires),
                FutureTransferReader.DateAt(bytes, SignedDate),
                BitConverter.ToInt32(bytes, AgreedPrice), BitConverter.ToInt32(bytes, BuyBackFee),
                BitConverter.ToInt32(bytes, PlayingMonthlyFee), BitConverter.ToInt32(bytes, UnusedMonthlyFee)),
            Convert.ToHexString(bytes));
    }

    /// <summary>
    /// Writes the edited terms in one go. The loan contract must still hold every byte <paramref name="loan"/> was read
    /// with, and the player must still point at the same contract and holder; otherwise nothing is written.
    /// </summary>
    public void SetTerms(ActiveLoan loan, LoanTermsEdit edit)
    {
        var f = profile.FutureTransfers ?? throw new InvalidOperationException("Profile has no future transfer offsets");
        if (edit.Wage is { } wage && wage < 0) throw new ArgumentOutOfRangeException(nameof(edit), "Wages cannot be negative");
        foreach (var money in new[] { edit.AgreedPrice, edit.BuyBackFee, edit.MonthlyFee, edit.UnusedMonthlyFee })
            if (money is { } m && m < -1) throw new ArgumentOutOfRangeException(nameof(edit), "Money cannot be below -1 (none)");
        if (edit.Started is { } started && edit.Expires is { } expires && expires < started)
            throw new ArgumentOutOfRangeException(nameof(edit), "The loan cannot end before it starts");

        var before = Convert.FromHexString(loan.Snapshot);
        if (before.Length != SnapshotSize) throw new InvalidOperationException("The loan snapshot has the wrong size");
        if (!memory.Read(loan.LoanContractAddress, SnapshotSize).AsSpan().SequenceEqual(before))
            throw new StaleObjectException("The loan contract changed since it was read");
        VerifyPointers(loan);

        var after = (byte[])before.Clone();
        if (edit.Wage is { } w) BitConverter.GetBytes(w).CopyTo(after, f.Wage);
        if (edit.Started is { } s) PutDate(after, f.Started, s);
        if (edit.Expires is { } e) PutDate(after, f.Expires, e);
        if (edit.Signed is { } g) PutDate(after, SignedDate, g);
        if (edit.AgreedPrice is { } ap) BitConverter.GetBytes(ap).CopyTo(after, AgreedPrice);
        if (edit.BuyBackFee is { } bb) BitConverter.GetBytes(bb).CopyTo(after, BuyBackFee);
        if (edit.MonthlyFee is { } mf) BitConverter.GetBytes(mf).CopyTo(after, PlayingMonthlyFee);
        if (edit.UnusedMonthlyFee is { } uf) BitConverter.GetBytes(uf).CopyTo(after, UnusedMonthlyFee);

        if (after.AsSpan().SequenceEqual(before)) return;
        if (!memory.TryWrite(loan.LoanContractAddress, after) ||
            !memory.Read(loan.LoanContractAddress, SnapshotSize).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException($"Could not write the loan contract at 0x{loan.LoanContractAddress:X}");
    }

    /// <summary>
    /// Plans the recall: leaves the borrowing squad, joins the parent squad, plays for the parent team and loses the
    /// loan holder pointer. Every write carries the bytes it expects to find, so <see cref="ApplyRecall"/> refuses a
    /// plan whose objects changed in between.
    /// </summary>
    public LoanRecallPlan PlanRecall(PlayerRecord player)
    {
        if (profile.FutureTransfers is not { CurrentLoanHolder: { } holderOffset }) throw new InvalidOperationException("Profile has no current loan offset");
        if (!IsPlayer(player.Address)) throw new StaleObjectException($"0x{player.Address:X} is no longer a player object");
        if (!Q(player.Address + (ulong)C.PlayerContract, out var contract) || contract == 0 ||
            !Q(contract, out var contractVtable) || contractVtable != moduleBase + C.ContractVtableRva)
            throw new StaleObjectException("The player's contract is missing or is no longer a contract object");
        if (!Q(contract + (ulong)C.Team, out var parent) || !IsTeam(parent))
            throw new StaleObjectException("The contract team is missing or is no longer a team object");
        if (!Q(player.Address + (ulong)T.PlayerTeam, out var borrowing) || !IsTeam(borrowing))
            throw new StaleObjectException("The playing team is missing or is no longer a team object");
        if (borrowing == parent) throw new InvalidOperationException("The player already plays for his contract team");
        ulong slot = player.Address + (ulong)holderOffset;
        if (!Q(slot, out var holder) || holder == 0) throw new InvalidOperationException("The player has no active loan");

        var writes = new List<PlannedWrite>();
        int index = SquadIndex(borrowing, player.Address, out ulong borrowBegin, out ulong borrowEnd)
            ?? throw new StaleObjectException("The player is not in the borrowing team's squad");
        // Shift the tail left over the player, then move the end pointer back by one.
        var old = memory.Read(borrowBegin, (int)(borrowEnd - borrowBegin));
        for (int i = index; i + 1 < old.Length / 8; i++)
            writes.Add(new PlannedWrite(borrowBegin + (ulong)i * 8, old[(i * 8)..(i * 8 + 8)], old[((i + 1) * 8)..((i + 2) * 8)], "borrowing squad shift"));
        writes.Add(new PlannedWrite(borrowing + (ulong)T.Squad + 8, memory.Read(borrowing + (ulong)T.Squad + 8, 8),
            BitConverter.GetBytes(borrowEnd - 8), "borrowing squad end"));

        // The parent squad: already there is fine, otherwise append in place or grow.
        IReadOnlyList<ulong>? newSquad = null;
        if (SquadIndex(parent, player.Address, out _, out _) is null)
        {
            ulong squad = parent + (ulong)T.Squad;
            ulong begin = memory.ReadUInt64(squad), end = memory.ReadUInt64(squad + 8), capacity = memory.ReadUInt64(squad + 16);
            if (end < capacity)
            {
                writes.Add(new PlannedWrite(end, memory.Read(end, 8), BitConverter.GetBytes(player.Address), "parent squad slot"));
                writes.Add(new PlannedWrite(squad + 8, memory.Read(squad + 8, 8), BitConverter.GetBytes(end + 8), "parent squad end"));
            }
            else
            {
                var items = memory.Read(begin, (int)(end - begin));
                newSquad = [.. Enumerable.Range(0, items.Length / 8).Select(i => BitConverter.ToUInt64(items, i * 8)), player.Address];
            }
        }

        writes.Add(new PlannedWrite(player.Address + (ulong)T.PlayerTeam, memory.Read(player.Address + (ulong)T.PlayerTeam, 8),
            BitConverter.GetBytes(parent), "playing team"));
        writes.Add(new PlannedWrite(slot, memory.Read(slot, 8), BitConverter.GetBytes(0UL), "loan holder"));
        return new LoanRecallPlan(player, parent, borrowing, index, borrowEnd, writes, newSquad);
    }

    /// <summary>
    /// Re-checks every expected value, then writes. <paramref name="allocate"/> is required when the parent squad is
    /// full (for the live game: <see cref="WindowsProcessMemory.Allocate"/>).
    /// </summary>
    public void ApplyRecall(LoanRecallPlan plan, Func<int, ulong>? allocate = null)
    {
        if (!IsPlayer(plan.Player.Address)) throw new StaleObjectException("The player object changed since the plan was made");
        foreach (var w in plan.Writes)
            if (!memory.Read(w.Address, w.Expected.Length).AsSpan().SequenceEqual(w.Expected))
                throw new StaleObjectException($"{w.What} changed since the plan was made; nothing written");
        var writes = plan.Writes.ToList();
        if (plan.NewParentSquad is { } items)
        {
            if (allocate is null) throw new InvalidOperationException("The parent squad is full; an allocator is needed");
            ulong squad = plan.ParentTeam + (ulong)T.Squad;
            var header = memory.Read(squad, 24);
            int capacity = items.Count * 2;
            ulong buffer = allocate(capacity * 8);
            var bytes = items.SelectMany(BitConverter.GetBytes).ToArray();
            writes.Add(new PlannedWrite(buffer, memory.Read(buffer, bytes.Length), bytes, "new parent squad buffer"));
            writes.Add(new PlannedWrite(squad, header,
                [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)bytes.Length),
                    .. BitConverter.GetBytes(buffer + (ulong)capacity * 8)], "new parent squad begin/end/capacity"));
        }
        foreach (var w in writes)
        {
            if (!memory.Read(w.Address, w.Expected.Length).AsSpan().SequenceEqual(w.Expected))
                throw new StaleObjectException($"{w.What} changed while writing");
            if (!memory.TryWrite(w.Address, w.Value) || !memory.Read(w.Address, w.Value.Length).AsSpan().SequenceEqual(w.Value))
                throw new MemoryAccessException($"Write failed: {w.What}");
        }
    }

    void VerifyPointers(ActiveLoan loan)
    {
        if (!IsPlayer(loan.Player.Address)) throw new StaleObjectException("The player object changed since the loan was read");
        if (!Q(loan.Player.Address + (ulong)C.PlayerContract, out var contract) || contract != loan.ContractAddress)
            throw new StaleObjectException("The player's contract changed since the loan was read");
        if (!Q(loan.HolderSlot, out var holder) || holder != loan.Holder)
            throw new StaleObjectException("The loan holder changed since the loan was read");
        if (!Q(loan.Holder, out var target) || target != loan.LoanContractAddress)
            throw new StaleObjectException("The loan contract pointer changed since the loan was read");
    }

    /// <summary>Index of the player inside the team's squad vector, or null when he is not there.</summary>
    int? SquadIndex(ulong team, ulong player, out ulong begin, out ulong end)
    {
        begin = end = 0;
        if (!Q(team + (ulong)T.Squad, out begin) || !Q(team + (ulong)T.Squad + 8, out end)) return null;
        if (begin == 0 || end < begin || (end - begin) % 8 != 0 || (end - begin) / 8 > MaxSquad) return null;
        var raw = new byte[end - begin];
        if (!memory.TryRead(begin, raw)) return null;
        for (int i = 0; i < raw.Length; i += 8)
            if (BitConverter.ToUInt64(raw, i) == player) return i / 8;
        return null;
    }

    static void PutDate(byte[] b, int at, DateOnly d)
    {
        short raw = BitConverter.ToInt16(b, at);
        BitConverter.GetBytes((short)((raw & ~0x1FF) | d.DayOfYear)).CopyTo(b, at);
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

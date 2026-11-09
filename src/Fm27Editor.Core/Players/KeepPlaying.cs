using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Players;

public sealed record RetirementGuard(ulong Address, byte[] Bytes, string What);
public sealed record KeepPlayingPlan(PlayerRecord Player, uint PersonId, DateOnly? RetirementDate, ulong Root, ulong Manager,
    IReadOnlyList<RetirementGuard> Guards, IReadOnlyList<PlannedWrite> Writes);

/// <summary>Removes a player's planned retirement from both manager indexes and changes the two retirement traits.</summary>
public sealed class KeepPlayingWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    readonly PlannedRetirements reader = new(memory, profile, moduleBase);

    public KeepPlayingPlan Plan(PlayerRecord player) => TryPlan(player) ?? throw new InvalidOperationException("Player is already set to keep playing");

    public KeepPlayingPlan? TryPlan(PlayerRecord player)
    {
        uint person = reader.PersonId(player);
        if (person == 0) throw new StaleObjectException("Player has no person id");
        var state = reader.Snapshot();
        var guards = new List<RetirementGuard>
        {
            new(state.Root, BitConverter.GetBytes(state.Manager), "retirement root"),
            new(state.Manager, BitConverter.GetBytes(moduleBase + reader.ManagerVtable), "retirement manager"),
            new(state.Manager + 0x30, state.ListHeader, "retirement list header"),
            new(state.Manager + 0x90, state.IndexHeader, "retirement index header"),
            new(player.Address + 0x290, BitConverter.GetBytes(person), "player person id"),
        };
        if (state.Pointers.Length > 0)
        {
            guards.Add(new(state.ListBegin, state.Pointers, "retirement list"));
            guards.Add(new(state.IndexBegin, state.Index, "retirement index"));
        }
        var writes = new List<PlannedWrite>();
        DateOnly? date = null;
        if (state.ByPerson.TryGetValue(person, out var found))
        {
            date = found.Date;
            int count = state.Pointers.Length / 8;
            int index = Enumerable.Range(0, count).Single(i => BitConverter.ToUInt32(state.Index, i * 16) == person);
            ulong entry = BitConverter.ToUInt64(state.Index, index * 16 + 8);
            int listIndex = Enumerable.Range(0, count).Single(i => BitConverter.ToUInt64(state.Pointers, i * 8) == entry);
            guards.Add(new(entry, state.Entries[listIndex], "retirement entry"));
            int last = count - 1;
            if (index < last)
                writes.Add(new(state.IndexBegin + (ulong)index * 16, state.Index[(index * 16)..(last * 16)], state.Index[((index + 1) * 16)..], "retirement index shift"));
            ulong indexEnd = BitConverter.ToUInt64(state.IndexHeader, 8);
            writes.Add(new(state.Manager + 0x98, BitConverter.GetBytes(indexEnd), BitConverter.GetBytes(indexEnd - 16), "retirement index end"));
            writes.Add(new(state.IndexBegin + (ulong)last * 16, state.Index[(last * 16)..], new byte[16], "retirement index last"));
            if (listIndex < last)
                writes.Add(new(state.ListBegin + (ulong)listIndex * 8, state.Pointers[(listIndex * 8)..(last * 8)], state.Pointers[((listIndex + 1) * 8)..], "retirement list shift"));
            ulong listEnd = BitConverter.ToUInt64(state.ListHeader, 8);
            writes.Add(new(state.Manager + 0x38, BitConverter.GetBytes(listEnd), BitConverter.GetBytes(listEnd - 8), "retirement list end"));
            writes.Add(new(state.ListBegin + (ulong)last * 8, state.Pointers[(last * 8)..], new byte[8], "retirement list last"));
        }
        int traits = profile.PreferredMoves ?? throw new InvalidOperationException("No player traits offset");
        ulong traitAddress = player.Address + (ulong)traits + 6;
        var before = memory.Read(traitAddress, 1);
        byte after = (byte)((before[0] & ~1) | 2);
        guards.Add(new(traitAddress, before, "retirement traits"));
        if (before[0] != after) writes.Add(new(traitAddress, before, [after], "retirement traits"));
        if (writes.Count == 0) return null;
        return new(player, person, date, state.Root, state.Manager, guards, writes);
    }

    /// <summary>Refreshes shared vector positions after an earlier batch item while retaining this player's preview.</summary>
    public KeepPlayingPlan Replan(KeepPlayingPlan previous)
    {
        var current = TryPlan(previous.Player) ?? throw new StaleObjectException("Player's retirement changed since preview");
        var oldEntry = previous.Guards.FirstOrDefault(g => g.What == "retirement entry");
        var newEntry = current.Guards.FirstOrDefault(g => g.What == "retirement entry");
        var oldTrait = previous.Guards.Single(g => g.What == "retirement traits");
        var newTrait = current.Guards.Single(g => g.What == "retirement traits");
        if (current.Root != previous.Root || current.Manager != previous.Manager || current.PersonId != previous.PersonId ||
            current.RetirementDate != previous.RetirementDate ||
            (oldEntry is null) != (newEntry is null) ||
            oldEntry is not null && (oldEntry.Address != newEntry!.Address || !oldEntry.Bytes.AsSpan().SequenceEqual(newEntry.Bytes)) ||
            oldTrait.Address != newTrait.Address || !oldTrait.Bytes.AsSpan().SequenceEqual(newTrait.Bytes))
            throw new StaleObjectException("Player's retirement changed since preview");
        return current;
    }

    public void Apply(KeepPlayingPlan plan, Action? onWritten = null) => Change(plan, undo: false, onWritten);
    public void Undo(KeepPlayingPlan plan, Action? onWritten = null) => Change(plan, undo: true, onWritten);

    void Change(KeepPlayingPlan plan, bool undo, Action? onWritten)
    {
        Validate(plan);
        foreach (var guard in plan.Guards)
        {
            var expected = (byte[])guard.Bytes.Clone();
            if (undo) foreach (var write in plan.Writes) Overlay(expected, guard.Address, write.Address, write.Value);
            if (!memory.Read(guard.Address, expected.Length).AsSpan().SequenceEqual(expected))
                throw new StaleObjectException($"{guard.What} changed; nothing written");
        }
        foreach (var write in plan.Writes)
        {
            var expected = undo ? write.Value : write.Expected;
            if (!memory.Read(write.Address, expected.Length).AsSpan().SequenceEqual(expected))
                throw new StaleObjectException($"{write.What} changed; nothing written");
        }
        var completed = new List<PlannedWrite>();
        try
        {
            foreach (var write in undo ? plan.Writes.Reverse() : plan.Writes)
            {
                Validate(plan);
                byte[] expected = undo ? write.Value : write.Expected, value = undo ? write.Expected : write.Value;
                if (!memory.Read(write.Address, expected.Length).AsSpan().SequenceEqual(expected))
                    throw new StaleObjectException($"{write.What} changed while writing");
                if (!memory.TryWrite(write.Address, value)) throw new MemoryAccessException($"Write failed: {write.What}");
                completed.Add(write);
                if (!memory.Read(write.Address, value.Length).AsSpan().SequenceEqual(value))
                    throw new MemoryAccessException($"Readback failed: {write.What}");
            }
        }
        catch (Exception failure)
        {
            try
            {
                foreach (var write in completed.AsEnumerable().Reverse())
                {
                    Validate(plan);
                    byte[] expected = undo ? write.Expected : write.Value, restore = undo ? write.Value : write.Expected;
                    if (!memory.Read(write.Address, expected.Length).AsSpan().SequenceEqual(expected) ||
                        !memory.TryWrite(write.Address, restore) || !memory.Read(write.Address, restore.Length).AsSpan().SequenceEqual(restore))
                        throw new MemoryAccessException($"Could not restore {write.What}");
                }
            }
            catch (Exception rollback) { throw new MemoryAccessException($"{failure.Message}; rollback failed: {rollback.Message}"); }
            throw;
        }
        onWritten?.Invoke();
    }

    void Validate(KeepPlayingPlan plan)
    {
        if (reader.PersonId(plan.Player) != plan.PersonId || memory.ReadUInt64(plan.Root) != plan.Manager ||
            !reader.IsSupported || memory.ReadUInt64(plan.Manager) != moduleBase + reader.ManagerVtable)
            throw new StaleObjectException("Player or retirement manager changed");
    }

    static void Overlay(byte[] target, ulong address, ulong sourceAddress, byte[] source)
    {
        ulong start = Math.Max(address, sourceAddress), end = Math.Min(address + (ulong)target.Length, sourceAddress + (ulong)source.Length);
        if (start < end) Array.Copy(source, (long)(start - sourceAddress), target, (long)(start - address), (long)(end - start));
    }
}

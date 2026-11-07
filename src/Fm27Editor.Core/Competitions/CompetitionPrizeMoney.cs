using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Competitions;

public enum CompetitionPrizeField { PerWin, PerDraw, PerDefeat }

/// <summary>One competition's prize group, including the pointer chain needed to guard later writes.</summary>
public sealed record CompetitionPrizeMoney(CompetitionRecord Competition, ulong Holder, ulong StagesBegin, ulong StagesEnd,
    ulong Stage, ulong Group, int PerWin, int PerDraw, int PerDefeat);

public sealed record CompetitionClassificationPrizes(CompetitionPrizeMoney Prize, ulong Begin, ulong End, ulong Capacity,
    IReadOnlyList<int> Amounts);

/// <summary>
/// FMRTE 27 follows competition+0xB0 to a stage vector at holder+0x1C0, then stage+0xF0 to the prize group.
/// Selects the unique owned league stage with a valid prize group; ambiguous stage vectors stay unavailable.
/// </summary>
public sealed class CompetitionPrizeMoneyAccess(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    CompetitionOffsets Competition => profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");
    CompetitionPrizeOffsets Layout => Competition.PrizeMoney ?? throw new InvalidOperationException("Prize Money is unavailable for this build");

    public CompetitionPrizeMoney? Read(CompetitionRecord competition)
    {
        var c = Competition;
        if (c.PrizeMoney is not { } p || c.Id is not { } id || competition.Id <= 0) return null;
        bool Q(ulong address, out ulong value)
        {
            Span<byte> bytes = stackalloc byte[8];
            bool ok = memory.TryRead(address, bytes);
            value = ok ? BitConverter.ToUInt64(bytes) : 0;
            return ok;
        }
        Span<byte> identity = stackalloc byte[4];
        if (!Q(competition.Address, out ulong vt) || vt != moduleBase + c.VtableRva ||
            !memory.TryRead(competition.Address + (ulong)id, identity) || BitConverter.ToInt32(identity) != competition.Id ||
            !Q(competition.Address + (ulong)p.Holder, out ulong holder) || holder == 0 ||
            !Q(holder, out ulong holderVt) || holderVt != moduleBase + p.HolderVtableRva ||
            !Q(holder + (ulong)p.Stages, out ulong begin) || !Q(holder + (ulong)p.Stages + 8, out ulong end) ||
            begin == 0 || end < begin || end - begin == 0 || (end - begin) % 8 != 0 || end - begin > 2048) return null;
        ulong stage = 0, group = 0;
        for (ulong slot = begin; slot < end; slot += 8)
        {
            if (!Q(slot, out ulong candidate) || candidate == 0 || !Q(candidate, out ulong candidateVt)) return null;
            if (candidateVt != moduleBase + p.StageVtableRva) continue;
            if (!Q(candidate + (ulong)p.StageOwner, out ulong owner) || owner != competition.Address ||
                !Q(candidate + (ulong)p.StagePrize, out ulong candidateGroup)) return null;
            if (candidateGroup == 0) continue;
            if (!Q(candidateGroup, out ulong groupVt) || groupVt != moduleBase + p.PrizeVtableRva) return null;
            if (stage != 0) return null;
            stage = candidate;
            group = candidateGroup;
        }
        if (stage == 0) return null;
        Span<byte> win = stackalloc byte[4]; Span<byte> draw = stackalloc byte[4]; Span<byte> defeat = stackalloc byte[4];
        if (!memory.TryRead(group + (ulong)p.PerWin, win) || !memory.TryRead(group + (ulong)p.PerDraw, draw) ||
            !memory.TryRead(group + (ulong)p.PerDefeat, defeat)) return null;
        return new(competition, holder, begin, end, stage, group, BitConverter.ToInt32(win), BitConverter.ToInt32(draw), BitConverter.ToInt32(defeat));
    }

    public void Set(CompetitionPrizeMoney snapshot, CompetitionPrizeField field, int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Prize money cannot be negative");
        var p = Layout;
        int offset = field switch
        {
            CompetitionPrizeField.PerWin => p.PerWin,
            CompetitionPrizeField.PerDraw => p.PerDraw,
            CompetitionPrizeField.PerDefeat => p.PerDefeat,
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        int before = field switch
        {
            CompetitionPrizeField.PerWin => snapshot.PerWin,
            CompetitionPrizeField.PerDraw => snapshot.PerDraw,
            _ => snapshot.PerDefeat,
        };
        var current = Read(snapshot.Competition);
        if (current is null || current.Holder != snapshot.Holder || current.StagesBegin != snapshot.StagesBegin ||
            current.StagesEnd != snapshot.StagesEnd || current.Stage != snapshot.Stage || current.Group != snapshot.Group)
            throw new StaleObjectException("Competition prize group changed since it was read");
        ulong address = snapshot.Group + (ulong)offset;
        byte[] expected = BitConverter.GetBytes(before), after = BitConverter.GetBytes(value);
        if (!memory.Read(address, 4).AsSpan().SequenceEqual(expected)) throw new StaleObjectException("Prize money changed since it was read");
        if (!memory.TryWrite(address, after) || !memory.Read(address, 4).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException("Could not write prize money");
    }

    public CompetitionClassificationPrizes? ReadClassification(CompetitionPrizeMoney prize)
    {
        var current = Read(prize.Competition);
        if (current is null || current.Holder != prize.Holder || current.StagesBegin != prize.StagesBegin ||
            current.StagesEnd != prize.StagesEnd || current.Stage != prize.Stage || current.Group != prize.Group) return null;
        Span<byte> header = stackalloc byte[24];
        if (!memory.TryRead(prize.Group + (ulong)Layout.Classification, header)) return null;
        ulong begin = BitConverter.ToUInt64(header), end = BitConverter.ToUInt64(header[8..]), cap = BitConverter.ToUInt64(header[16..]);
        if (begin == 0 && end == 0 && cap == 0) return new(prize, begin, end, cap, Array.Empty<int>());
        if (begin == 0 || end < begin || cap < end || (end - begin) % 4 != 0 || end - begin > 4096) return null;
        byte[] data = new byte[(int)(end - begin)];
        if (data.Length != 0 && !memory.TryRead(begin, data)) return null;
        var amounts = new int[data.Length / 4];
        for (int i = 0; i < amounts.Length; i++) amounts[i] = BitConverter.ToInt32(data, i * 4);
        return new(prize, begin, end, cap, Array.AsReadOnly(amounts));
    }

    public void SetClassification(CompetitionClassificationPrizes snapshot, int position, int value)
    {
        if (position < 1 || position > snapshot.Amounts.Count) throw new ArgumentOutOfRangeException(nameof(position));
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        var current = ReadClassification(snapshot.Prize);
        if (current is null || current.Begin != snapshot.Begin || current.End != snapshot.End || current.Capacity != snapshot.Capacity ||
            current.Amounts[position - 1] != snapshot.Amounts[position - 1])
            throw new StaleObjectException("Competition classification prize changed since it was read");
        ulong address = snapshot.Begin + (ulong)((position - 1) * 4);
        byte[] before = BitConverter.GetBytes(snapshot.Amounts[position - 1]), after = BitConverter.GetBytes(value);
        if (!memory.Read(address, 4).AsSpan().SequenceEqual(before)) throw new StaleObjectException("Competition classification prize changed since it was read");
        if (!memory.TryWrite(address, after) || !memory.Read(address, 4).AsSpan().SequenceEqual(after))
            throw new MemoryAccessException("Could not write classification prize money");
    }

    /// <summary>FMRTE clears the vector on disable and creates one zero amount per standing on enable.</summary>
    public void SetClassificationEnabled(CompetitionClassificationPrizes snapshot, bool enabled, Func<int, ulong> allocate)
    {
        var current = ReadClassification(snapshot.Prize);
        if (current is null || current.Begin != snapshot.Begin || current.End != snapshot.End || current.Capacity != snapshot.Capacity ||
            !current.Amounts.SequenceEqual(snapshot.Amounts)) throw new StaleObjectException("Classification prize table changed since it was read");
        if (enabled == (snapshot.Amounts.Count > 0)) return;
        ulong headerAddress = snapshot.Prize.Group + (ulong)Layout.Classification;
        byte[] expected = [.. BitConverter.GetBytes(snapshot.Begin), .. BitConverter.GetBytes(snapshot.End), .. BitConverter.GetBytes(snapshot.Capacity)];
        byte[] after;
        byte[]? initialData = null;
        byte[]? expectedBuffer = null;
        ulong buffer = 0;
        if (!enabled) after = [.. BitConverter.GetBytes(snapshot.Begin), .. BitConverter.GetBytes(snapshot.Begin), .. BitConverter.GetBytes(snapshot.Capacity)];
        else
        {
            var rows = new CompetitionStandingsAccess(memory, profile, moduleBase).Read(snapshot.Prize.Competition);
            if (rows is not { Count: > 0 }) throw new InvalidOperationException("Standings are unavailable");
            int size = checked(rows.Count * 4);
            buffer = snapshot.Begin != 0 && snapshot.Capacity - snapshot.Begin >= (ulong)size
                ? snapshot.Begin : allocate(size);
            if (buffer == 0) throw new MemoryAccessException("Could not allocate classification prize table");
            initialData = new byte[size];
            expectedBuffer = memory.Read(buffer, size);
            var again = new CompetitionStandingsAccess(memory, profile, moduleBase).Read(snapshot.Prize.Competition);
            if (again is null || !again.Select(x => (x.Address, x.Team, x.Club, x.ClubId)).SequenceEqual(rows.Select(x => (x.Address, x.Team, x.Club, x.ClubId))))
                throw new StaleObjectException("Standings changed since they were read");
            ulong capacity = buffer == snapshot.Begin ? snapshot.Capacity : buffer + (ulong)size;
            after = [.. BitConverter.GetBytes(buffer), .. BitConverter.GetBytes(buffer + (ulong)size), .. BitConverter.GetBytes(capacity)];
        }
        var fresh = ReadClassification(snapshot.Prize);
        if (fresh is null || fresh.Begin != snapshot.Begin || fresh.End != snapshot.End || fresh.Capacity != snapshot.Capacity || !fresh.Amounts.SequenceEqual(snapshot.Amounts) ||
            !memory.Read(headerAddress, 24).AsSpan().SequenceEqual(expected)) throw new StaleObjectException("Classification prize table changed since it was read");
        if (initialData is not null)
        {
            if (!memory.Read(buffer, initialData.Length).AsSpan().SequenceEqual(expectedBuffer)) throw new StaleObjectException("Classification prize buffer changed since it was read");
            if (!memory.TryWrite(buffer, initialData) || !memory.Read(buffer, initialData.Length).AsSpan().SequenceEqual(initialData)) throw new MemoryAccessException("Could not initialize classification prize table");
        }
        // FMRTE disables a populated table by changing only end; preserve begin and capacity.
        bool endOnly = !enabled || buffer == snapshot.Begin;
        ulong writeAddress = endOnly ? headerAddress + 8 : headerAddress;
        ReadOnlySpan<byte> writeBytes = endOnly ? after.AsSpan(8, 8) : after;
        if (!memory.TryWrite(writeAddress, writeBytes) || !memory.Read(headerAddress, 24).AsSpan().SequenceEqual(after)) throw new MemoryAccessException("Could not update classification prize table");
    }
}

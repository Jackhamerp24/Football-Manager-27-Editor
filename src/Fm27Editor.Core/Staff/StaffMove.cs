using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Staff;

public sealed record StaffMoveGuard(ulong Address, byte[] Bytes, string What);
public sealed record StaffMovePlan(StaffRecord Staff, ClubEntry From, ClubEntry To, ulong Contract, ulong DestinationTeam,
    IReadOnlyList<string> Departments, IReadOnlyList<StaffMoveGuard> Guards, IReadOnlyList<PlannedWrite> Writes);

/// <summary>Moves an ordinary staff member between club department vectors without allocating game memory.</summary>
public sealed class StaffMoveService(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    ContractOffsets C => profile.Contract ?? throw new InvalidOperationException("No contract offsets");
    ClubOffsets K => profile.Club ?? throw new InvalidOperationException("No club offsets");
    TeamOffsets T => profile.Team ?? throw new InvalidOperationException("No team offsets");
    StaffOffsets S => profile.Staff ?? throw new InvalidOperationException("No staff offsets");

    public StaffMovePlan Plan(StaffRecord staff, ClubEntry from, ClubEntry to, DateOnly? gameDate = null)
    {
        ValidateStaff(staff);
        if (staff.Kind.Manager || staff.Kind.StaffPart != 0x100) throw new InvalidOperationException("Human managers and unusual staff objects cannot be moved");
        if (from.Address == to.Address) throw new InvalidOperationException("Staff already belongs to that club");
        var guards = new List<StaffMoveGuard>();
        var writes = new List<PlannedWrite>();
        byte[] Capture(ulong address, int length, string what)
        {
            var bytes = memory.Read(address, length);
            guards.Add(new(address, bytes, what));
            return bytes;
        }
        ulong contract = memory.ReadUInt64(staff.Address + (ulong)S.Job);
        if (contract == 0 || memory.ReadUInt64(contract) != moduleBase + C.ContractVtableRva)
            throw new InvalidOperationException("Staff has no readable contract");
        int owner = C.Owner ?? throw new InvalidOperationException("No contract owner offset");
        int jobAt = C.Job ?? throw new InvalidOperationException("No contract job offset");
        int seniorAt = C.SeniorTeam ?? throw new InvalidOperationException("No contract senior team offset");
        var contractBytes = Capture(contract, Math.Max(Math.Max(C.JoinedDate + 4, (C.Started ?? 0) + 4),
            Math.Max(jobAt + 1, Math.Max(seniorAt + 8, owner + 8))), "contract");
        ulong contractOwner = BitConverter.ToUInt64(contractBytes, owner);
        ulong employer = BitConverter.ToUInt64(contractBytes, C.Team);
        ulong senior = BitConverter.ToUInt64(contractBytes, seniorAt);
        byte job = contractBytes[jobAt];
        if (contractOwner != staff.Address || employer == 0 || senior == 0 || !SupportedJob(job))
            throw new InvalidOperationException("Staff contract or job is not supported");
        if (memory.ReadUInt64(employer) != moduleBase + T.VtableRva || memory.ReadUInt64(senior) != moduleBase + T.VtableRva ||
            memory.ReadUInt64(employer + (ulong)C.TeamClub) != from.Address || memory.ReadUInt64(senior + (ulong)C.TeamClub) != from.Address)
            throw new StaleObjectException("Staff contract teams no longer belong to the source club");
        int gender = K.Gender ?? throw new InvalidOperationException("No club gender offset");
        int teamList = K.Lists?.Teams ?? throw new InvalidOperationException("No club teams offset");
        int idOffset = K.Id ?? throw new InvalidOperationException("No club id offset");
        foreach (var club in new[] { from, to })
        {
            if (club.Id is not > 0 || memory.ReadUInt64(club.Address) != moduleBase + K.ClubVtableRva ||
                BitConverter.ToInt32(memory.Read(club.Address + (ulong)idOffset, 4)) != club.Id)
                throw new StaleObjectException("Club identity changed");
            if (memory.Read(club.Address + (ulong)gender, 1)[0] != 0)
                throw new InvalidOperationException("Only men's clubs are supported");
        }
        var fromClub = Capture(from.Address, 0x170, "source club");
        var toClub = Capture(to.Address, 0x170, "destination club");
        Vector ReadVector(ulong headerAddress, string what, int max = 1024)
        {
            var header = Capture(headerAddress, 24, what + " header");
            ulong begin = BitConverter.ToUInt64(header, 0), end = BitConverter.ToUInt64(header, 8), cap = BitConverter.ToUInt64(header, 16);
            if (begin == 0 || end < begin || cap < end || (end - begin) % 8 != 0 || (cap - begin) % 8 != 0 || (cap - begin) / 8 > (ulong)max)
                throw new InvalidOperationException($"Invalid {what} vector");
            var items = Capture(begin, checked((int)(end - begin)), what + " entries");
            return new(headerAddress, begin, end, cap, items);
        }
        var fromTeams = ReadVector(from.Address + (ulong)teamList, "source teams", 128);
        var toTeams = ReadVector(to.Address + (ulong)teamList, "destination teams", 128);
        var sourceTeams = fromTeams.Pointers();
        var destinationTeams = toTeams.Pointers();
        if (sourceTeams.Length == 0 || destinationTeams.Length == 0 || sourceTeams.Distinct().Count() != sourceTeams.Length ||
            destinationTeams.Distinct().Count() != destinationTeams.Length || !sourceTeams.Contains(employer) || !sourceTeams.Contains(senior))
            throw new InvalidOperationException("Invalid club team membership");
        ulong destinationTeam = destinationTeams[0];
        foreach (var (teams, club) in new[] { (sourceTeams, from), (destinationTeams, to) })
            foreach (ulong team in teams)
                if (team == 0 || memory.ReadUInt64(team) != moduleBase + T.VtableRva || memory.ReadUInt64(team + (ulong)C.TeamClub) != club.Address)
                    throw new StaleObjectException("Club team changed");
        // A club or team can also hold a direct role pointer which department edits would leave behind.
        bool HoldsStaff(byte[] bytes) => Enumerable.Range(0, bytes.Length / 8).Any(i =>
        {
            ulong ptr = BitConverter.ToUInt64(bytes, i * 8);
            return ptr == staff.StaffAddress || ptr == staff.Address;
        });
        if (HoldsStaff(fromClub)) throw new InvalidOperationException("Source club directly references this staff member");
        foreach (ulong team in sourceTeams)
            if (HoldsStaff(Capture(team, 0x800, "source team"))) throw new InvalidOperationException("Source team directly references this staff member");
        Capture(staff.Address + (ulong)S.Job, 8, "staff contract pointer");
        Capture(staff.Address + (ulong)S.Id, 4, "staff identity");
        var departments = new[]
        {
            ("Medical", K.MedicalStaff), ("Coaching", K.CoachingStaff), ("Scouting", K.ScoutingStaff),
        };
        var active = new List<string>();
        foreach (var (name, offset) in departments)
        {
            if (offset is not { } at) throw new InvalidOperationException("Missing staff department offset");
            var source = ReadVector(from.Address + (ulong)at, "source " + name);
            var entries = source.Pointers();
            int[] indices = Enumerable.Range(0, entries.Length).Where(i => entries[i] == staff.StaffAddress).ToArray();
            if (indices.Length > 1) throw new InvalidOperationException("Staff appears more than once in a department");
            if (indices.Length == 0) continue;
            var destination = ReadVector(to.Address + (ulong)at, "destination " + name);
            if (destination.Pointers().Contains(staff.StaffAddress)) throw new InvalidOperationException("Destination already contains this staff member");
            if (destination.End == destination.Capacity)
            {
                var classes = K.StaffAllocationClasses;
                ulong bytes = destination.Capacity - destination.Begin;
                if (classes is null || classes.Count < 2 || classes.Any(x => x <= 0) ||
                    !classes.SequenceEqual(classes.Order()) || bytes <= (ulong)classes[0])
                    throw new InvalidOperationException($"Destination {name} department is full");
                int block = classes.Skip(1).FirstOrDefault(x => (ulong)x >= bytes);
                if (block == 0 || (ulong)block - bytes < 8)
                    throw new InvalidOperationException($"Destination {name} department is full");
                var spare = Capture(destination.Capacity, checked(block - (int)bytes), "destination " + name + " allocator spare");
                if (spare.Any(b => b != 0)) throw new InvalidOperationException($"Destination {name} allocator spare is not empty");
            }
            active.Add(name);
            writes.Add(new(destination.End, memory.Read(destination.End, 8), BitConverter.GetBytes(staff.StaffAddress), "destination " + name + " slot"));
            writes.Add(new(destination.Header + 8,
                [.. BitConverter.GetBytes(destination.End), .. BitConverter.GetBytes(destination.Capacity)],
                [.. BitConverter.GetBytes(destination.End + 8), .. BitConverter.GetBytes(Math.Max(destination.Capacity, destination.End + 8))],
                "destination " + name + " end and capacity"));
            int index = indices[0], last = entries.Length - 1;
            if (index < last)
                writes.Add(new(source.Begin + (ulong)index * 8, source.Items[(index * 8)..(last * 8)], source.Items[((index + 1) * 8)..], "source " + name + " shift"));
            writes.Add(new(source.Header + 8, BitConverter.GetBytes(source.End), BitConverter.GetBytes(source.End - 8), "source " + name + " end"));
            writes.Add(new(source.End - 8, source.Items[(last * 8)..], new byte[8], "source " + name + " last slot"));
        }
        if (active.Count == 0) throw new InvalidOperationException("Staff is not in any supported source department");
        writes.Add(new(contract + (ulong)C.Team, BitConverter.GetBytes(employer), BitConverter.GetBytes(destinationTeam), "contract team"));
        writes.Add(new(contract + (ulong)seniorAt, BitConverter.GetBytes(senior), BitConverter.GetBytes(destinationTeam), "contract senior team"));
        if (gameDate is { Year: > 1900 and <= 2300 } date && C.Started is { } started)
        {
            foreach (int offset in new[] { started, C.JoinedDate }.Distinct())
            {
                var old = memory.Read(contract + (ulong)offset, 4);
                int day = BitConverter.ToUInt16(old) & 0x1FF, year = BitConverter.ToInt16(old, 2);
                if (year <= 1900 || year > 2300 || day < 1 || day > (DateTime.IsLeapYear(year) ? 366 : 365)) continue;
                ushort raw = BitConverter.ToUInt16(old);
                byte[] value = [.. BitConverter.GetBytes((ushort)((raw & ~0x1FF) | date.DayOfYear)), .. BitConverter.GetBytes((short)date.Year)];
                if (!old.AsSpan().SequenceEqual(value)) writes.Add(new(contract + (ulong)offset, old, value, "contract date"));
            }
        }
        return new(staff, from, to, contract, destinationTeam, active, guards, writes);
    }

    public void Apply(StaffMovePlan plan) => Change(plan, undo: false);
    public void Undo(StaffMovePlan plan) => Change(plan, undo: true);

    void Change(StaffMovePlan plan, bool undo)
    {
        ValidateStaff(plan.Staff);
        if (memory.ReadUInt64(plan.Staff.Address + (ulong)S.Job) != plan.Contract || memory.ReadUInt64(plan.Contract) != moduleBase + C.ContractVtableRva)
            throw new StaleObjectException("Staff contract changed");
        foreach (var guard in plan.Guards)
        {
            var expected = (byte[])guard.Bytes.Clone();
            if (undo) foreach (var write in plan.Writes) Overlay(expected, guard.Address, write.Address, write.Value);
            if (!memory.Read(guard.Address, expected.Length).AsSpan().SequenceEqual(expected))
                throw new StaleObjectException($"{guard.What} changed; nothing written");
        }
        foreach (var write in plan.Writes)
        {
            byte[] expected = undo ? write.Value : write.Expected;
            if (!memory.Read(write.Address, expected.Length).AsSpan().SequenceEqual(expected))
                throw new StaleObjectException($"{write.What} changed; nothing written");
        }
        var writes = undo ? plan.Writes.Reverse() : plan.Writes;
        foreach (var write in writes)
        {
            ValidateStaff(plan.Staff);
            if (memory.ReadUInt64(plan.Contract) != moduleBase + C.ContractVtableRva ||
                memory.ReadUInt64(plan.From.Address) != moduleBase + K.ClubVtableRva ||
                memory.ReadUInt64(plan.To.Address) != moduleBase + K.ClubVtableRva ||
                memory.ReadUInt64(plan.DestinationTeam) != moduleBase + T.VtableRva)
                throw new StaleObjectException("Staff, contract, club or team changed while writing");
            byte[] expected = undo ? write.Value : write.Expected, value = undo ? write.Expected : write.Value;
            if (!memory.Read(write.Address, expected.Length).AsSpan().SequenceEqual(expected))
                throw new StaleObjectException($"{write.What} changed while writing");
            if (!memory.TryWrite(write.Address, value) || !memory.Read(write.Address, value.Length).AsSpan().SequenceEqual(value))
                throw new MemoryAccessException($"Write failed: {write.What}");
        }
    }

    void ValidateStaff(StaffRecord staff)
    {
        if (staff.Id <= 0 || !S.Kinds.Contains(staff.Kind) ||
            memory.ReadUInt64(staff.Address) != moduleBase + staff.Kind.PersonVtableRva ||
            memory.ReadUInt64(staff.StaffAddress) != moduleBase + staff.Kind.StaffVtableRva ||
            BitConverter.ToInt32(memory.Read(staff.Address + (ulong)S.Id, 4)) != staff.Id)
            throw new StaleObjectException("Staff identity changed");
    }

    static bool SupportedJob(byte job) => job is 2 or 20 or 22 or 26 or 34 or 60 or 64 or 12 or 40 or 48 or 50 or 14 or 44 or 58;

    static void Overlay(byte[] target, ulong targetAddress, ulong sourceAddress, byte[] source)
    {
        ulong start = Math.Max(targetAddress, sourceAddress), end = Math.Min(targetAddress + (ulong)target.Length, sourceAddress + (ulong)source.Length);
        if (start < end) Array.Copy(source, (long)(start - sourceAddress), target, (long)(start - targetAddress), (long)(end - start));
    }

    sealed record Vector(ulong Header, ulong Begin, ulong End, ulong Capacity, byte[] Items)
    {
        public ulong[] Pointers() => Enumerable.Range(0, Items.Length / 8).Select(i => BitConverter.ToUInt64(Items, i * 8)).ToArray();
    }
}

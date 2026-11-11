using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Staff;

namespace Fm27Editor.Core.Nations;

public sealed record NationStaffRow(StaffRecord Staff, NationTeamRecord? Team, byte? JobCode, byte? TeamType,
    bool HasFullContract, DateOnly? Expires)
{
    // England Staff labels plus the shared FMRTE job catalogue; unfamiliar values remain numeric.
    public string JobName => JobCode switch
    {
        1 => "Player", 2 => "Coach", 3 => "Under 18s Coach", 4 => "Chairperson", 6 => "Director",
        8 => "Managing Director", 10 => "Director of Football", 12 => "Physio", 14 => "Scout", 16 => "Manager",
        17 => "Under 18s Manager", 20 => "Ast. Manager", 21 => "Under 18s Assistant Manager", 22 => "Set Piece Coach",
        24 => "General Manager", 26 => "Fitness Coach", 27 => "Under 18s Fitness Coach", 34 => "GK Coach",
        35 => "Under 18s Goalkeeping Coach", 38 => "Chief Doctor", 40 => "Head of Sports Science", 44 => "Chief Scout",
        45 => "Under 18s Chief Scout", 46 => "Doctor", 48 => "Sports Scientist", 50 => "Head Physio", 52 => "U19 Manager",
        58 => "Recruitment Analyst", 60 => "Performance Analyst", 61 => "Under 18s Performance Analyst",
        62 => "Head Performance Analyst", 64 => "Head of Youth Development", 66 => "Owner", 70 => "President",
        86 => "Loan Manager", 88 => "Technical Director", 89 => "Under 18s Technical Director", 144 => "Caretaker Manager",
        null => "", _ => JobCode.Value.ToString()
    };
    public string TeamSuffix => TeamType switch { 0 => "", 10 => " U21", 18 => " U20", 11 => " U19", 12 => " U18", _ => "" };
}

/// <summary>Reads the men's national staff lists (measured on Game Pass and Steam 27.0.0+2493426).</summary>
public sealed class NationStaff(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    /// <summary>Reads a staff member's separate national appointment, including job and national team.</summary>
    public IReadOnlyList<NationStaffRow> ReadDetails(ulong nation)
    {
        var teams = new NationTeamReader(memory, profile, moduleBase).Read(nation).ToDictionary(t => t.Address);
        var result = new List<NationStaffRow>();
        Span<byte> ptr = stackalloc byte[8];
        foreach (var person in Read(nation))
        {
            NationTeamRecord? team = null;
            byte? job = null;
            byte? teamType = null;
            bool hasFullContract = false;
            DateOnly? expires = null;
            if (memory.TryRead(person.Address + 0xB0, ptr))
            {
                ulong holder = BitConverter.ToUInt64(ptr);
                if (holder != 0 && memory.TryRead(holder + 0x10, ptr))
                {
                    ulong appointment = BitConverter.ToUInt64(ptr);
                    var bytes = new byte[0x28];
                    if (appointment != 0 && memory.TryRead(appointment, bytes) &&
                        (BitConverter.ToUInt64(bytes) == moduleBase + profile.NationAppointmentVtable.GetValueOrDefault() || BitConverter.ToUInt64(bytes) == moduleBase + profile.Contract!.ContractVtableRva) &&
                        BitConverter.ToUInt64(bytes, 8) == person.Address && teams.TryGetValue(BitConverter.ToUInt64(bytes, 0x10), out var matched))
                    {
                        team = matched;
                        job = bytes[0x26];
                        var type = new byte[1];
                        if (memory.TryRead(matched.Address + 0x28, type)) teamType = type[0];
                        if (BitConverter.ToUInt64(bytes) == moduleBase + profile.Contract!.ContractVtableRva && profile.Contract is { } contract)
                        {
                            var date = new byte[4];
                            if (memory.TryRead(appointment + (ulong)contract.ExpiryDate, date))
                            {
                                int year = BitConverter.ToInt16(date, 2), day = BitConverter.ToUInt16(date) & 0x1FF;
                                hasFullContract = year == 1900 && day == 1 ||
                                    year is > 1900 and < 2300 && day >= 1 && day <= (DateTime.IsLeapYear(year) ? 366 : 365);
                                if (hasFullContract && year > 1900)
                                    expires = new DateOnly(year, 1, 1).AddDays(day - 1);
                            }
                        }
                    }
                }
            }
            result.Add(new(person, team, job, teamType, hasFullContract, expires));
        }
        return result;
    }

    public IReadOnlyList<StaffRecord> Read(ulong nation)
    {
        if (profile.NationAppointmentVtable is null || profile.Contract is null || profile.Nation?.Teams is not { } teams || profile.Staff is not { } staff)
            return [];
        var nationalTeams = new NationTeamReader(memory, profile, moduleBase).Read(nation);
        if (nationalTeams.Count == 0) return [];
        var nationBytes = new byte[teams.Pointer + 8];
        if (!memory.TryRead(nation, nationBytes)) return [];
        ulong holder = BitConverter.ToUInt64(nationBytes, teams.Pointer);
        if (holder == 0) return [];
        var repository = new StaffRepository(memory, profile, moduleBase);
        var found = new Dictionary<ulong, StaffRecord>();
        void Add(ulong address)
        {
            if (address == 0 || found.ContainsKey(address)) return;
            foreach (var kind in staff.Kinds)
                if (repository.Reload(address + (ulong)kind.StaffPart) is { } person && person.StaffAddress == address)
                { found.Add(address, person); break; }
        }
        // FMRTE lists the national-team managers first, then coaching and medical staff.
        foreach (var team in nationalTeams)
        {
            var pointer = new byte[8];
            if (memory.TryRead(team.Address + 0x80, pointer)) Add(BitConverter.ToUInt64(pointer));
        }
        foreach (int offset in new[] { 0x78, 0x60, 0x90 })
        {
            var header = new byte[24];
            if (!memory.TryRead(holder + (ulong)offset, header)) return [];
            ulong begin = BitConverter.ToUInt64(header), end = BitConverter.ToUInt64(header, 8), capacity = BitConverter.ToUInt64(header, 16);
            if (begin == 0 && end == 0 && capacity == 0) continue;
            if (begin == 0 || end < begin || capacity < end || (end - begin) % 8 != 0 || (end - begin) / 8 > 500) return [];
            var pointers = new byte[(int)(end - begin)];
            if (pointers.Length != 0 && !memory.TryRead(begin, pointers)) return [];
            for (int i = 0; i < pointers.Length; i += 8) Add(BitConverter.ToUInt64(pointers, i));
        }
        return found.Values.ToArray();
    }
}

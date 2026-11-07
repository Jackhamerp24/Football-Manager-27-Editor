using System.Text;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;

namespace Fm27Editor.Core.Nations;

/// <summary>A continent object (vtable and name offsets are the career-plan ones: Africa, Asia, Europe, North America, Oceania, South America, World).</summary>
public sealed record ContinentRecord(ulong Address, string Name);

/// <summary>
/// Nation → Capital (City*, read only: FMRTE shows plain text) and Continent (Continent*, a dropdown). Measured on Vietnam (Ha Noi,
/// Asia), Brazil (Brasília (DF), South America) and England (London, Europe) against FMRTE 27's Information page; FMRTE changing
/// Brazil's Continent to Asia rewrote only the men's pointer and left the one at +0x2F0 alone.
/// </summary>
public sealed class NationLinks(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxName = 200;

    public CityRecord? Capital(ulong nation) =>
        profile.Nation?.Capital is { } at && memory.TryRead(nation, new byte[8]) ? new CityRepository(memory, profile, moduleBase).Read(memory.ReadUInt64(nation + (ulong)at)) : null;

    /// <summary>The raw Continent pointer (0 when unreadable): the snapshot for <see cref="SetContinent"/>.</summary>
    public ulong ContinentPointer(ulong nation) =>
        profile.Nation?.Continent is { } at && IsNation(nation) ? memory.ReadUInt64(nation + (ulong)at) : 0;

    public ContinentRecord? Continent(ulong nation) => ReadContinent(ContinentPointer(nation));

    /// <summary>The national team's Stadium (nation+0x118), null when unset or not a stadium.</summary>
    public Fm27Editor.Core.Stadiums.StadiumRecord? Stadium(ulong nation) =>
        profile.Nation?.Stadium is { } at && IsNation(nation) && profile.Stadium is not null
            ? new Fm27Editor.Core.Stadiums.StadiumRepository(memory, profile, moduleBase).Reload(memory.ReadUInt64(nation + (ulong)at)) : null;

    /// <summary>Continent catalogue, including objects such as World that no nation points to.</summary>
    public IReadOnlyList<ContinentRecord> Continents(IEnumerable<NationRecord> nations)
    {
        var known = nations.Select(n => ContinentPointer(n.Address)).Where(p => p != 0).Distinct()
            .Select(ReadContinent).OfType<ContinentRecord>().ToList();
        var catalogue = FindContinentCatalogue(known) ?? known;
        return catalogue.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
    }

    IReadOnlyList<ContinentRecord>? FindContinentCatalogue(IReadOnlyList<ContinentRecord> known)
    {
        if (profile.Nation?.ListRootRva is not { } configuredRva || known.Count < 2) return null;
        var required = known.Select(c => c.Address).ToHashSet();
        IReadOnlyList<ContinentRecord>? best = null;
        var holderBytes = new byte[0x810];
        Span<byte> pointer = stackalloc byte[8];
        for (long delta = -0x20; delta <= 0x20; delta += 8)
        {
            if ((long)configuredRva + delta < 0 || !memory.TryRead(moduleBase + (ulong)((long)configuredRva + delta), pointer)) continue;
            ulong holder = BitConverter.ToUInt64(pointer);
            if (holder == 0 || !memory.TryRead(holder, holderBytes)) continue;
            for (int offset = 0; offset + 24 <= holderBytes.Length; offset += 8)
            {
                ulong begin = BitConverter.ToUInt64(holderBytes, offset), end = BitConverter.ToUInt64(holderBytes, offset + 8), capacity = BitConverter.ToUInt64(holderBytes, offset + 16);
                if (begin == 0 || end <= begin || capacity < end || (end - begin) % 8 != 0) continue;
                ulong count = (end - begin) / 8;
                if (count < (ulong)required.Count || count > 64 || (best is not null && count <= (ulong)best.Count)) continue;
                var bytes = new byte[(int)count * 8];
                if (!memory.TryRead(begin, bytes)) continue;
                var rows = new List<ContinentRecord>();
                for (int i = 0; i < (int)count; i++)
                {
                    if (ReadContinent(BitConverter.ToUInt64(bytes, i * 8)) is not { } continent) break;
                    rows.Add(continent);
                }
                if (rows.Count != (int)count || rows.Select(c => c.Address).Distinct().Count() != rows.Count || !required.IsSubsetOf(rows.Select(c => c.Address))) continue;
                best = rows;
            }
        }
        return best;
    }

    /// <summary>Guarded pointer swap: nation vtable, the pointer still the one read, target still a continent; read back.</summary>
    public void SetContinent(NationRecord nation, ulong expected, ContinentRecord continent)
    {
        if (profile.Nation?.Continent is not { } at) throw new InvalidOperationException("Profile has no nation continent offset");
        if (!IsNation(nation.Address)) throw new StaleObjectException("The nation changed since it was read");
        if (ReadContinent(continent.Address) is null) throw new StaleObjectException("The continent object changed since it was read");
        ulong field = nation.Address + (ulong)at;
        if (memory.ReadUInt64(field) != expected) throw new StaleObjectException("The continent changed since it was read");
        if (!memory.TryWrite(field, BitConverter.GetBytes(continent.Address)) || memory.ReadUInt64(field) != continent.Address)
            throw new MemoryAccessException($"Could not write 0x{field:X}");
    }

    bool IsNation(ulong address) => profile.Nation is { } n && address != 0 && memory.TryRead(address, new byte[8]) && memory.ReadUInt64(address) == moduleBase + n.VtableRva;

    ContinentRecord? ReadContinent(ulong address)
    {
        if (profile.CareerPlans is not { } c || address == 0) return null;
        var b = new byte[c.ContinentName + 8];
        if (!memory.TryRead(address, b) || BitConverter.ToUInt64(b) != moduleBase + c.ContinentVtable) return null;
        return StringAt(BitConverter.ToUInt64(b, c.ContinentName)) is { } name ? new(address, name) : null;
    }

    string? StringAt(ulong p)
    {
        if (p == 0) return null;
        Span<byte> len = stackalloc byte[4];
        if (!memory.TryRead(p, len)) return null;
        int n = BitConverter.ToInt32(len);
        if (n < 0 || n > MaxName) return null;
        var bytes = new byte[n];
        return memory.TryRead(p + 4, bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }
}

/// <summary>One national team of the men's side; the first entry of a nation is the senior team.</summary>
public sealed record NationTeamSnapshot(ulong Nation, ulong Holder, ulong Begin, ulong End, ulong Capacity, ulong Slot);
public sealed record NationTeamRecord(ulong Address, int Index, int Reputation, NationTeamSnapshot? Snapshot);

/// <summary>
/// National team Reputation (FMRTE 27's National Teams → Teams panel). Measured against FMRTE: Vietnam 3,000, Brazil 9,200, England
/// 9,600 (plus Spain 9,750 read from RAM); the value is an int32 at +0xA8 of the first team object in the holder's vector.
/// FMRTE saving 3001 changed only those four bytes; it also accepted 10001 and 30000, so the editor does not cap it at 10000.
/// </summary>
public sealed class NationTeamReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxTeams = 64;
    public const int MaxReputation = 30000;

    public IReadOnlyList<NationTeamRecord> Read(ulong nation)
    {
        if (profile.Nation is not { Teams: { } t } n || nation == 0 || !memory.TryRead(nation, new byte[8])
            || memory.ReadUInt64(nation) != moduleBase + n.VtableRva) return [];
        ulong holder = memory.ReadUInt64(nation + (ulong)t.Pointer);
        if (holder == 0 || !memory.TryRead(holder + (ulong)t.List, new byte[24])) return [];
        ulong begin = memory.ReadUInt64(holder + (ulong)t.List), end = memory.ReadUInt64(holder + (ulong)t.List + 8),
            capacity = memory.ReadUInt64(holder + (ulong)t.List + 16);
        if (begin == 0 || end < begin || capacity < end || (end - begin) % 8 != 0
            || (capacity - begin) % 8 != 0 || (end - begin) / 8 > MaxTeams) return [];
        var raw = memory.Read(begin, (int)(end - begin));
        var result = new List<NationTeamRecord>();
        for (int i = 0; i < raw.Length; i += 8)
            if (Team(BitConverter.ToUInt64(raw, i), i / 8, new(nation, holder, begin, end, capacity, begin + (ulong)i)) is { } team) result.Add(team);
        return result;
    }

    NationTeamRecord? Team(ulong address, int index, NationTeamSnapshot? snapshot = null)
    {
        if (profile.Nation?.Teams is not { } t || address == 0) return null;
        var b = new byte[t.Reputation + 4];
        if (!memory.TryRead(address, b) || BitConverter.ToUInt64(b) != moduleBase + t.TeamVtable) return null;
        return new(address, index, BitConverter.ToInt32(b, t.Reputation), snapshot);
    }

    /// <summary>
    /// FMRTE's "Allow Licensed Kits" checkbox (National Teams → Teams): bit 0x08 of the byte at team+0x2E, cleared when
    /// the box is ticked. Measured with a Frida write trace on England and Spain, both 0x24 while ticked; unticking wrote 0x2C.
    /// </summary>
    public const int LicensedKitsOffset = 0x2E, LicensedKitsMask = 0x08;

    public bool? AllowLicensedKits(ulong teamAddress)
    {
        if (profile.Nation?.Teams is not { } t || !Vtable(teamAddress, t.TeamVtable)) return null;
        var b = new byte[LicensedKitsOffset + 1];
        return memory.TryRead(teamAddress, b) ? (b[LicensedKitsOffset] & LicensedKitsMask) == 0 : null;
    }

    /// <summary>Guarded write: team vtable, current byte, then write and read back; other bits are preserved.</summary>
    public void SetAllowLicensedKits(NationTeamRecord team, bool value)
    {
        if (profile.Nation?.Teams is not { } t || !Vtable(team.Address, t.TeamVtable))
            throw new StaleObjectException("The national team changed since it was read");
        var current = new byte[LicensedKitsOffset + 1];
        if (!memory.TryRead(team.Address, current)) throw new MemoryAccessException("Could not read the licensed-kits byte");
        byte before = current[LicensedKitsOffset];
        byte after = value ? (byte)(before & ~LicensedKitsMask) : (byte)(before | LicensedKitsMask);
        if (after == before) return;
        ulong address = team.Address + LicensedKitsOffset;
        if (!memory.TryWrite(address, [after]) || !memory.Read(address, 1)[0].Equals(after))
            throw new MemoryAccessException($"Could not write the licensed-kits flag at 0x{address:X}");
    }

    bool Vtable(ulong address, uint rva) =>
        address != 0 && memory.TryRead(address, new byte[8]) && memory.ReadUInt64(address) == moduleBase + rva;

    /// <summary>Guarded: team vtable, reputation still equal to the snapshot, then write and read back.</summary>
    public void SetReputation(NationTeamRecord team, int value)
    {
        if (profile.Nation?.Teams is not { } t) throw new InvalidOperationException("Profile has no nation team offsets");
        if (value < 0 || value > MaxReputation) throw new ArgumentOutOfRangeException(nameof(value), $"Reputation is 0–{MaxReputation}");
        if (team.Snapshot is not { } s || profile.Nation is not { } n || !memory.TryRead(s.Nation, new byte[8])
            || memory.ReadUInt64(s.Nation) != moduleBase + n.VtableRva
            || memory.ReadUInt64(s.Nation + (ulong)t.Pointer) != s.Holder
            || memory.ReadUInt64(s.Holder + (ulong)t.List) != s.Begin
            || memory.ReadUInt64(s.Holder + (ulong)t.List + 8) != s.End
            || memory.ReadUInt64(s.Holder + (ulong)t.List + 16) != s.Capacity
            || s.Slot < s.Begin || s.Slot >= s.End || (s.Slot - s.Begin) % 8 != 0
            || memory.ReadUInt64(s.Slot) != team.Address || Team(team.Address, team.Index) is null)
            throw new StaleObjectException("The national team or its list changed since it was read");
        ulong at = team.Address + (ulong)t.Reputation;
        if (BitConverter.ToInt32(memory.Read(at, 4)) != team.Reputation) throw new StaleObjectException("Reputation changed since it was read");
        var bytes = BitConverter.GetBytes(value);
        if (!memory.TryWrite(at, bytes) || BitConverter.ToInt32(memory.Read(at, 4)) != value) throw new MemoryAccessException($"Could not write 0x{at:X}");
    }
}

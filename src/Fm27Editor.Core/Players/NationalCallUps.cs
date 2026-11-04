using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// National call-ups (FMRTE's Swap National Call Up). National teams are team objects with vtable <see cref="Vtable"/>,
/// listed among all teams at module + <see cref="ListRva"/> (begin/end right there); +<see cref="Nation"/> is the nation's
/// football object (<c>[nation+0x108]</c>) and +<see cref="Squad"/> the squad vector of player pointers. FMRTE 27 swapping
/// Bruno Fernandes with Fábio Vieira (2/11/2026) rewrote only Bruno's slot in Portugal's squad, with Vieira's pointer.
/// </summary>
public sealed record CallUpOffsets(uint ListRva, uint Vtable, int Nation, int Squad)
{
    static readonly string[] Keys = ["listRva", "vtable", "nation", "squad"];

    internal static CallUpOffsets Load(JsonElement e)
    {
        long[] v = Keys.Select(key => Convert.ToInt64(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"player.callUps.{key}: expected hex"), 16)).ToArray();
        return new((uint)v[0], (uint)v[1], (int)v[2], (int)v[3]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        long[] v = [ListRva, Vtable, Nation, Squad];
        w.WriteStartObject("callUps");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        w.WriteEndObject();
    }
}

/// <summary>A player's place in a national squad: the team, its nation football object and the slot holding the player.</summary>
public sealed record NationalCallUp(ulong Team, ulong NationFootball, ulong Slot, ulong Player);

public sealed class NationalCallUpReader(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    const int MaxTeams = 200_000, MaxSquad = 100;

    /// <summary>The national squad the player is in, or null.</summary>
    public NationalCallUp? Find(PlayerRecord player) => All().GetValueOrDefault(player.Address);

    /// <summary>Every called-up player, by player address.</summary>
    public IReadOnlyDictionary<ulong, NationalCallUp> All()
    {
        var map = new Dictionary<ulong, NationalCallUp>();
        if (profile.CallUps is not { } o) return map;
        ulong list = moduleBase + o.ListRva;
        if (!Q(list, out ulong begin) || !Q(list + 8, out ulong end) || begin == 0 || end < begin || (end - begin) / 8 > MaxTeams) return map;
        var teams = memory.Read(begin, (int)(end - begin));
        var head = new byte[o.Squad + 16];
        for (int i = 0; i < teams.Length; i += 8)
        {
            ulong team = BitConverter.ToUInt64(teams, i);
            if (team == 0 || !memory.TryRead(team, head) || BitConverter.ToUInt64(head, 0) != moduleBase + o.Vtable) continue;
            ulong sb = BitConverter.ToUInt64(head, o.Squad), se = BitConverter.ToUInt64(head, o.Squad + 8);
            if (sb == 0 || se <= sb || (se - sb) % 8 != 0 || (se - sb) / 8 > MaxSquad) continue;
            var squad = memory.Read(sb, (int)(se - sb));
            for (int k = 0; k < squad.Length; k += 8)
            {
                ulong p = BitConverter.ToUInt64(squad, k);
                if (p != 0) map.TryAdd(p, new(team, BitConverter.ToUInt64(head, o.Nation), sb + (ulong)k, p));
            }
        }
        return map;
    }

    bool Q(ulong address, out ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        bool ok = address != 0 && memory.TryRead(address, b);
        value = ok ? BitConverter.ToUInt64(b) : 0;
        return ok;
    }
}

/// <summary>
/// Swaps two players' national call-ups like FMRTE 27: each one's slot gets the other's pointer; a player with no call-up just
/// gives up or takes the other's place. Guarded: both are players, each slot still holds its player, the team is still a
/// national team; read-back.
/// </summary>
public sealed class NationalCallUpWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public void Swap(PlayerRecord a, NationalCallUp? aCallUp, PlayerRecord b, NationalCallUp? bCallUp)
    {
        var o = profile.CallUps ?? throw new InvalidOperationException("Profile has no call-up offsets");
        if (aCallUp is null && bCallUp is null) throw new InvalidOperationException("Neither player is in a national squad");
        if (a.Address == b.Address) throw new InvalidOperationException("Pick another player");
        if (aCallUp is not null && bCallUp is not null && aCallUp.Team == bCallUp.Team) throw new InvalidOperationException("Both players are already in that squad");
        foreach (var p in new[] { a, b })
            if (!profile.PlayerVtableRvas.Any(r => memory.ReadUInt64(p.Address) == moduleBase + r)) throw new StaleObjectException($"{p.DisplayName} changed since it was read");
        foreach (var (c, p) in new[] { (aCallUp, a), (bCallUp, b) })
            if (c is not null && (memory.ReadUInt64(c.Slot) != p.Address || memory.ReadUInt64(c.Team) != moduleBase + o.Vtable))
                throw new StaleObjectException("The national squads changed since they were read");
        if (aCallUp is not null) Write(aCallUp.Slot, b.Address);
        if (bCallUp is not null) Write(bCallUp.Slot, a.Address);
    }

    void Write(ulong at, ulong value)
    {
        var data = BitConverter.GetBytes(value);
        if (!memory.TryWrite(at, data) || memory.ReadUInt64(at) != value) throw new MemoryAccessException($"Could not write 0x{at:X}");
    }
}

using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// Team cohesion: relations holder + <see cref="Vector"/> is a vector of inline 8-byte entries (int32 item index, kind byte,
/// sbyte value). Kind 1 entries index clubs (club + <see cref="ClubIndex"/>; Man Utd 0x199). FMRTE 27's "Improve Team
/// Cohesion" set Kobbie Mainoo's Man Utd entry 87 → 100 and left the kind-2 entry alone (2/11/2026); FMRTE 26 sets 100
/// (Improve) or 1 (Destroy) on entries matching the club and adds none.
/// </summary>
public sealed record CohesionOffsets(int Vector, int EntrySize, int Kind, int Value, int ClubKind, int ClubIndex)
{
    static readonly string[] Keys = ["vector", "entrySize", "kind", "value", "clubKind", "clubIndex"];

    internal static CohesionOffsets Load(JsonElement e)
    {
        int[] v = Keys.Select(key => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"player.cohesion.{key}: expected hex"), 16)).ToArray();
        return new(v[0], v[1], v[2], v[3], v[4], v[5]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        int[] v = [Vector, EntrySize, Kind, Value, ClubKind, ClubIndex];
        w.WriteStartObject("cohesion");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        w.WriteEndObject();
    }
}

/// <summary>Snapshot of the selected club's cohesion row and the vector that contained it.</summary>
public sealed record TeamCohesionSnapshot(ulong Player, ulong Club, int ClubIndex, ulong Holder, ulong Begin, ulong End, ulong Capacity,
    ulong Entry, string Bytes);

public sealed class TeamCohesionWriter(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const sbyte Improve = 100, Destroy = 1;
    const int MaxEntries = 500;

    /// <summary>The player's cohesion with <paramref name="club"/>, or null when the game keeps none.</summary>
    public int? Read(PlayerRecord player, ulong club) => Capture(player, club) is { } s
        ? (sbyte)Convert.FromHexString(s.Bytes)[profile.Cohesion!.Value] : null;

    /// <summary>Sets the cohesion entry for <paramref name="club"/>; false when there is none (FMRTE adds none either).</summary>
    public bool Set(PlayerRecord player, ulong club, sbyte value)
    {
        if (Capture(player, club) is not { } snapshot) return false;
        Set(player, club, snapshot, value);
        return true;
    }

    /// <summary>Checks the original owner, vector and complete row before writing and reading back the value.</summary>
    public void Set(PlayerRecord player, ulong club, TeamCohesionSnapshot snapshot, sbyte value)
    {
        var c = profile.Cohesion ?? throw new InvalidOperationException("Profile has no cohesion offsets");
        var current = Capture(player, club);
        if (current is null || current != snapshot) throw new StaleObjectException("The team cohesion changed since it was read");
        ulong at = snapshot.Entry + (ulong)c.Value;
        if (!memory.TryWrite(at, [(byte)value]) || (sbyte)memory.Read(at, 1)[0] != value) throw new MemoryAccessException($"Could not write 0x{at:X}");
    }

    public TeamCohesionSnapshot? Capture(PlayerRecord player, ulong club)
    {
        var c = profile.Cohesion ?? throw new InvalidOperationException("Profile has no cohesion offsets");
        var r = profile.Relations ?? throw new InvalidOperationException("Profile has no relation offsets");
        if (!profile.PlayerVtableRvas.Any(rva => memory.ReadUInt64(player.Address) == moduleBase + rva)) throw new StaleObjectException("The player changed since it was read");
        if (profile.Club is not { } k || memory.ReadUInt64(club) != moduleBase + k.ClubVtableRva) throw new ArgumentException("Not a club", nameof(club));
        int index = BitConverter.ToInt32(memory.Read(club + (ulong)c.ClubIndex, 4));
        ulong holder = memory.ReadUInt64(player.Address + (ulong)r.Pointer);
        if (holder == 0) return null;
        var header = memory.Read(holder + (ulong)c.Vector, 24);
        ulong begin = BitConverter.ToUInt64(header, 0), end = BitConverter.ToUInt64(header, 8), capacity = BitConverter.ToUInt64(header, 16);
        if (begin == 0 || end < begin || capacity < end || c.EntrySize <= 0
            || (end - begin) % (ulong)c.EntrySize != 0 || (capacity - begin) % (ulong)c.EntrySize != 0
            || (end - begin) / (ulong)c.EntrySize > MaxEntries) return null;
        var entries = memory.Read(begin, (int)(end - begin));
        for (int i = 0; i < entries.Length; i += c.EntrySize)
            if (BitConverter.ToInt32(entries, i) == index && entries[i + c.Kind] == c.ClubKind)
                return new(player.Address, club, index, holder, begin, end, capacity, begin + (ulong)i,
                    Convert.ToHexString(entries.AsSpan(i, c.EntrySize)));
        return null;
    }
}

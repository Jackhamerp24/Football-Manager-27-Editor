using System.Text.Json;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;

namespace Fm27Editor.Core.Players;

/// <summary>
/// A player's Tactical Understanding (what FMRTE 27's "Tactical Understanding" preset maximises). Measured on Game Pass 27.0.0+2493426,
/// 9/11/2026, by pressing the preset on Declan Rice with Frida on FMRTE: player + <see cref="Holder"/> points at an object whose six
/// vectors (begin/end/cap, <see cref="Stride"/> apart, from +0) hold dwords, one list per slider (Build Up, Chance Creation, Passing
/// Style, Pressing Trap, Line of Engagement, Pressing Intensity); bits 0-13 of each dword are value × 100 and the higher bits are flags
/// the preset keeps. A seventh vector at +<see cref="RoleVector"/> holds <see cref="RoleEntry"/>-byte entries (Position/Role, one per
/// position the player has played) whose u16 at +<see cref="RoleValue"/> is value × 100. The preset wrote 10000 to every one of them.
/// </summary>
public sealed record TacticalUnderstandingOffsets(int Holder, int Stride, int Count, int RoleVector, int RoleEntry, int RoleValue)
{
    static readonly string[] Keys = ["holder", "stride", "count", "roleVector", "roleEntry", "roleValue"];

    internal static TacticalUnderstandingOffsets Load(JsonElement e)
    {
        int[] v = Keys.Select(key => Convert.ToInt32(e.GetProperty(key).GetString() is { } s && s.StartsWith("0x")
            ? s[2..] : throw new FormatException($"player.tacticalUnderstanding.{key}: expected hex"), 16)).ToArray();
        return new(v[0], v[1], v[2], v[3], v[4], v[5]);
    }

    internal void Write(Utf8JsonWriter w)
    {
        int[] v = [Holder, Stride, Count, RoleVector, RoleEntry, RoleValue];
        w.WriteStartObject("tacticalUnderstanding");
        for (int i = 0; i < Keys.Length; i++) w.WriteString(Keys[i], $"0x{v[i]:X}");
        w.WriteEndObject();
    }
}

/// <summary>The lowest value (0-100) in each of the six slider lists and the role list, null for a list the player has none of.</summary>
public sealed record PlayerTacticalValues(IReadOnlyList<int?> Sliders, int? PositionRole);

public sealed class PlayerTacticalUnderstanding(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int Max = 10_000, ValueMask = 0x3FFF, MaxItems = 64, AreaNumberOffset = 0x18;

    /// <summary>The area each of the six lists belongs to, as the game numbers them (the number sits at +0x18 of each 0x20-byte block); the seventh holds Pressing Trap and Out Of Possession Shape (SemHiel2003/fm27-trainer, disassembly of FM's TEAM_MAXIMISE_TACTICAL_UNDERSTANDING_ACTION).</summary>
    public static readonly string[] AreaNames = ["Passing Style", "Build Up", "Chance Creation", "In Possession Shape", "Line of Engagement", "Pressing Intensity", "Pressing Trap / Out Of Possession Shape"];

    public bool Available => profile.TacticalUnderstanding is not null;

    TacticalUnderstandingOffsets O => profile.TacticalUnderstanding ?? throw new InvalidOperationException("Profile has no tactical understanding offsets");

    /// <summary>Every value the player holds, as (address, size, current raw value); empty when the player has no such object.</summary>
    public IReadOnlyList<(ulong At, int Size, int Raw, int Slider)> Cells(PlayerRecord player)
    {
        var o = O;
        var list = new List<(ulong, int, int, int)>();
        if (!IsPlayer(player.Address)) return list;
        ulong holder = Q(player.Address + (ulong)o.Holder);
        if (holder == 0) return list;
        for (int i = 0; i < o.Count; i++)
        {
            if (Q(holder + (ulong)(i * o.Stride + AreaNumberOffset)) != (ulong)i) return [];   // not the record this layout describes
            var (begin, end) = Vector(holder + (ulong)(i * o.Stride));
            if (!Valid(begin, end, 4)) continue;
            var raw = memory.Read(begin, (int)(end - begin));
            for (int k = 0; k < raw.Length; k += 4) list.Add((begin + (ulong)k, 4, (int)BitConverter.ToUInt32(raw, k), i));
        }
        var (rb, re) = Vector(holder + (ulong)o.RoleVector);
        if (Valid(rb, re, o.RoleEntry))
        {
            var raw = memory.Read(rb, (int)(re - rb));
            for (int k = 0; k < raw.Length; k += o.RoleEntry) list.Add((rb + (ulong)(k + o.RoleValue), 2, BitConverter.ToUInt16(raw, k + o.RoleValue), o.Count));
        }
        return list;
    }

    public PlayerTacticalValues? Read(PlayerRecord player)
    {
        if (!Available) return null;
        var cells = Cells(player);
        int? Lowest(int slider) => cells.Where(c => c.Slider == slider).Select(c => (c.Size == 4 ? c.Raw & ValueMask : c.Raw) / 100).DefaultIfEmpty(-1).Min() is var m and >= 0 ? m : null;
        return new(Enumerable.Range(0, O.Count).Select(Lowest).ToList(), Lowest(O.Count));
    }

    /// <summary>Sets every value to 100% like FMRTE's preset, keeping each dword's flag bits. Returns how many values were changed.</summary>
    public int Maximise(PlayerRecord player)
    {
        var cells = Cells(player);
        int changed = 0;
        foreach (var (at, size, raw, _) in cells)
        {
            byte[] now = memory.Read(at, size), want;
            if (size == 4)
            {
                uint current = BitConverter.ToUInt32(now);
                if (current != (uint)raw) throw new StaleObjectException("A tactical value changed since it was read");
                if ((current & ValueMask) == Max) continue;
                want = BitConverter.GetBytes((current & ~(uint)ValueMask) | Max);
            }
            else
            {
                if (BitConverter.ToUInt16(now) != raw) throw new StaleObjectException("A tactical value changed since it was read");
                if (raw == Max) continue;
                want = BitConverter.GetBytes((ushort)Max);
            }
            if (!memory.TryWrite(at, want) || !memory.Read(at, size).AsSpan().SequenceEqual(want))
                throw new MemoryAccessException($"Could not write tactical understanding at 0x{at:X}");
            changed++;
        }
        return changed;
    }

    (ulong begin, ulong end) Vector(ulong at) => (Q(at), Q(at + 8));

    static bool Valid(ulong begin, ulong end, int item) => begin != 0 && end > begin && (end - begin) % (ulong)item == 0 && (end - begin) / (ulong)item <= MaxItems;

    ulong Q(ulong address)
    {
        Span<byte> b = stackalloc byte[8];
        return address != 0 && memory.TryRead(address, b) ? BitConverter.ToUInt64(b) : 0;
    }

    bool IsPlayer(ulong address) => Q(address) is var vt and not 0 && profile.PlayerVtableRvas.Any(r => vt == moduleBase + r);
}
